namespace Oluso.Admin.Controllers.Service;

/// <summary>
/// A single grantable Service API permission, with the copy the admin UI
/// renders for it. The catalog is the source of truth for both the checkbox
/// list and the <see cref="Oluso.Core.Domain.Entities.ApiScope"/> rows that
/// get provisioned when a client is granted service access.
/// </summary>
/// <param name="Name">The scope string carried in the access token.</param>
/// <param name="DisplayName">Short label.</param>
/// <param name="Description">What the scope lets the client do.</param>
/// <param name="Group">Grouping key for the UI (e.g. "Users").</param>
/// <param name="RequiredAdminPermission">
/// Set on a sensitive scope: the admin permission, itself granted by name only,
/// an admin must hold to give a client this scope. Never pre-selected in the UI.
/// </param>
public sealed record ServiceScopeDefinition(
    string Name,
    string DisplayName,
    string Description,
    string Group,
    string? RequiredAdminPermission = null)
{
    public bool RequiresExplicitGrant => RequiredAdminPermission != null;
}

/// <summary>
/// Scopes required for Service API access (<c>api/service/*</c>), granted to
/// clients using the <c>client_credentials</c> grant.
///
/// Every name is namespaced under <see cref="ReservedPrefix"/>. The prefix is
/// reserved: admins cannot hand-create scopes in it (see ApiScopesController),
/// because an unqualified name like "service" could otherwise be minted by a
/// tenant for their own API and would then satisfy the ServiceApi policy.
///
/// Tenant context comes from client registration, not request headers.
/// </summary>
public static class ServiceScopes
{
    /// <summary>
    /// Namespace owned by the platform. Scopes under it are provisioned and
    /// managed by Oluso, never by hand.
    /// </summary>
    public const string ReservedPrefix = "oluso:";

    /// <summary>
    /// Base scope every Service API call requires — the gate enforced by the
    /// "ServiceApi" authorization policy. Granular scopes below decide which
    /// endpoints within that surface the client may call.
    /// </summary>
    public const string ServiceApi = "oluso:service";

    // User management scopes
    public const string UsersRead = "oluso:users:read";
    public const string UsersWrite = "oluso:users:write";
    public const string UsersDelete = "oluso:users:delete";
    public const string UsersManageRoles = "oluso:users:manage-roles";

    // Role management scopes
    public const string RolesRead = "oluso:roles:read";
    public const string RolesWrite = "oluso:roles:write";
    public const string RolesDelete = "oluso:roles:delete";

    // Conversation thread scopes (comments on approvals and other entities)
    public const string ConversationsRead = "oluso:conversations:read";
    public const string ConversationsWrite = "oluso:conversations:write";

    private static readonly ServiceScopeDefinition[] BuiltIn =
    {
        new ServiceScopeDefinition(UsersRead, "Read users",
            "List users and read their profiles, roles, and claims.", "Users"),
        new ServiceScopeDefinition(UsersWrite, "Create and update users",
            "Create users, update profiles, and change activation state.", "Users"),
        new ServiceScopeDefinition(UsersDelete, "Delete users",
            "Permanently delete user accounts in the tenant. Deleted accounts can't be recovered.", "Users",
            RequiredAdminPermission: Authorization.AdminPermissions.UsersDelete),
        new ServiceScopeDefinition(UsersManageRoles, "Manage user roles",
            "Assign and remove roles on a user.", "Users"),
        new ServiceScopeDefinition(RolesRead, "Read roles",
            "List roles and read their claims and members.", "Roles"),
        new ServiceScopeDefinition(RolesWrite, "Create and update roles",
            "Create roles and edit their names, descriptions, and claims.", "Roles"),
        new ServiceScopeDefinition(RolesDelete, "Delete roles",
            "Delete roles from the tenant.", "Roles"),
        new ServiceScopeDefinition(ConversationsRead, "Read conversations",
            "Read the comment threads on approval requests and other records, including deleted comments.", "Conversations"),
        new ServiceScopeDefinition(ConversationsWrite, "Write conversations as users",
            "Post, edit and delete comments on behalf of a named user who is allowed on the thread.", "Conversations"),
    };

    private static readonly object RegistrationLock = new();
    private static IReadOnlyList<ServiceScopeDefinition> _catalog = BuiltIn;
    private static IReadOnlyList<string> _allNames = Names(BuiltIn);

    /// <summary>
    /// Every granular permission a client can be granted, in display order:
    /// the platform's own, then those modules registered. Only scopes backed
    /// by a live endpoint appear here — an entry with no controller behind it
    /// would render as a checkbox that grants nothing.
    /// </summary>
    public static IReadOnlyList<ServiceScopeDefinition> Catalog => Volatile.Read(ref _catalog);

    /// <summary>
    /// The base scope plus every granular scope — the full set Oluso manages.
    /// </summary>
    public static IReadOnlyList<string> AllNames => Volatile.Read(ref _allNames);

    /// <summary>
    /// Adds a module's Service API scopes to the catalog, so clients can be
    /// granted them and they are provisioned like the platform's. Called from a
    /// module's service registration; registering the same definition again is a
    /// no-op. Names must sit in <see cref="ReservedPrefix"/>, and a name already
    /// defined differently is refused, so a module can't redefine another's scope.
    /// </summary>
    public static void Register(params ServiceScopeDefinition[] definitions)
    {
        lock (RegistrationLock)
        {
            var catalog = _catalog.ToList();
            foreach (var definition in definitions)
            {
                if (!IsReserved(definition.Name) || string.Equals(definition.Name, ServiceApi, StringComparison.Ordinal))
                    throw new ArgumentException($"Service scope '{definition.Name}' must be a granular scope under '{ReservedPrefix}'.", nameof(definitions));

                var existing = catalog.FirstOrDefault(d => string.Equals(d.Name, definition.Name, StringComparison.Ordinal));
                if (existing == definition) continue;
                if (existing != null)
                    throw new InvalidOperationException($"Service scope '{definition.Name}' is already defined differently.");
                catalog.Add(definition);
            }

            Volatile.Write(ref _allNames, Names(catalog));
            Volatile.Write(ref _catalog, catalog);
        }
    }

    private static IReadOnlyList<string> Names(IEnumerable<ServiceScopeDefinition> catalog) =>
        new[] { ServiceApi }.Concat(catalog.Select(d => d.Name)).ToList();

    /// <summary>
    /// True when the name sits in the platform-reserved namespace, whether or
    /// not Oluso currently defines it. Used to reject hand-created scopes.
    /// </summary>
    public static bool IsReserved(string? name) =>
        !string.IsNullOrEmpty(name) &&
        name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the name is one Oluso defines and will provision on demand.
    /// </summary>
    public static bool IsManaged(string? name) =>
        name != null && AllNames.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// Looks up the catalog entry for a granular scope. Returns null for the
    /// base scope and for anything unknown.
    /// </summary>
    public static ServiceScopeDefinition? Find(string name) =>
        Catalog.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));
}
