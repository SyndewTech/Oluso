namespace Oluso.Admin.Authorization;

/// <summary>
/// Interface for plugins to register their own permissions
/// </summary>
public interface IPermissionProvider
{
    /// <summary>
    /// Get all permissions provided by this plugin
    /// </summary>
    IEnumerable<PermissionDefinition> GetPermissions();
}

/// <summary>
/// Registry for collecting permissions from core and plugins
/// </summary>
public class PermissionRegistry
{
    private readonly List<PermissionDefinition> _pluginPermissions = new();
    private static PermissionRegistry? _instance;

    public static PermissionRegistry Instance => _instance ??= new PermissionRegistry();

    /// <summary>
    /// Register permissions from a plugin
    /// </summary>
    public void RegisterPermissions(IEnumerable<PermissionDefinition> permissions)
    {
        foreach (var permission in permissions)
        {
            if (!_pluginPermissions.Any(p => p.Name == permission.Name))
            {
                _pluginPermissions.Add(permission);
            }
        }
    }

    /// <summary>
    /// Register a single permission
    /// </summary>
    public void RegisterPermission(PermissionDefinition permission)
    {
        if (!_pluginPermissions.Any(p => p.Name == permission.Name))
        {
            _pluginPermissions.Add(permission);
        }
    }

    /// <summary>
    /// Get all registered plugin permissions
    /// </summary>
    public IReadOnlyList<PermissionDefinition> GetPluginPermissions() => _pluginPermissions.AsReadOnly();

    /// <summary>
    /// Clear all registered permissions (for testing)
    /// </summary>
    public void Clear() => _pluginPermissions.Clear();
}

/// <summary>
/// Centralized definition of all admin API permissions.
/// These permissions can be assigned to roles and are enforced by the RequirePermissionAttribute.
/// </summary>
public static class AdminPermissions
{
    /// <summary>
    /// Permission categories for organization
    /// </summary>
    public static class Categories
    {
        public const string Users = "Users";
        public const string Roles = "Roles";
        public const string Clients = "Clients";
        public const string Resources = "Resources";
        public const string Scopes = "Scopes";
        public const string IdentityResources = "Identity Resources";
        public const string IdentityProviders = "Identity Providers";
        public const string Grants = "Grants";
        public const string Sessions = "Sessions";
        public const string SigningKeys = "Signing Keys";
        public const string Journeys = "User Journeys";
        public const string Webhooks = "Webhooks";
        public const string AuditLogs = "Audit Logs";
        public const string Settings = "Settings";
        public const string Tenants = "Tenants";
        public const string Organizations = "Organizations";
        public const string Dashboard = "Dashboard";
        public const string Plugins = "Plugins";
        public const string Submissions = "Data Collection";
        public const string Telemetry = "Telemetry";
        public const string Fido2 = "FIDO2 / Passkeys";
        public const string Ldap = "LDAP";
        public const string Saml = "SAML";
        public const string Scim = "SCIM";
    }

    // ============ Users ============
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string UsersDelete = "users.delete";
    public const string UsersManageRoles = "users.manage_roles";
    public const string UsersManageClaims = "users.manage_claims";
    public const string UsersImpersonate = "users.impersonate";

    // ============ Roles ============
    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
    public const string RolesDelete = "roles.delete";

    // ============ Clients ============
    public const string ClientsRead = "clients.read";
    public const string ClientsWrite = "clients.write";
    public const string ClientsDelete = "clients.delete";
    public const string ClientsManageSecrets = "clients.manage_secrets";

    // ============ Resources (RFC 8707) ============
    public const string ResourcesRead = "resources.read";
    public const string ResourcesWrite = "resources.write";
    public const string ResourcesDelete = "resources.delete";

    // ============ API Scopes ============
    public const string ScopesRead = "scopes.read";
    public const string ScopesWrite = "scopes.write";
    public const string ScopesDelete = "scopes.delete";

    // ============ Identity Resources ============
    public const string IdentityResourcesRead = "identity_resources.read";
    public const string IdentityResourcesWrite = "identity_resources.write";
    public const string IdentityResourcesDelete = "identity_resources.delete";

    // ============ Identity Providers ============
    public const string IdentityProvidersRead = "identity_providers.read";
    public const string IdentityProvidersWrite = "identity_providers.write";
    public const string IdentityProvidersDelete = "identity_providers.delete";

    // ============ Grants ============
    public const string GrantsRead = "grants.read";
    public const string GrantsRevoke = "grants.revoke";

    // ============ Sessions ============
    public const string SessionsRead = "sessions.read";
    public const string SessionsRevoke = "sessions.revoke";

    // ============ Signing Keys ============
    public const string SigningKeysRead = "signing_keys.read";
    public const string SigningKeysWrite = "signing_keys.write";
    public const string SigningKeysRotate = "signing_keys.rotate";

    // ============ User Journeys ============
    public const string JourneysRead = "journeys.read";
    public const string JourneysWrite = "journeys.write";
    public const string JourneysDelete = "journeys.delete";
    public const string JourneysPublish = "journeys.publish";

    // ============ Webhooks ============
    public const string WebhooksRead = "webhooks.read";
    public const string WebhooksWrite = "webhooks.write";
    public const string WebhooksDelete = "webhooks.delete";

    // ============ Audit Logs ============
    public const string AuditLogsRead = "audit_logs.read";
    public const string AuditLogsExport = "audit_logs.export";

    // ============ Settings ============
    public const string SettingsRead = "settings.read";
    public const string SettingsWrite = "settings.write";

    // ============ Tenants (SuperAdmin only) ============
    public const string TenantsRead = "tenants.read";
    public const string TenantsWrite = "tenants.write";
    public const string TenantsDelete = "tenants.delete";
    public const string TenantsManageSettings = "tenants.manage_settings";

    // ============ Organizations ============
    public const string OrganizationsRead = "organizations.read";
    public const string OrganizationsWrite = "organizations.write";
    public const string OrganizationsDelete = "organizations.delete";
    public const string OrganizationsManageMembers = "organizations.manage_members";
    public const string OrganizationsManageInvitations = "organizations.manage_invitations";
    public const string OrganizationsManageTenants = "organizations.manage_tenants";

    // ============ Dashboard ============
    public const string DashboardView = "dashboard.view";

    // ============ Plugins ============
    public const string PluginsRead = "plugins.read";
    public const string PluginsWrite = "plugins.write";
    public const string PluginsManage = "plugins.manage";

    // ============ Submissions / Data Collection ============
    public const string SubmissionsRead = "submissions.read";
    public const string SubmissionsWrite = "submissions.write";
    public const string SubmissionsDelete = "submissions.delete";

    // ============ Telemetry ============
    public const string TelemetryRead = "telemetry.read";

    // ============ FIDO2 / Passkeys ============
    public const string Fido2Read = "fido2.read";
    public const string Fido2Write = "fido2.write";
    public const string Fido2Delete = "fido2.delete";

    // ============ LDAP ============
    public const string LdapRead = "ldap.read";
    public const string LdapWrite = "ldap.write";

    // ============ SAML ============
    public const string SamlRead = "saml.read";
    public const string SamlWrite = "saml.write";

    // ============ SCIM ============
    public const string ScimRead = "scim.read";
    public const string ScimWrite = "scim.write";

    /// <summary>
    /// Get all defined permissions with their metadata (core + plugin permissions)
    /// </summary>
    public static IReadOnlyList<PermissionDefinition> GetAll()
    {
        var corePermissions = GetCorePermissions();
        var pluginPermissions = PermissionRegistry.Instance.GetPluginPermissions();

        // Combine core and plugin permissions, avoiding duplicates
        var all = new List<PermissionDefinition>(corePermissions);
        foreach (var pluginPerm in pluginPermissions)
        {
            if (!all.Any(p => p.Name == pluginPerm.Name))
            {
                all.Add(pluginPerm);
            }
        }

        return all;
    }

    /// <summary>
    /// Get core permissions only (without plugin permissions)
    /// </summary>
    public static IReadOnlyList<PermissionDefinition> GetCorePermissions()
    {
        return new List<PermissionDefinition>
        {
            // Users
            new(UsersRead, "View users", Categories.Users, "View user list and details"),
            new(UsersWrite, "Create/Edit users", Categories.Users, "Create new users and edit existing users"),
            new(UsersDelete, "Delete users", Categories.Users, "Delete users from the system"),
            new(UsersManageRoles, "Manage user roles", Categories.Users, "Assign and remove roles from users"),
            new(UsersManageClaims, "Manage user claims", Categories.Users, "Add and remove claims from users"),
            new(UsersImpersonate, "Impersonate users", Categories.Users, "Sign in as another user (audit logged)"),

            // Roles
            new(RolesRead, "View roles", Categories.Roles, "View role list and details"),
            new(RolesWrite, "Create/Edit roles", Categories.Roles, "Create new roles and edit existing roles"),
            new(RolesDelete, "Delete roles", Categories.Roles, "Delete roles from the system"),

            // Clients
            new(ClientsRead, "View clients", Categories.Clients, "View OAuth client list and details"),
            new(ClientsWrite, "Create/Edit clients", Categories.Clients, "Create new clients and edit existing clients"),
            new(ClientsDelete, "Delete clients", Categories.Clients, "Delete OAuth clients"),
            new(ClientsManageSecrets, "Manage client secrets", Categories.Clients, "Create, view, and delete client secrets"),

            // Resources
            new(ResourcesRead, "View resources", Categories.Resources, "View protected resources (RFC 8707)"),
            new(ResourcesWrite, "Create/Edit resources", Categories.Resources, "Create and edit protected resources"),
            new(ResourcesDelete, "Delete resources", Categories.Resources, "Delete protected resources"),

            // Scopes
            new(ScopesRead, "View API scopes", Categories.Scopes, "View API scope list and details"),
            new(ScopesWrite, "Create/Edit API scopes", Categories.Scopes, "Create and edit API scopes"),
            new(ScopesDelete, "Delete API scopes", Categories.Scopes, "Delete API scopes"),

            // Identity Resources
            new(IdentityResourcesRead, "View identity resources", Categories.IdentityResources, "View identity resource list"),
            new(IdentityResourcesWrite, "Create/Edit identity resources", Categories.IdentityResources, "Create and edit identity resources"),
            new(IdentityResourcesDelete, "Delete identity resources", Categories.IdentityResources, "Delete identity resources"),

            // Identity Providers
            new(IdentityProvidersRead, "View identity providers", Categories.IdentityProviders, "View external identity provider configurations"),
            new(IdentityProvidersWrite, "Create/Edit identity providers", Categories.IdentityProviders, "Create and edit identity provider configurations"),
            new(IdentityProvidersDelete, "Delete identity providers", Categories.IdentityProviders, "Delete identity provider configurations"),

            // Grants
            new(GrantsRead, "View grants", Categories.Grants, "View user consent grants"),
            new(GrantsRevoke, "Revoke grants", Categories.Grants, "Revoke user consent grants"),

            // Sessions
            new(SessionsRead, "View sessions", Categories.Sessions, "View active user sessions"),
            new(SessionsRevoke, "Revoke sessions", Categories.Sessions, "Terminate user sessions"),

            // Signing Keys
            new(SigningKeysRead, "View signing keys", Categories.SigningKeys, "View signing key information"),
            new(SigningKeysWrite, "Manage signing keys", Categories.SigningKeys, "Create and configure signing keys"),
            new(SigningKeysRotate, "Rotate signing keys", Categories.SigningKeys, "Rotate signing keys"),

            // Journeys
            new(JourneysRead, "View user journeys", Categories.Journeys, "View user journey configurations"),
            new(JourneysWrite, "Create/Edit user journeys", Categories.Journeys, "Create and edit user journeys"),
            new(JourneysDelete, "Delete user journeys", Categories.Journeys, "Delete user journeys"),
            new(JourneysPublish, "Publish user journeys", Categories.Journeys, "Publish user journeys to production"),

            // Webhooks
            new(WebhooksRead, "View webhooks", Categories.Webhooks, "View webhook configurations"),
            new(WebhooksWrite, "Create/Edit webhooks", Categories.Webhooks, "Create and edit webhook configurations"),
            new(WebhooksDelete, "Delete webhooks", Categories.Webhooks, "Delete webhook configurations"),

            // Audit Logs
            new(AuditLogsRead, "View audit logs", Categories.AuditLogs, "View audit log entries"),
            new(AuditLogsExport, "Export audit logs", Categories.AuditLogs, "Export audit logs to file"),

            // Settings
            new(SettingsRead, "View settings", Categories.Settings, "View system settings"),
            new(SettingsWrite, "Modify settings", Categories.Settings, "Modify system settings"),

            // Tenants (SuperAdmin)
            new(TenantsRead, "View tenants", Categories.Tenants, "View tenant list and details", RequiresSuperAdmin: true),
            new(TenantsWrite, "Create/Edit tenants", Categories.Tenants, "Create and edit tenants", RequiresSuperAdmin: true),
            new(TenantsDelete, "Delete tenants", Categories.Tenants, "Delete tenants", RequiresSuperAdmin: true),
            new(TenantsManageSettings, "Manage tenant settings", Categories.Tenants, "Configure tenant-specific settings", RequiresSuperAdmin: true),

            // Organizations
            new(OrganizationsRead, "View organizations", Categories.Organizations, "View organization list and details"),
            new(OrganizationsWrite, "Create/Edit organizations", Categories.Organizations, "Create and edit organizations"),
            new(OrganizationsDelete, "Delete organizations", Categories.Organizations, "Delete organizations"),
            new(OrganizationsManageMembers, "Manage organization members", Categories.Organizations, "Add, remove, and update organization members"),
            new(OrganizationsManageInvitations, "Manage organization invitations", Categories.Organizations, "Create, revoke, and resend organization invitations"),
            new(OrganizationsManageTenants, "Manage organization tenants", Categories.Organizations, "Create and manage tenants within an organization"),

            // Dashboard
            new(DashboardView, "View dashboard", Categories.Dashboard, "View the admin dashboard"),

            // Plugins
            new(PluginsRead, "View plugins", Categories.Plugins, "View installed plugins"),
            new(PluginsWrite, "Configure plugins", Categories.Plugins, "Configure plugin settings"),
            new(PluginsManage, "Manage plugins", Categories.Plugins, "Install and uninstall plugins"),

            // Submissions / Data Collection
            new(SubmissionsRead, "View submissions", Categories.Submissions, "View data collection submissions"),
            new(SubmissionsWrite, "Manage submissions", Categories.Submissions, "Edit and process submissions"),
            new(SubmissionsDelete, "Delete submissions", Categories.Submissions, "Delete data collection submissions"),

            // Telemetry
            new(TelemetryRead, "View telemetry", Categories.Telemetry, "View telemetry logs and metrics"),

            // FIDO2 / Passkeys
            new(Fido2Read, "View passkeys", Categories.Fido2, "View FIDO2 passkey configurations and credentials"),
            new(Fido2Write, "Manage passkeys", Categories.Fido2, "Configure FIDO2 passkey settings"),
            new(Fido2Delete, "Delete passkeys", Categories.Fido2, "Delete user passkeys"),

            // LDAP
            new(LdapRead, "View LDAP settings", Categories.Ldap, "View LDAP server configuration"),
            new(LdapWrite, "Manage LDAP settings", Categories.Ldap, "Configure LDAP server settings"),

            // SAML
            new(SamlRead, "View SAML settings", Categories.Saml, "View SAML IdP configuration"),
            new(SamlWrite, "Manage SAML settings", Categories.Saml, "Configure SAML IdP settings"),

            // SCIM
            new(ScimRead, "View SCIM settings", Categories.Scim, "View SCIM provisioning configuration"),
            new(ScimWrite, "Manage SCIM settings", Categories.Scim, "Configure SCIM provisioning settings"),
        };
    }

    /// <summary>
    /// Get permissions grouped by category
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<PermissionDefinition>> GetByCategory()
    {
        return GetAll()
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PermissionDefinition>)g.ToList());
    }

    /// <summary>
    /// Check if a permission exists
    /// </summary>
    public static bool Exists(string permission)
    {
        return GetAll().Any(p => p.Name == permission);
    }

    /// <summary>
    /// Get all permission names as a simple list
    /// </summary>
    public static IReadOnlyList<string> GetAllNames()
    {
        return GetAll().Select(p => p.Name).ToList();
    }
}

/// <summary>
/// Represents a permission with its metadata
/// </summary>
public record PermissionDefinition(
    string Name,
    string DisplayName,
    string Category,
    string Description,
    bool RequiresSuperAdmin = false
);
