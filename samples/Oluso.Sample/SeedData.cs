using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Admin.Controllers.Service;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Licensing;
using Oluso.EntityFramework;

namespace Oluso.Sample;

public static class SeedData
{
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Sample:SeedTestData=false seeds only what a working installation needs: no test accounts or
        // test clients, and a platform administrator from Sample:SuperAdminEmail/SuperAdminPassword.
        var seedTestData = configuration.GetValue("Sample:SeedTestData", true);

        await SeedPlatformOrganizationAsync(dbContext);
        await SeedPlatformTenantAsync(dbContext);
        await SeedOrganizationsAsync(dbContext);
        await SeedTenantsAsync(dbContext);
        await SeedServiceApiScopesAsync(dbContext);
        if (seedTestData)
            await SeedTestClientsAsync(dbContext, configuration);
        await SeedAccountUiClientAsync(dbContext, configuration);
        await SeedPlatformClientsAsync(dbContext, configuration);
        await SeedIdentityResourcesAsync(dbContext);
        await SeedRolesAsync(dbContext);
        if (seedTestData)
            await SeedUsersAsync(dbContext, configuration);
        else
            await SeedPlatformAdministratorAsync(scope.ServiceProvider, dbContext, configuration);
        await SeedJourneyPoliciesAsync(dbContext, "default");
        await SeedEmailTemplatesAsync(scope.ServiceProvider, "default");
    }

    private static async Task SeedEmailTemplatesAsync(IServiceProvider services, string tenantId)
    {
        var persister = services.GetService<ITenantSeedPersister>();
        if (persister == null)
            return;

        var modules = ModuleRegistry.Instance.GetRegisteredModules();
        var allTemplates = new List<EmailTemplateSeed>();

        foreach (var module in modules)
        {
            try
            {
                allTemplates.AddRange(module.GetEmailTemplateSeeds());
            }
            catch
            {
                // Skip modules that fail to provide seeds
            }
        }

        if (allTemplates.Count > 0)
        {
            await persister.SeedEmailTemplatesAsync(tenantId, allTemplates);
            await persister.SeedWorkflowsForTemplatesAsync(tenantId, allTemplates);
        }
    }

    private static async Task SeedPlatformOrganizationAsync(OlusoDbContext dbContext)
    {
        var existing = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == PlatformConstants.OrganizationId);

        if (existing != null) return;

        dbContext.Organizations.Add(new Organization
        {
            Id = PlatformConstants.OrganizationId,
            Name = PlatformConstants.OrganizationName,
            Slug = "_platform",
            Enabled = true,
            Description = "Platform-level organization for system-owned entities",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        Console.WriteLine("Seeded platform organization (_platform-org)");
    }

    private static async Task SeedPlatformTenantAsync(OlusoDbContext dbContext)
    {
        var existing = await dbContext.Tenants!
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == PlatformConstants.TenantId);

        if (existing != null) return;

        var platformTenant = new Tenant
        {
            Id = PlatformConstants.TenantId,
            Name = PlatformConstants.TenantName,
            DisplayName = "Platform",
            Identifier = "_platform",
            Enabled = true,
            OrganizationId = PlatformConstants.OrganizationId,
            Environment = TenantEnvironment.Production,
            AllowSelfRegistration = false
            // No custom domain, no branding - purely for entity storage
        };

        dbContext.Tenants!.Add(platformTenant);
        await dbContext.SaveChangesAsync();
        Console.WriteLine("Seeded platform tenant (_platform)");
    }

    private static async Task SeedPlatformClientsAsync(OlusoDbContext dbContext, IConfiguration configuration)
    {
        // Use IgnoreQueryFilters to check if already seeded
        var existingClients = await dbContext.Clients
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == PlatformConstants.TenantId)
            .Select(c => c.ClientId)
            .ToListAsync();

        var adminPortalUrl = configuration.GetValue<string>("Oluso:Urls:AdminPortalUrl")
            ?? configuration.GetValue<string>("Oluso:Urls:AdminUiUrl") ?? "http://localhost:4200";
        var workspacePortalUrl = configuration.GetValue<string>("Oluso:Urls:WorkspacePortalUrl")
            ?? configuration.GetValue<string>("Oluso:Urls:WorkspaceUiUrl") ?? "http://localhost:4203";

        if (!existingClients.Contains("admin-portal"))
        {
            var adminPortal = new Client
            {
                TenantId = PlatformConstants.TenantId,
                ClientId = "admin-portal",
                ClientName = "Admin Portal",
                Description = "Platform-level admin portal client (multi-tenant)",
                Enabled = true,
                RequireClientSecret = false, // Public client
                RequirePkce = true,
                AllowOfflineAccess = true,
                NonEditable = true,
                AllowedGrantTypes = new List<ClientGrantType>
                {
                    new() { GrantType = "authorization_code", TenantId = PlatformConstants.TenantId },
                    new() { GrantType = "refresh_token", TenantId = PlatformConstants.TenantId }
                },
                AllowedScopes = new List<ClientScope>
                {
                    new() { Scope = "openid", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "profile", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "email", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "admin", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "permissions", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "offline_access", TenantId = PlatformConstants.TenantId }
                },
                RedirectUris = new List<ClientRedirectUri>
                {
                    new() { RedirectUri = $"{adminPortalUrl}/callback", TenantId = PlatformConstants.TenantId },
                    new() { RedirectUri = $"{adminPortalUrl}/silent-renew.html", TenantId = PlatformConstants.TenantId }
                },
                PostLogoutRedirectUris = new List<ClientPostLogoutRedirectUri>
                {
                    new() { PostLogoutRedirectUri = adminPortalUrl, TenantId = PlatformConstants.TenantId },
                    new() { PostLogoutRedirectUri = $"{adminPortalUrl}/logout", TenantId = PlatformConstants.TenantId }
                },
                AllowedCorsOrigins = new List<ClientCorsOrigin>
                {
                    new() { Origin = adminPortalUrl, TenantId = PlatformConstants.TenantId }
                },
                // Restrict access via permission - tenant admins can create custom roles with this permission
                RequiredPermission = "admin.access"
            };
            dbContext.Clients.Add(adminPortal);
            Console.WriteLine("Seeding platform client: admin-portal");
        }

        if (!existingClients.Contains("workspace-portal"))
        {
            var workspacePortal = new Client
            {
                TenantId = PlatformConstants.TenantId,
                ClientId = "workspace-portal",
                ClientName = "Workspace Portal",
                Description = "Platform-level workspace portal client (multi-tenant)",
                Enabled = true,
                RequireClientSecret = false,
                RequirePkce = true,
                AllowOfflineAccess = true,
                NonEditable = true,
                AllowedGrantTypes = new List<ClientGrantType>
                {
                    new() { GrantType = "authorization_code", TenantId = PlatformConstants.TenantId },
                    new() { GrantType = "refresh_token", TenantId = PlatformConstants.TenantId }
                },
                AllowedScopes = new List<ClientScope>
                {
                    new() { Scope = "openid", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "profile", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "email", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "workspace", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "offline_access", TenantId = PlatformConstants.TenantId }
                },
                RedirectUris = new List<ClientRedirectUri>
                {
                    new() { RedirectUri = $"{workspacePortalUrl}/callback", TenantId = PlatformConstants.TenantId },
                    new() { RedirectUri = $"{workspacePortalUrl}/silent-renew.html", TenantId = PlatformConstants.TenantId }
                },
                PostLogoutRedirectUris = new List<ClientPostLogoutRedirectUri>
                {
                    new() { PostLogoutRedirectUri = workspacePortalUrl, TenantId = PlatformConstants.TenantId },
                    new() { PostLogoutRedirectUri = $"{workspacePortalUrl}/logout", TenantId = PlatformConstants.TenantId }
                },
                AllowedCorsOrigins = new List<ClientCorsOrigin>
                {
                    new() { Origin = workspacePortalUrl, TenantId = PlatformConstants.TenantId }
                }
            };
            dbContext.Clients.Add(workspacePortal);
            Console.WriteLine("Seeding platform client: workspace-portal");
        }

        if (!existingClients.Contains("oluso-mobile"))
        {
            var mobileClient = new Client
            {
                TenantId = PlatformConstants.TenantId,
                ClientId = "oluso-mobile",
                ClientName = "Oluso Mobile",
                Description = "Native mobile companion app (MAUI). Public PKCE client; matches the value baked into /.well-known/oluso-instance.",
                Enabled = true,
                RequireClientSecret = false,
                RequirePkce = true,
                AllowOfflineAccess = true,
                NonEditable = true,
                AllowedGrantTypes = new List<ClientGrantType>
                {
                    new() { GrantType = "authorization_code", TenantId = PlatformConstants.TenantId },
                    new() { GrantType = "refresh_token", TenantId = PlatformConstants.TenantId }
                },
                AllowedScopes = new List<ClientScope>
                {
                    new() { Scope = "openid", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "profile", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "email", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "workspace", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "permissions", TenantId = PlatformConstants.TenantId },
                    new() { Scope = "offline_access", TenantId = PlatformConstants.TenantId }
                },
                RedirectUris = new List<ClientRedirectUri>
                {
                    new() { RedirectUri = "oluso://callback", TenantId = PlatformConstants.TenantId }
                },
                PostLogoutRedirectUris = new List<ClientPostLogoutRedirectUri>
                {
                    new() { PostLogoutRedirectUri = "oluso://callback", TenantId = PlatformConstants.TenantId }
                }
            };
            dbContext.Clients.Add(mobileClient);
            Console.WriteLine("Seeding platform client: oluso-mobile");
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedOrganizationsAsync(OlusoDbContext dbContext)
    {
        if (await dbContext.Organizations.AnyAsync(o => o.Id == "default-org"))
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
        if (await dbContext.Tenants!.AnyAsync(t => t.Id == "default"))
            return;

        dbContext.Tenants!.Add(new Tenant
        {
            Id = "default",
            Name = "Default",
            DisplayName = "Default Tenant",
            Identifier = "default",
            Enabled = true,
            Description = "Default tenant for development",
            UseJourneyFlow = true,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            AllowSelfRegistration = false,
            OrganizationId = "default-org",
            Environment = TenantEnvironment.Development
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedTestClientsAsync(OlusoDbContext dbContext, IConfiguration configuration)
    {
        if (await dbContext.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == "test-client"))
            return;

        var baseUrl = configuration.GetValue<string>("Oluso:Urls:BaseUrl", "http://localhost:5050")!;
        var testClientUrl = configuration.GetValue<string>("Oluso:Urls:TestClientUrl", "http://localhost:5100")!;
        var frontendUrl = configuration.GetValue<string>("Oluso:Urls:FrontendUrl", "http://localhost:3000")!;
        var workspacePortalUrl = configuration.GetValue<string>("Oluso:Urls:WorkspacePortalUrl", "http://localhost:5175")!;

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

        // Client Credentials client — granted the Service API (api/service/*),
        // matching what the admin UI's Service API toggle configures.
        var ccClient = CreateClient("cc-client", "Client Credentials Client", requireSecret: true, requirePkce: false, allowOffline: false);
        ccClient.ClientSecrets.Add(new ClientSecret { Value = HashSecret("test-secret"), Type = "SharedSecret" });
        ccClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "client_credentials" });
        ccClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        foreach (var scope in ServiceScopes.AllNames)
        {
            ccClient.AllowedScopes.Add(new ClientScope { Scope = scope });
        }
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

        // Interactive auth-code client used by integration tests to drive the full
        // browser login flow (authorize -> /account/login -> callback -> token).
        // In the 'default' tenant (where testuser lives) with UseJourneyFlow=false to
        // force the standalone login path, and offline_access allowed so the flow can
        // assert a refresh token is issued.
        var interactiveClient = CreateClient("interactive-test-client", "Interactive Test Client", requireSecret: true, requirePkce: false, allowOffline: true);
        interactiveClient.TenantId = "default";
        interactiveClient.UseJourneyFlow = false;
        interactiveClient.ClientSecrets.Add(new ClientSecret { Value = HashSecret("test-secret"), Type = "SharedSecret" });
        interactiveClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code" });
        interactiveClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token" });
        interactiveClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = "http://localhost:9876/callback" });
        interactiveClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        interactiveClient.AllowedScopes.Add(new ClientScope { Scope = "profile" });
        interactiveClient.AllowedScopes.Add(new ClientScope { Scope = "email" });
        interactiveClient.AllowedScopes.Add(new ClientScope { Scope = "offline_access" });
        dbContext.Clients.Add(interactiveClient);

        // Note: admin-portal and workspace-portal are platform-level clients
        // seeded by SeedPlatformClientsAsync (TenantId = _platform)

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedAccountUiClientAsync(OlusoDbContext dbContext, IConfiguration configuration)
    {
        if (await dbContext.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == "account-ui"))
            return;

        var accountUiUrl = configuration.GetValue<string>("Oluso:Urls:AccountUiUrl", "http://localhost:5173")!;
        var accountSaasUrl = configuration.GetValue<string>("Oluso:Urls:AccountSaasUrl", "http://localhost:3101")!;

        // Account UI client (SPA with PKCE) — lives in default tenant
        // Supports both React (5173) and Angular (4201) account UIs
        var accountUiAngularUrl = "http://localhost:4201";
        var accountUiClient = CreateClient("account-ui", "Account Management UI", requireSecret: false, requirePkce: true, allowOffline: true);
        accountUiClient.TenantId = "default";
        accountUiClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code" });
        accountUiClient.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token" });
        accountUiClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{accountUiUrl}/callback" });
        accountUiClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{accountSaasUrl}/callback" });
        accountUiClient.RedirectUris.Add(new ClientRedirectUri { RedirectUri = $"{accountUiAngularUrl}/callback" });
        accountUiClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = accountUiUrl });
        accountUiClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = $"{accountUiUrl}/logout" });
        accountUiClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = accountSaasUrl });
        accountUiClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = $"{accountSaasUrl}/logout" });
        accountUiClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = accountUiAngularUrl });
        accountUiClient.PostLogoutRedirectUris.Add(new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = $"{accountUiAngularUrl}/logout" });
        accountUiClient.AllowedCorsOrigins.Add(new ClientCorsOrigin { Origin = accountUiUrl });
        accountUiClient.AllowedCorsOrigins.Add(new ClientCorsOrigin { Origin = accountSaasUrl });
        accountUiClient.AllowedCorsOrigins.Add(new ClientCorsOrigin { Origin = accountUiAngularUrl });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "openid" });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "profile" });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "email" });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "account" });
        accountUiClient.AllowedScopes.Add(new ClientScope { Scope = "offline_access" });
        dbContext.Clients.Add(accountUiClient);

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// The ApiScope rows the Service API resolves against. Token issuance
    /// rejects a scope with no row, so these have to exist before a client can
    /// be granted service access — in the admin UI, ServiceApiScopeProvisioner
    /// creates them when the Service API toggle is switched on.
    /// </summary>
    private static async Task SeedServiceApiScopesAsync(OlusoDbContext dbContext)
    {
        // Same tenant as the sample's clients (see CreateClient) — an ApiScope in
        // another tenant is invisible to the query filter at token time.
        var existing = await dbContext.ApiScopes
            .Where(s => s.TenantId == PlatformConstants.TenantId && ServiceScopes.AllNames.Contains(s.Name))
            .Select(s => s.Name)
            .ToListAsync();

        foreach (var name in ServiceScopes.AllNames.Where(n => !existing.Contains(n)))
        {
            var definition = ServiceScopes.Find(name);

            dbContext.ApiScopes.Add(new ApiScope
            {
                Name = name,
                DisplayName = definition?.DisplayName ?? "Service API access",
                Description = definition?.Description
                    ?? "Grants access to the machine-to-machine Service API.",
                TenantId = PlatformConstants.TenantId,
                Enabled = true,
                ShowInDiscoveryDocument = false,
                NonEditable = true
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static Client CreateClient(string clientId, string clientName, bool requireSecret, bool requirePkce, bool allowOffline)
    {
        return new Client
        {
            ClientId = clientId,
            ClientName = clientName,
            TenantId = PlatformConstants.TenantId,
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
        if (await dbContext.IdentityResources.AnyAsync(r => r.TenantId == "default"))
            return;

        // UserClaims matter: a seeded row REPLACES the built-in defaults for that
        // scope name, so an empty one silently releases nothing.
        dbContext.IdentityResources.AddRange(
            new IdentityResource { Name = "openid", DisplayName = "Your user identifier", Required = true, TenantId = "default" },
            new IdentityResource
            {
                Name = "profile",
                DisplayName = "User profile",
                Description = "Your user profile information (first name, last name, etc.)",
                TenantId = "default",
                UserClaims = new List<IdentityResourceClaim>
                {
                    new() { Type = "name" },
                    new() { Type = "given_name" },
                    new() { Type = "family_name" },
                    new() { Type = "picture" }
                }
            },
            new IdentityResource
            {
                Name = "email",
                DisplayName = "Your email address",
                TenantId = "default",
                UserClaims = new List<IdentityResourceClaim>
                {
                    new() { Type = "email" },
                    new() { Type = "email_verified" }
                }
            },
            new IdentityResource { Name = "account", DisplayName = "Account Management", Description = "Access to manage your account settings, sessions, and connected applications", TenantId = "default" },
            new IdentityResource { Name = "workspace", DisplayName = "Workspace Portal", Description = "Access to workplace self-service features", TenantId = "default" }
        );
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedRolesAsync(OlusoDbContext dbContext)
    {
        // Check for the specific core roles (not just any role, since module roles may already exist)
        if (await dbContext.Roles.IgnoreQueryFilters().AnyAsync(r => r.Name == "SuperAdmin"))
            return;

        using var _ = dbContext.BeginSeedMode();

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

        await dbContext.SaveChangesAsync();

        // Add permissions to roles
        await SeedRolePermissionsAsync(dbContext, superAdminRole, orgAdminRole, adminRole, userRole);

        Console.WriteLine("Seeded default roles with permissions");
    }

    /// <summary>
    /// Re-seeds permissions for SuperAdmin, OrgAdmin, and Admin (TenantAdmin) roles
    /// without deleting the database. Wipes their existing permission claims and re-applies.
    /// Uncomment the call in Program.cs when you need to refresh these after code changes.
    /// </summary>
    public static async Task ReseedAdminRolePermissionsAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();

        var roleNames = new[] { "SuperAdmin", "OrgAdmin", "Admin" };
        var roles = await dbContext.Roles.IgnoreQueryFilters()
            .Where(r => roleNames.Contains(r.Name))
            .ToListAsync();

        if (roles.Count == 0)
        {
            Console.WriteLine("[Reseed] No admin roles found — skipping.");
            return;
        }

        var roleIds = roles.Select(r => r.Id).ToList();

        // Remove existing permission claims for these roles
        var existingClaims = await dbContext.RoleClaims.IgnoreQueryFilters()
            .Where(c => roleIds.Contains(c.RoleId) && c.ClaimType == "permission")
            .ToListAsync();

        dbContext.RoleClaims.RemoveRange(existingClaims);
        Console.WriteLine($"[Reseed] Removed {existingClaims.Count} existing permission claims.");

        var now = DateTime.UtcNow;
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

        var superAdmin = roles.FirstOrDefault(r => r.Name == "SuperAdmin");
        var orgAdmin = roles.FirstOrDefault(r => r.Name == "OrgAdmin");
        var admin = roles.FirstOrDefault(r => r.Name == "Admin");

        if (superAdmin != null)
        {
            foreach (var p in new[] { "*", "admin.access", "tenants.read", "tenants.write", "tenants.delete", "tenants.manage_settings" })
                AddPermission(superAdmin, p);
        }

        if (orgAdmin != null)
        {
            foreach (var p in new[] { "admin.access", "*" })
                AddPermission(orgAdmin, p);
        }

        if (admin != null)
        {
            foreach (var p in new[] { "admin.access", "*" })
                AddPermission(admin, p);
        }

        await dbContext.SaveChangesAsync();
        Console.WriteLine("[Reseed] Admin role permissions re-seeded successfully.");
    }

    private static async Task SeedRolePermissionsAsync(
        OlusoDbContext dbContext,
        OlusoRole superAdminRole,
        OlusoRole orgAdminRole,
        OlusoRole adminRole,
        OlusoRole userRole)
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

        // SuperAdmin gets wildcard — same as TenantAdmin but with superAdminOnly nav visibility.
        // No role-based bypass: RequiresExplicitGrant permissions (salary, payroll, fiscal close, etc.)
        // must still be explicitly assigned even for SuperAdmin.
        // Module administration permissions (hr.administration, etc.) also require explicit grant.
        var allPermissions = new[]
        {
            "*",
            // Admin dashboard access
            "admin.access",
            // Tenants permissions (SuperAdmin needs these for tenant management)
            "tenants.read", "tenants.write", "tenants.delete", "tenants.manage_settings"
        };

        foreach (var permission in allPermissions)
        {
            AddPermission(superAdminRole, permission);
        }

        // OrgAdmin gets * wildcard — full access to all standard admin operations
        // within their organization's tenants. The backend enforces org-scoping
        // (tenant must belong to caller's org). RequiresSuperAdmin permissions
        // (e.g., tenants.delete, platform.*) are excluded from wildcard expansion,
        // so org admins can't perform system-level operations.
        var orgAdminPermissions = new[]
        {
            "admin.access",
            "*"
        };

        foreach (var permission in orgAdminPermissions)
        {
            AddPermission(orgAdminRole, permission);
        }

        // Tenant Admin gets wildcard — full access to all standard admin operations.
        // Sensitive permissions (RequiresExplicitGrant) like salary/payroll and module administration
        // permissions are excluded from wildcard matching.
        // Frontend nav visibility is controlled by superAdminOnly flags (e.g., "Tenants" page hidden).
        var tenantAdminPermissions = new[]
        {
            // Admin dashboard access
            "admin.access",
            "*"
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

        // Module-specific role permissions (HRAdmin, PayrollAdmin, etc.) are now
        // seeded by SeedModuleRolesAsync() via PermissionRegistry module role definitions.

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// A test account the sample seeds; <see cref="DemoAccountReset"/> restores the same accounts. The fixed
    /// <paramref name="Id"/> lets the reset find an account whose email and user name were both changed.
    /// <paramref name="RoleTenantId"/> is the tenant the role is defined in (null for the platform roles).
    /// </summary>
    internal sealed record TestAccount(string Id, string Email, string FirstName, string LastName, string DisplayName, string RoleName, string? RoleTenantId, string Password);

    internal const string OrgAdminOrganizationId = "default-org";

    internal static readonly TestAccount[] TestAccounts =
    [
        new("test-superadmin", "superadmin@oluso.local", "Super", "Admin", "Super Administrator", "SuperAdmin", null, "SuperAdmin123!"),
        new("test-orgadmin", "orgadmin@oluso.local", "Org", "Admin", "Organization Administrator", "OrgAdmin", null, "OrgAdmin123!"),
        new("test-tenantadmin", "admin@default.local", "Tenant", "Admin", "Default Tenant Administrator", "Admin", "default", "TenantAdmin123!"),
        new("test-user", "testuser@example.com", "Test", "User", "Test User", "User", "default", "Password123!"),
    ];

    /// <summary>
    /// The tenant an account lives in: the platform tenant for the super admin, the first tenant of the
    /// organization it owns for the org admin, and the default tenant for the others.
    /// </summary>
    internal static async Task<string> TestAccountTenantIdAsync(OlusoDbContext dbContext, TestAccount account) => account.RoleName switch
    {
        "SuperAdmin" => PlatformConstants.TenantId,
        "OrgAdmin" => await dbContext.Tenants!
            .IgnoreQueryFilters()
            .Where(t => t.OrganizationId == OrgAdminOrganizationId)
            .OrderBy(t => t.Created)
            .Select(t => t.Id)
            .FirstAsync(),
        _ => "default",
    };

    /// <summary>The seeded role, matched on its tenant too, so a same-named role in another tenant can't stand in.</summary>
    internal static Task<OlusoRole> TestAccountRoleAsync(OlusoDbContext dbContext, TestAccount account, CancellationToken cancellationToken = default) =>
        dbContext.Roles.IgnoreQueryFilters().FirstAsync(r => r.Name == account.RoleName && r.TenantId == account.RoleTenantId, cancellationToken);

    /// <summary>The tenant the account's role assignment is recorded under.</summary>
    internal static string TestAccountRoleAssignmentTenantId(TestAccount account, OlusoUser user, OlusoRole role) =>
        account.RoleName is "SuperAdmin" or "OrgAdmin" ? user.TenantId! : role.TenantId!;

    /// <summary>
    /// The password an account is seeded and reset with: the published one, except the super admin's
    /// when the demo reset is on, which is never the published one, not even before the first reset.
    /// </summary>
    internal static string TestAccountPassword(TestAccount account, DemoAccountResetOptions demoReset) =>
        account.RoleName == "SuperAdmin" && demoReset.Enabled ? demoReset.SuperAdminPassword! : account.Password;

    /// <summary>Adds a test account (not saved): the user, its role, and for the org admin its organization ownership.</summary>
    internal static async Task<OlusoUser> AddTestAccountAsync(OlusoDbContext dbContext, TestAccount account, string password)
    {
        var role = await TestAccountRoleAsync(dbContext, account);
        var user = CreateUser(account.Email, account.FirstName, account.LastName, account.DisplayName,
            tenantId: await TestAccountTenantIdAsync(dbContext, account), id: account.Id);
        user.PasswordHash = new PasswordHasher<OlusoUser>().HashPassword(user, password);
        user.PasswordChangedAt = DateTime.UtcNow;
        dbContext.Users.Add(user);
        dbContext.Set<OlusoUserRole>().Add(new OlusoUserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            TenantId = TestAccountRoleAssignmentTenantId(account, user, role),
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "system"
        });

        if (account.RoleName == "OrgAdmin")
        {
            dbContext.OrganizationMemberships.Add(new OrganizationMembership
            {
                Id = Guid.NewGuid().ToString(),
                OrganizationId = OrgAdminOrganizationId,
                UserId = user.Id,
                Role = OrganizationRole.Owner,
                JoinedAt = DateTime.UtcNow
            });
        }

        return user;
    }

    private static async Task SeedUsersAsync(OlusoDbContext dbContext, IConfiguration configuration)
    {
        if (await dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == TestAccounts[0].Email))
            return;

        var demoReset = DemoAccountResetOptions.From(configuration);
        foreach (var account in TestAccounts)
            await AddTestAccountAsync(dbContext, account, TestAccountPassword(account, demoReset));

        await dbContext.SaveChangesAsync();

        Console.WriteLine("Seeded users:");
        foreach (var account in TestAccounts)
            Console.WriteLine($"  {account.DisplayName}: {account.Email}" + (TestAccountPassword(account, demoReset) == account.Password ? $" / {account.Password}" : " (password from Sample:DemoReset)"));
    }

    /// <summary>
    /// With test data off, the only account seeded is the platform administrator, from
    /// configuration. Once any user holds SuperAdmin this does nothing, so the password can be
    /// removed from configuration after the first start.
    /// </summary>
    private static async Task SeedPlatformAdministratorAsync(IServiceProvider services, OlusoDbContext dbContext, IConfiguration configuration)
    {
        var superAdminRole = await dbContext.Roles.IgnoreQueryFilters().FirstAsync(r => r.Name == "SuperAdmin");
        if (await dbContext.Set<OlusoUserRole>().IgnoreQueryFilters().AnyAsync(ur => ur.RoleId == superAdminRole.Id))
            return;

        var email = configuration["Sample:SuperAdminEmail"];
        var password = configuration["Sample:SuperAdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException(
                "Sample:SeedTestData is false and there is no platform administrator yet: set Sample:SuperAdminEmail and Sample:SuperAdminPassword for the first start.");

        var admin = CreateUser(email, "Platform", "Administrator", "Platform Administrator", tenantId: PlatformConstants.TenantId);

        var userManager = services.GetRequiredService<UserManager<OlusoUser>>();
        var errors = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, admin, password);
            errors.AddRange(result.Errors.Select(e => e.Description));
        }
        if (errors.Count > 0)
            throw new InvalidOperationException("Sample:SuperAdminPassword doesn't meet the password policy: " + string.Join(" ", errors));

        admin.PasswordHash = userManager.PasswordHasher.HashPassword(admin, password);
        dbContext.Users.Add(admin);
        dbContext.Set<OlusoUserRole>().Add(new OlusoUserRole { UserId = admin.Id, RoleId = superAdminRole.Id, TenantId = admin.TenantId!, AssignedAt = DateTime.UtcNow, AssignedBy = "system" });
        await dbContext.SaveChangesAsync();

        Console.WriteLine($"Seeded platform administrator: {email}");
    }

    private static OlusoUser CreateUser(string email, string firstName, string lastName, string displayName, string tenantId, string? id = null)
    {
        return new OlusoUser
        {
            Id = id ?? Guid.NewGuid().ToString(),
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
