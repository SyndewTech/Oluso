namespace Oluso.Core.Authorization;

/// <summary>
/// Interface for plugins to register their own permissions
/// </summary>
public interface IPermissionProvider
{
    IEnumerable<PermissionDefinition> GetPermissions();
}

/// <summary>
/// Registry for collecting permissions from core and plugins.
/// Modules and enterprise packages register their permissions here during DI setup.
/// </summary>
public class PermissionRegistry
{
    private readonly List<PermissionDefinition> _pluginPermissions = new();
    private readonly List<ModuleRoleDefinition> _moduleRoles = new();
    private readonly object _lock = new();
    private static PermissionRegistry? _instance;

    public static PermissionRegistry Instance => _instance ??= new PermissionRegistry();

    /// <summary>
    /// Register permissions from a plugin, optionally setting a feature key on all of them.
    /// </summary>
    public void RegisterPermissions(IEnumerable<PermissionDefinition> permissions, string? featureKey = null)
    {
        lock (_lock)
        {
            foreach (var permission in permissions)
            {
                var p = featureKey != null ? permission with { FeatureKey = featureKey } : permission;
                if (!_pluginPermissions.Any(x => x.Name == p.Name))
                {
                    _pluginPermissions.Add(p);
                }
            }
        }
    }

    /// <summary>
    /// Register a single permission
    /// </summary>
    public void RegisterPermission(PermissionDefinition permission)
    {
        lock (_lock)
        {
            if (!_pluginPermissions.Any(p => p.Name == permission.Name))
            {
                _pluginPermissions.Add(permission);
            }
        }
    }

    /// <summary>
    /// Get all registered plugin permissions
    /// </summary>
    public IReadOnlyList<PermissionDefinition> GetPluginPermissions()
    {
        lock (_lock)
        {
            return _pluginPermissions.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Check if a permission requires explicit grant (cannot be matched by wildcards).
    /// </summary>
    public bool RequiresExplicitGrant(string permission)
    {
        lock (_lock)
        {
            return _pluginPermissions.Any(p =>
                p.Name.Equals(permission, StringComparison.OrdinalIgnoreCase) &&
                p.RequiresExplicitGrant);
        }
    }

    /// <summary>
    /// Get the distinct set of feature keys across all registered permissions.
    /// </summary>
    public IReadOnlySet<string> GetFeatureKeys()
    {
        lock (_lock)
        {
            return _pluginPermissions
                .Where(p => p.FeatureKey != null)
                .Select(p => p.FeatureKey!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Clear all registered permissions (for testing)
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _pluginPermissions.Clear();
            _moduleRoles.Clear();
        }
    }

    /// <summary>
    /// Register a module admin role definition.
    /// The role will be seeded into the database at startup.
    /// </summary>
    public void RegisterModuleRole(ModuleRoleDefinition role)
    {
        lock (_lock)
        {
            if (!_moduleRoles.Any(r => r.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase)))
                _moduleRoles.Add(role);
        }
    }

    /// <summary>
    /// Get all registered module role definitions.
    /// </summary>
    public IReadOnlyList<ModuleRoleDefinition> GetModuleRoles()
    {
        lock (_lock)
        {
            return _moduleRoles.ToList().AsReadOnly();
        }
    }
}

public record PermissionDefinition(
    string Name,
    string DisplayName,
    string Category,
    string Description,
    bool RequiresSuperAdmin = false,
    /// <summary>
    /// When true, this permission cannot be matched by wildcard grants (e.g., <module>.*).
    /// It must be explicitly assigned to a role. Use for sensitive permissions.
    /// </summary>
    bool RequiresExplicitGrant = false,
    /// <summary>
    /// The feature key that gates this permission.
    /// When set, this permission is only available if the feature is enabled for the tenant.
    /// Null means the permission is a core permission and always available.
    /// </summary>
    string? FeatureKey = null
);

/// <summary>
/// Defines a module admin role that should be seeded into the database at startup.
/// Each module registers its admin role during DI setup.
/// </summary>
public record ModuleRoleDefinition(
    string Name,
    string DisplayName,
    string Description,
    string Category,
    string[] Permissions,
    /// <summary>
    /// The feature key that gates this role.
    /// When set, this role is only visible/assignable if the feature is enabled for the tenant.
    /// </summary>
    string? FeatureKey = null
);
