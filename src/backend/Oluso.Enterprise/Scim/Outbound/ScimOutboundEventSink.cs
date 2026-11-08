using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// Turns user, role and team lifecycle events into outbound queue items.
/// Only enqueues; the worker does the HTTP work, so event dispatch stays
/// fast and a slow provider never delays the request that raised the event.
/// A change that came in over SCIM is not queued for a provider linked to the
/// inbound client it came from: that system already has it.
/// </summary>
public sealed class ScimOutboundEventSink : IOlusoEventSink
{
    private readonly IScimProviderEnqueuer _enqueuer;
    private readonly IRoleStore _roles;
    private readonly ITeamStore _teams;
    private readonly IOlusoUserService _users;
    private readonly ILogger<ScimOutboundEventSink> _logger;

    public ScimOutboundEventSink(
        IScimProviderEnqueuer enqueuer,
        IRoleStore roles,
        ITeamStore teams,
        IOlusoUserService users,
        ILogger<ScimOutboundEventSink> logger)
    {
        _enqueuer = enqueuer;
        _roles = roles;
        _teams = teams;
        _users = users;
        _logger = logger;
    }

    public string Name => "ScimOutbound";

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        switch (evt)
        {
            case UserRegisteredEvent e:
                await UserAsync(e.TenantId, e.SubjectId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case UserUpdatedEvent e:
                await UserAsync(e.TenantId, e.SubjectId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case UserDeletedEvent e:
                await UserAsync(e.TenantId, e.SubjectId, ScimOutboxOperations.Remove, e, cancellationToken);
                break;
            // The account still exists, so providers get its new active flag
            // rather than a removal.
            case UserDeactivatedEvent e:
                await UserAsync(e.TenantId, e.SubjectId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case UserReactivatedEvent e:
                await UserAsync(e.TenantId, e.SubjectId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;

            case UserRoleAssignedEvent e:
                await UserAsync(e.TenantId, e.SubjectId, ScimOutboxOperations.Upsert, e, cancellationToken);
                await RoleByIdAsync(e.TenantId, e.RoleId, e, cancellationToken);
                break;
            case UserRoleRemovedEvent e:
                await UserAsync(e.TenantId, e.SubjectId, ScimOutboxOperations.Upsert, e, cancellationToken);
                await RoleByIdAsync(e.TenantId, e.RoleId, e, cancellationToken);
                break;
            case AdminRoleUpdatedEvent e:
                await GroupAsync(e.TenantId, ScimGroupKinds.Role, e.ResourceId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case AdminRoleDeletedEvent e:
                await GroupAsync(e.TenantId, ScimGroupKinds.Role, e.ResourceId, ScimOutboxOperations.Remove, e, cancellationToken);
                break;

            case TeamMemberAddedEvent e:
                await UserAsync(e.TenantId, e.UserId, ScimOutboxOperations.Upsert, e, cancellationToken);
                await TeamAndAncestorsAsync(e.TenantId, e.TeamId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case TeamMemberRemovedEvent e:
                await UserAsync(e.TenantId, e.UserId, ScimOutboxOperations.Upsert, e, cancellationToken);
                await TeamAndAncestorsAsync(e.TenantId, e.TeamId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case TeamCreatedEvent e:
                await GroupAsync(e.TenantId, ScimGroupKinds.Team, e.TeamId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case TeamUpdatedEvent e:
                // A parent change moves the whole subtree's effective membership: the new ancestors gain the team's
                // members and the old ones lose them.
                await TeamAndAncestorsAsync(e.TenantId, e.TeamId, ScimOutboxOperations.Upsert, e, cancellationToken);
                if (e.ParentChanged && !string.IsNullOrEmpty(e.PreviousParentTeamId))
                    await TeamAndAncestorsAsync(e.TenantId, e.PreviousParentTeamId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case TeamMemberRoleChangedEvent e:
                // Membership is unchanged; only the team's own group can carry the role (its leads).
                await GroupAsync(e.TenantId, ScimGroupKinds.Team, e.TeamId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case TeamRestoredEvent e:
                await TeamAndAncestorsAsync(e.TenantId, e.TeamId, ScimOutboxOperations.Upsert, e, cancellationToken);
                break;
            case TeamArchivedEvent e:
                await TeamAndAncestorsAsync(e.TenantId, e.TeamId, ScimOutboxOperations.Remove, e, cancellationToken);
                break;
            case TeamDeletedEvent e:
                await GroupAsync(e.TenantId, ScimGroupKinds.Team, e.TeamId, ScimOutboxOperations.Remove, e, cancellationToken);
                break;
        }
    }

    private async Task UserAsync(string? tenantId, string? userId, string operation, OlusoEvent evt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId))
            return;

        if (string.IsNullOrEmpty(tenantId))
        {
            var user = await _users.GetByIdAsync(userId, cancellationToken);
            tenantId = user?.TenantId;
        }

        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogDebug("SCIM outbound ignored {EventType} for user {UserId}: no tenant", evt.EventType, userId);
            return;
        }

        await _enqueuer.EnqueueUserAsync(tenantId, userId, operation, Reason(evt), evt.Origin, cancellationToken);
    }

    /// <summary>
    /// Only the tenant's own roles are pushed as groups; a platform-wide role
    /// held in the tenant is not one of its groups.
    /// </summary>
    private async Task RoleByIdAsync(string? tenantId, string roleId, OlusoEvent evt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tenantId))
            return;

        var role = (await _roles.GetByIdsAsync(new[] { roleId }, cancellationToken)).FirstOrDefault();
        if (role == null || !string.Equals(role.TenantId, tenantId, StringComparison.Ordinal))
            return;

        await GroupAsync(tenantId, ScimGroupKinds.Role, role.Id, ScimOutboxOperations.Upsert, evt, cancellationToken);
    }

    private async Task GroupAsync(string? tenantId, string kind, string? id, string operation, OlusoEvent evt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(id))
            return;

        await _enqueuer.EnqueueGroupAsync(tenantId, ScimGroupKinds.Key(kind, id), operation, Reason(evt), evt.Origin, cancellationToken);
    }

    /// <summary>
    /// The team itself gets <paramref name="operation"/>; every ancestor is
    /// refreshed, because effective membership flows up the hierarchy.
    /// </summary>
    private async Task TeamAndAncestorsAsync(string? tenantId, string teamId, string operation, OlusoEvent evt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tenantId))
            return;

        await GroupAsync(tenantId, ScimGroupKinds.Team, teamId, operation, evt, cancellationToken);

        var visited = new HashSet<string>(StringComparer.Ordinal) { teamId };
        var current = await _teams.GetByIdAsync(tenantId, teamId, cancellationToken);
        var parentId = current?.ParentTeamId;
        while (!string.IsNullOrEmpty(parentId) && visited.Add(parentId) && visited.Count <= 64)
        {
            await GroupAsync(tenantId, ScimGroupKinds.Team, parentId, ScimOutboxOperations.Upsert, evt, cancellationToken);
            var parent = await _teams.GetByIdAsync(tenantId, parentId, cancellationToken);
            parentId = parent?.ParentTeamId;
        }
    }

    private static string Reason(OlusoEvent evt) => $"event:{evt.WebhookEventType ?? evt.EventType}";
}
