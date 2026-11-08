using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Higher-level team operations that enforce invariants and raise events. Most
/// callers (controllers, importers) go through this rather than poking
/// <see cref="ITeamStore"/> directly so cycle-prevention, audit, and event
/// publishing all happen in one place.
/// </summary>
public interface ITeamService
{
    /// <summary>
    /// Creates a team. Validates that <see cref="Team.Code"/> is unique in the
    /// tenant and that the parent (if any) exists. Stamps audit fields.
    /// </summary>
    Task<Team> CreateAsync(
        string tenantId,
        Team team,
        string? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates team metadata. If <see cref="Team.ParentTeamId"/> changes, walks
    /// the proposed new lineage to reject cycles (A → B → A) and self-parenting.
    /// </summary>
    Task<Team> UpdateAsync(
        string tenantId,
        Team team,
        string? updatedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives the team (sets <see cref="Team.IsActive"/>=false). Pickers stop
    /// showing it, but historical references (approvals already-assigned to
    /// this team, billing subscriptions) keep resolving so reports stay intact.
    ///
    /// <para>If <paramref name="cascadeChildren"/> is true, archives all
    /// descendants too. Otherwise leaves children untouched (they become
    /// orphaned roots from a UI standpoint until the parent is restored).</para>
    /// </summary>
    Task ArchiveAsync(
        string tenantId,
        string teamId,
        bool cascadeChildren,
        string? archivedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-archived team. Doesn't auto-restore descendants — caller
    /// archives / restores them explicitly.
    /// </summary>
    Task RestoreAsync(
        string tenantId,
        string teamId,
        string? restoredByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes the team. Reserved for tenant-admin recovery; most
    /// teardowns should go through <see cref="ArchiveAsync"/>.
    /// </summary>
    Task DeleteAsync(
        string tenantId,
        string teamId,
        string? deletedByUserId,
        CancellationToken cancellationToken = default);

    // ── Membership operations ────────────────────────────────────────────

    /// <summary>
    /// Adds (or reactivates) a user as a member. Idempotent: if the user is
    /// already an active member, the existing membership is returned.
    /// </summary>
    Task<TeamMembership> AddMemberAsync(
        string tenantId,
        string teamId,
        string userId,
        TeamMemberRole role,
        string? addedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a member's role (Member ↔ Lead).</summary>
    Task<TeamMembership> ChangeMemberRoleAsync(
        string tenantId,
        string teamId,
        string userId,
        TeamMemberRole newRole,
        string? changedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-removes the user from the team. Historical attribution preserved;
    /// the membership row stays with <see cref="TeamMembership.LeftAt"/> set.
    /// </summary>
    Task RemoveMemberAsync(
        string tenantId,
        string teamId,
        string userId,
        string? removedByUserId,
        CancellationToken cancellationToken = default);

    // ── Resolution helpers used by other modules ─────────────────────────

    /// <summary>
    /// All active user IDs in the team and (optionally) its descendants.
    /// Used by approval resolution and billing entitlement checks.
    ///
    /// <para><paramref name="includeDescendants"/>=true expands the subtree —
    /// "all of Finance and its branch sub-teams". =false returns direct members
    /// only.</para>
    /// </summary>
    Task<IReadOnlyList<string>> GetEffectiveMemberUserIdsAsync(
        string tenantId,
        string teamId,
        bool includeDescendants,
        CancellationToken cancellationToken = default);
}
