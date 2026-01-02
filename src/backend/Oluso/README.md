# Oluso

Modern OAuth 2.0 and OpenID Connect identity server for ASP.NET Core.

## Features

- 🔐 **OAuth 2.0 / OIDC Compliant** - Full support for authorization code, client credentials, refresh tokens, device flow, and more
- 🏢 **Multi-Tenant** - Built-in tenant isolation for SaaS applications
- 🔄 **User Journey Engine** - Customizable authentication flows (MFA, passwordless, progressive profiling)
- 🗃️ **Entity Framework Core** - Works with SQL Server, PostgreSQL, SQLite, or your existing DbContext
- 🔑 **JWT & Private Key JWT** - Full client authentication support including `private_key_jwt`

## Quick Start

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Oluso identity server
builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddEntityFrameworkStores(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

app.UseRouting();
app.UseOluso();  // Adds tenant resolution and OIDC middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapOluso();  // Maps OIDC endpoints

app.Run();
```

## Using Your Own DbContext

If you have an existing DbContext, implement `IOlusoDbContext`:

```csharp
public class AppDbContext : IdentityDbContext<OlusoUser, OlusoRole, string>, IOlusoDbContext
{
    public DbSet<Client> Clients { get; set; }
    public DbSet<ClientSecret> ClientSecrets { get; set; }
    public DbSet<ApiResource> ApiResources { get; set; }
    public DbSet<ApiScope> ApiScopes { get; set; }
    public DbSet<IdentityResource> IdentityResources { get; set; }
    public DbSet<PersistedGrant> PersistedGrants { get; set; }
    public DbSet<SigningKey> SigningKeys { get; set; }
    public DbSet<Consent> Consents { get; set; }
    public DbSet<DeviceFlowCode> DeviceFlowCodes { get; set; }
    public DbSet<Tenant>? Tenants { get; set; }  // Optional, for multi-tenancy

    // Your existing entities...
    public DbSet<Product> Products { get; set; }
}

// Then register it:
builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddEntityFrameworkStores<AppDbContext>();
```

## Configuration

### Basic Configuration

```csharp
builder.Services.AddOluso(builder.Configuration, options =>
{
    options.IssuerUri = "https://auth.myapp.com";
    options.AutoMigrate = true;

    options.Tokens.AccessTokenLifetimeSeconds = 3600;
    options.Tokens.RefreshTokenLifetimeSeconds = 2592000;

    options.Password.RequiredLength = 10;
    options.Password.RequireNonAlphanumeric = true;
});
```

### Multi-Tenancy Options

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy(options =>
    {
        // Resolve tenant from URL path: /tenant-id/connect/token
        options.ResolutionStrategy = TenantResolutionStrategy.Path;

        // Or from subdomain: tenant-id.auth.example.com
        options.ResolutionStrategy = TenantResolutionStrategy.Subdomain;

        // Or from header
        options.ResolutionStrategy = TenantResolutionStrategy.Header;
        options.TenantHeaderName = "X-Tenant-Id";

        options.DefaultTenantId = "default";
    });
```

### User Journey Engine

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine(options =>
    {
        options.PluginDirectory = "./Plugins";
        options.EnablePluginHotReload = builder.Environment.IsDevelopment();
    });
```

## Customization

### Custom Profile Service

Add custom claims to tokens:

```csharp
public class CustomProfileService : IProfileService
{
    private readonly IUserRepository _users;

    public async Task GetProfileDataAsync(ProfileDataRequestContext context)
    {
        var user = await _users.GetByIdAsync(context.SubjectId);

        context.IssuedClaims.Add(new Claim("department", user.Department));
        context.IssuedClaims.Add(new Claim("employee_id", user.EmployeeId));
    }

    public Task IsActiveAsync(IsActiveContext context)
    {
        context.IsActive = true;
        return Task.CompletedTask;
    }
}

// Register it:
builder.Services.AddOluso(configuration)
    .AddProfileService<CustomProfileService>();
```

### Custom Password Validator

Integrate with LDAP or legacy systems:

```csharp
public class LdapPasswordValidator : IResourceOwnerPasswordValidator
{
    private readonly ILdapService _ldap;

    public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
    {
        var result = await _ldap.AuthenticateAsync(context.Username, context.Password);
        if (result.Success)
        {
            context.Result = GrantValidationResult.Success(result.UserId);
        }
        else
        {
            context.Result = GrantValidationResult.Invalid("invalid_grant", "Invalid credentials");
        }
    }
}

builder.Services.AddOluso(configuration)
    .AddResourceOwnerValidator<LdapPasswordValidator>();
```

### Custom Grant Types

Add support for custom OAuth grant types:

```csharp
public class SmsGrantValidator : IExtensionGrantValidator
{
    public string GrantType => "urn:mycompany:sms";

    public async Task ValidateAsync(ExtensionGrantValidationContext context)
    {
        var phone = context.Request["phone_number"];
        var code = context.Request["verification_code"];

        if (await _smsService.VerifyCode(phone, code))
        {
            var user = await _users.GetByPhoneAsync(phone);
            context.Result = GrantValidationResult.Success(user.Id);
        }
    }
}

builder.Services.AddOluso(configuration)
    .AddExtensionGrantValidator<SmsGrantValidator>();
```

### Event Handling

React to authentication events:

```csharp
public class SecurityEventSink : IOlusoEventSink
{
    public async Task HandleAsync(OlusoEvent evt)
    {
        switch (evt)
        {
            case UserSignedInEvent signIn:
                _logger.LogInformation("User {User} signed in from {IP}",
                    signIn.Username, signIn.IpAddress);
                break;

            case UserSignInFailedEvent failed:
                await _alertService.NotifySecurityTeam(failed);
                break;

            case TokenIssuingEvent issuing:
                // Add custom claims before token is issued
                issuing.AdditionalClaims.Add(new Claim("custom", "value"));
                break;
        }
    }
}

builder.Services.AddOluso(configuration)
    .AddEventSink<SecurityEventSink>()
    .AddEventSink<AuditLogEventSink>(); // Multiple sinks supported
```

### UI Customization

Customize login page appearance:

```csharp
builder.Services.AddOluso(configuration)
    .ConfigureUi(ui =>
    {
        ui.ApplicationName = "MyApp Login";
        ui.LogoUrl = "/images/logo.png";
        ui.PrimaryColor = "#007bff";
        ui.ShowRememberMe = true;
        ui.ShowForgotPassword = true;
        ui.CustomCss = ".login-box { border-radius: 8px; }";
    });
```

### Custom Pages

Override default authentication pages with your own:

```csharp
builder.Services.AddOluso(configuration)
    .ConfigurePages(pages =>
    {
        pages.LoginPage = "/Auth/Login";      // Your custom login page
        pages.ConsentPage = "/Auth/Consent";  // Your custom consent page
        pages.ErrorPage = "/Auth/Error";      // Your custom error page
    });
```

Then create your own Razor Pages at those paths.

## appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyApp;Trusted_Connection=True;"
  },
  "Oluso": {
    "IssuerUri": "https://auth.myapp.com",
    "Tokens": {
      "AccessTokenLifetimeSeconds": 3600,
      "RefreshTokenLifetimeSeconds": 2592000
    }
  }
}
```

## License

MIT
