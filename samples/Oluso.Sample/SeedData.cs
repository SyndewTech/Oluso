using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;

namespace Oluso.Sample;

public static class SeedData
{
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await SeedOrganizationsAsync(dbContext);
        await SeedTenantsAsync(dbContext);
        await SeedClientsAsync(dbContext, configuration);
        await SeedIdentityResourcesAsync(dbContext);
        await SeedRolesAsync(dbContext);
        await SeedUsersAsync(dbContext);
        await SeedJourneyPoliciesAsync(dbContext, "default");
    }

    private static async Task SeedOrganizationsAsync(OlusoDbContext dbContext)
    {
        if (await dbContext.Organizations.AnyAsync())
            return;

        dbContext.Organizations.Add(new Organization
        {
            Id = "default-org",
            Name = "Default Organization",
            Slug = "default",
            Enabled = true,
            Description = "Default organization for development",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedTenantsAsync(OlusoDbContext dbContext)
    {
        if (await dbContext.Tenants.AnyAsync())
            return;

        dbContext.Tenants.Add(new Tenant
        {
            Id = "default",
            Name = "Default",
            DisplayName = "Default Tenant",
            Identifier = "default",
            Enabled = true,
            Description = "Default tenant for development",
            UseJourneyFlow = false,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            AllowSelfRegistration = false,
            OrganizationId = "default-org",
            Environment = TenantEnvironment.Development
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedClientsAsync(OlusoDbContext dbContext, IConfiguration configuration)
    {
        if (await dbContext.Clients.AnyAsync())
            return;

        var baseUrl = configuration.GetValue<string>("Oluso:Urls:BaseUrl", "http://localhost:5050")!;
        var testClientUrl = configuration.GetValue<string>("Oluso:Urls:TestClientUrl", "http://localhost:5100")!;
        var accountUiUrl = configuration.GetValue<string>("Oluso:Urls:AccountUiUrl", "http://localhost:5173")!;
        var accountSaasUrl = configuration.GetValue<string>("Oluso:Urls:AccountSaasUrl", "http://localhost:3101")!;
        var frontendUrl = configuration.GetValue<string>("Oluso:Urls:FrontendUrl", "http://localhost:3000")!;
        var workspacePortalUrl = configuration.GetValue<string>("Oluso:Urls:WorkspacePortalUrl", "http://localhost:5175")!;

        // Test client (Authorization Code flow)
        var testClient = CreateClient("test-client", "Test Client", requireSecret: true, requirePkce: false, allowOffline: true);
        testClient.ClientSecrets.Add(new ClientSecret { Value = HashSecret("test-secret"), Type = "SharedSecret" });
        testClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code" });
        testClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token" });
        testClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{frontendUrl}/callback" });
        testClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{baseUrl}/test/oidc/callback" });
        testClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{testClientUrl}/signin-oidc" });
        testClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = "https://oauth.pstmn.io/v1/callback" });
        testClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = frontendUrl });
        testClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = $"{testClientUrl}/signout-callback-oidc" });
        testClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        testClient.AllowedScopes.Add(new ClientScope { Scope = "profile" });
        testClient.AllowedScopes.Add(new ClientScope { Scope = "email" });
        dbContext.Clients.Add(testClient);

        // Client Credentials client
        var ccClient = CreateClient("cc-client", "Client Credentials Client", requireSecret: true, requirePkce: false, allowOffline: false);
        ccClient.ClientSecrets.Add(new ClientSecret { Value = HashSecret("test-secret"), Type = "SharedSecret" });
        ccClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "client_credentials" });
        ccClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        ccClient.AllowedScopes.Add(new ClientScope { Scope = "api" });
        dbContext.Clients.Add(ccClient);

        // Resource Owner Password client
        var ropcClient = CreateClient("ropc-client", "Resource Owner Password Client", requireSecret: true, requirePkce: false, allowOffline: true);
        ropcClient.ClientSecrets.Add(new ClientSecret { Value = HashSecret("test-secret"), Type = "SharedSecret" });
        ropcClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "password" });
        ropcClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token" });
        ropcClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        ropcClient.AllowedScopes.Add(new ClientScope { Scope = "profile" });
        ropcClient.AllowedScopes.Add(new ClientScope { Scope = "email" });
        ropcClient.AllowedScopes.Add(new ClientScope { Scope = "offline_access" });
        dbContext.Clients.Add(ropcClient);

        // Device Flow client
        var deviceClient = CreateClient("device-client", "Device Flow Client", requireSecret: false, requirePkce: false, allowOffline: true);
        deviceClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "urn:ietf:params:oauth:grant-type:device_code" });
        deviceClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token" });
        deviceClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        deviceClient.AllowedScopes.Add(new ClientScope { Scope = "profile" });
        deviceClient.AllowedScopes.Add(new ClientScope { Scope = "offline_access" });
        dbContext.Clients.Add(deviceClient);

        // Account UI client (SPA with PKCE)
        var accountUiClient = CreateClient("account-ui", "Account Management UI", requireSecret: false, requirePkce: true, allowOffline: true);
        accountUiClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code" });
        accountUiClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token" });
        accountUiClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{accountUiUrl}/callback" });
        accountUiClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{accountSaasUrl}/callback" });
        accountUiClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = accountUiUrl });
        accountUiClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = accountSaasUrl });
        accountUiClient.AllowedCorsOrigins.Add(new ClientCorsOrigin { Origin = accountUiUrl });
        accountUiClient.AllowedCorsOrigins.Add(new ClientCorsOrigin { Origin = accountSaasUrl });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "profile" });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "email" });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "account" });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "offline_access" });
        dbContext.Clients.Add(accountUiClient);

        // Workspace Portal client (SPA with PKCE)
        var workspaceClient = CreateClient("workspace-portal", "Workspace Portal", requireSecret: false, requirePkce: true, allowOffline: true);
        workspaceClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code" });
        workspaceClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token" });
        workspaceClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{workspacePortalUrl}/callback" });
        workspaceClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = workspacePortalUrl });
        workspaceClient.AllowedCorsOrigins.Add(new ClientCorsOrigin { Origin = workspacePortalUrl });
        workspaceClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        workspaceClient.AllowedScopes.Add(new ClientScope { Scope = "profile" });
        workspaceClient.AllowedScopes.Add(new ClientScope { Scope = "email" });
        workspaceClient.AllowedScopes.Add(new ClientScope { Scope = "workspace" });
        workspaceClient.AllowedScopes.Add(new ClientScope { Scope = "offline_access" });
        dbContext.Clients.Add(workspaceClient);

        await dbContext.SaveChangesAsync();
    }

    private static Client CreateClient(string clientId, string clientName, bool requireSecret, bool requirePkce, bool allowOffline)
    {
        return new Client
        {
            ClientId = clientId,
            ClientName = clientName,
            TenantId = "default",
            Enabled = true,
            RequireClientSecret = requireSecret,
            RequirePkce = requirePkce,
            AllowOfflineAccess = allowOffline
        };
    }

    private static string HashSecret(string secret)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static async Task SeedIdentityResourcesAsync(OlusoDbContext dbContext)
    {
        if (await dbContext.IdentityResources.AnyAsync())
            return;

        dbContext.IdentityResources.AddRange(
            new IdentityResource { Name = "openid", DisplayName = "Your user identifier", Required = true, TenantId = "default" },
            new IdentityResource { Name = "profile", DisplayName = "User profile", Description = "Your user profile information (first name, last name, etc.)", TenantId = "default" },
            new IdentityResource { Name = "email", DisplayName = "Your email address", TenantId = "default" },
            new IdentityResource { Name = "account", DisplayName = "Account Management", Description = "Access to manage your account settings, sessions, and connected applications", TenantId = "default" },
            new IdentityResource { Name = "workspace", DisplayName = "Workspace Portal", Description = "Access to workplace self-service features", TenantId = "default" }
        );
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedRolesAsync(OlusoDbContext dbContext)
    {
        if (await dbContext.Roles.AnyAsync())
            return;

        var now = DateTime.UtcNow;

        // System-level roles (Category: system)
        var superAdminRole = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = "SuperAdmin",
            NormalizedName = "SUPERADMIN",
            DisplayName = "Super Administrator",
            Description = "Full system access across all tenants",
            TenantId = null,
            IsSystemRole = true,
            Category = "system",
            ManagedByRole = null, // No one manages SuperAdmin
            CreatedAt = now
        };
        dbContext.Roles.Add(superAdminRole);

        var orgAdminRole = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = "OrgAdmin",
            NormalizedName = "ORGADMIN",
            DisplayName = "Organization Administrator",
            Description = "Manage organizations and their tenants",
            TenantId = null,
            IsSystemRole = true,
            Category = "system",
            ManagedByRole = "SuperAdmin",
            CreatedAt = now
        };
        dbContext.Roles.Add(orgAdminRole);

        // Tenant-level roles (Category: tenant)
        var adminRole = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Admin",
            NormalizedName = "ADMIN",
            DisplayName = "Administrator",
            Description = "Full access within the tenant",
            TenantId = "default",
            IsSystemRole = false,
            Category = "tenant",
            ManagedByRole = "SuperAdmin",
            CreatedAt = now
        };
        dbContext.Roles.Add(adminRole);

        var userRole = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = "User",
            NormalizedName = "USER",
            DisplayName = "User",
            Description = "Standard user access",
            TenantId = "default",
            IsSystemRole = false,
            Category = "tenant",
            ManagedByRole = "Admin",
            CreatedAt = now
        };
        dbContext.Roles.Add(userRole);

        // HR Admin role - managed by SuperAdmin, manages other HR roles
        // This is the "bridge" role that SuperAdmin can assign
        var hrAdminRole = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = "HRAdmin",
            NormalizedName = "HRADMIN",
            DisplayName = "HR Administrator",
            Description = "Manage HR functions, employees, departments - delegates payroll to PayrollAdmin",
            TenantId = "default",
            IsSystemRole = false,
            Category = "hr",
            ManagedByRole = "SuperAdmin", // SuperAdmin can assign HRAdmin
            CreatedAt = now
        };
        dbContext.Roles.Add(hrAdminRole);

        // Payroll Admin role - managed by HRAdmin
        var payrollAdminRole = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = "PayrollAdmin",
            NormalizedName = "PAYROLLADMIN",
            DisplayName = "Payroll Administrator",
            Description = "Full payroll access including employee salaries",
            TenantId = "default",
            IsSystemRole = false,
            Category = "hr",
            ManagedByRole = "HRAdmin", // Only HRAdmin can assign PayrollAdmin
            CreatedAt = now
        };
        dbContext.Roles.Add(payrollAdminRole);

        // HR Manager role - managed by HRAdmin (can approve leave, manage team)
        var hrManagerRole = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = "HRManager",
            NormalizedName = "HRMANAGER",
            DisplayName = "HR Manager",
            Description = "Approve leave requests, manage team members",
            TenantId = "default",
            IsSystemRole = false,
            Category = "hr",
            ManagedByRole = "HRAdmin",
            CreatedAt = now
        };
        dbContext.Roles.Add(hrManagerRole);

        // HR Viewer role - managed by HRAdmin (read-only HR access)
        var hrViewerRole = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = "HRViewer",
            NormalizedName = "HRVIEWER",
            DisplayName = "HR Viewer",
            Description = "Read-only access to HR data (no salary visibility)",
            TenantId = "default",
            IsSystemRole = false,
            Category = "hr",
            ManagedByRole = "HRAdmin",
            CreatedAt = now
        };
        dbContext.Roles.Add(hrViewerRole);

        await dbContext.SaveChangesAsync();

        // Add permissions to roles
        await SeedRolePermissionsAsync(dbContext, superAdminRole, orgAdminRole, adminRole, userRole, hrAdminRole, payrollAdminRole, hrManagerRole, hrViewerRole);

        Console.WriteLine("Seeded default roles with permissions");
    }

    private static async Task SeedRolePermissionsAsync(
        OlusoDbContext dbContext,
        OlusoRole superAdminRole,
        OlusoRole orgAdminRole,
        OlusoRole adminRole,
        OlusoRole userRole,
        OlusoRole hrAdminRole,
        OlusoRole payrollAdminRole,
        OlusoRole hrManagerRole,
        OlusoRole hrViewerRole)
    {
        var now = DateTime.UtcNow;

        // Helper to add permission claim to a role
        void AddPermission(OlusoRole role, string permission)
        {
            dbContext.RoleClaims.Add(new OlusoRoleClaim
            {
                RoleId = role.Id,
                ClaimType = "permission",
                ClaimValue = permission,
                CreatedAt = now
            });
        }

        // SuperAdmin gets ALL permissions
        var allPermissions = new[]
        {
            // Users
            "users.read", "users.write", "users.delete", "users.manage_roles", "users.manage_claims", "users.impersonate",
            // Roles
            "roles.read", "roles.write", "roles.delete",
            // Clients
            "clients.read", "clients.write", "clients.delete", "clients.manage_secrets",
            // Resources
            "resources.read", "resources.write", "resources.delete",
            // Scopes
            "scopes.read", "scopes.write", "scopes.delete",
            // Identity Resources
            "identity_resources.read", "identity_resources.write", "identity_resources.delete",
            // Identity Providers
            "identity_providers.read", "identity_providers.write", "identity_providers.delete",
            // Grants
            "grants.read", "grants.revoke",
            // Sessions
            "sessions.read", "sessions.revoke",
            // Signing Keys
            "signing_keys.read", "signing_keys.write", "signing_keys.rotate",
            // Journeys
            "journeys.read", "journeys.write", "journeys.delete", "journeys.publish",
            // Webhooks
            "webhooks.read", "webhooks.write", "webhooks.delete",
            // Audit Logs
            "audit_logs.read", "audit_logs.export",
            // Settings
            "settings.read", "settings.write",
            // Tenants (SuperAdmin only)
            "tenants.read", "tenants.write", "tenants.delete", "tenants.manage_settings",
            // Organizations
            "organizations.read", "organizations.write", "organizations.delete",
            "organizations.manage_members", "organizations.manage_invitations", "organizations.manage_tenants",
            // Dashboard
            "dashboard.view",
            // Plugins
            "plugins.read", "plugins.write", "plugins.manage",
            // Submissions
            "submissions.read", "submissions.write", "submissions.delete",
            // Telemetry
            "telemetry.read",
            // FIDO2 / Passkeys
            "fido2.read", "fido2.write", "fido2.delete",
            // LDAP
            "ldap.read", "ldap.write",
            // SAML
            "saml.read", "saml.write",
            // SCIM
            "scim.read", "scim.write"
        };

        foreach (var permission in allPermissions)
        {
            AddPermission(superAdminRole, permission);
        }

        // OrgAdmin gets organization and tenant management permissions
        var orgAdminPermissions = new[]
        {
            // Organizations
            "organizations.read", "organizations.write",
            "organizations.manage_members", "organizations.manage_invitations", "organizations.manage_tenants",
            // Tenants (within their organization)
            "tenants.read", "tenants.write", "tenants.manage_settings",
            // Dashboard
            "dashboard.view",
            // Users (read only at org level)
            "users.read",
            // Telemetry (read only)
            "telemetry.read",
            // Enterprise plugins (read only for org-level visibility)
            "fido2.read",
            "ldap.read",
            "saml.read",
            "scim.read"
        };

        foreach (var permission in orgAdminPermissions)
        {
            AddPermission(orgAdminRole, permission);
        }

        // Tenant Admin gets full tenant-level permissions (no tenant/org management)
        // NOTE: Admin does NOT get hr.payroll.salaries.* or hr.employees.view_salary
        var tenantAdminPermissions = new[]
        {
            // Users
            "users.read", "users.write", "users.delete", "users.manage_roles", "users.manage_claims",
            // Roles
            "roles.read", "roles.write", "roles.delete",
            // Clients
            "clients.read", "clients.write", "clients.delete", "clients.manage_secrets",
            // Resources
            "resources.read", "resources.write", "resources.delete",
            // Scopes
            "scopes.read", "scopes.write", "scopes.delete",
            // Identity Resources
            "identity_resources.read", "identity_resources.write", "identity_resources.delete",
            // Identity Providers
            "identity_providers.read", "identity_providers.write", "identity_providers.delete",
            // Grants
            "grants.read", "grants.revoke",
            // Sessions
            "sessions.read", "sessions.revoke",
            // Signing Keys
            "signing_keys.read", "signing_keys.write", "signing_keys.rotate",
            // Journeys
            "journeys.read", "journeys.write", "journeys.delete", "journeys.publish",
            // Webhooks
            "webhooks.read", "webhooks.write", "webhooks.delete",
            // Audit Logs
            "audit_logs.read", "audit_logs.export",
            // Settings
            "settings.read", "settings.write",
            // Dashboard
            "dashboard.view",
            // Submissions
            "submissions.read", "submissions.write", "submissions.delete",
            // Telemetry
            "telemetry.read",
            // FIDO2 / Passkeys
            "fido2.read", "fido2.write", "fido2.delete",
            // LDAP
            "ldap.read", "ldap.write",
            // SAML
            "saml.read", "saml.write",
            // SCIM
            "scim.read", "scim.write",
            // HR - Basic (no salary access)
            "hr.employees.read", "hr.employees.write",
            "hr.departments.read", "hr.departments.write",
            "hr.positions.read", "hr.positions.write",
            "hr.leave_policies.read",
            "hr.approval_workflows.read",
            "hr.org_chart.view",
            "hr.reports.view",
            // Payroll (view only, NO salaries)
            "hr.payroll.view", "hr.payroll.structures.view"
        };

        foreach (var permission in tenantAdminPermissions)
        {
            AddPermission(adminRole, permission);
        }

        // Regular User gets minimal permissions
        var userPermissions = new[]
        {
            "dashboard.view"
        };

        foreach (var permission in userPermissions)
        {
            AddPermission(userRole, permission);
        }

        // HR Admin gets HR permissions EXCEPT salary/payroll sensitive data
        // Plus HR role management permissions
        var hrAdminPermissions = new[]
        {
            // Employees (no salary view)
            "hr.employees.read", "hr.employees.write", "hr.employees.delete",
            "hr.employees.manage_sensitive", "hr.employees.export",
            // Departments
            "hr.departments.read", "hr.departments.write", "hr.departments.delete",
            // Positions
            "hr.positions.read", "hr.positions.write", "hr.positions.delete",
            // Leave Management
            "hr.leave_policies.read", "hr.leave_policies.write", "hr.leave_policies.delete",
            "hr.leave_requests.read", "hr.leave_requests.manage", "hr.leave_balances.manage",
            // Approval Workflows
            "hr.approval_workflows.read", "hr.approval_workflows.write", "hr.approval_workflows.delete",
            // Expenses
            "hr.expenses.read", "hr.expenses.write", "hr.expenses.delete", "hr.expenses.manage",
            // Organization
            "hr.org_chart.view", "hr.org_chart.manage",
            // Reports (non-salary)
            "hr.reports.view", "hr.reports.generate",
            // Payroll (view schedules, structures - but NOT salaries)
            "hr.payroll.view", "hr.payroll.structures.view",
            "hr.payroll.tax.view", "hr.payroll.payslips.view",
            // HR Role Management - HRAdmin can manage HR roles and assign them
            "hr.roles.read", "hr.roles.write", "hr.roles.assign"
        };

        foreach (var permission in hrAdminPermissions)
        {
            AddPermission(hrAdminRole, permission);
        }

        // Payroll Admin gets FULL payroll access including salaries
        var payrollAdminPermissions = new[]
        {
            // Full Payroll Access
            "hr.payroll.view", "hr.payroll.process", "hr.payroll.approve",
            "hr.payroll.structures.view", "hr.payroll.structures.manage",
            "hr.payroll.salaries.view", "hr.payroll.salaries.manage",  // SENSITIVE!
            "hr.payroll.tax.view", "hr.payroll.tax.manage",
            "hr.payroll.adjustments.manage",
            "hr.payroll.payslips.view",
            // Basic HR read access
            "hr.employees.read", "hr.employees.view_salary",  // Can view salaries
            "hr.departments.read",
            "hr.positions.read",
            "hr.reports.view"
        };

        foreach (var permission in payrollAdminPermissions)
        {
            AddPermission(payrollAdminRole, permission);
        }

        // HR Manager permissions (team management, leave approvals)
        var hrManagerPermissions = new[]
        {
            "hr.employees.read",
            "hr.departments.read",
            "hr.positions.read",
            "hr.leave_requests.read", "hr.leave_requests.manage",
            "hr.approval_workflows.read",
            "hr.expenses.read", "hr.expenses.manage",
            "hr.org_chart.view",
            "hr.reports.view"
        };

        foreach (var permission in hrManagerPermissions)
        {
            AddPermission(hrManagerRole, permission);
        }

        // HR Viewer permissions (read-only, no salary)
        var hrViewerPermissions = new[]
        {
            "hr.employees.read",
            "hr.departments.read",
            "hr.positions.read",
            "hr.leave_policies.read",
            "hr.leave_requests.read",
            "hr.org_chart.view",
            "hr.reports.view"
        };

        foreach (var permission in hrViewerPermissions)
        {
            AddPermission(hrViewerRole, permission);
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedUsersAsync(OlusoDbContext dbContext)
    {
        if (await dbContext.Users.AnyAsync())
            return;

        var passwordHasher = new PasswordHasher<OlusoUser>();
        var superAdminRole = await dbContext.Roles.FirstAsync(r => r.Name == "SuperAdmin");
        var orgAdminRole = await dbContext.Roles.FirstAsync(r => r.Name == "OrgAdmin");
        var adminRole = await dbContext.Roles.FirstAsync(r => r.Name == "Admin");
        var userRole = await dbContext.Roles.FirstAsync(r => r.Name == "User");

        // 1. Super Admin (system-level)
        var superAdmin = CreateUser("superadmin@oluso.local", "Super", "Admin", "Super Administrator", tenantId: null);
        superAdmin.PasswordHash = passwordHasher.HashPassword(superAdmin, "SuperAdmin123!");
        dbContext.Users.Add(superAdmin);
        dbContext.Set<OlusoUserRole>().Add(new OlusoUserRole { UserId = superAdmin.Id, RoleId = superAdminRole.Id, AssignedAt = DateTime.UtcNow, AssignedBy = "system" });

        // 2. Org Admin (organization-level)
        var orgAdmin = CreateUser("orgadmin@oluso.local", "Org", "Admin", "Organization Administrator", tenantId: null);
        orgAdmin.PasswordHash = passwordHasher.HashPassword(orgAdmin, "OrgAdmin123!");
        dbContext.Users.Add(orgAdmin);
        dbContext.Set<OlusoUserRole>().Add(new OlusoUserRole { UserId = orgAdmin.Id, RoleId = orgAdminRole.Id, AssignedAt = DateTime.UtcNow, AssignedBy = "system" });

        // 3. Tenant Admin (tenant-level)
        var tenantAdmin = CreateUser("admin@default.local", "Tenant", "Admin", "Default Tenant Administrator", tenantId: "default");
        tenantAdmin.PasswordHash = passwordHasher.HashPassword(tenantAdmin, "TenantAdmin123!");
        dbContext.Users.Add(tenantAdmin);
        dbContext.Set<OlusoUserRole>().Add(new OlusoUserRole { UserId = tenantAdmin.Id, RoleId = adminRole.Id, AssignedAt = DateTime.UtcNow, AssignedBy = "system" });

        // 4. Test user
        var testUser = CreateUser("testuser@example.com", "Test", "User", "Test User", tenantId: "default");
        testUser.PasswordHash = passwordHasher.HashPassword(testUser, "Password123!");
        dbContext.Users.Add(testUser);
        dbContext.Set<OlusoUserRole>().Add(new OlusoUserRole { UserId = testUser.Id, RoleId = userRole.Id, AssignedAt = DateTime.UtcNow, AssignedBy = "system" });

        // Organization memberships
        dbContext.OrganizationMemberships.Add(new OrganizationMembership
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = "default-org",
            UserId = superAdmin.Id,
            Role = OrganizationRole.Owner,
            JoinedAt = DateTime.UtcNow
        });

        dbContext.OrganizationMemberships.Add(new OrganizationMembership
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = "default-org",
            UserId = orgAdmin.Id,
            Role = OrganizationRole.Admin,
            JoinedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        Console.WriteLine("Seeded users:");
        Console.WriteLine("  SuperAdmin:   superadmin@oluso.local / SuperAdmin123!");
        Console.WriteLine("  Org Admin:    orgadmin@oluso.local / OrgAdmin123!");
        Console.WriteLine("  Tenant Admin: admin@default.local / TenantAdmin123!");
        Console.WriteLine("  Test User:    testuser@example.com / Password123!");
    }

    private static OlusoUser CreateUser(string email, string firstName, string lastName, string displayName, string? tenantId)
    {
        return new OlusoUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            TenantId = tenantId,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
    }

    private static async Task SeedJourneyPoliciesAsync(OlusoDbContext dbContext, string tenantId)
    {
        if (await dbContext.JourneyPolicies.AnyAsync())
            return;

        var now = DateTime.UtcNow;
        var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

        static Dictionary<string, object?> Step(string id, string type, string displayName, int order,
            bool optional = false, Dictionary<string, object>? configuration = null, Dictionary<string, string>? branches = null)
        {
            var step = new Dictionary<string, object?> { ["id"] = id, ["type"] = type, ["displayName"] = displayName, ["order"] = order };
            if (optional) step["optional"] = true;
            if (configuration != null) step["configuration"] = configuration;
            if (branches != null) step["branches"] = branches;
            return step;
        }

        // Sign-in policy
        dbContext.JourneyPolicies.Add(new JourneyPolicyEntity
        {
            Id = "signin", TenantId = tenantId, Name = "Sign In", Type = "SignIn",
            Description = "Default sign-in policy with optional MFA", Enabled = true, Priority = 100,
            Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
            {
                Step("login", "local_login", "Sign In", 1, configuration: new Dictionary<string, object> { ["allowRememberMe"] = true, ["allowSelfRegistration"] = false }),
                Step("mfa", "mfa", "Multi-Factor Authentication", 2, optional: true, configuration: new Dictionary<string, object> { ["required"] = false, ["methods"] = new[] { "totp", "phone" } }),
                Step("consent", "consent", "Consent", 3)
            }, jsonOptions),
            CreatedAt = now, UpdatedAt = now
        });

        // Sign-up/Sign-in combined policy
        dbContext.JourneyPolicies.Add(new JourneyPolicyEntity
        {
            Id = "signup-signin", TenantId = tenantId, Name = "Sign Up or Sign In", Type = "SignInSignUp",
            Description = "Combined sign-up and sign-in flow", Enabled = true, Priority = 90,
            Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
            {
                Step("login", "local_login", "Sign In or Sign Up", 1, configuration: new Dictionary<string, object> { ["allowRememberMe"] = true, ["allowSelfRegistration"] = true }, branches: new Dictionary<string, string> { ["signup"] = "create_user" }),
                Step("create_user", "create_user", "Create Account", 2, optional: true),
                Step("consent", "consent", "Consent", 3)
            }, jsonOptions),
            CreatedAt = now, UpdatedAt = now
        });

        // Sign-up policy
        dbContext.JourneyPolicies.Add(new JourneyPolicyEntity
        {
            Id = "signup", TenantId = tenantId, Name = "Sign Up", Type = "SignUp",
            Description = "Self-registration flow", Enabled = true, Priority = 95,
            Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
            {
                Step("create_user", "create_user", "Create Account", 1),
                Step("consent", "consent", "Consent", 2)
            }, jsonOptions),
            CreatedAt = now, UpdatedAt = now
        });

        // Password reset policy
        dbContext.JourneyPolicies.Add(new JourneyPolicyEntity
        {
            Id = "password-reset", TenantId = tenantId, Name = "Password Reset", Type = "PasswordReset",
            Description = "Self-service password reset", Enabled = true, Priority = 100,
            Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
            {
                Step("reset", "password_reset", "Reset Password", 1)
            }, jsonOptions),
            CreatedAt = now, UpdatedAt = now
        });

        // Profile edit policy
        dbContext.JourneyPolicies.Add(new JourneyPolicyEntity
        {
            Id = "profile-edit", TenantId = tenantId, Name = "Edit Profile", Type = "ProfileEdit",
            Description = "Update user profile information", Enabled = true, Priority = 100,
            Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
            {
                Step("update", "update_user", "Update Profile", 1)
            }, jsonOptions),
            CreatedAt = now, UpdatedAt = now
        });

        // MFA-required sign-in policy
        dbContext.JourneyPolicies.Add(new JourneyPolicyEntity
        {
            Id = "signin-mfa", TenantId = tenantId, Name = "Sign In with MFA", Type = "SignIn",
            Description = "Sign-in requiring multi-factor authentication", Enabled = true, Priority = 85,
            Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
            {
                Step("login", "local_login", "Sign In", 1),
                Step("mfa", "mfa", "Multi-Factor Authentication", 2, configuration: new Dictionary<string, object> { ["required"] = true }),
                Step("consent", "consent", "Consent", 3)
            }, jsonOptions),
            Conditions = System.Text.Json.JsonSerializer.Serialize(new List<object>
            {
                new Dictionary<string, object> { ["type"] = "AcrValue", ["operator"] = "contains", ["value"] = "mfa" }
            }, jsonOptions),
            CreatedAt = now, UpdatedAt = now
        });

        await dbContext.SaveChangesAsync();
        Console.WriteLine("Seeded default journey policies");
    }
}
