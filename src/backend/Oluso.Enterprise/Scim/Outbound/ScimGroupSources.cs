using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// The two tenant primitives a SCIM Group can be built from. Roles carry
/// permissions; teams are "these people work together". A provider chooses
/// which (or both) it wants pushed as groups.
/// </summary>
public static class ScimGroupKinds
{
    public const string Role = "role";
    public const string Team = "team";

    /// <summary>
    /// Queue and mapping key for a group: <c>role:&lt;id&gt;</c> or <c>team:&lt;id&gt;</c>.
    /// Also used as the remote <c>externalId</c>, so lookups stay unambiguous
    /// when both kinds are pushed to the same provider.
    /// </summary>
    public static string Key(string kind, string id) => $"{kind}:{id}";

    public static bool TryParse(string key, out string kind, out string id)
    {
        kind = string.Empty;
        id = string.Empty;
        var separator = key.IndexOf(':');
        if (separator <= 0 || separator == key.Length - 1)
            return false;

        kind = key[..separator];
        id = key[(separator + 1)..];
        return kind is Role or Team;
    }
}

public static class ScimProviderGroupSources
{
    public const string Roles = "roles";
    public const string Teams = "teams";
    public const string Both = "both";

    public static readonly string[] All = { Roles, Teams, Both };

    public static IReadOnlyList<string> KindsFor(string? source) => source?.ToLowerInvariant() switch
    {
        Teams => new[] { ScimGroupKinds.Team },
        Both => new[] { ScimGroupKinds.Role, ScimGroupKinds.Team },
        _ => new[] { ScimGroupKinds.Role },
    };
}

public sealed record ScimGroupInfo(string Kind, string Id, string DisplayName)
{
    public string Key => ScimGroupKinds.Key(Kind, Id);

    /// <summary>
    /// Where the group sits in a hierarchy, for flavors that send it (<see cref="ScimProviderFlavorProfile.SendsTeamExtension"/>).
    /// Only <see cref="ScimGroupSourceRegistry"/> sources with a hierarchy fill it, and only in <see cref="IScimGroupSource.GetAsync"/>.
    /// </summary>
    public ScimGroupHierarchy? Hierarchy { get; init; }
}

/// <summary>
/// A group's place in its source's hierarchy, in internal ids: its stable code, the queue key of its parent group (null
/// at the top), the users who lead it directly (a subset of its members) and the module that manages it, if any.
/// </summary>
public sealed record ScimGroupHierarchy(string? Code, string? ParentKey, IReadOnlyList<string> LeadUserIds, string? ManagedBy);

/// <summary>
/// Reads one kind of group from the tenant. Everything the group pipeline
/// needs to push a Group resource, with no knowledge of roles or teams
/// leaking into the pipeline itself.
/// </summary>
public interface IScimGroupSource
{
    string Kind { get; }

    /// <summary>The group, or null when it does not exist, is archived, or belongs to another tenant.</summary>
    Task<ScimGroupInfo?> GetAsync(string tenantId, string id, CancellationToken cancellationToken = default);

    /// <summary>Every live group of this kind in the tenant.</summary>
    Task<IReadOnlyList<ScimGroupInfo>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>User ids that should be members of the remote group.</summary>
    Task<IReadOnlyList<string>> GetMemberUserIdsAsync(string tenantId, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of every group of this kind whose remote membership includes the
    /// user, so they can be refreshed when the user changes.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserGroupIdsAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
}

public sealed class RoleScimGroupSource : IScimGroupSource
{
    private readonly IRoleStore _roles;

    public RoleScimGroupSource(IRoleStore roles)
    {
        _roles = roles;
    }

    public string Kind => ScimGroupKinds.Role;

    public async Task<ScimGroupInfo?> GetAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var role = await _roles.GetByIdAsync(id, cancellationToken);
        if (role == null || !string.Equals(role.TenantId, tenantId, StringComparison.Ordinal))
            return null;

        return new ScimGroupInfo(Kind, role.Id, DisplayNameOf(role.DisplayName, role.Name, role.Id));
    }

    public async Task<IReadOnlyList<ScimGroupInfo>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var roles = await _roles.GetRolesAsync(tenantId, includeSystem: true, cancellationToken);
        return roles
            .Where(r => string.Equals(r.TenantId, tenantId, StringComparison.Ordinal))
            .Select(r => new ScimGroupInfo(Kind, r.Id, DisplayNameOf(r.DisplayName, r.Name, r.Id)))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetMemberUserIdsAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var users = await _roles.GetUsersByRoleInTenantAsync(id, tenantId, cancellationToken);
        return users
            .Where(u => string.Equals(u.TenantId, tenantId, StringComparison.Ordinal))
            .Select(u => u.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetUserGroupIdsAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var roles = await _roles.GetUserRolesInTenantAsync(userId, tenantId, cancellationToken);
        return roles
            .Where(r => string.Equals(r.TenantId, tenantId, StringComparison.Ordinal))
            .Select(r => r.Id)
            .ToList();
    }

    private static string DisplayNameOf(string? displayName, string? name, string id) =>
        !string.IsNullOrWhiteSpace(displayName) ? displayName : (!string.IsNullOrWhiteSpace(name) ? name : id);
}

/// <summary>
/// Teams as groups. Membership is the effective membership: direct members
/// plus everyone in descendant teams, since SCIM targets such as mail
/// servers have no nested groups and "all of Finance" is what a Finance
/// group is expected to mean.
/// </summary>
public sealed class TeamScimGroupSource : IScimGroupSource
{
    private readonly ITeamStore _teams;
    private readonly ITeamService _teamService;

    public TeamScimGroupSource(ITeamStore teams, ITeamService teamService)
    {
        _teams = teams;
        _teamService = teamService;
    }

    public string Kind => ScimGroupKinds.Team;

    public async Task<ScimGroupInfo?> GetAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var team = await _teams.GetByIdAsync(tenantId, id, cancellationToken);
        if (team == null || !team.IsActive)
            return null;

        // Leads of this team itself: a lead of a sub-team is an effective member here, not a lead.
        var leads = team.Members
            .Where(m => m.IsActive && m.Role == TeamMemberRole.Lead)
            .Select(m => m.UserId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();
        var parentKey = string.IsNullOrEmpty(team.ParentTeamId) ? null : ScimGroupKinds.Key(Kind, team.ParentTeamId);
        return new ScimGroupInfo(Kind, team.Id, team.Name)
        {
            Hierarchy = new ScimGroupHierarchy(team.Code, parentKey, leads, team.ManagedBy),
        };
    }

    public async Task<IReadOnlyList<ScimGroupInfo>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var teams = await _teams.GetPickerOptionsAsync(tenantId, type: null, activeOnly: true, cancellationToken);
        return teams.Select(t => new ScimGroupInfo(Kind, t.Id, t.Name)).ToList();
    }

    public Task<IReadOnlyList<string>> GetMemberUserIdsAsync(string tenantId, string id, CancellationToken cancellationToken = default) =>
        _teamService.GetEffectiveMemberUserIdsAsync(tenantId, id, includeDescendants: true, cancellationToken);

    /// <summary>
    /// Direct teams plus their ancestors, because the user is an effective
    /// member of every team above the ones they belong to.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetUserGroupIdsAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var direct = await _teams.GetUserTeamsAsync(tenantId, userId, activeOnly: true, cancellationToken);
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var team in direct)
        {
            result.Add(team.Id);
            var parentId = team.ParentTeamId;
            var guard = 0;
            while (!string.IsNullOrEmpty(parentId) && result.Add(parentId) && guard++ < 64)
            {
                var parent = await _teams.GetByIdAsync(tenantId, parentId, cancellationToken);
                if (parent == null || !parent.IsActive)
                {
                    result.Remove(parentId);
                    break;
                }

                parentId = parent.ParentTeamId;
            }
        }

        return result.ToList();
    }
}

/// <summary>
/// Narrows which groups of a kind a provider pushes, beyond its configured scope and without narrowing who is
/// provisioned (a team scope narrows both). A module that owns a provider registers one, for example to push
/// only the teams an admin linked to a workspace or room. Filters are asked when a group is pushed and at reconcile,
/// so a group that leaves the allowed set is deleted from the provider.
/// </summary>
public interface IScimGroupFilter
{
    /// <summary>
    /// The ids of <paramref name="kind"/> the provider may push, or null when this filter has no say for the provider
    /// or the kind.
    /// </summary>
    Task<IReadOnlySet<string>?> AllowedAsync(ScimProvider provider, string kind, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves sources by kind and answers the provider-scoped questions the
/// pipeline asks: is this kind enabled, which ids are in scope, which of a
/// user's groups should be refreshed.
/// </summary>
public sealed class ScimGroupSourceRegistry
{
    private readonly IReadOnlyDictionary<string, IScimGroupSource> _sources;
    private readonly IReadOnlyList<IScimGroupFilter> _filters;

    public ScimGroupSourceRegistry(IEnumerable<IScimGroupSource> sources, IEnumerable<IScimGroupFilter>? filters = null)
    {
        _sources = sources.ToDictionary(s => s.Kind, StringComparer.OrdinalIgnoreCase);
        _filters = filters?.ToList() ?? new List<IScimGroupFilter>();
    }

    public IScimGroupSource Get(string kind) =>
        _sources.TryGetValue(kind, out var source)
            ? source
            : throw new ScimOutboundMappingException($"No group source registered for kind '{kind}'");

    public IReadOnlyList<IScimGroupSource> EnabledFor(ScimProvider provider) =>
        provider.SyncGroups
            ? ScimProviderGroupSources.KindsFor(provider.GroupSource).Select(Get).ToList()
            : Array.Empty<IScimGroupSource>();

    public bool IsKindEnabled(ScimProvider provider, string kind) =>
        provider.SyncGroups && ScimProviderGroupSources.KindsFor(provider.GroupSource).Contains(kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>Scope ids configured on the provider for this kind; empty means every group of the kind.</summary>
    public static IReadOnlyList<string> ScopeFor(ScimProvider provider, string kind) =>
        kind == ScimGroupKinds.Team ? provider.GetScopeTeamIds() : provider.GetScopeRoleIds();

    public static bool IsGroupInScope(ScimProvider provider, string kind, string id)
    {
        var scope = ScopeFor(provider, kind);
        return scope.Count == 0 || scope.Contains(id, StringComparer.Ordinal);
    }

    /// <summary>
    /// The ids of <paramref name="kind"/> every registered <see cref="IScimGroupFilter"/> allows for the provider (their
    /// intersection), or null when none has a say. Applies on top of <see cref="IsGroupInScope"/>.
    /// </summary>
    public async Task<IReadOnlySet<string>?> FilterAsync(ScimProvider provider, string kind, CancellationToken cancellationToken = default)
    {
        HashSet<string>? allowed = null;
        foreach (var filter in _filters)
        {
            var ids = await filter.AllowedAsync(provider, kind, cancellationToken);
            if (ids == null)
                continue;
            if (allowed == null)
                allowed = new HashSet<string>(ids, StringComparer.Ordinal);
            else
                allowed.IntersectWith(ids);
        }
        return allowed;
    }

    /// <summary>Whether the provider pushes this group: inside its scope and allowed by every filter.</summary>
    public async Task<bool> IsGroupPushedAsync(ScimProvider provider, string kind, string id, CancellationToken cancellationToken = default)
    {
        if (!IsGroupInScope(provider, kind, id))
            return false;
        var allowed = await FilterAsync(provider, kind, cancellationToken);
        return allowed == null || allowed.Contains(id);
    }
}
