using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// EF-backed implementation of <see cref="ITeamStore"/>. Lives in
/// Oluso.EntityFramework rather than alongside the entity itself because Core
/// stays persistence-agnostic — only Core *interfaces* are referenced by other
/// modules.
/// </summary>
public class TeamStore : ITeamStore
{
    private readonly IOlusoDbContext _context;

    public TeamStore(IOlusoDbContext context)
    {
        _context = context;
    }

    // ── Team CRUD ────────────────────────────────────────────────────────

    public async Task<Team?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);
    }

    public async Task<Team?> GetByCodeAsync(string tenantId, string code, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Code == code, cancellationToken);
    }

    public async Task<(IReadOnlyList<Team> Items, int TotalCount)> QueryAsync(
        string tenantId,
        TeamQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Teams.Where(t => t.TenantId == tenantId);

        if (query.Type.HasValue) q = q.Where(t => t.Type == query.Type.Value);
        if (query.ParentTeamId != null) q = q.Where(t => t.ParentTeamId == query.ParentTeamId);
        if (query.IsActive.HasValue) q = q.Where(t => t.IsActive == query.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(t => t.Code.ToLower().Contains(s)
                          || t.Name.ToLower().Contains(s)
                          || (t.Description != null && t.Description.ToLower().Contains(s)));
        }

        // Visibility filter: tenant admins pass null and see everything. Regular
        // users see TenantWide + Private teams they're members of.
        if (!string.IsNullOrEmpty(query.VisibleToUserId))
        {
            var userId = query.VisibleToUserId;
            q = q.Where(t => t.Visibility == TeamVisibility.TenantWide
                          || t.Members.Any(m => m.UserId == userId && m.IsActive));
        }

        var totalCount = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(t => t.DisplayOrder)
            .ThenBy(t => t.Name)
            .Skip(query.Skip)
            .Take(query.Take)
            // Include memberships so the DTO mapper can compute
            // MemberCount. AsSplitQuery keeps the JOIN explosion off the
            // page query — a second query fetches the memberships keyed
            // on the page's team ids.
            .Include(t => t.Members)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<TeamSummary>> GetPickerOptionsAsync(
        string tenantId,
        TeamType? type = null,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Teams.Where(t => t.TenantId == tenantId);
        if (type.HasValue) q = q.Where(t => t.Type == type.Value);
        if (activeOnly) q = q.Where(t => t.IsActive);

        return await q
            .OrderBy(t => t.Name)
            .Select(t => new TeamSummary(t.Id, t.Code, t.Name, t.Type, t.ParentTeamId, t.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<Team> CreateAsync(Team team, CancellationToken cancellationToken = default)
    {
        _context.Teams.Add(team);
        await ((DbContext)_context).SaveChangesAsync(cancellationToken);
        return team;
    }

    public async Task<Team> UpdateAsync(Team team, CancellationToken cancellationToken = default)
    {
        _context.Teams.Update(team);
        await ((DbContext)_context).SaveChangesAsync(cancellationToken);
        return team;
    }

    public async Task DeleteAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var team = await _context.Teams
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);
        if (team == null) return;

        _context.Teams.Remove(team);
        await ((DbContext)_context).SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(
        string tenantId,
        string code,
        string? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams.AnyAsync(
            t => t.TenantId == tenantId
              && t.Code == code
              && (excludeId == null || t.Id != excludeId),
            cancellationToken);
    }

    // ── Hierarchy ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Team>> GetChildTeamsAsync(
        string tenantId,
        string parentTeamId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Teams.Where(t => t.TenantId == tenantId && t.ParentTeamId == parentTeamId);
        if (activeOnly) q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.Name).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Walks the subtree iteratively (no recursion-stack risk on deep
    /// hierarchies). Stops descending into archived branches — keeps "Audit
    /// approvers" sensible after a sub-team is retired.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetDescendantIdsAsync(
        string tenantId,
        string rootTeamId,
        CancellationToken cancellationToken = default)
    {
        // Pull all active teams in the tenant once; walk the parent map in
        // memory. For tenants with <~10k teams this is cheaper than N recursive
        // round-trips and avoids provider-specific CTE syntax differences.
        var allActive = await _context.Teams
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .Select(t => new { t.Id, t.ParentTeamId })
            .ToListAsync(cancellationToken);

        var childrenByParent = allActive
            .Where(t => t.ParentTeamId != null)
            .GroupBy(t => t.ParentTeamId!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var result = new List<string> { rootTeamId };
        var stack = new Stack<string>();
        stack.Push(rootTeamId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenByParent.TryGetValue(current, out var children)) continue;
            foreach (var childId in children)
            {
                result.Add(childId);
                stack.Push(childId);
            }
        }

        return result;
    }

    // ── Membership ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TeamMembership>> GetMembersAsync(
        string tenantId,
        string teamId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var q = _context.TeamMemberships
            .Where(m => m.TenantId == tenantId && m.TeamId == teamId);
        if (activeOnly) q = q.Where(m => m.IsActive);
        return await q.OrderBy(m => m.JoinedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Team>> GetUserTeamsAsync(
        string tenantId,
        string userId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var q = _context.TeamMemberships.Where(m => m.TenantId == tenantId && m.UserId == userId);
        if (activeOnly) q = q.Where(m => m.IsActive);

        return await q
            .Join(_context.Teams, m => m.TeamId, t => t.Id, (m, t) => t)
            .Where(t => !activeOnly || t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<TeamMembership?> GetMembershipAsync(
        string tenantId,
        string teamId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TeamMemberships
            .FirstOrDefaultAsync(
                m => m.TenantId == tenantId && m.TeamId == teamId && m.UserId == userId,
                cancellationToken);
    }

    public async Task<TeamMembership> AddMemberAsync(TeamMembership membership, CancellationToken cancellationToken = default)
    {
        _context.TeamMemberships.Add(membership);
        await ((DbContext)_context).SaveChangesAsync(cancellationToken);
        return membership;
    }

    public async Task<TeamMembership> UpdateMembershipAsync(TeamMembership membership, CancellationToken cancellationToken = default)
    {
        _context.TeamMemberships.Update(membership);
        await ((DbContext)_context).SaveChangesAsync(cancellationToken);
        return membership;
    }

    public async Task RemoveMemberAsync(
        string tenantId,
        string teamId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(tenantId, teamId, userId, cancellationToken);
        if (membership == null || !membership.IsActive) return;

        membership.IsActive = false;
        membership.LeftAt = DateTime.UtcNow;
        membership.UpdatedAt = DateTime.UtcNow;
        await ((DbContext)_context).SaveChangesAsync(cancellationToken);
    }
}
