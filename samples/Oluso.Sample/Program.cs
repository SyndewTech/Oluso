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
using Oluso.Telemetry.OpenTelemetry.Logging;
using Oluso.Enterprise.AzureKeyVault;

var builder = WebApplication.CreateBuilder(args);

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

// Add Oluso identity server with enterprise add-ons
var olusoBuilder = builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddSigningKeys(option => option.DefaultStorageProvider = Oluso.Core.Domain.Entities.KeyStorageProvider.AzureKeyVault)
    .AddOlusoAzureKeyVault(options =>
    {
        options.VaultUri = builder.Configuration.GetValue<string>("Oluso:AzureKeyVault:VaultUri", string.Empty);
        options.UseHsmKeys = true;
        options.SkipLicenseValidation = true;
    })
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
    .AddLdap();
    

// Enterprise: Azure Webhooks for queue-based webhook processing
// Uncomment when you have Azure Storage or Service Bus configured
// builder.Services.AddAzureQueueWebhookProcessor(options =>
// {
//     options.ConnectionString = builder.Configuration.GetConnectionString("AzureStorage")!;
//     options.QueueName = "oluso-webhooks";
// });

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

// Enterprise: FIDO2/WebAuthn credential storage
builder.Services.AddFido2ForProvider(provider, connectionString);

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
await Oluso.Sample.SeedData.SeedAsync(app);

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

// Expose Program class for integration tests
public partial class Program { }
