using Microsoft.EntityFrameworkCore;
using Oluso;
using Oluso.Account;
using Oluso.Admin;
using Oluso.EntityFramework;
using Oluso.Enterprise.Fido2;
using Oluso.Enterprise.Ldap;
using Oluso.Enterprise.Saml;
using Oluso.Enterprise.Scim;
using Oluso.Webhooks;
using Oluso.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Get database configuration
// Set Oluso:Database:Provider to "SqlServer", "PostgreSQL", or "Sqlite" (default)
var connectionString = builder.Configuration.GetConnectionString("OlusoDb")!;
var provider = builder.Configuration.GetValue<string>("Oluso:Database:Provider", "Sqlite")!;

// Add Oluso identity server with enterprise add-ons
var olusoBuilder = builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddAdminApi(mvc => mvc.AddOlusoAdmin().AddOlusoAccount()) // Register Admin + Account API controllers
    .AddFileSystemPluginStore(Path.Combine(builder.Environment.ContentRootPath, "plugins")) // WASM plugin storage
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    // Enterprise: FIDO2/WebAuthn Passkey authentication
    // Origins are dynamically validated based on RP ID - any localhost:* origin is accepted
    .AddFido2(fido2 => fido2
        .WithRelyingParty(
            builder.Configuration.GetValue<string>("Oluso:Fido2:RelyingPartyId", "localhost")!,
            builder.Configuration.GetValue<string>("Oluso:Fido2:RelyingPartyName", "Oluso Sample")!)
        .RequireUserVerification()
        .PreferPlatformAuthenticator())
    // Enterprise: LDAP/Active Directory authentication (client mode - for authenticating against external LDAP)
    .AddLdap()
    // Signing key management
    .AddSigningKeys();
    

// Enterprise: LDAP Server
builder.Services.AddLdapServer(builder.Configuration)
    .AddLdapForProvider(provider, connectionString);

// Enterprise: SAML 2.0 (SP and IdP)
builder.Services.AddSaml(builder.Configuration)
    .AddSamlForProvider(provider, connectionString);

// Enterprise: SCIM 2.0 provisioning
builder.Services.AddScim(options =>
{
    options.BasePath = "/scim/v2";
    options.MaxResults = 200;
    options.SoftDeleteUsers = true;
    options.LogRetention = TimeSpan.FromDays(90);
})
.AddScimForProvider(provider, connectionString);

// Webhook dispatching
builder.Services.AddOlusoWebhooks();
builder.Services.AddCoreWebhookEvents();
builder.Services.AddOlusoNullTelemetry();

// CORS is handled dynamically by OlusoCorsPolicyProvider which checks:
// 1. Cors:Origins from appsettings (for admin UI, dev servers)
// 2. Client.AllowedCorsOrigins from database (for OAuth SPAs)
builder.Services.AddCors(options =>
{
    // Empty "Oluso" policy - the ICorsPolicyProvider builds it dynamically
    options.AddPolicy("Oluso", _ => { });
});

// Add controllers and apply Oluso conventions
builder.Services.AddControllers()
    .ApplyOlusoConventions(olusoBuilder);

// Add Razor Pages for login UI
builder.Services.AddRazorPages();

// Add Swagger for API exploration
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply database migrations for all registered migratable DbContexts
// This replaces EnsureCreatedAsync() and allows proper schema versioning
await app.MigrateOlusoDatabaseAsync();

// Initialize database with seed data
// Note: Plugin seeding (like SCIM) is now handled automatically via ISeedableDbContext
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    await SeedDataAsync(dbContext, configuration);
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();

// CORS - uses dynamic OlusoCorsPolicyProvider
app.UseCors("Oluso");

// Add Oluso middleware (tenant resolution, OIDC CORS)
app.UseOluso();

app.UseAuthentication();
app.UseAuthorization();
app.UseSession(); // Required for FIDO2 registration/assertion state

app.MapRazorPages();
app.MapControllers();

// Enterprise: SAML endpoints
app.MapSamlEndpoints();

// Enterprise: SCIM endpoints (auto-discovered via [ApiController])
app.UseScim();

// Health check
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

// Config URLs endpoint (for frontend dashboards)
app.MapGet("/api/config/urls", (IConfiguration config) => Results.Ok(new
{
    adminUiUrl = config.GetValue<string>("Oluso:Urls:AdminUiUrl", "http://localhost:3100"),
    accountUiUrl = config.GetValue<string>("Oluso:Urls:AccountUiUrl", "http://localhost:5173")
}));

// Serve the landing page at root
app.MapGet("/", () => Results.Redirect("/index.html"));

// JSON info endpoint (for programmatic access)
app.MapGet("/info", () => Results.Ok(new
{
    Name = "Oluso Sample",
    Version = "1.0.0",
    Discovery = "/.well-known/openid-configuration",
    Enterprise = new
    {
        Fido2 = "Passkey authentication enabled",
        Ldap = new
        {
            Server = "LDAP server on port 10389 (test mode)",
            TestPage = "/test/ldap.html"
        },
        Saml = new
        {
            Metadata = "/saml/metadata",
            IdpMetadata = "/saml/idp/metadata",
            TestPage = "/test/saml.html"
        },
        Scim = new
        {
            Users = "/scim/v2/Users",
            Groups = "/scim/v2/Groups",
            ServiceProviderConfig = "/scim/v2/ServiceProviderConfig"
        }
    },
    Testing = new
    {
        LdapTest = "/test/ldap.html",
        SamlTest = "/test/saml.html",
        OidcTest = "/test/oidc.html"
    }
}));

app.Run();

static async Task SeedDataAsync(OlusoDbContext dbContext, IConfiguration configuration)
{
    // Get URLs from configuration (under Oluso section)
    var baseUrl = configuration.GetValue<string>("Oluso:Urls:BaseUrl", "http://localhost:5050")!;
    var testClientUrl = configuration.GetValue<string>("Oluso:Urls:TestClientUrl", "http://localhost:5100")!;
    var accountUiUrl = configuration.GetValue<string>("Oluso:Urls:AccountUiUrl", "http://localhost:5173")!;
    var accountSaasUrl = configuration.GetValue<string>("Oluso:Urls:AccountSaasUrl", "http://localhost:3101")!;
    var frontendUrl = configuration.GetValue<string>("Oluso:Urls:FrontendUrl", "http://localhost:3000")!;

    // Seed default tenant
    if (!await dbContext.Tenants.AnyAsync())
    {
        dbContext.Tenants.Add(new Oluso.Core.Domain.Entities.Tenant
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
            AllowSelfRegistration = false            
        });
        await dbContext.SaveChangesAsync();
    }

    // Seed a test client
    if (!await dbContext.Clients.AnyAsync())
    {
        var client = new Oluso.Core.Domain.Entities.Client
        {
            ClientId = "test-client",
            ClientName = "Test Client",
            TenantId = "default",
            Enabled = true,
            RequireClientSecret = true,
            RequirePkce = false, // Disable PKCE for simpler testing
            AllowOfflineAccess = true
        };

        // Add client secret (test-secret hashed with SHA256)
        client.ClientSecrets.Add(new Oluso.Core.Domain.Entities.ClientSecret
        {
            Value = "nK8Gu0Q2zb+iCvkSGmJrwQk8T1SzHA+pN5V4VhNTRbY=", // SHA256 of "test-secret"
            Type = "SharedSecret"
        });

        // Add grant types (Authorization Code flow only for this client)
        client.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "authorization_code" });
        client.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "refresh_token" });

        // Add redirect URIs
        client.RedirectUris.Add(new Oluso.Core.Domain.Entities.ClientRedirectUri { RedirectUri = $"{frontendUrl}/callback" });
        client.RedirectUris.Add(new Oluso.Core.Domain.Entities.ClientRedirectUri { RedirectUri = $"{baseUrl}/test/oidc/callback" }); // OIDC test harness
        client.RedirectUris.Add(new Oluso.Core.Domain.Entities.ClientRedirectUri { RedirectUri = $"{testClientUrl}/signin-oidc" }); // Test client
        client.RedirectUris.Add(new Oluso.Core.Domain.Entities.ClientRedirectUri { RedirectUri = "https://oauth.pstmn.io/v1/callback" });

        // Add post logout redirect URIs
        client.PostLogoutRedirectUris.Add(new Oluso.Core.Domain.Entities.ClientPostLogoutRedirectUri { PostLogoutRedirectUri = frontendUrl });
        client.PostLogoutRedirectUris.Add(new Oluso.Core.Domain.Entities.ClientPostLogoutRedirectUri { PostLogoutRedirectUri = $"{testClientUrl}/signout-callback-oidc" });

        // Add scopes
        client.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "openid" });
        client.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "profile" });
        client.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "email" });

        dbContext.Clients.Add(client);

        // Client Credentials Grant Client
        var ccClient = new Oluso.Core.Domain.Entities.Client
        {
            ClientId = "cc-client",
            ClientName = "Client Credentials Client",
            TenantId = "default",
            Enabled = true,
            RequireClientSecret = true,
            RequirePkce = false,
            AllowOfflineAccess = false
        };

        ccClient.ClientSecrets.Add(new Oluso.Core.Domain.Entities.ClientSecret
        {
            Value = "nK8Gu0Q2zb+iCvkSGmJrwQk8T1SzHA+pN5V4VhNTRbY=", // SHA256 of "test-secret"
            Type = "SharedSecret"
        });

        ccClient.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "client_credentials" });
        ccClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "openid" });
        ccClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "api" });

        dbContext.Clients.Add(ccClient);

        // Resource Owner Password Grant Client
        var ropcClient = new Oluso.Core.Domain.Entities.Client
        {
            ClientId = "ropc-client",
            ClientName = "Resource Owner Password Client",
            TenantId = "default",
            Enabled = true,
            RequireClientSecret = true,
            RequirePkce = false,
            AllowOfflineAccess = true
        };

        ropcClient.ClientSecrets.Add(new Oluso.Core.Domain.Entities.ClientSecret
        {
            Value = "nK8Gu0Q2zb+iCvkSGmJrwQk8T1SzHA+pN5V4VhNTRbY=", // SHA256 of "test-secret"
            Type = "SharedSecret"
        });

        ropcClient.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "password" });
        ropcClient.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "refresh_token" });
        ropcClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "openid" });
        ropcClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "profile" });
        ropcClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "email" });
        ropcClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "offline_access" });

        dbContext.Clients.Add(ropcClient);

        // Device Authorization Grant Client
        var deviceClient = new Oluso.Core.Domain.Entities.Client
        {
            ClientId = "device-client",
            ClientName = "Device Flow Client",
            TenantId = "default",
            Enabled = true,
            RequireClientSecret = false, // Public client for device flow
            RequirePkce = false,
            AllowOfflineAccess = true
        };

        deviceClient.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "urn:ietf:params:oauth:grant-type:device_code" });
        deviceClient.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "refresh_token" });
        deviceClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "openid" });
        deviceClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "profile" });
        deviceClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "offline_access" });

        dbContext.Clients.Add(deviceClient);

        // Account UI Client (SPA - public client with PKCE)
        var accountUiClient = new Oluso.Core.Domain.Entities.Client
        {
            ClientId = "account-ui",
            ClientName = "Account Management UI",
            TenantId = "default",
            Enabled = true,
            RequireClientSecret = false, // Public client (SPA)
            RequirePkce = true, // Require PKCE for security
            AllowOfflineAccess = true
        };

        accountUiClient.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "authorization_code" });
        accountUiClient.AllowedGrantTypes.Add(new Oluso.Core.Domain.Entities.ClientGrantType { GrantType = "refresh_token" });

        // Redirect URIs for various dev environments
        accountUiClient.RedirectUris.Add(new Oluso.Core.Domain.Entities.ClientRedirectUri { RedirectUri = $"{accountUiUrl}/callback" }); // AccountUI dev
        accountUiClient.RedirectUris.Add(new Oluso.Core.Domain.Entities.ClientRedirectUri { RedirectUri = $"{accountSaasUrl}/callback" }); // account-saas shell

        // Post logout redirect URIs
        accountUiClient.PostLogoutRedirectUris.Add(new Oluso.Core.Domain.Entities.ClientPostLogoutRedirectUri { PostLogoutRedirectUri = accountUiUrl });
        accountUiClient.PostLogoutRedirectUris.Add(new Oluso.Core.Domain.Entities.ClientPostLogoutRedirectUri { PostLogoutRedirectUri = accountSaasUrl });

        // CORS origins for SPA
        accountUiClient.AllowedCorsOrigins.Add(new Oluso.Core.Domain.Entities.ClientCorsOrigin { Origin = accountUiUrl });
        accountUiClient.AllowedCorsOrigins.Add(new Oluso.Core.Domain.Entities.ClientCorsOrigin { Origin = accountSaasUrl });

        // Scopes - include account for account management APIs
        accountUiClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "openid" });
        accountUiClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "profile" });
        accountUiClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "email" });
        accountUiClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "account" });
        accountUiClient.AllowedScopes.Add(new Oluso.Core.Domain.Entities.ClientScope { Scope = "offline_access" });

        dbContext.Clients.Add(accountUiClient);

        await dbContext.SaveChangesAsync();
    }

    // Seed identity resources if not present
    if (!await dbContext.IdentityResources.AnyAsync())
    {
        dbContext.IdentityResources.AddRange(
            new Oluso.Core.Domain.Entities.IdentityResource
            {
                Name = "openid",
                DisplayName = "Your user identifier",
                Required = true,
                TenantId = "default"
            },
            new Oluso.Core.Domain.Entities.IdentityResource
            {
                Name = "profile",
                DisplayName = "User profile",
                Description = "Your user profile information (first name, last name, etc.)",
                TenantId = "default"
            },
            new Oluso.Core.Domain.Entities.IdentityResource
            {
                Name = "email",
                DisplayName = "Your email address",
                TenantId = "default"
            },
            new Oluso.Core.Domain.Entities.IdentityResource
            {
                Name = "account",
                DisplayName = "Account Management",
                Description = "Access to manage your account settings, sessions, and connected applications",
                TenantId = "default"
            }
        );
        await dbContext.SaveChangesAsync();
    }

    // Seed roles if not present
    if (!await dbContext.Roles.AnyAsync())
    {
        await SeedRolesAsync(dbContext);
        Console.WriteLine("Seeded default roles");
    }

    // Seed users if not present (includes admin users)
    if (!await dbContext.Users.AnyAsync())
    {
        await SeedUsersAsync(dbContext);
    }

    // Seed journey policies if not present
    if (!await dbContext.JourneyPolicies.AnyAsync())
    {
        await SeedJourneyPoliciesAsync(dbContext, "default");
        Console.WriteLine("Seeded default journey policies");
    }
}

static async Task SeedJourneyPoliciesAsync(OlusoDbContext dbContext, string tenantId)
{
    var now = DateTime.UtcNow;
    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    // Helper to create step dictionaries
    static Dictionary<string, object?> Step(string id, string type, string displayName, int order,
        bool optional = false, Dictionary<string, object>? configuration = null, Dictionary<string, string>? branches = null)
    {
        var step = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = type,
            ["displayName"] = displayName,
            ["order"] = order
        };
        if (optional) step["optional"] = true;
        if (configuration != null) step["configuration"] = configuration;
        if (branches != null) step["branches"] = branches;
        return step;
    }

    // Default sign-in policy
    dbContext.JourneyPolicies.Add(new Oluso.Core.Domain.Entities.JourneyPolicyEntity
    {
        Id = "signin",
        TenantId = tenantId,
        Name = "Sign In",
        Type = "SignIn",
        Description = "Default sign-in policy with optional MFA",
        Enabled = true,
        Priority = 100,
        Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
        {
            Step("login", "local_login", "Sign In", 1,
                configuration: new Dictionary<string, object> { ["allowRememberMe"] = true, ["allowSelfRegistration"] = false }),
            Step("mfa", "mfa", "Multi-Factor Authentication", 2, optional: true,
                configuration: new Dictionary<string, object> { ["required"] = false, ["methods"] = new[] { "totp", "phone" } }),
            Step("consent", "consent", "Consent", 3)
        }, jsonOptions),
        CreatedAt = now,
        UpdatedAt = now
    });

    // Sign-up/Sign-in combined policy
    dbContext.JourneyPolicies.Add(new Oluso.Core.Domain.Entities.JourneyPolicyEntity
    {
        Id = "signup-signin",
        TenantId = tenantId,
        Name = "Sign Up or Sign In",
        Type = "SignInSignUp",
        Description = "Combined sign-up and sign-in flow",
        Enabled = true,
        Priority = 90,
        Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
        {
            Step("login", "local_login", "Sign In or Sign Up", 1,
                configuration: new Dictionary<string, object> { ["allowRememberMe"] = true, ["allowSelfRegistration"] = true },
                branches: new Dictionary<string, string> { ["signup"] = "create_user" }),
            Step("create_user", "create_user", "Create Account", 2, optional: true),
            Step("consent", "consent", "Consent", 3)
        }, jsonOptions),
        CreatedAt = now,
        UpdatedAt = now
    });

    // Sign-up only policy
    dbContext.JourneyPolicies.Add(new Oluso.Core.Domain.Entities.JourneyPolicyEntity
    {
        Id = "signup",
        TenantId = tenantId,
        Name = "Sign Up",
        Type = "SignUp",
        Description = "Self-registration flow",
        Enabled = true,
        Priority = 95,
        Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
        {
            Step("create_user", "create_user", "Create Account", 1),
            Step("consent", "consent", "Consent", 2)
        }, jsonOptions),
        CreatedAt = now,
        UpdatedAt = now
    });

    // Password reset policy
    dbContext.JourneyPolicies.Add(new Oluso.Core.Domain.Entities.JourneyPolicyEntity
    {
        Id = "password-reset",
        TenantId = tenantId,
        Name = "Password Reset",
        Type = "PasswordReset",
        Description = "Self-service password reset",
        Enabled = true,
        Priority = 100,
        Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
        {
            Step("reset", "password_reset", "Reset Password", 1)
        }, jsonOptions),
        CreatedAt = now,
        UpdatedAt = now
    });

    // Profile edit policy
    dbContext.JourneyPolicies.Add(new Oluso.Core.Domain.Entities.JourneyPolicyEntity
    {
        Id = "profile-edit",
        TenantId = tenantId,
        Name = "Edit Profile",
        Type = "ProfileEdit",
        Description = "Update user profile information",
        Enabled = true,
        Priority = 100,
        Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
        {
            Step("update", "update_user", "Update Profile", 1)
        }, jsonOptions),
        CreatedAt = now,
        UpdatedAt = now
    });

    // MFA-required sign-in policy
    dbContext.JourneyPolicies.Add(new Oluso.Core.Domain.Entities.JourneyPolicyEntity
    {
        Id = "signin-mfa",
        TenantId = tenantId,
        Name = "Sign In with MFA",
        Type = "SignIn",
        Description = "Sign-in requiring multi-factor authentication",
        Enabled = true,
        Priority = 85,
        Steps = System.Text.Json.JsonSerializer.Serialize(new List<object>
        {
            Step("login", "local_login", "Sign In", 1),
            Step("mfa", "mfa", "Multi-Factor Authentication", 2,
                configuration: new Dictionary<string, object> { ["required"] = true }),
            Step("consent", "consent", "Consent", 3)
        }, jsonOptions),
        Conditions = System.Text.Json.JsonSerializer.Serialize(new List<object>
        {
            new Dictionary<string, object> { ["type"] = "AcrValue", ["operator"] = "contains", ["value"] = "mfa" }
        }, jsonOptions),
        CreatedAt = now,
        UpdatedAt = now
    });

    await dbContext.SaveChangesAsync();
}

static async Task SeedRolesAsync(OlusoDbContext dbContext)
{
    var now = DateTime.UtcNow;

    // System-level roles (TenantId = null, IsSystemRole = true)
    // SuperAdmin: Full system access across all tenants
    dbContext.Roles.Add(new Oluso.Core.Domain.Entities.OlusoRole
    {
        Id = Guid.NewGuid().ToString(),
        Name = "SuperAdmin",
        NormalizedName = "SUPERADMIN",
        DisplayName = "Super Administrator",
        Description = "Full system access across all tenants",
        TenantId = null, // System-level role
        IsSystemRole = true,
        CreatedAt = now
    });

    // Tenant-level roles (TenantId = "default", IsSystemRole = false)
    // Admin: Full access within the tenant
    dbContext.Roles.Add(new Oluso.Core.Domain.Entities.OlusoRole
    {
        Id = Guid.NewGuid().ToString(),
        Name = "Admin",
        NormalizedName = "ADMIN",
        DisplayName = "Administrator",
        Description = "Full access within the tenant",
        TenantId = "default",
        IsSystemRole = false,
        CreatedAt = now
    });

    // User: Standard user role
    dbContext.Roles.Add(new Oluso.Core.Domain.Entities.OlusoRole
    {
        Id = Guid.NewGuid().ToString(),
        Name = "User",
        NormalizedName = "USER",
        DisplayName = "User",
        Description = "Standard user access",
        TenantId = "default",
        IsSystemRole = false,
        CreatedAt = now
    });

    await dbContext.SaveChangesAsync();
}

static async Task SeedUsersAsync(OlusoDbContext dbContext)
{
    var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<Oluso.Core.Domain.Entities.OlusoUser>();

    // Get roles for assignment
    var superAdminRole = await dbContext.Roles.FirstAsync(r => r.Name == "SuperAdmin");
    var adminRole = await dbContext.Roles.FirstAsync(r => r.Name == "Admin");
    var userRole = await dbContext.Roles.FirstAsync(r => r.Name == "User");

    // 1. SuperAdmin user (system-level, TenantId = null)
    var superAdmin = new Oluso.Core.Domain.Entities.OlusoUser
    {
        Id = Guid.NewGuid().ToString(),
        UserName = "superadmin@localhost",
        NormalizedUserName = "SUPERADMIN@LOCALHOST",
        Email = "superadmin@localhost",
        NormalizedEmail = "SUPERADMIN@LOCALHOST",
        EmailConfirmed = true,
        TenantId = null, // System-level user (can access any tenant)
        FirstName = "Super",
        LastName = "Admin",
        DisplayName = "Super Administrator",
        IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString()
    };
    superAdmin.PasswordHash = passwordHasher.HashPassword(superAdmin, "SuperAdmin123!");
    dbContext.Users.Add(superAdmin);

    // Assign SuperAdmin role
    dbContext.Set<Oluso.Core.Domain.Entities.OlusoUserRole>().Add(new Oluso.Core.Domain.Entities.OlusoUserRole
    {
        UserId = superAdmin.Id,
        RoleId = superAdminRole.Id,
        AssignedAt = DateTime.UtcNow,
        AssignedBy = "system"
    });

    // 2. Tenant Admin user (tenant-scoped, TenantId = "default")
    var tenantAdmin = new Oluso.Core.Domain.Entities.OlusoUser
    {
        Id = Guid.NewGuid().ToString(),
        UserName = "admin@default.local",
        NormalizedUserName = "ADMIN@DEFAULT.LOCAL",
        Email = "admin@default.local",
        NormalizedEmail = "ADMIN@DEFAULT.LOCAL",
        EmailConfirmed = true,
        TenantId = "default", // Tenant-scoped admin
        FirstName = "Tenant",
        LastName = "Admin",
        DisplayName = "Default Tenant Administrator",
        IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString()
    };
    tenantAdmin.PasswordHash = passwordHasher.HashPassword(tenantAdmin, "TenantAdmin123!");
    dbContext.Users.Add(tenantAdmin);

    // Assign Admin role
    dbContext.Set<Oluso.Core.Domain.Entities.OlusoUserRole>().Add(new Oluso.Core.Domain.Entities.OlusoUserRole
    {
        UserId = tenantAdmin.Id,
        RoleId = adminRole.Id,
        AssignedAt = DateTime.UtcNow,
        AssignedBy = "system"
    });

    // 3. Regular test user (tenant-scoped)
    var testUser = new Oluso.Core.Domain.Entities.OlusoUser
    {
        Id = Guid.NewGuid().ToString(),
        UserName = "testuser@example.com",
        NormalizedUserName = "TESTUSER@EXAMPLE.COM",
        Email = "testuser@example.com",
        NormalizedEmail = "TESTUSER@EXAMPLE.COM",
        EmailConfirmed = true,
        TenantId = "default",
        FirstName = "Test",
        LastName = "User",
        DisplayName = "Test User",
        IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString()
    };
    testUser.PasswordHash = passwordHasher.HashPassword(testUser, "Password123!");
    dbContext.Users.Add(testUser);

    // Assign User role
    dbContext.Set<Oluso.Core.Domain.Entities.OlusoUserRole>().Add(new Oluso.Core.Domain.Entities.OlusoUserRole
    {
        UserId = testUser.Id,
        RoleId = userRole.Id,
        AssignedAt = DateTime.UtcNow,
        AssignedBy = "system"
    });

    await dbContext.SaveChangesAsync();

    Console.WriteLine("Seeded users:");
    Console.WriteLine("  SuperAdmin:   superadmin@localhost / SuperAdmin123!");
    Console.WriteLine("  Tenant Admin: admin@default.local / TenantAdmin123!");
    Console.WriteLine("  Test User:    testuser@example.com / Password123!");
}

// Expose Program class for integration tests
public partial class Program { }
