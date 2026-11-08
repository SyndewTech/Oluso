using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;

namespace Oluso.EntityFramework.Interceptors;

/// <summary>
/// Keeps <see cref="TenantMembership"/> — the single source of truth for tenant
/// access — in step with the writes that imply it:
/// <list type="bullet">
///   <item>every new user gets a membership for their home tenant;</item>
///   <item>a tenant role assignment gives the user a membership in that tenant.</item>
/// </list>
/// <para>
/// Only a handful of write paths ever created memberships themselves. Users
/// arriving via SCIM, LDAP, self-registration, the journey handlers or a seeder
/// had none, and role grants (admin Users page, module roles, SCIM, invitations)
/// could leave a user holding a role in a tenant they couldn't reach. Sitting at
/// SaveChanges rather than in UserManager or a store is deliberate: it covers
/// every path that adds users or assignments straight to the DbContext.
/// </para>
/// <para>
/// Global roles (SuperAdmin, SystemAdmin) and OrgAdmin are platform-scoped, not
/// tenant roles, so they never create a membership.
/// </para>
/// </summary>
public sealed class TenantMembershipInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            var candidates = CollectCandidates(context, out var roleIds);
            if (candidates.Count > 0)
            {
                var platformRoleIds = PlatformRoleIds(context.Set<OlusoRole>().IgnoreQueryFilters()
                    .Where(r => roleIds.Contains(r.Id))
                    .Select(r => new RoleRef(r.Id, r.TenantId, r.Name, r.NormalizedName))
                    .ToList());
                var wanted = Wanted(candidates, platformRoleIds);
                var existing = wanted.Count == 0 ? new HashSet<(string, string)>() : ExistingQuery(context, wanted)
                    .Select(m => new { m.TenantId, m.UserId }).ToList()
                    .Select(m => (m.TenantId, m.UserId)).ToHashSet();
                AddMissing(context, wanted, existing);
            }
        }
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var candidates = CollectCandidates(context, out var roleIds);
            if (candidates.Count > 0)
            {
                var platformRoleIds = PlatformRoleIds(await context.Set<OlusoRole>().IgnoreQueryFilters()
                    .Where(r => roleIds.Contains(r.Id))
                    .Select(r => new RoleRef(r.Id, r.TenantId, r.Name, r.NormalizedName))
                    .ToListAsync(cancellationToken));
                var wanted = Wanted(candidates, platformRoleIds);
                var existing = wanted.Count == 0 ? new HashSet<(string, string)>() : (await ExistingQuery(context, wanted)
                        .Select(m => new { m.TenantId, m.UserId }).ToListAsync(cancellationToken))
                    .Select(m => (m.TenantId, m.UserId)).ToHashSet();
                AddMissing(context, wanted, existing);
            }
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed record RoleRef(string Id, string? TenantId, string? Name, string? NormalizedName);

    private sealed record Candidate(string TenantId, string UserId, string? RoleId);

    /// <summary>New users (home tenant) and new role assignments (assignment tenant).</summary>
    private static List<Candidate> CollectCandidates(DbContext context, out List<string> roleIds)
    {
        var candidates = context.ChangeTracker.Entries<OlusoUser>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(u => !string.IsNullOrEmpty(u.TenantId) && !string.IsNullOrEmpty(u.Id))
            .Select(u => new Candidate(u.TenantId!, u.Id, null))
            .ToList();

        candidates.AddRange(context.ChangeTracker.Entries<OlusoUserRole>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(ur => !string.IsNullOrEmpty(ur.TenantId) && !string.IsNullOrEmpty(ur.UserId))
            .Select(ur => new Candidate(ur.TenantId, ur.UserId, ur.RoleId)));

        roleIds = candidates.Where(c => c.RoleId != null).Select(c => c.RoleId!).Distinct().ToList();
        return candidates;
    }

    private static HashSet<string> PlatformRoleIds(IEnumerable<RoleRef> roles) =>
        roles.Where(r => r.TenantId == null
                && (GlobalRoles.IsGlobalName(r.NormalizedName ?? r.Name?.ToUpperInvariant())
                    || string.Equals(r.Name, EffectiveRoleResolver.OrgAdminRoleName, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);

    private static List<(string TenantId, string UserId)> Wanted(List<Candidate> candidates, HashSet<string> platformRoleIds) =>
        candidates
            .Where(c => c.RoleId == null || !platformRoleIds.Contains(c.RoleId))
            .Select(c => (c.TenantId, c.UserId))
            .Distinct()
            .ToList();

    private static IQueryable<TenantMembership> ExistingQuery(DbContext context, List<(string TenantId, string UserId)> wanted)
    {
        var tenantIds = wanted.Select(w => w.TenantId).Distinct().ToList();
        var userIds = wanted.Select(w => w.UserId).Distinct().ToList();
        return context.Set<TenantMembership>()
            .Where(m => tenantIds.Contains(m.TenantId) && userIds.Contains(m.UserId));
    }

    private static void AddMissing(DbContext context, List<(string TenantId, string UserId)> wanted, HashSet<(string, string)> existing)
    {
        // A caller may have queued the membership itself in this same unit of
        // work; adding a second would break the (TenantId, UserId) unique index.
        var pending = context.ChangeTracker.Entries<TenantMembership>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => (e.Entity.TenantId, e.Entity.UserId))
            .ToHashSet();

        foreach (var (tenantId, userId) in wanted)
        {
            if (pending.Contains((tenantId, userId)) || existing.Contains((tenantId, userId))) continue;

            context.Add(new TenantMembership
            {
                TenantId = tenantId,
                UserId = userId,
                InvitedByUserId = "system"
            });
        }
    }
}
