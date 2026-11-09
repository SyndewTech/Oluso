namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Tenant-scoped group of users. The platform's canonical "addressable set of
/// people" primitive — used by approval workflows (team-as-approver), billing
/// (team-as-subscriber), notifications, and any module that needs to target a
/// group rather than a single user or a role.
///
/// <para>Teams are intentionally lightweight: no permission semantics (that's
/// Roles), no formal org-chart semantics (that's a dedicated org-structure module),
/// no finance semantics (that's CostCenter / OrganizationUnit). A team is
/// just "these people work together for some purpose."</para>
///
/// <para>Supports unlimited nesting via <see cref="ParentTeamId"/>. The service
/// layer enforces no-cycles. Transitive membership resolution (used by approvals
/// and entitlement checks) walks down from a team into its descendants.</para>
/// </summary>
public class Team : AuditableTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Stable user-facing identifier (e.g., <c>FINANCE</c>, <c>AUDIT-LAGOS</c>).
    /// Unique per tenant. Used by external integrations and seed scripts so
    /// they don't have to know the generated GUID.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TeamType Type { get; set; } = TeamType.Functional;

    /// <summary>
    /// Self-FK enabling n-level deep team hierarchies. A child team's transitive
    /// members are visible when resolving the parent for approvals or billing
    /// entitlement. Service rejects cycles at write time.
    /// </summary>
    public string? ParentTeamId { get; set; }
    public Team? ParentTeam { get; set; }
    public List<Team> ChildTeams { get; set; } = new();

    /// <summary>
    /// User who manages the team — can edit metadata and add / remove members
    /// without holding the tenant-admin permission. Optional: many teams are
    /// admin-managed and have no designated owner.
    /// </summary>
    public string? OwnerUserId { get; set; }

    public TeamVisibility Visibility { get; set; } = TeamVisibility.TenantWide;

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    /// <summary>
    /// The module that generates and maintains this team (its feature key, e.g.
    /// <c>education</c>), or null for a team people manage by hand. A managed
    /// team is a projection of the module's own records: the module creates it,
    /// renames it, archives it and keeps its members in sync, and the Teams
    /// admin shows it read-only. Generic on purpose: any module may manage teams,
    /// and consumers of teams (approvals, chat, billing) need no knowledge of it.
    /// </summary>
    public string? ManagedBy { get; set; }

    /// <summary>True when a module maintains this team (see <see cref="ManagedBy"/>).</summary>
    public bool IsManaged => !string.IsNullOrEmpty(ManagedBy);

    public List<TeamMembership> Members { get; set; } = new();
}

/// <summary>
/// Membership of an <see cref="OlusoUser"/> in a <see cref="Team"/>. A user can
/// belong to many teams; a team has many users. Junction-table semantics with
/// extra metadata (role within the team, join / leave dates).
/// </summary>
public class TeamMembership : AuditableTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string TeamId { get; set; } = string.Empty;
    public Team? Team { get; set; }

    /// <summary>FK to <see cref="OlusoUser.Id"/>.</summary>
    public string UserId { get; set; } = string.Empty;

    public TeamMemberRole Role { get; set; } = TeamMemberRole.Member;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }

    /// <summary>
    /// False after the member leaves. Kept rather than hard-deleted so we can
    /// still attribute historical approvals / postings to the right person and
    /// audit who was in the team at any point in time.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
