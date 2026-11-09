using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.EntityFramework.Services;

/// <summary>
/// Default <see cref="ITeamService"/>. Wraps <see cref="ITeamStore"/> with the
/// invariants that have to hold across calls (unique codes, cycle-free parent
/// chains, audit stamping) so controllers and importers don't each re-implement
/// them.
/// </summary>
public class TeamService : ITeamService
{
    private readonly ITeamStore _store;
    private readonly ILogger<TeamService> _logger;
    private readonly IServiceProvider? _services;

    public TeamService(ITeamStore store, ILogger<TeamService> logger, IServiceProvider? services = null)
    {
        _store = store;
        _logger = logger;
        _services = services;
    }

    /// <summary>
    /// Resolved per call rather than injected: the event service constructs
    /// every sink, and sinks reach stores that depend on this service, so a
    /// constructor dependency is a cycle.
    /// </summary>
    private Task RaiseAsync(OlusoEvent evt, CancellationToken cancellationToken)
    {
        var events = _services?.GetService<IOlusoEventService>();
        return events?.RaiseAsync(evt, cancellationToken) ?? Task.CompletedTask;
    }

    public async Task<Team> CreateAsync(
        string tenantId,
        Team team,
        string? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(team.Code))
            throw new InvalidOperationException("Team.Code is required.");
        if (string.IsNullOrWhiteSpace(team.Name))
            throw new InvalidOperationException("Team.Name is required.");

        if (await _store.CodeExistsAsync(tenantId, team.Code, excludeId: null, cancellationToken))
            throw new InvalidOperationException($"Team code '{team.Code}' already exists in this tenant.");

        if (!string.IsNullOrEmpty(team.ParentTeamId))
        {
            var parent = await _store.GetByIdAsync(tenantId, team.ParentTeamId, cancellationToken)
                ?? throw new InvalidOperationException($"Parent team '{team.ParentTeamId}' not found.");
            // New teams can't form cycles (they don't exist yet to be a parent),
            // so the only check is that the named parent actually exists.
            _ = parent;
        }

        team.TenantId = tenantId;
        team.CreatedAt = DateTime.UtcNow;
        team.CreatedBy = createdByUserId;

        var created = await _store.CreateAsync(team, cancellationToken);
        await RaiseAsync(new TeamCreatedEvent
        {
            TenantId = tenantId,
            TeamId = created.Id,
            TeamName = created.Name,
            TeamCode = created.Code,
            ActorUserId = createdByUserId,
        }, cancellationToken);
        return created;
    }

    public async Task<Team> UpdateAsync(
        string tenantId,
        Team team,
        string? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetByIdAsync(tenantId, team.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Team '{team.Id}' not found.");

        // Code changes still need to be unique within the tenant.
        if (!string.Equals(existing.Code, team.Code, StringComparison.Ordinal))
        {
            if (await _store.CodeExistsAsync(tenantId, team.Code, excludeId: team.Id, cancellationToken))
                throw new InvalidOperationException($"Team code '{team.Code}' already exists in this tenant.");
        }

        // Parent change → reject cycles. A team can't be its own parent, and
        // the proposed new parent's ancestor chain must not include this team.
        if (existing.ParentTeamId != team.ParentTeamId && !string.IsNullOrEmpty(team.ParentTeamId))
        {
            if (string.Equals(team.ParentTeamId, team.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("A team cannot be its own parent.");

            await GuardNoCycleAsync(tenantId, team.Id, team.ParentTeamId, cancellationToken);
        }

        var previousParentId = existing.ParentTeamId;
        var parentChanged = !string.Equals(previousParentId, team.ParentTeamId, StringComparison.Ordinal);

        existing.Code = team.Code;
        existing.Name = team.Name;
        existing.Description = team.Description;
        existing.Type = team.Type;
        existing.ParentTeamId = team.ParentTeamId;
        existing.OwnerUserId = team.OwnerUserId;
        existing.Visibility = team.Visibility;
        existing.DisplayOrder = team.DisplayOrder;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = updatedByUserId;

        var updated = await _store.UpdateAsync(existing, cancellationToken);
        await RaiseAsync(new TeamUpdatedEvent
        {
            TenantId = tenantId,
            TeamId = updated.Id,
            TeamName = updated.Name,
            TeamCode = updated.Code,
            ActorUserId = updatedByUserId,
            ParentChanged = parentChanged,
            PreviousParentTeamId = parentChanged ? previousParentId : null,
        }, cancellationToken);
        return updated;
    }

    public async Task ArchiveAsync(
        string tenantId,
        string teamId,
        bool cascadeChildren,
        string? archivedByUserId,
        CancellationToken cancellationToken = default)
    {
        var team = await _store.GetByIdAsync(tenantId, teamId, cancellationToken);
        if (team == null) return;

        if (cascadeChildren)
        {
            // Archive the whole subtree so abandoned children don't dangle.
            var ids = await _store.GetDescendantIdsAsync(tenantId, teamId, cancellationToken);
            foreach (var id in ids)
            {
                var t = await _store.GetByIdAsync(tenantId, id, cancellationToken);
                if (t == null || !t.IsActive) continue;
                t.IsActive = false;
                t.UpdatedAt = DateTime.UtcNow;
                t.UpdatedBy = archivedByUserId;
                await _store.UpdateAsync(t, cancellationToken);
                await RaiseAsync(new TeamArchivedEvent
                {
                    TenantId = tenantId,
                    TeamId = t.Id,
                    TeamName = t.Name,
                    TeamCode = t.Code,
                    ActorUserId = archivedByUserId,
                }, cancellationToken);
            }
        }
        else
        {
            team.IsActive = false;
            team.UpdatedAt = DateTime.UtcNow;
            team.UpdatedBy = archivedByUserId;
            await _store.UpdateAsync(team, cancellationToken);
            await RaiseAsync(new TeamArchivedEvent
            {
                TenantId = tenantId,
                TeamId = team.Id,
                TeamName = team.Name,
                TeamCode = team.Code,
                ActorUserId = archivedByUserId,
            }, cancellationToken);
        }
    }

    public async Task RestoreAsync(
        string tenantId,
        string teamId,
        string? restoredByUserId,
        CancellationToken cancellationToken = default)
    {
        var team = await _store.GetByIdAsync(tenantId, teamId, cancellationToken);
        if (team == null || team.IsActive) return;

        team.IsActive = true;
        team.UpdatedAt = DateTime.UtcNow;
        team.UpdatedBy = restoredByUserId;
        await _store.UpdateAsync(team, cancellationToken);
        await RaiseAsync(new TeamRestoredEvent
        {
            TenantId = tenantId,
            TeamId = team.Id,
            TeamName = team.Name,
            TeamCode = team.Code,
            ActorUserId = restoredByUserId,
        }, cancellationToken);
    }

    public async Task DeleteAsync(
        string tenantId,
        string teamId,
        string? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        // Hard delete. Cascades to TeamMembership rows via the FK definition.
        // Parent FK is DeleteBehavior.Restrict — child teams must be re-parented
        // or deleted explicitly first.
        var children = await _store.GetChildTeamsAsync(tenantId, teamId, activeOnly: false, cancellationToken);
        if (children.Count > 0)
            throw new InvalidOperationException(
                $"Cannot delete team '{teamId}': it has {children.Count} child team(s). Re-parent or delete them first.");

        var team = await _store.GetByIdAsync(tenantId, teamId, cancellationToken);
        await _store.DeleteAsync(tenantId, teamId, cancellationToken);
        _logger.LogInformation("Team {TeamId} deleted by {UserId} in tenant {TenantId}",
            teamId, deletedByUserId, tenantId);

        if (team != null)
        {
            await RaiseAsync(new TeamDeletedEvent
            {
                TenantId = tenantId,
                TeamId = team.Id,
                TeamName = team.Name,
                TeamCode = team.Code,
                ActorUserId = deletedByUserId,
            }, cancellationToken);
        }
    }

    // ── Membership operations ────────────────────────────────────────────

    public async Task<TeamMembership> AddMemberAsync(
        string tenantId,
        string teamId,
        string userId,
        TeamMemberRole role,
        string? addedByUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetMembershipAsync(tenantId, teamId, userId, cancellationToken);
        if (existing != null)
        {
            // Reactivate or role-promote rather than insert a duplicate. Keeps
            // the membership ID stable so audit trails don't fork.
            var rejoined = !existing.IsActive;
            var previousRole = existing.Role;
            if (rejoined)
            {
                existing.IsActive = true;
                existing.LeftAt = null;
                existing.JoinedAt = DateTime.UtcNow;
            }
            existing.Role = role;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = addedByUserId;
            var reactivated = await _store.UpdateMembershipAsync(existing, cancellationToken);
            if (rejoined)
                await RaiseMemberAddedAsync(tenantId, teamId, userId, role, addedByUserId, cancellationToken);
            else if (previousRole != role)
                await RaiseRoleChangedAsync(tenantId, teamId, userId, previousRole, role, addedByUserId, cancellationToken);
            return reactivated;
        }

        var membership = new TeamMembership
        {
            TenantId = tenantId,
            TeamId = teamId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = addedByUserId,
        };
        var added = await _store.AddMemberAsync(membership, cancellationToken);
        await RaiseMemberAddedAsync(tenantId, teamId, userId, role, addedByUserId, cancellationToken);
        return added;
    }

    private async Task RaiseMemberAddedAsync(string tenantId, string teamId, string userId, TeamMemberRole role, string? actorUserId, CancellationToken cancellationToken)
    {
        var team = await _store.GetByIdAsync(tenantId, teamId, cancellationToken);
        await RaiseAsync(new TeamMemberAddedEvent
        {
            TenantId = tenantId,
            TeamId = teamId,
            TeamName = team?.Name ?? teamId,
            TeamCode = team?.Code,
            UserId = userId,
            MemberRole = role.ToString(),
            ActorUserId = actorUserId,
        }, cancellationToken);
    }

    public async Task<TeamMembership> ChangeMemberRoleAsync(
        string tenantId,
        string teamId,
        string userId,
        TeamMemberRole newRole,
        string? changedByUserId,
        CancellationToken cancellationToken = default)
    {
        var membership = await _store.GetMembershipAsync(tenantId, teamId, userId, cancellationToken)
            ?? throw new InvalidOperationException($"User '{userId}' is not a member of team '{teamId}'.");

        if (!membership.IsActive)
            throw new InvalidOperationException($"User '{userId}' is no longer a member of team '{teamId}'.");

        var previousRole = membership.Role;
        membership.Role = newRole;
        membership.UpdatedAt = DateTime.UtcNow;
        membership.UpdatedBy = changedByUserId;
        var updated = await _store.UpdateMembershipAsync(membership, cancellationToken);
        if (previousRole != newRole)
            await RaiseRoleChangedAsync(tenantId, teamId, userId, previousRole, newRole, changedByUserId, cancellationToken);
        return updated;
    }

    private async Task RaiseRoleChangedAsync(string tenantId, string teamId, string userId, TeamMemberRole previousRole, TeamMemberRole role,
        string? actorUserId, CancellationToken cancellationToken)
    {
        var team = await _store.GetByIdAsync(tenantId, teamId, cancellationToken);
        await RaiseAsync(new TeamMemberRoleChangedEvent
        {
            TenantId = tenantId,
            TeamId = teamId,
            TeamName = team?.Name ?? teamId,
            TeamCode = team?.Code,
            UserId = userId,
            MemberRole = role.ToString(),
            PreviousRole = previousRole.ToString(),
            ActorUserId = actorUserId,
        }, cancellationToken);
    }

    public async Task RemoveMemberAsync(
        string tenantId,
        string teamId,
        string userId,
        string? removedByUserId,
        CancellationToken cancellationToken = default)
    {
        var membership = await _store.GetMembershipAsync(tenantId, teamId, userId, cancellationToken);
        if (membership == null || !membership.IsActive) return;

        membership.IsActive = false;
        membership.LeftAt = DateTime.UtcNow;
        membership.UpdatedAt = DateTime.UtcNow;
        membership.UpdatedBy = removedByUserId;
        await _store.UpdateMembershipAsync(membership, cancellationToken);

        {
            var team = await _store.GetByIdAsync(tenantId, teamId, cancellationToken);
            await RaiseAsync(new TeamMemberRemovedEvent
            {
                TenantId = tenantId,
                TeamId = teamId,
                TeamName = team?.Name ?? teamId,
                TeamCode = team?.Code,
                UserId = userId,
                ActorUserId = removedByUserId,
            }, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<string>> GetEffectiveMemberUserIdsAsync(
        string tenantId,
        string teamId,
        bool includeDescendants,
        CancellationToken cancellationToken = default)
    {
        var teamIds = includeDescendants
            ? await _store.GetDescendantIdsAsync(tenantId, teamId, cancellationToken)
            : new[] { teamId };

        var userIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in teamIds)
        {
            var members = await _store.GetMembersAsync(tenantId, id, activeOnly: true, cancellationToken);
            foreach (var m in members)
            {
                userIds.Add(m.UserId);
            }
        }
        return userIds.ToList();
    }

    /// <summary>
    /// Walks up the proposed new parent's ancestor chain. If <paramref name="teamId"/>
    /// appears as an ancestor, setting it as the new parent would form a cycle.
    /// Bounded to a sane depth (50) to fail loudly if someone constructs a
    /// pathological hierarchy.
    /// </summary>
    private async Task GuardNoCycleAsync(
        string tenantId,
        string teamId,
        string newParentId,
        CancellationToken cancellationToken)
    {
        const int maxDepth = 50;
        var cursor = newParentId;
        for (var depth = 0; depth < maxDepth; depth++)
        {
            if (string.Equals(cursor, teamId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Cannot set parent of team '{teamId}' to '{newParentId}': would form a cycle.");

            var parent = await _store.GetByIdAsync(tenantId, cursor, cancellationToken);
            if (parent?.ParentTeamId == null) return;
            cursor = parent.ParentTeamId;
        }
        throw new InvalidOperationException(
            $"Team hierarchy depth exceeded {maxDepth} levels when checking for cycles. Refusing to update.");
    }
}
