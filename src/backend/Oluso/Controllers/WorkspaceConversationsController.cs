using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Notifications;
using Oluso.Dto.Conversations;

namespace Oluso.Controllers;

/// <summary>
/// Generic conversation-thread endpoint for the workspace. Any module
/// that registers an <see cref="IConversationAuthorizer"/> for its
/// entity type gets a comments thread without writing its own controller —
/// approval requests, candidate profiles, etc.
///
/// <para>Without a registered authorizer, every operation is refused —
/// so a caller can't reach a module's thread by guessing entity types.</para>
///
/// <para>Writes go through <see cref="IConversationService"/>, shared with the
/// Service API, so both raise the same realtime pushes and
/// <c>conversation.message.*</c> events.</para>
/// </summary>
[Route("api/workspace/conversations/{entityType}/{entityId}")]
public class WorkspaceConversationsController : WorkspaceBaseController
{
    private readonly IConversationService _conversations;
    private readonly IConversationMentionReadStore _mentionReadStore;

    /// <summary>Channel name the conversation thread broadcasts on.</summary>
    public const string ConversationChannel = ConversationService.ConversationChannel;

    /// <summary>
    /// Shared Action Center channel — every module that contributes to the
    /// unified inbox pings here so the badge re-fetches its count without
    /// polling. Mentioned users gain a "mentioned you" row in the inbox.
    /// </summary>
    public const string ActionCenterChannel = ConversationService.ActionCenterChannel;

    public WorkspaceConversationsController(
        ITenantContext tenantContext,
        IConversationService conversations,
        IConversationMentionReadStore mentionReadStore)
        : base(tenantContext)
    {
        _conversations = conversations;
        _mentionReadStore = mentionReadStore;
    }

    private string? CallerClientId => User.FindFirst("client_id")?.Value;

    [HttpGet]
    [ConversationScope(ConversationScopeAccess.Read)]
    public async Task<ActionResult<IEnumerable<ConversationMessageDto>>> List(
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var auth = _conversations.FindAuthorizer(entityType);
        if (auth == null) return NotFound();

        if (!await auth.CanReadAsync(TenantId, entityId, UserId, cancellationToken))
            return Forbid();

        var messages = await _conversations.ListAsync(TenantId, entityType, entityId, cancellationToken);

        // Auto-ack any unread mentions of the current user surfaced in this
        // thread load. Opening the thread is the natural "I saw it" signal
        // so the Action Center count drops back to zero without a separate
        // "mark read" click. Idempotent — repeat opens are no-ops.
        var mentionedMessageIds = messages
            .Select(v => v.Message)
            .Where(m => m.DeletedAt == null)
            .Where(m => ConversationService.ParseMentions(m.MentionedUserIdsJson).Contains(UserId, StringComparer.Ordinal))
            .Select(m => m.Id)
            .ToList();
        if (mentionedMessageIds.Count > 0)
        {
            await _mentionReadStore.MarkReadAsync(TenantId, UserId, mentionedMessageIds, cancellationToken);
        }

        return Ok(messages.Select(MapToDto));
    }

    /// <summary>
    /// Users that can be tagged with <c>@</c> on this thread. Drives
    /// the autocomplete picker in the comment composer. Read access is
    /// required — observers might be valid mention targets but we don't
    /// leak the participant list to non-participants.
    /// </summary>
    [HttpGet("participants")]
    [ConversationScope(ConversationScopeAccess.Read)]
    public async Task<ActionResult<IEnumerable<ConversationParticipant>>> Participants(
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var auth = _conversations.FindAuthorizer(entityType);
        if (auth == null) return NotFound();

        if (!await auth.CanReadAsync(TenantId, entityId, UserId, cancellationToken))
            return Forbid();

        var participants = await auth.GetParticipantsAsync(TenantId, entityId, cancellationToken);
        return Ok(participants);
    }

    [HttpPost]
    [ConversationScope(ConversationScopeAccess.Write)]
    public async Task<ActionResult<ConversationMessageDto>> Post(
        string entityType,
        string entityId,
        [FromBody] PostConversationMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _conversations.PostAsync(
            TenantId, entityType, entityId, UserId, request?.Body, request?.MentionedUserIds,
            CallerClientId, cancellationToken);
        return result.Succeeded ? Ok(MapToDto(result.Value!)) : Failure(result);
    }

    /// <summary>
    /// Edit an existing message. Only the author can edit. Edits set
    /// <c>EditedAt</c> so the UI can show "(edited)" in line.
    /// </summary>
    [HttpPut("{messageId}")]
    [ConversationScope(ConversationScopeAccess.Write)]
    public async Task<ActionResult<ConversationMessageDto>> Edit(
        string entityType,
        string entityId,
        string messageId,
        [FromBody] PostConversationMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _conversations.EditAsync(
            TenantId, entityType, entityId, messageId, UserId, request?.Body, request?.MentionedUserIds,
            CallerClientId, cancellationToken);
        return result.Succeeded ? Ok(MapToDto(result.Value!)) : Failure(result);
    }

    /// <summary>
    /// Soft-delete a message. Only the author can delete; the row is
    /// kept so the audit trail stays intact and the UI shows a deleted
    /// placeholder.
    /// </summary>
    [HttpDelete("{messageId}")]
    [ConversationScope(ConversationScopeAccess.Write)]
    public async Task<ActionResult> Delete(
        string entityType,
        string entityId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var result = await _conversations.DeleteAsync(
            TenantId, entityType, entityId, messageId, UserId, CallerClientId, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    private ActionResult Failure<T>(ConversationResult<T> result) => result.Outcome switch
    {
        ConversationOutcome.Invalid => BadRequest(new { error = result.Error }),
        ConversationOutcome.Forbidden => Forbid(),
        ConversationOutcome.Deleted => Conflict(new { error = result.Error }),
        _ => NotFound(),
    };

    private static ConversationMessageDto MapToDto(ConversationMessageView view)
    {
        var m = view.Message;
        var mentions = ConversationService.ParseMentions(m.MentionedUserIdsJson);
        return new ConversationMessageDto
        {
            Id = m.Id,
            EntityType = m.EntityType,
            EntityId = m.EntityId,
            AuthorUserId = m.AuthorUserId,
            AuthorName = view.AuthorName,
            Body = m.DeletedAt == null ? m.Body : null,
            CreatedAt = m.CreatedAt,
            EditedAt = m.EditedAt,
            DeletedAt = m.DeletedAt,
            MentionedUserIds = mentions.Count > 0 ? mentions.ToList() : null,
        };
    }
}

/// <summary>Which of a thread's restricted-client scopes an endpoint needs.</summary>
public enum ConversationScopeAccess
{
    Read,
    Write,
}

/// <summary>
/// Scope requirement for a scope-restricted client on the conversation endpoints: the scopes the
/// route's entity type authorizer declares (<see cref="IConversationAuthorizer.RestrictedClientReadScopes"/>
/// or <see cref="IConversationAuthorizer.RestrictedClientWriteScopes"/>). Entity types that declare
/// none stay closed to such clients. Write scopes also admit reads.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ConversationScopeAttribute : Attribute, IScopeRequirementMetadata
{
    public ConversationScopeAttribute(ConversationScopeAccess access)
    {
        Access = access;
    }

    public ConversationScopeAccess Access { get; }

    public IReadOnlyList<string>? GetRequiredScopes(HttpContext httpContext)
    {
        var entityType = httpContext.Request.RouteValues.TryGetValue("entityType", out var v) ? v?.ToString() : null;
        if (string.IsNullOrEmpty(entityType)) return Array.Empty<string>();

        var auth = httpContext.RequestServices.GetService<IConversationService>()?.FindAuthorizer(entityType);
        if (auth == null) return Array.Empty<string>();

        return Access == ConversationScopeAccess.Write
            ? auth.RestrictedClientWriteScopes
            : auth.RestrictedClientReadScopes.Concat(auth.RestrictedClientWriteScopes).Distinct(StringComparer.Ordinal).ToList();
    }
}
