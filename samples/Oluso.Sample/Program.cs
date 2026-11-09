using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Oluso;
using Oluso.UI;
using Oluso.Account;
using Oluso.Admin;
using Oluso.EntityFramework;
using Oluso.Enterprise.Fido2;
using Oluso.Enterprise.Ldap;
using Oluso.Enterprise.Saml;
using Oluso.Enterprise.Scim;
using Oluso.Enterprise.Verification;
using Oluso.Core.Licensing;
using Oluso.Messaging;
using Oluso.Webhooks;
using Oluso.Notifications;
using Oluso.Telemetry;
using Oluso.Telemetry.OpenTelemetry;
using Oluso.Telemetry.OpenTelemetry.Logging;
using Oluso.Enterprise.AzureKeyVault;
using Oluso.Core.Licensing;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Data Protection encrypts the signing keys' private keys and protected tenant settings, so its key
// ring has to outlive the process. dotnet run keeps it in your user profile; in a container, set
// DataProtection:KeysPath to a directory on a volume (the Docker image uses /app/data/dataprotection-keys).
// A relative path is under the app's folder (App_Data/dataprotection-keys on IIS hosting).
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrEmpty(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, dataProtectionKeysPath)));
}

// Add database logging for telemetry
builder.Logging.AddDatabaseLogger(options =>
{
    options.MinimumLevel = LogLevel.Information;
    // Exclude noisy categories
    options.ExcludeCategories.Add("Microsoft.EntityFrameworkCore");
    options.ExcludeCategories.Add("Microsoft.AspNetCore.Routing");
    options.ExcludeCategories.Add("Microsoft.AspNetCore.StaticFiles");
    options.ExcludeCategories.Add("Microsoft.AspNetCore.Cors");
    options.ExcludeCategories.Add("Microsoft.AspNetCore.Hosting");
    options.ExcludeCategories.Add("Microsoft.AspNetCore.Server");
});

// Get database configuration
// Set Oluso:Database:Provider to "SqlServer", "PostgreSQL", or "Sqlite" (default)
var connectionString = builder.Configuration.GetConnectionString("OlusoDb")!;
var provider = builder.Configuration.GetValue<string>("Oluso:Database:Provider", "Sqlite")!;

var olusoBuilder = builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddSigningKeys()
    // .AddOlusoAzureKeyVault(options =>
    // {
    //     options.VaultUri = builder.Configuration.GetValue<string>("Oluso:AzureKeyVault:VaultUri", string.Empty);
    //     options.UseHsmKeys = true;
    //     options.SkipLicenseValidation = true;
    // })
    .AddAdminApi(mvc => 
        mvc.AddOlusoAdmin()
        .AddOlusoAccount())
    .AddFileSystemPluginStore(Path.Combine(builder.Environment.ContentRootPath, "plugins"))
    .AddLocalFileStorage(Path.Combine(builder.Environment.ContentRootPath, "storage"))
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddFido2(fido2 => fido2
        .WithRelyingParty(
            builder.Configuration.GetValue<string>("Oluso:Fido2:RelyingPartyId", "localhost")!,
            builder.Configuration.GetValue<string>("Oluso:Fido2:RelyingPartyName", "Oluso Sample")!)
        .RequireUserVerification()
        .PreferPlatformAuthenticator())
    .AddLdap()
    .AddOlusoVerification();
    

// Enterprise: Azure Webhooks for queue-based webhook processing
// Uncomment when you have Azure Storage or Service Bus configured
// builder.Services.AddAzureQueueWebhookProcessor(options =>
// {
//     options.ConnectionString = builder.Configuration.GetConnectionString("AzureStorage")!;
//     options.QueueName = "oluso-webhooks";
// });

// Enterprise: LDAP Server
builder.Services
    //.AddLdapServer(builder.Configuration)
    //.AddLdapForProvider(provider, connectionString)
    .AddSaml(builder.Configuration)
    .AddSamlForProvider(provider, connectionString)
    .AddScim(options =>
    {
        options.BasePath = "/scim/v2";
        options.MaxResults = 200;
        options.SoftDeleteUsers = true;
        options.LogRetention = TimeSpan.FromDays(90);
    })
    .AddScimForProvider(provider, connectionString);


// Enterprise: FIDO2/WebAuthn credential storage
builder.Services.AddFido2ForProvider(provider, connectionString);

// Enterprise: business (KYB) verification storage
builder.Services.AddOlusoVerificationForProvider(provider, connectionString);

// Organization-level feature entitlements
builder.Services.AddOrganizationFeatureProvider();

// Tenant-level feature entitlements (within org-level bounds)
builder.Services.AddTenantFeatureProvider();

// Per-user module seat access (when MaxSeats is configured)
builder.Services.AddUserModuleAccessFeatureProvider();

// Messaging (email, SMS, WhatsApp)
builder.Services.AddMessaging(builder.Configuration);

// Webhook dispatching. Failed deliveries are retried by an in-process hosted
// service; swap in AddQueueBasedWebhookRetries (Oluso.Enterprise.AzureWebhooks) to retry from a queue when running several instances.
builder.Services.AddOlusoWebhooks();
builder.Services.AddWebhookRetryProcessing();
builder.Services.AddCoreWebhookEvents();

// Push + in-app notifications (OneSignal push + rich action-center realtime payloads)
builder.Services.AddOlusoPush(builder.Configuration);
// Telemetry: real OpenTelemetry pipeline when OTLP/Tempo endpoints are
// configured (via Aspire or appsettings); falls back to a no-op when
// nothing's set so single-process `dotnet run` still starts cleanly.
var otlpEndpoint = builder.Configuration["Oluso:Telemetry:OtlpEndpoint"];
var tempoEndpoint = builder.Configuration["Oluso:Telemetry:TempoEndpoint"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint) || !string.IsNullOrWhiteSpace(tempoEndpoint))
{
    builder.Services.AddOlusoOpenTelemetry(opt =>
    {
        opt.ServiceName = "Oluso.Sample";
        opt.OtlpEndpoint = otlpEndpoint;
        opt.TempoEndpoint = tempoEndpoint;
        opt.SkipLicenseValidation = true; // dev sample
    });
}
else
{
    builder.Services.AddOlusoNullTelemetry();
}

// Forwarded headers — accept X-Forwarded-* from local reverse proxy (Caddy)
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.All;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddCors(options =>
{
    // Empty "Oluso" policy - the ICorsPolicyProvider builds it dynamically
    options.AddPolicy("Oluso", _ => { });
});

// Host validation — reject requests from unknown hosts (DNS rebinding protection)
builder.Services.Configure<Oluso.Middleware.HostValidationOptions>(options =>
{
    options.Enabled = true;
    options.AllowLocalhostInDevelopment = true;
});

// Add controllers and apply Oluso conventions
builder.Services.AddControllers(options =>
    {
        // Disable implicit required validation for non-nullable reference types
        // to allow proper camelCase JSON binding without validation errors
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    })
    .ApplyOlusoConventions(olusoBuilder);

// Add Razor Pages for login UI with JSON-based localization
builder.Services.AddOlusoUI();
builder.Services.AddRazorPages()
    .AddViewLocalization();

// Add Swagger for API exploration with separate docs per API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("admin", new OpenApiInfo
    {
        Title = "Oluso Admin API",
        Version = "v1",
        Description = "Administrative API for managing users, clients, and tenants"
    });
    options.SwaggerDoc("account", new OpenApiInfo
    {
        Title = "Oluso Account API",
        Version = "v1",
        Description = "End-user self-service API for profile and security management"
    });
    options.SwaggerDoc("service", new OpenApiInfo
    {
        Title = "Oluso Service API",
        Version = "v1",
        Description = "Service-to-service API for machine clients"
    });
    options.SwaggerDoc("other", new OpenApiInfo
    {
        Title = "Oluso Other APIs",
        Version = "v1",
        Description = "Enterprise features and other endpoints"
    });

    // Assign ungrouped controllers to "other" doc
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        var groupName = apiDesc.GroupName;
        if (string.IsNullOrEmpty(groupName))
            return docName == "other";
        return groupName == docName;
    });
});

var app = builder.Build();

// Apply database migrations for all registered migratable DbContexts
await app.MigrateOlusoDatabaseAsync();

// Seed the admin role of each installed module
await app.SeedModuleRolesAsync();

// Initialize database with seed data
await Oluso.Sample.SeedData.SeedAsync(app);

// Uncomment to re-seed admin role permissions without wiping the database:
//await Oluso.Sample.SeedData.ReseedAdminRolePermissionsAsync(app);

// Configure pipeline

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/admin/swagger.json", "Admin API");
        options.SwaggerEndpoint("/swagger/account/swagger.json", "Account API");
        options.SwaggerEndpoint("/swagger/service/swagger.json", "Service API");
        options.SwaggerEndpoint("/swagger/other/swagger.json", "Other APIs");
    });
    app.UseDeveloperExceptionPage();
}
else if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
// Forwarded headers — required when behind Caddy/nginx reverse proxy.
// Without this, Request.Host returns the internal hostname (e.g., oluso:5050)
// instead of the external domain the client used.
app.UseForwardedHeaders();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

// CORS - uses dynamic OlusoCorsPolicyProvider
app.UseCors("Oluso");

app.UseAuthentication();

// Add Oluso middleware (host validation, tenant resolution, OIDC CORS)
// Must run AFTER UseAuthentication() so JWT claims are available
app.UseOluso();
app.UseOlusoUI();

app.UseAuthorization();
app.UseSession(); // Required for FIDO2 registration/assertion state

app.MapRazorPages();
app.MapControllers();
app.MapOlusoRealtime();

// Enterprise: SAML endpoints
app.MapSamlEndpoints();

// Enterprise: SCIM endpoints (auto-discovered via [ApiController])
app.UseScim();

// Health check
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

// Config URLs endpoint (for frontend dashboards)
app.MapGet("/api/config/urls", (IConfiguration config) => Results.Ok(new
{
    adminUiUrl = config.GetValue<string>("Oluso:Urls:AdminUiUrl"),
    accountUiUrl = config.GetValue<string>("Oluso:Urls:AccountUiUrl")
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

// Expose Program class for integration tests
public partial class Program { }
