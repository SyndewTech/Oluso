using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Read / write access to teams + memberships. Single store rather than two
/// because teams and memberships always travel together at the query layer
/// (e.g., "give me a team with its members", "give me a user's teams").
///
/// <para>Implemented in <c>Oluso.EntityFramework</c>. Consumers in other
/// modules depend on this interface, not the concrete EF type.</para>
/// </summary>
public interface ITeamStore
{
    // ── Team CRUD ────────────────────────────────────────────────────────

    Task<Team?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken = default);
    Task<Team?> GetByCodeAsync(string tenantId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated list with optional filters. Mirrors the QueryPagedAsync shape
    /// used elsewhere — the FE list page reuses the same paginated-list pattern.
    /// </summary>
    Task<(IReadOnlyList<Team> Items, int TotalCount)> QueryAsync(
        string tenantId,
        TeamQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight picker payload — id + code + name + type, no members. Used
    /// by every team-picker dropdown across the platform.
    /// </summary>
    Task<IReadOnlyList<TeamSummary>> GetPickerOptionsAsync(
        string tenantId,
        TeamType? type = null,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<Team> CreateAsync(Team team, CancellationToken cancellationToken = default);
    Task<Team> UpdateAsync(Team team, CancellationToken cancellationToken = default);
    Task DeleteAsync(string tenantId, string id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(
        string tenantId,
        string code,
        string? excludeId = null,
        CancellationToken cancellationToken = default);

    // ── Hierarchy ────────────────────────────────────────────────────────

    /// <summary>
    /// Direct child teams. Use <see cref="GetDescendantIdsAsync"/> when you
    /// need transitive descendants for membership resolution.
    /// </summary>
    Task<IReadOnlyList<Team>> GetChildTeamsAsync(
        string tenantId,
        string parentTeamId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All descendant team IDs (transitive). Includes <paramref name="rootTeamId"/>
    /// itself. Walks recursively, stops at archived teams. Used by approval
    /// resolution and entitlement checks for "all members of team-and-its-subtree".
    /// </summary>
    Task<IReadOnlyList<string>> GetDescendantIdsAsync(
        string tenantId,
        string rootTeamId,
        CancellationToken cancellationToken = default);

    // ── Membership ───────────────────────────────────────────────────────

    Task<IReadOnlyList<TeamMembership>> GetMembersAsync(
        string tenantId,
        string teamId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All teams the user is a (direct) member of. Doesn't expand parent teams —
    /// that's the caller's job via <see cref="GetDescendantIdsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Team>> GetUserTeamsAsync(
        string tenantId,
        string userId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<TeamMembership?> GetMembershipAsync(
        string tenantId,
        string teamId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<TeamMembership> AddMemberAsync(TeamMembership membership, CancellationToken cancellationToken = default);
    Task<TeamMembership> UpdateMembershipAsync(TeamMembership membership, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-removes the member (sets <see cref="TeamMembership.IsActive"/>=false
    /// and stamps <see cref="TeamMembership.LeftAt"/>). Historical attribution
    /// remains intact.
    /// </summary>
    Task RemoveMemberAsync(
        string tenantId,
        string teamId,
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>Filter shape for <see cref="ITeamStore.QueryAsync"/>.</summary>
public sealed class TeamQuery
{
    public TeamType? Type { get; init; }
    public string? ParentTeamId { get; init; }
    public bool? IsActive { get; init; }
    public string? Search { get; init; }

    /// <summary>
    /// If set, narrows results to teams the given user can see (TenantWide +
    /// any Private team they're a member of). Tenant admins should pass null.
    /// </summary>
    public string? VisibleToUserId { get; init; }

    public int Skip { get; init; } = 0;
    public int Take { get; init; } = 50;
}

/// <summary>Lightweight picker projection — no children, no members.</summary>
public sealed record TeamSummary(
    string Id,
    string Code,
    string Name,
    TeamType Type,
    string? ParentTeamId,
    bool IsActive);
