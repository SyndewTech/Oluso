using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Notifications;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers.Service;

/// <summary>
/// Service-to-Service API for conversation threads (comments on approval requests and other
/// records). Lets a trusted integration (e.g. a chat bridge) mirror chat into a thread
/// as the people who wrote it.
/// </summary>
/// <remarks>
/// Required scopes:
/// - oluso:conversations:read - Read a thread, including deleted comments
/// - oluso:conversations:write - Post, edit and delete comments as a named author
///
/// The client holds no user authority of its own: every write names <c>authorUserId</c>, and that
/// author's access is checked with the entity type's conversation authorizer exactly as the
/// workspace endpoint checks the signed-in user. Only entity types with a registered authorizer
/// are reachable. The writing client's id is stored on the message and carried on the
/// <c>conversation.message.*</c> events, whose origin is this client, so a webhook endpoint with
/// SkipOwnClientEvents doesn't hear its own writes. Tenant context comes from the client registration.
/// </remarks>
[Route("api/service/conversations/{entityType}/{entityId}/messages")]
public class ServiceConversationsController : ServiceBaseController
{
    /// <summary>Error code for a write whose author may not write on the thread (or doesn't exist).</summary>
    public const string AuthorNotAuthorized = "author_not_authorized";

    private readonly IConversationService _conversations;
    private readonly IOlusoUserService _users;

    public ServiceConversationsController(
        ITenantContext tenantContext,
        IConversationService conversations,
        IOlusoUserService users)
        : base(tenantContext)
    {
        _conversations = conversations;
        _users = users;
    }

    /// <summary>
    /// The thread, oldest first, including soft-deleted comments (deletedAt set, body null).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServiceConversationMessageDto>>> List(
        string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        if (!HasScope(ServiceScopes.ConversationsRead)) return Forbid();

        var auth = _conversations.FindAuthorizer(entityType);
        if (auth == null) return NotFound();
        if (await auth.ExistsAsync(TenantId!, entityId, cancellationToken) == false) return NotFound();

        var messages = await _conversations.ListAsync(TenantId!, entityType, entityId, cancellationToken);
        return Ok(messages.Select(ServiceConversationMessageDto.From));
    }

    /// <summary>Posts a comment as <c>authorUserId</c>.</summary>
    [HttpPost]
    public async Task<ActionResult<ServiceConversationMessageDto>> Post(
        string entityType, string entityId,
        [FromBody] ServicePostConversationMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasScope(ServiceScopes.ConversationsWrite)) return Forbid();
        if (request is null || string.IsNullOrWhiteSpace(request.AuthorUserId))
            return BadRequest(new { error = "authorUserId is required" });

        if (await CheckThreadAndAuthorAsync(entityType, entityId, request.AuthorUserId, cancellationToken) is { } refused)
            return refused;

        var result = await _conversations.PostAsync(
            TenantId!, entityType, entityId, request.AuthorUserId, request.Body, request.MentionedUserIds,
            ClientId, cancellationToken);
        if (!result.Succeeded) return Failure(result);

        var dto = ServiceConversationMessageDto.From(result.Value!);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    /// <summary>Edits a comment; <c>authorUserId</c> must be its author and still on the thread.</summary>
    [HttpPut("{messageId}")]
    public async Task<ActionResult<ServiceConversationMessageDto>> Edit(
        string entityType, string entityId, string messageId,
        [FromBody] ServiceEditConversationMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasScope(ServiceScopes.ConversationsWrite)) return Forbid();
        if (request is null || string.IsNullOrWhiteSpace(request.AuthorUserId))
            return BadRequest(new { error = "authorUserId is required" });

        if (await CheckThreadAndAuthorAsync(entityType, entityId, request.AuthorUserId, cancellationToken) is { } refused)
            return refused;

        var result = await _conversations.EditAsync(
            TenantId!, entityType, entityId, messageId, request.AuthorUserId, request.Body, request.MentionedUserIds,
            ClientId, cancellationToken);
        return result.Succeeded ? Ok(ServiceConversationMessageDto.From(result.Value!)) : Failure(result);
    }

    /// <summary>Soft-deletes a comment; <c>authorUserId</c> must be its author and still on the thread.</summary>
    [HttpDelete("{messageId}")]
    public async Task<ActionResult> Delete(
        string entityType, string entityId, string messageId,
        [FromQuery] string? authorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!HasScope(ServiceScopes.ConversationsWrite)) return Forbid();
        if (string.IsNullOrWhiteSpace(authorUserId))
            return BadRequest(new { error = "authorUserId is required" });

        if (await CheckThreadAndAuthorAsync(entityType, entityId, authorUserId, cancellationToken) is { } refused)
            return refused;

        var result = await _conversations.DeleteAsync(
            TenantId!, entityType, entityId, messageId, authorUserId, ClientId, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>
    /// 404 for an unknown entity type or a missing entity; 403 author_not_authorized for an author
    /// who isn't an active user of the client's tenant.
    /// </summary>
    private async Task<ActionResult?> CheckThreadAndAuthorAsync(
        string entityType, string entityId, string authorUserId, CancellationToken cancellationToken)
    {
        var auth = _conversations.FindAuthorizer(entityType);
        if (auth == null) return NotFound();
        if (await auth.ExistsAsync(TenantId!, entityId, cancellationToken) == false) return NotFound();

        var author = await _users.FindByIdInTenantAsync(authorUserId, TenantId!, cancellationToken);
        if (author == null || !author.IsActive) return AuthorRefused();
        return null;
    }

    private ObjectResult AuthorRefused() =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = AuthorNotAuthorized });

    private ActionResult Failure<T>(ConversationResult<T> result) => result.Outcome switch
    {
        ConversationOutcome.Invalid => BadRequest(new { error = result.Error }),
        ConversationOutcome.Forbidden => AuthorRefused(),
        ConversationOutcome.Deleted => Conflict(new { error = result.Error }),
        _ => NotFound(),
    };
}

/// <summary>A conversation message as the Service API returns it.</summary>
public class ServiceConversationMessageDto
{
    public string Id { get; set; } = null!;
    public string AuthorUserId { get; set; } = null!;
    public string? AuthorName { get; set; }

    /// <summary>Null once the message is deleted.</summary>
    public string? Body { get; set; }

    public List<string> MentionedUserIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>OAuth client the message was written through, when known.</summary>
    public string? ClientId { get; set; }

    public static ServiceConversationMessageDto From(ConversationMessageView view)
    {
        var m = view.Message;
        return new ServiceConversationMessageDto
        {
            Id = m.Id,
            AuthorUserId = m.AuthorUserId,
            AuthorName = view.AuthorName,
            Body = m.DeletedAt == null ? m.Body : null,
            MentionedUserIds = ConversationService.ParseMentions(m.MentionedUserIdsJson).ToList(),
            CreatedAt = m.CreatedAt,
            EditedAt = m.EditedAt,
            DeletedAt = m.DeletedAt,
            ClientId = m.ClientId,
        };
    }
}

public class ServicePostConversationMessageRequest
{
    public string AuthorUserId { get; set; } = null!;
    public string? Body { get; set; }

    /// <summary>Mentioned user ids; ids that aren't thread participants are dropped.</summary>
    public List<string>? MentionedUserIds { get; set; }
}

public class ServiceEditConversationMessageRequest
{
    public string AuthorUserId { get; set; } = null!;
    public string? Body { get; set; }
    public List<string>? MentionedUserIds { get; set; }
}
