# Oluso v2 Quick Start Guide

This guide covers the modular setup options for Oluso Identity Server v2. The reference host for everything
below is `samples/Oluso.Sample/Program.cs`.

## Table of Contents

- [Project Structure](#project-structure)
- [Run the Sample](#run-the-sample)
- [Basic Setup](#basic-setup)
- [User Journey Engine](#user-journey-engine)
- [Step Handlers](#step-handlers)
- [State Storage](#state-storage)
- [Views and UI](#views-and-ui)
- [MFA Configuration](#mfa-configuration)
- [External Authentication](#external-authentication)
- [Multi-Tenant External Providers](#multi-tenant-external-providers)
- [Proxy Mode (Federation Broker)](#proxy-mode-federation-broker)
- [Custom IExternalAuthService](#custom-iexternalauthservice)
- [External Login Step Configuration](#external-login-step-configuration)
- [Custom User Store](#custom-user-store)
- [Custom Step Handlers](#custom-step-handlers)
  - [Custom Authentication Steps](#custom-authentication-steps)
- [Journey Policies](#journey-policies)
- [Signing Keys](#signing-keys)
- [Enterprise Modules](#enterprise-modules)
- [Complete Example](#complete-example)
- [Events, Webhooks, and Audit Logging](#events-webhooks-and-audit-logging)
- [Webhooks](#webhooks)
- [Audit Logging](#audit-logging)
- [Licensing](#licensing)
- [OpenTelemetry Integration](#opentelemetry-integration)
- [Telemetry Dashboard (Admin UI)](#telemetry-dashboard-admin-ui)

---

## Project Structure

Oluso v2 organizes code into `backend` and `frontend` folders within `src/`:

```
src/
├── backend/                        # .NET 10 projects
│   ├── Oluso/                      # Identity server: OIDC endpoints, journeys, builder extensions
│   ├── Oluso.Core/                 # Domain entities, interfaces, events, licensing
│   ├── Oluso.EntityFramework/      # EF Core stores, OlusoDbContext, migrations
│   ├── Oluso.Admin/                # Admin API controllers
│   ├── Oluso.Account/              # Account API (end-user self-service)
│   ├── Oluso.UI/                   # Razor pages and journey views
│   ├── Oluso.Telemetry.Abstractions/
│   ├── Oluso.Telemetry.OpenTelemetry/
│   └── Oluso.Enterprise/           # Enterprise add-ons
│       ├── Fido2/                  # FIDO2/Passkeys
│       ├── Saml/                   # SAML 2.0 IdP and SP
│       ├── Scim/                   # SCIM 2.0 provisioning
│       ├── Ldap/                   # LDAP authentication and LDAP server
│       ├── Verification/           # Business (KYB) verification
│       ├── AzureKeyVault/          # Key Vault signing keys and certificates
│       ├── AzureBlob/              # Blob file storage
│       ├── AzureDataProtection/    # Shared Data Protection key ring
│       ├── AzureWebhooks/          # Queue-based webhook retries
│       └── Cloudflare/             # Custom hostnames
│
└── frontend/                       # Angular / Nx workspace
    ├── package.json
    ├── apps/
    │   ├── admin-standalone/       # Admin dashboard SPA
    │   ├── account-standalone/     # End-user self-service portal
    │   └── workspace-standalone/   # Workspace SPA
    └── libs/
        ├── ui-core/                # Shared components and plugin models (@oluso/ui-core)
        ├── admin-ui/ account-ui/ workspace-ui/ journey-ui/ auth/
        └── plugins/                # UI plugins
            ├── fido2/              # @oluso/plugin-fido2
            ├── ldap/               # @oluso/plugin-ldap
            ├── saml/               # @oluso/plugin-saml
            ├── scim/               # @oluso/plugin-scim
            ├── telemetry/          # @oluso/plugin-telemetry
            └── verification/       # @oluso/plugin-verification
```

The JavaScript/TypeScript client SDK (`@oluso/sdk`) lives in `sdk/js`; see `sdk/js/README.md`.

### NuGet Packages

The backend projects are published to NuGet with the `Syndew.` prefix on the package ID (assembly names and
namespaces stay `Oluso.*`):

| Package | Contains |
|---------|----------|
| `Syndew.Oluso` | `AddOluso`, the OIDC server, journeys, EF store registration (brings in `Syndew.Oluso.Core` and `Syndew.Oluso.EntityFramework`) |
| `Syndew.Oluso.UI` | Razor login, consent and journey views |
| `Syndew.Oluso.Admin` | Admin API (`AddOlusoAdmin`) |
| `Syndew.Oluso.Account` | Account API (`AddOlusoAccount`) |
| `Syndew.Oluso.Enterprise.Fido2`, `.Saml`, `.Scim`, `.Ldap`, `.Verification`, `.AzureKeyVault`, ... | Enterprise add-ons |
| `Syndew.Oluso.Telemetry.OpenTelemetry` | OpenTelemetry pipeline and telemetry API |

```bash
dotnet add package Syndew.Oluso
dotnet add package Syndew.Oluso.UI
dotnet add package Syndew.Oluso.Admin
dotnet add package Syndew.Oluso.Account
```

Inside this repository, reference the projects directly instead, as `samples/Oluso.Sample/Oluso.Sample.csproj`
does.

### Frontend Workspace

The frontend is an Nx workspace. Run commands from `src/frontend/`:

```bash
cd src/frontend
npm install                          # Install dependencies
npx nx serve admin-standalone        # Admin UI dev server (port 4201)
npx nx serve account-standalone      # Account UI dev server (port 4200)
npx nx build admin-standalone        # Production build
```

Each app reads its backend URL at runtime from `public/config.js` (`serverUrl`, default
`http://localhost:5050`).

### UI Plugin Architecture

The Admin and Account UIs are extended with plugins. The plugin models (`AdminPlugin`, `AccountPlugin`) are
defined in `@oluso/ui-core`:

```typescript
import { AdminPlugin, AccountPlugin } from '@oluso/ui-core';

// Admin plugin (for the Admin UI)
export const SAML_PLUGIN: AdminPlugin = {
  name: 'oluso-saml',
  version: '1.0.0',
  routes: [{ path: 'saml', children: SAML_ROUTES }],
  navItems: [
    { id: 'saml', label: 'SAML', path: '/saml', icon: 'shield-check', order: 10 },
  ],
};

// Account plugin (for the Account UI - end-user self-service)
export const FIDO2_ACCOUNT_PLUGIN: AccountPlugin = {
  name: 'oluso-fido2-account',
  version: '1.0.0',
  routes: [{ path: 'security/passkeys', component: PasskeysPageComponent }],
  securityTabs: [
    { id: 'passkeys', label: 'Passkeys', icon: 'fingerprint', component: PasskeysPageComponent, order: 30 },
  ],
};
```

Plugins are registered at app start-up through `PluginService` (see
`apps/admin-standalone/src/app/app.config.ts`):

```typescript
pluginService.registerAdminPlugin(SAML_PLUGIN);
pluginService.registerAccountPlugin(FIDO2_ACCOUNT_PLUGIN);
```

---

## Run the Sample

`samples/Oluso.Sample` wires up every public module. Its `appsettings.json` uses SQLite and localhost URLs
(the frontends on ports 4200 to 4202), with outgoing email and SMS turned off. The settings it needs are:

```json
{
  "ConnectionStrings": {
    "OlusoDb": "Data Source=Oluso.db"
  },
  "Oluso": {
    "IssuerUri": "http://localhost:5050",
    "Database": { "Provider": "Sqlite" }
  }
}
```

```bash
cd samples/Oluso.Sample
dotnet run
```

The server listens on `http://localhost:5050`. On start-up it applies all database migrations
(see [migrations.md](./migrations.md)) and seeds a super admin (`superadmin@oluso.local` / `SuperAdmin123!`),
test users and clients. Swagger UI is at `/swagger` in Development. Every seeded account, client and
secret is listed under [Test Credentials](../README.md#test-credentials) in the README.

To run it in Docker with the portals behind Caddy, use `docker compose up` from the repository root
(`https://localhost:8443`). [docker.md](./docker.md) covers databases, volumes, configuration, running on a
real domain and troubleshooting.

For production settings, start from `templates/appsettings.Production.json`.

---

## Basic Setup

### Minimal Configuration

A working host needs the Oluso builder, a store, MVC with the Oluso conventions applied, and the Oluso
middleware:

```csharp
using Oluso;
using Oluso.EntityFramework;
using Oluso.UI;

var builder = WebApplication.CreateBuilder(args);

var olusoBuilder = builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddSigningKeys()
    .AddEntityFrameworkStoresForProvider(
        builder.Configuration.GetValue<string>("Oluso:Database:Provider", "Sqlite")!,
        builder.Configuration.GetConnectionString("OlusoDb")!);

// Oluso's protocol endpoints are MVC controllers; ApplyOlusoConventions adds their routes
builder.Services.AddControllers()
    .ApplyOlusoConventions(olusoBuilder);

// Login, consent and journey pages
builder.Services.AddOlusoUI();
builder.Services.AddRazorPages();

// Empty "Oluso" policy: Oluso's ICorsPolicyProvider builds it from Oluso:Cors:Origins and client origins
builder.Services.AddCors(options => options.AddPolicy("Oluso", _ => { }));

var app = builder.Build();

await app.MigrateOlusoDatabaseAsync();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("Oluso");
app.UseAuthentication();
app.UseOluso();       // Host validation, tenant resolution, sign-in rate limits. After UseAuthentication.
app.UseOlusoUI();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapOlusoRealtime(); // SignalR hub used by the frontends

app.Run();
```

Configuration in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "OlusoDb": "Data Source=Oluso.db"
  },
  "Oluso": {
    "IssuerUri": "https://auth.example.com",
    "Database": { "Provider": "Sqlite" },
    "Cors": { "Origins": [ "https://app.example.com" ] }
  }
}
```

`Oluso:Database:Provider` accepts `Sqlite` (default), `SqlServer` or `PostgreSQL` (`Postgres`).
`MigrateOlusoDatabaseAsync()` creates and upgrades the schema on start-up, so there is no separate migration
step.

`AddOluso` already registers the OIDC endpoints, the event service, feature gating and database-driven external
providers. The OIDC endpoints are served at `/.well-known/openid-configuration`, `/.well-known/jwks`,
`/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/revocation`, `/connect/introspect`,
`/connect/endsession`, `/connect/deviceauthorization`, `/connect/par` and `/connect/ciba`.

Clients, users and tenants live in the database. Create them through the Admin API/UI, or seed them at start-up
the way `samples/Oluso.Sample/SeedData.cs` does.

### Admin and Account APIs

```csharp
using Oluso.Admin;
using Oluso.Account;

var olusoBuilder = builder.Services.AddOluso(builder.Configuration)
    // ...
    .AddAdminApi(mvc => mvc
        .AddOlusoAdmin()      // /api/admin/*
        .AddOlusoAccount());  // /api/account/*
```

The MVC configuration is applied by `ApplyOlusoConventions(olusoBuilder)`, so keep that call on
`AddControllers()`.

### With Your Own DbContext

Instead of the built-in `OlusoDbContext`, the stores can use your context if it implements `IOlusoDbContext`:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneyEngine();
```

You then own that context's migrations. The provider-specific helpers (`AddEntityFrameworkStoresSqlite`,
`AddEntityFrameworkStoresSqlServer`, `AddEntityFrameworkStoresNpgsql`, or `AddEntityFrameworkStoresForProvider`)
use Oluso's own migrations.

### Core Options

Core options bind from the `Oluso` configuration section:

| Key | Description | Default |
|-----|-------------|---------|
| `Oluso:IssuerUri` | Public issuer URL; also the base domain for tenant subdomains | none |
| `Oluso:WorkspaceBaseUrl` | Base URL of the workspace SPA for user-facing links | `IssuerUri` |
| `Oluso:RunSigningKeyMaintenanceInProcess` | Run the hourly signing key maintenance in this host | `true` |
| `Oluso:RunDomainChecksInProcess` | Run the custom domain and email domain checks in this host; with several instances, leave it on in exactly one | `true` |
| `Oluso:EnableOrganizationSignIn` | Offer "Sign in with your organisation" on platform login pages | `true` |
| `Oluso:HostValidation:Enabled` | Reject requests for unknown hosts | `false` |
| `Oluso:Cors:Origins` | Platform CORS origins | empty |

Set them in configuration rather than in code: the `AddOluso(configuration, options => ...)` overload only
changes the builder's own copy and does not reach `IOptions<OlusoOptions>`.

### Multi-Tenancy Options

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy(options =>
    {
        // Header (X-Tenant-Id) and query string (?tenant=) are always checked first
        options.ResolutionStrategy = TenantResolutionStrategy.Subdomain; // acme.auth.example.com -> "acme"
        options.BaseDomain = "auth.example.com"; // Derived from IssuerUri when not set
    });
```

`TenantResolutionStrategy` values: `Header` (default), `Subdomain`, `Domain`.

---

## User Journey Engine

The User Journey Engine provides a flexible, policy-driven authentication flow system.

### Enable with All Built-in Steps

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine();
```

This registers all built-in step handlers:

**UI Steps:**
- `local_login` - Username/password authentication
- `composite_login` - Combined local, passkey and external login
- `signup` - User registration
- `mfa` - Multi-factor authentication
- `consent` - OAuth scope consent
- `password_reset` - Forgot password flow
- `password_change` - In-journey password change
- `external_login` - Social/external identity providers
- `passwordless_email` - Email OTP/magic links
- `passwordless_sms` - SMS OTP
- `email_verification` - Email address verification
- `link_account` - Account linking
- `terms_acceptance` - Terms acceptance step
- `dynamic_form` - Custom form collection
- `custom_page` - Custom content page
- `captcha` - CAPTCHA verification

**Logic Steps (no UI):**
- `condition` - Conditional flow control
- `branch` - Branching logic
- `transform` - Data transformation
- `api_call` - External API calls
- `webhook` - Webhook notifications
- `create_user` - User creation
- `update_user` - User profile update
- `assign_roles` - Role assignment
- `custom_plugin` - Dispatches to a registered custom step or plugin

Enterprise modules add their own steps (for example `fido2_login`, `fido2_register`, `saml`, `ldap`).

Engine options bind from `Oluso:UserJourneys` and can be changed in code:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine(options =>
    {
        options.DefaultJourneyTimeoutMinutes = 30;
        options.MaxStepsPerJourney = 50;
        options.PluginDirectory = "/plugins";
        options.EnablePluginHotReload = true;
    });
```

### Selective Step Registration

`AddUserJourneys` registers the core journey services and only the steps you add:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneys(journeys =>
    {
        // Add only UI steps you need
        journeys.AddLocalLogin();
        journeys.AddSignUp();
        journeys.AddMfa();
        journeys.AddPasswordReset();
        journeys.AddExternalLogin();

        // Or add all built-in UI steps at once
        // journeys.AddBuiltInSteps();

        // Or add all including logic steps
        // journeys.AddAllBuiltInSteps();
    });
```

`AddUserJourneys` can also follow `AddUserJourneyEngine()` to change stores (see
[State Storage](#state-storage)).

---

## Step Handlers

### Local Login

Username/password authentication with lockout support.

```csharp
journeys.AddLocalLogin();
```

**View:** `Views/Shared/Journey/_LocalLogin.cshtml`

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "login",
      "type": "local_login",
      "configuration": {
        "allowRememberMe": true,
        "allowSelfRegistration": false,
        "allowForgotPassword": true,
        "showPasskey": false
      }
    }
  ]
}
```

On success the step takes the `auth_local` branch (or `auth_passkey` after a passkey sign-in) when the policy
defines one, and otherwise continues to the next step.

### Sign Up

User registration with email verification and terms acceptance.

```csharp
journeys.AddSignUp();
```

**View:** `Views/Shared/Journey/_SignUp.cshtml`

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "register",
      "type": "signup",
      "configuration": {
        "allowSelfRegistration": true,
        "requireEmailVerification": true,
        "requireTermsAcceptance": true,
        "allowedEmailDomains": ["example.com", "company.org"],
        "termsUrl": "https://example.com/terms",
        "privacyUrl": "https://example.com/privacy",
        "defaultRoles": ["User"]
      }
    }
  ]
}
```

### MFA (Multi-Factor Authentication)

Supports TOTP authenticator apps, email, and SMS verification.

```csharp
journeys.AddMfa();
```

**Views:**
- `Views/Shared/Journey/_MfaVerify.cshtml` - Code entry
- `Views/Shared/Journey/_MfaSetup.cshtml` - Method selection
- `Views/Shared/Journey/_MfaTotpSetup.cshtml` - Authenticator app setup
- `Views/Shared/Journey/_MfaEmailSetup.cshtml` - Email verification setup
- `Views/Shared/Journey/_MfaSmsSetup.cshtml` - SMS verification setup
- `Views/Shared/Journey/_MfaRecoveryCodes.cshtml` - Recovery codes display
- `Views/Shared/Journey/_MfaRecoveryVerify.cshtml` - Recovery code entry

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "mfa",
      "type": "mfa",
      "configuration": {
        "required": true,
        "methods": ["totp", "email"],
        "allowSetup": true,
        "rememberDevice": true,
        "rememberDeviceDays": 30
      }
    }
  ]
}
```

`methods` defaults to `["totp", "email"]`; `sms` is also supported.

**Custom MFA Service:** see [MFA Configuration](#mfa-configuration).

### Consent

OAuth scope consent screen for client applications.

```csharp
journeys.AddConsent();
```

**View:** `Views/Shared/Journey/_Consent.cshtml`

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "consent",
      "type": "consent",
      "configuration": {
        "allowRemember": true,
        "rememberDays": 30
      }
    }
  ]
}
```

### Password Reset

Multi-phase password reset flow with email verification.

```csharp
journeys.AddPasswordReset();
```

**Views:**
- `Views/Shared/Journey/_PasswordResetRequest.cshtml` - Email entry
- `Views/Shared/Journey/_PasswordResetVerify.cshtml` - Code verification
- `Views/Shared/Journey/_PasswordResetNewPassword.cshtml` - New password entry

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "reset",
      "type": "password_reset",
      "configuration": {
        "tokenExpirationMinutes": 60,
        "minLength": 8,
        "requireDigit": true,
        "requireLowercase": true,
        "requireUppercase": true,
        "requireNonAlphanumeric": false
      }
    }
  ]
}
```

Reset and verification emails are sent through the messaging services; register them with
`builder.Services.AddMessaging(builder.Configuration)` (namespace `Oluso.Messaging`) and configure
`Oluso:Messaging:Email` (`Provider`, `FromAddress`, `FromName`, and the provider's settings such as
`SendGrid:ApiKey`).

### External Login

Social and enterprise identity provider authentication.

```csharp
journeys.AddExternalLogin();
```

**View:** `Views/Shared/Journey/_ExternalLogin.cshtml`

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "external",
      "type": "external_login",
      "configuration": {
        "providers": ["Google", "Microsoft", "GitHub"],
        "autoRedirect": false,
        "autoProvision": true
      }
    }
  ]
}
```

---

## State Storage

Journey state is held by an `IJourneyStateStore`.

| Setup | Store |
|-------|-------|
| Journey engine without EF stores | `InMemoryJourneyStateStore` (single instance, development only) |
| With `AddEntityFrameworkStores*` | `JourneyStateStore`, persisted in the Oluso database |
| `journeys.UseDistributedCache()` | `DistributedCacheJourneyStateStore` on `IDistributedCache` |
| `journeys.UseStateStore<T>()` | Your own store |

### In-Memory (Development Only)

```csharp
journeys.UseStateStore<InMemoryJourneyStateStore>();
```

### Distributed Cache (Redis, SQL Server, etc.)

```csharp
// Configure distributed cache first
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

// Then use it for journey state. Call it after the EF stores so it replaces their store.
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddUserJourneys(journeys => journeys.UseDistributedCache());
```

**Configuration Options:**
```csharp
builder.Services.AddSingleton(new DistributedCacheJourneyStateStoreOptions
{
    KeyPrefix = "oluso:",
    DefaultExpiration = TimeSpan.FromMinutes(30),
    UserIndexExpiration = TimeSpan.FromHours(1)
});
```

### Custom State Store

```csharp
public class DatabaseJourneyStateStore : IJourneyStateStore
{
    public Task<JourneyState?> GetAsync(string journeyId, CancellationToken ct = default) { ... }
    public Task SaveAsync(JourneyState state, CancellationToken ct = default) { ... }
    public Task DeleteAsync(string journeyId, CancellationToken ct = default) { ... }
    public Task<IEnumerable<JourneyState>> GetByUserAsync(string userId, CancellationToken ct = default) { ... }
    public Task CleanupExpiredAsync(CancellationToken ct = default) { ... }
}

// Register
journeys.UseStateStore<DatabaseJourneyStateStore>();
```

---

## Views and UI

### Option 1: Default Views (Oluso.UI Package)

Add the `Syndew.Oluso.UI` NuGet package (or a project reference to `src/backend/Oluso.UI`) for ready-to-use
views:

```bash
dotnet add package Syndew.Oluso.UI
```

```csharp
builder.Services.AddOlusoUI();
builder.Services.AddRazorPages();
// ...
app.UseOlusoUI();
app.MapRazorPages();
```

Views are discovered from the Razor Class Library.

### Option 2: Custom Views (Recommended for Production)

Steps render views by name (for example `Journey/_LocalLogin`). Create views with the same names in your host
application:

```
YourApp/
└── Views/
    └── Shared/
        └── Journey/
            ├── _LocalLogin.cshtml
            ├── _SignUp.cshtml
            ├── _MfaVerify.cshtml
            ├── _MfaSetup.cshtml
            ├── _MfaTotpSetup.cshtml
            ├── _MfaEmailSetup.cshtml
            ├── _MfaRecoveryCodes.cshtml
            ├── _Consent.cshtml
            ├── _PasswordResetRequest.cshtml
            ├── _PasswordResetVerify.cshtml
            ├── _PasswordResetNewPassword.cshtml
            ├── _ExternalLogin.cshtml
            └── _Lockout.cshtml
```

Host application views take precedence over package views.

### View Models

Each step handler provides a strongly-typed view model:

| Step | View Model |
|------|------------|
| local_login | `LocalLoginViewModel` |
| signup | `SignUpViewModel` |
| mfa | `MfaVerifyViewModel`, `MfaSetupViewModel`, `MfaTotpSetupViewModel`, `MfaEmailSetupViewModel`, `MfaRecoveryCodesViewModel` |
| consent | `ConsentViewModel` |
| password_reset | `PasswordResetRequestViewModel`, `PasswordResetVerifyViewModel`, `PasswordResetNewPasswordViewModel` |
| external_login | `ExternalLoginViewModel` |

---

## MFA Configuration

The EF stores register `IdentityMfaService`, built on ASP.NET Core Identity. To replace it, implement
`IMfaService` and register it after the EF stores.

### Implement IMfaService

```csharp
public class CustomMfaService : IMfaService
{
    public Task<MfaSetupResult> GenerateTotpSetupAsync(string userId, CancellationToken ct = default)
    {
        // Generate TOTP shared key and QR code URI
        var key = GenerateSecretKey();
        var uri = $"otpauth://totp/YourApp:{userId}?secret={key}&issuer=YourApp";

        return Task.FromResult(MfaSetupResult.Success(key, uri));
    }

    public Task<bool> VerifyTotpCodeAsync(string userId, string code, CancellationToken ct = default)
    {
        // Verify TOTP code
        return Task.FromResult(ValidateTotpCode(userId, code));
    }

    public Task<bool> SendVerificationCodeAsync(string userId, string provider, CancellationToken ct = default)
    {
        // Send code via email/SMS
        return Task.FromResult(true);
    }

    public Task<MfaEnableResult> EnableMfaAsync(string userId, string provider, CancellationToken ct = default)
    {
        // Enable MFA and generate recovery codes
        var recoveryCodes = GenerateRecoveryCodes(10);
        return Task.FromResult(MfaEnableResult.Success(recoveryCodes));
    }

    // Also: VerifyEmailCodeAsync, VerifySmsCodeAsync, DisableMfaAsync,
    // GetEnabledProvidersAsync, GenerateRecoveryCodesAsync, VerifyRecoveryCodeAsync
}

// Register
builder.Services.AddScoped<IMfaService, CustomMfaService>();
```

---

## External Authentication

External authentication allows users to sign in using their existing accounts from third-party identity providers like Google, Microsoft, GitHub, and others. This eliminates the need for users to create and remember yet another password while leveraging the security infrastructure of established providers.

Oluso provides comprehensive external authentication support with:

- **Built-in Social Providers**: Google, Microsoft, Facebook, Twitter/X, GitHub, LinkedIn, and Apple Sign In
- **Generic Providers**: any OpenID Connect or OAuth 2.0 provider
- **Multi-Tenant Support**: Each tenant can have their own OAuth credentials configured in the database
- **Proxy Mode**: Act as a federation broker without storing user data locally
- **Token Caching**: Cache external tokens for subsequent API calls to upstream providers
- **Claim Transformation**: Filter and transform claims from external providers

### Quick Start: Database-Configured Providers

`AddOluso` turns on database-driven providers by default (`AddDynamicExternalProviders()`), and the EF stores
supply the provider store. Add a provider for a tenant in the Admin UI (Identity Providers) or through
`/api/admin/identity-providers`, then reference its scheme from an `external_login` or `composite_login` step.
No code is needed per provider, and changes take effect without a restart.

### Supported Provider Types

Each provider requires credentials from its developer console:

| Provider | Provider Type | Required Settings | Developer Console |
|----------|---------------|-------------------|-------------------|
| Google | `Google` | ClientId, ClientSecret | [Google Cloud Console](https://console.cloud.google.com/) |
| Microsoft | `Microsoft` | ClientId, ClientSecret | [Azure Portal](https://portal.azure.com/) |
| Facebook | `Facebook` | AppId (ClientId), AppSecret (ClientSecret) | [Meta Developer Portal](https://developers.facebook.com/) |
| Twitter/X | `Twitter` | ConsumerKey (ClientId), ConsumerSecret (ClientSecret) | [Twitter Developer Portal](https://developer.twitter.com/) |
| GitHub | `GitHub` | ClientId, ClientSecret | [GitHub Developer Settings](https://github.com/settings/developers) |
| LinkedIn | `LinkedIn` | ClientId, ClientSecret | [LinkedIn Developer Portal](https://developer.linkedin.com/) |
| Apple | `Apple` | ClientId, TeamId, KeyId, PrivateKey | [Apple Developer Portal](https://developer.apple.com/) |
| Any OpenID Connect provider | `Oidc` | ClientId, ClientSecret, Authority or MetadataAddress | |
| Any OAuth 2.0 provider | `OAuth` | ClientId, ClientSecret, authorization, token and user info endpoints | |

### Static Providers in Code

For a provider configured in code rather than in the database, register a generic OAuth or OpenID Connect
scheme under a scheme name of your own:

```csharp
// OAuth 2.0 provider
builder.Services.AddOluso(builder.Configuration)
    .AddOAuthProvider("custom-oauth", "Custom Provider", options =>
    {
        options.ClientId = "...";
        options.ClientSecret = "...";
        options.CallbackPath = "/signin-custom-oauth";
        options.AuthorizationEndpoint = "https://provider.example.com/authorize";
        options.TokenEndpoint = "https://provider.example.com/token";
        options.UserInformationEndpoint = "https://provider.example.com/userinfo";
    });

// OpenID Connect provider
builder.Services.AddOluso(builder.Configuration)
    .AddOidcProvider("custom-oidc", "Enterprise SSO", options =>
    {
        options.ClientId = "...";
        options.ClientSecret = "...";
        options.Authority = "https://sso.enterprise.com";
        options.ResponseType = "code";
    });
```

The provider-specific helpers (`AddGoogle`, `AddMicrosoftAccount`, `AddFacebook`, `AddTwitter`, `AddGitHub`,
`AddLinkedIn`, `AddApple`) register under the provider's default scheme name (`Google`, `Microsoft`, and so on).
`AddOluso` already reserves those names for the database-driven providers, so configure those providers in the
database instead.

---

## Multi-Tenant External Providers

In multi-tenant SaaS applications, different tenants often need their own OAuth credentials. For example:

- **White-label scenarios**: Each customer wants their own branded Google/Microsoft login
- **Enterprise requirements**: Corporate clients require authentication through their own corporate identity provider
- **Compliance**: Some tenants may need their OAuth apps registered in specific regions

Dynamic provider configuration loads credentials per tenant from the database at runtime. A provider without a
tenant applies to every tenant that has no provider of its own for that scheme.

### When to Use Dynamic Providers

| Scenario | Use Static | Use Dynamic |
|----------|------------|-------------|
| Single-tenant app | ✅ | ✅ |
| All tenants share OAuth apps | ✅ | ✅ |
| Tenants have own OAuth apps | | ✅ |
| White-label/branded login | | ✅ |
| Enterprise SSO per tenant | | ✅ |

### Dynamic Providers Are On by Default

`AddOluso` calls `AddDynamicExternalProviders()` for you, with a 5 minute cache and tenant overrides enabled.
Do not call it again: it registers the provider placeholder schemes, and registering them twice fails at
start-up.

The EF stores register `ExternalProviderConfigStore`, which reads the identity providers managed in the Admin UI.
To load provider configuration from somewhere else, implement `IExternalProviderConfigStore` and register it
after the EF stores:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddExternalProviderStore<MyExternalProviderStore>();
```

### Implement IExternalProviderConfigStore

The store is called whenever authentication is initiated, and Oluso caches the results to minimize lookups:

```csharp
public class MyExternalProviderStore : IExternalProviderConfigStore
{
    private readonly ITenantContext _tenantContext;
    private readonly IMyProviderRepository _repository;

    public MyExternalProviderStore(ITenantContext tenantContext, IMyProviderRepository repository)
    {
        _tenantContext = tenantContext;
        _repository = repository;
    }

    public async Task<IReadOnlyList<ExternalProviderDefinition>> GetEnabledProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _repository.GetEnabledAsync(_tenantContext.TenantId, cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<ExternalProviderDefinition?> GetBySchemeAsync(
        string scheme,
        CancellationToken cancellationToken = default)
    {
        // Tenant-specific first, then the global (TenantId == null) row
        var row = await _repository.FindBySchemeAsync(_tenantContext.TenantId, scheme, cancellationToken);
        return row == null ? null : Map(row);
    }

    public async Task<ExternalProviderDefinition?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = await _repository.FindByIdAsync(id, cancellationToken);
        return row == null ? null : Map(row);
    }

    private static ExternalProviderDefinition Map(MyProviderRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        Scheme = row.Scheme,
        DisplayName = row.DisplayName,
        ProviderType = row.ProviderType,
        ClientId = row.ClientId,
        ClientSecret = row.ClientSecret, // Decrypted; store it encrypted at rest
        Scopes = row.Scopes,
    };
}
```

### ExternalProviderDefinition Properties

The `ExternalProviderDefinition` class contains all the information needed to configure an OAuth provider at runtime:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | string | Unique identifier for this configuration |
| `TenantId` | string? | Tenant this config belongs to (null = global/fallback) |
| `Scheme` | string | Authentication scheme name (e.g., "Google", "Microsoft") |
| `DisplayName` / `IconUrl` | string? | Shown on the login page |
| `ProviderType` | string | Provider type for handler selection (see [Supported Provider Types](#supported-provider-types)) |
| `ClientId` | string | OAuth Client ID from the provider's developer console |
| `ClientSecret` | string | OAuth Client Secret (always store encrypted) |
| `Authority` / `MetadataAddress` | string? | OpenID Connect discovery |
| `AuthorizationEndpoint` / `TokenEndpoint` / `UserInfoEndpoint` | string? | Generic OAuth 2.0 endpoints |
| `AppleTeamId` / `AppleKeyId` / `ApplePrivateKey` | string? | Sign in with Apple |
| `Scopes` | IReadOnlyList&lt;string&gt; | OAuth scopes to request (e.g., ["openid", "profile", "email"]) |
| `ProxyMode` | bool | Enable federation broker mode (see Proxy Mode section) |
| `StoreUserLocally` | bool | Whether to create local user records on login |
| `AutoProvisionUsers` | bool | Auto-create users on first external login |
| `DefaultRoles` | IReadOnlyList&lt;string&gt; | Roles given to auto-provisioned users |
| `AccessControl` | ExternalAccessControl? | Restrict who may sign in (allowed emails or a claim gate) |

### How Dynamic Resolution Works

When a user initiates external login, Oluso dynamically resolves the correct OAuth credentials for their tenant:

```
┌─────────────────────────────────────────────────────────────────────────┐
│  1. User clicks "Login with Google" on acme.example.com                 │
│                              ↓                                          │
│  2. Tenant middleware resolves tenant: "acme"                           │
│                              ↓                                          │
│  3. IExternalProviderConfigStore.GetBySchemeAsync("Google") called      │
│                              ↓                                          │
│  4. Store returns Acme's Google config (ClientId, ClientSecret, etc.)   │
│                              ↓                                          │
│  5. DynamicOAuthPostConfigureOptions injects credentials into handler   │
│                              ↓                                          │
│  6. User redirected to Google using Acme's OAuth app                    │
│                              ↓                                          │
│  7. Callback returns to Oluso, user authenticated as Acme tenant user   │
└─────────────────────────────────────────────────────────────────────────┘
```

### Cache Invalidation

Provider configurations are cached in memory (5 minutes) to avoid lookups on every authentication request. The
cache is tenant-aware, so changing one tenant's configuration doesn't affect others. The Admin API clears the
cache when a provider is created, updated or deleted. A custom store whose data changes elsewhere does the same
through `IExternalSchemeCacheInvalidator`:

```csharp
public class ExternalProviderAdminService
{
    private readonly IExternalSchemeCacheInvalidator _cacheInvalidator;

    public ExternalProviderAdminService(IExternalSchemeCacheInvalidator cacheInvalidator)
        => _cacheInvalidator = cacheInvalidator;

    public async Task UpdateProviderAsync(string tenantId, string scheme)
    {
        // Update in your store...

        // Drop the cached scheme and options for this tenant
        _cacheInvalidator.Invalidate(scheme, tenantId);
    }
}
```

---

## Proxy Mode (Federation Broker)

By default, when a user authenticates via an external provider (e.g., Google), Oluso creates a local user record and links it to the external identity. However, some scenarios require **Proxy Mode** where Oluso acts purely as a federation broker, authenticating users without storing any local data.

### When to Use Proxy Mode

| Scenario | Normal Mode | Proxy Mode |
|----------|-------------|------------|
| Consumer apps with social login | ✅ | |
| Apps that need local user profiles | ✅ | |
| B2B with partner-managed identities | | ✅ |
| Compliance: no external user data storage | | ✅ |
| Enterprise SSO passthrough | | ✅ |
| Federation hub for multiple IdPs | | ✅ |

### How Proxy Mode Works

In proxy mode:
1. User authenticates with external IdP (e.g., a corporate OpenID Connect provider)
2. Oluso receives the authentication response with claims
3. **No local user is created**: claims are passed through directly
4. Oluso issues its own tokens containing the external claims
5. External tokens can optionally be cached for upstream API calls

### Enable Proxy Mode

Turn on proxy mode on the identity provider in the Admin UI, or set `ProxyMode = true` in the definition a
custom store returns. You'll typically also set `StoreUserLocally = false`:

```csharp
new ExternalProviderDefinition
{
    Id = "partner-sso",
    Scheme = "EnterpriseSSO",
    ProviderType = "Oidc",
    ClientId = "...",
    ClientSecret = "...",
    MetadataAddress = "https://sso.partner.com/.well-known/openid-configuration",

    // Proxy mode settings
    ProxyMode = true,
    StoreUserLocally = false,
    CacheExternalTokens = true,
    TokenCacheDurationSeconds = 3600,

    // Claim filtering
    ProxyIncludeClaims = new[] { "sub", "email", "name", "groups" },
    ProxyExcludeClaims = new[] { "internal_id", "ssn" },

    // Token passthrough
    IncludeExternalAccessToken = true,
    IncludeExternalIdToken = false
}
```

### Proxy Mode Token Claims

When proxy mode is enabled, Oluso issues tokens with a special subject format and additional claims to identify the authentication source:

| Claim | Value |
|-------|-------|
| `sub` | `proxy:{provider}:{external_subject}` |
| `idp` | Provider name (e.g., "EnterpriseSSO") |
| `external_sub` | Original subject from external IdP |
| `proxy_mode` | `true` |
| `amr` | `["external", "{provider}", "proxy"]` (provider in lowercase) |

Plus all claims from the external IdP (filtered by `ProxyIncludeClaims`/`ProxyExcludeClaims`).

### Token Caching for Proxy Mode

In proxy mode, you may need to make API calls to the upstream identity provider on behalf of the user (e.g., fetching additional profile data or calling the provider's APIs). Enable `CacheExternalTokens` to store the external tokens for later use. They are cached under the proxy subject (the `sub` claim):

```csharp
// Retrieve cached tokens in your application
public class UserInfoProxyService
{
    private readonly IExternalAuthService _externalAuth;
    private readonly HttpClient _httpClient;

    public UserInfoProxyService(IExternalAuthService externalAuth, HttpClient httpClient)
    {
        _externalAuth = externalAuth;
        _httpClient = httpClient;
    }

    public async Task<ExternalUserInfo?> GetUserInfoAsync(string subject)
    {
        var tokens = await _externalAuth.GetCachedTokensAsync(subject);

        if (tokens?.AccessToken == null)
            throw new InvalidOperationException("No cached tokens");

        // Use external access token to call upstream IdP
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://external-idp.com/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await _httpClient.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<ExternalUserInfo>();
    }
}
```

---

## Custom IExternalAuthService

The default `IdentityExternalAuthService` implementation uses ASP.NET Core Identity and works for most scenarios. However, you may need a custom implementation for:

- **Non-Identity user stores**: Using a custom database or external user service
- **Custom token handling**: Special logic for caching or transforming tokens
- **Advanced multi-tenancy**: Complex tenant resolution beyond the default behavior
- **Testing**: Mock implementation for unit/integration tests

Implement `IExternalAuthService` to fully control external authentication behavior:

```csharp
public class CustomExternalAuthService : IExternalAuthService
{
    public Task<IReadOnlyList<ExternalProviderInfo>> GetAvailableProvidersAsync(
        CancellationToken ct = default)
    {
        // Return providers available for current tenant
    }

    public Task<IReadOnlyList<ExternalProviderInfo>> GetAvailableProvidersAsync(
        string clientId,
        CancellationToken ct = default)
    {
        // Return providers available for a client
    }

    public Task<ExternalProviderConfig?> GetProviderConfigAsync(
        string provider,
        CancellationToken ct = default)
    {
        // Return configuration including proxy mode settings
    }

    public Task<ExternalChallengeResult> ChallengeAsync(
        string provider,
        string returnUrl,
        CancellationToken ct = default)
    {
        // Initiate OAuth challenge
    }

    public Task<ExternalLoginResult?> GetExternalLoginResultAsync(
        CancellationToken ct = default)
    {
        // Process OAuth callback, extract claims and tokens
    }

    public Task SignOutExternalAsync(CancellationToken ct = default)
    {
        // Clear the external sign-in cookie
    }

    public Task<IList<ExternalLoginInfo>> GetUserLoginsAsync(
        string userId,
        CancellationToken ct = default)
    {
        // List the user's linked external logins
    }

    public Task<string?> FindUserByLoginAsync(
        string provider,
        string providerKey,
        CancellationToken ct = default)
    {
        // Find local user by external login
    }

    public Task<ExternalLoginOperationResult> LinkLoginAsync(
        string userId,
        string provider,
        string providerKey,
        string? displayName,
        CancellationToken ct = default)
    {
        // Link external login to existing user
    }

    public Task<ExternalLoginOperationResult> UnlinkLoginAsync(
        string userId,
        string provider,
        string providerKey,
        CancellationToken ct = default)
    {
        // Unlink external login from user
    }

    public Task CacheExternalTokensAsync(
        string sessionKey,
        ExternalTokenData tokens,
        TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        // Cache tokens for proxy mode
    }

    public Task<ExternalTokenData?> GetCachedTokensAsync(
        string sessionKey,
        CancellationToken ct = default)
    {
        // Retrieve cached tokens
    }
}

// Register (after the EF stores, which register the default)
builder.Services.AddScoped<IExternalAuthService, CustomExternalAuthService>();
```

---

## External Login Step Configuration

The `external_login` step type integrates external authentication into User Journeys. This step displays available providers and handles the OAuth flow, returning control to the journey upon successful authentication.

Configure the step in your journey policies:

```json
{
  "steps": [
    {
      "id": "external",
      "type": "external_login",
      "configuration": {
        "providers": ["Google", "Microsoft", "GitHub"],
        "autoRedirect": false,
        "autoProvision": true,
        "claimMappings": { "upn": "email" }
      }
    }
  ]
}
```

| Option | Description | Default |
|--------|-------------|---------|
| `providers` | List of allowed provider schemes | All enabled |
| `autoRedirect` | Auto-redirect if only one provider | `false` |
| `autoProvision` | Create user on first login (the provider's own setting wins when set) | `true` |
| `proxyMode` | Proxy mode when the provider does not set it | `false` |
| `claimMappings` | Map external claim types to Oluso claim types | none |

**View:** `Views/Shared/Journey/_ExternalLogin.cshtml`

### Combining with Local Login

A common pattern is to offer both local (username/password) and external login options on one page with the
`composite_login` step:

```json
{
  "steps": [
    {
      "id": "login",
      "type": "composite_login",
      "configuration": {
        "showLocalLogin": true,
        "showExternalLogin": true,
        "externalProviders": ["Google", "Microsoft"],
        "showPasskey": false,
        "allowRememberMe": true,
        "allowForgotPassword": true,
        "allowRegistration": false
      }
    },
    {
      "id": "mfa",
      "type": "mfa"
    }
  ]
}
```

---

## Custom User Store

### Implement IOlusoUserService

Replace the default ASP.NET Identity user service with your own:

```csharp
public class DirectoryUserService : IOlusoUserService
{
    private readonly IDirectoryClient _directory;

    public DirectoryUserService(IDirectoryClient directory) => _directory = directory;

    public async Task<UserValidationResult> ValidateCredentialsAsync(
        string username, string password, string? tenantId = null, CancellationToken ct = default)
    {
        var entry = await _directory.AuthenticateAsync(username, password, ct);

        if (entry == null)
            return UserValidationResult.Failed("invalid_credentials", "Invalid username or password");

        return UserValidationResult.Successful(new ValidatedUser
        {
            Id = entry.Id,
            UserName = entry.UserName,
            Email = entry.Email,
            FirstName = entry.GivenName,
            LastName = entry.Surname,
            DisplayName = entry.DisplayName,
            TenantId = tenantId
        });
    }

    public async Task<OlusoUserInfo?> FindByIdAsync(string userId, CancellationToken ct = default)
    {
        var entry = await _directory.FindByIdAsync(userId, ct);
        return entry == null ? null : new OlusoUserInfo
        {
            Id = entry.Id,
            Username = entry.UserName,
            Email = entry.Email,
            EmailVerified = true,
            DisplayName = entry.DisplayName
        };
    }

    // ... implement the other IOlusoUserService members
}

// Register
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddUserService<DirectoryUserService>();
```

Call `.SkipIdentity()` before the EF stores to leave ASP.NET Core Identity out entirely. For an LDAP directory,
the [LDAP module](#ldap-authentication-oluso-enterprise-ldap) already provides authentication.

---

## Custom Step Handlers

### Create a Custom Step

Step handlers are registered as singletons; resolve scoped services from `context.ServiceProvider` (the
request scope) inside `ExecuteAsync`. `ICustomStepHandler` extends `IStepHandler` and lets the step be
registered with designer metadata (below).

```csharp
public class InviteCodeStepHandler : ICustomStepHandler
{
    public string StepType => "invite_code";

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken ct = default)
    {
        var code = context.GetInput("invite_code");

        if (string.IsNullOrEmpty(code))
        {
            return StepHandlerResult.ShowUi("Journey/_InviteCode", new InviteCodeViewModel());
        }

        var invites = context.ServiceProvider.GetRequiredService<IInviteService>();
        if (!await invites.IsValidAsync(code, ct))
        {
            return StepHandlerResult.ShowUi("Journey/_InviteCode", new InviteCodeViewModel
            {
                ErrorMessage = "That invite code is not valid"
            });
        }

        context.SetData("invite_code", code);
        return StepHandlerResult.Continue();
    }
}
```

### Register Custom Step

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddUserJourneys(journeys => journeys.AddStepHandler<InviteCodeStepHandler>());
```

To also list the step in the Admin UI journey designer with a description and category, register it with
`AddCustomJourneyStep` instead:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddCustomJourneyStep<InviteCodeStepHandler>(step => step
        .WithDescription("Ask for an invite code")
        .InCategory("Validation")
        .FromModule("MyCompany.Invites"));
```

### Custom Authentication Steps

If your custom step performs user authentication (verifies credentials and establishes identity), you **must** call `SetAuthenticated()` to ensure a session cookie is issued when the journey completes.

```csharp
public class CustomAuthStepHandler : IStepHandler
{
    public string StepType => "custom_auth";

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken ct = default)
    {
        var username = context.GetInput("username");
        var token = context.GetInput("token");

        // Validate credentials...
        var authService = context.ServiceProvider.GetRequiredService<IMyTokenAuthService>();
        var user = await authService.ValidateAsync(username, token, ct);

        if (user == null)
        {
            return StepHandlerResult.ShowUi("Journey/_CustomAuth", new CustomAuthViewModel
            {
                ErrorMessage = "Invalid credentials"
            });
        }

        // IMPORTANT: Mark the user as authenticated
        // This triggers session cookie issuance on journey completion
        context.SetAuthenticated(user.Id, "custom_token");

        return StepHandlerResult.Success(new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName
        });
    }
}
```

**Why this matters:** The journey engine determines whether to issue a session cookie by checking for the `authenticated_at` flag. This allows `JourneyType.SignIn` policies to be used for non-authentication flows (e.g., collecting claims, showing terms) without accidentally creating sessions.

#### Authentication Data Keys

| Key | Description | Example |
|-----|-------------|---------|
| `authenticated_at` | Timestamp when authentication occurred | `DateTime.UtcNow` |
| `auth_method` | Authentication method (maps to `amr` claim) | `"pwd"`, `"fido2"`, `"saml"`, `"ldap"` |
| `idp` | Identity provider for external logins | `"google"`, `"saml:partner"` |

#### Alternative: Manual Data Setting

Instead of `SetAuthenticated()`, you can set the data manually:

```csharp
context.UserId = user.Id;
context.SetData("authenticated_at", DateTime.UtcNow);
context.SetData("auth_method", "custom");
context.SetData("idp", "my-provider");  // Optional, for external IdPs
```

#### Checking Authentication State

```csharp
// Check if user was authenticated during this journey
if (context.IsAuthenticated)
{
    // User has been authenticated by a previous step
}
```

---

## Journey Policies

A journey policy is an ordered list of steps. With the EF stores, policies are stored in the database
(`JourneyPolicyStore`) and managed in the Admin UI journey designer or through `/api/admin/journeys`. A client
can ask for a specific policy with the `policy` parameter on `/connect/authorize`.

### Define a Policy

```csharp
var loginPolicy = new JourneyPolicy
{
    Id = "default-login",
    Slug = "default-login",
    Name = "Default Login Flow",
    Type = JourneyType.SignIn,
    Steps = new List<JourneyPolicyStep>
    {
        new() { Id = "login", Type = "local_login", Order = 1 },
        new() { Id = "mfa", Type = "mfa", Order = 2, Optional = true },
        new() { Id = "consent", Type = "consent", Order = 3 }
    }
};
```

`JourneyType` values include `SignIn`, `SignUp`, `SignInSignUp`, `PasswordReset`, `PasswordChange`,
`ProfileEdit`, `LinkAccount`, `Consent`, data collection types (`Waitlist`, `ContactForm`, `Survey`, `Feedback`,
`DataCollection`) and `Custom`.

### Use a Custom Policy Store

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddUserJourneys(journeys => journeys.UsePolicyStore<MyPolicyStore>());
```

### Policy with Branches

A step's `Branches` map a branch id the step returns to the step that runs next. `local_login` returns
`auth_local` after a password sign-in and `auth_passkey` after a passkey sign-in, so this policy skips MFA for
passkeys:

```csharp
var loginPolicy = new JourneyPolicy
{
    Id = "login-passkey-skips-mfa",
    Slug = "login-passkey-skips-mfa",
    Name = "Login (passkeys skip MFA)",
    Steps = new List<JourneyPolicyStep>
    {
        new()
        {
            Id = "login",
            Type = "local_login",
            Order = 1,
            Configuration = new Dictionary<string, object> { ["showPasskey"] = true },
            Branches = new Dictionary<string, string>
            {
                ["auth_local"] = "mfa",
                ["auth_passkey"] = "consent"
            }
        },
        new() { Id = "mfa", Type = "mfa", Order = 2 },
        new() { Id = "consent", Type = "consent", Order = 3 }
    }
};
```

---

## Signing Keys

Oluso provides a pluggable signing key infrastructure for token signing (JWT access tokens, ID tokens) and verification. Keys can be stored locally with encryption at rest, or in external HSM-backed services like Azure Key Vault.

### Quick Start: Local Keys

For simple deployments, keys are stored encrypted in your database using ASP.NET Core Data Protection:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddSigningKeys();
```

This provides:
- Automatic key generation (RS256 by default); a platform signing key is created at start-up when none exists
- Key encryption at rest using Data Protection
- Hourly maintenance that marks expired keys, rotates ahead of expiry and cleans up
- JWKS endpoint for token validation

Without `AddSigningKeys()` the EF stores fall back to `DevelopmentSigningCredentialStore`, which is for
development only.

Because local keys are encrypted with Data Protection, every instance must share the same Data Protection key
ring (for example with `Syndew.Oluso.Enterprise.AzureDataProtection`); otherwise keys created by one instance
cannot be read by another.

### Signing Key Options

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddSigningKeys(options =>
    {
        options.DefaultStorageProvider = KeyStorageProvider.Local; // or AzureKeyVault
    });
```

`DefaultStorageProvider` chooses where new keys are created.

### Available Algorithms

| Algorithm | Type | Key Size | Use Case |
|-----------|------|----------|----------|
| RS256 | RSA | 2048+ | Standard, widely compatible |
| RS384 | RSA | 2048+ | Higher security |
| RS512 | RSA | 2048+ | Maximum RSA security |
| ES256 | EC | P-256 | Fast, compact tokens |
| ES384 | EC | P-384 | Higher security EC |
| ES512 | EC | P-521 | Maximum EC security |

RSA keys below 2048 bits are rejected.

### Azure Key Vault (Enterprise)

For production deployments requiring HSM-backed keys where the private key never leaves the vault:

```bash
# Install the package
dotnet add package Syndew.Oluso.Enterprise.AzureKeyVault
```

```csharp
using Oluso.Enterprise.AzureKeyVault;

builder.Services.AddOluso(builder.Configuration)
    .AddSigningKeys()
    .AddOlusoAzureKeyVault();
```

**What `AddOlusoAzureKeyVault()` does:**
1. Registers `AzureKeyVaultProvider` as an `IKeyMaterialProvider` - enables Key Vault as a key storage backend
2. Registers `AzureKeyVaultCertificateProvider` for certificate management
3. **Replaces** the default `DevelopmentSigningCredentialStore` with a production `SigningCredentialStore` that:
   - Uses the full key management system (`ISigningKeyService`)
   - Auto-provisions signing keys when none exist
   - Uses the default provider (Key Vault if configured, otherwise local)

**Configuration (appsettings.json):**

```json
{
  "Oluso": {
    "AzureKeyVault": {
      "VaultUri": "https://your-vault.vault.azure.net/"
    }
  }
}
```

**Options:**

```csharp
.AddOlusoAzureKeyVault(options =>
{
    options.UseAsDefault = true;           // Key Vault is the default for new certificates (default: true)
    options.UseHsmKeys = true;             // HSM-protected keys (Premium tier)
    options.SkipLicenseValidation = false; // Development/testing only
})
```

**Use Key Vault as the default provider for new keys:**

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddSigningKeys(opts => opts.DefaultStorageProvider = KeyStorageProvider.AzureKeyVault)
    .AddOlusoAzureKeyVault();
```

**Benefits:**
- Private keys never leave Key Vault
- HSM protection (with Premium tier)
- Automatic key versioning
- Built-in audit logging
- Azure RBAC for access control
- Production-ready signing credential management

**Authentication:**

Azure Key Vault uses `DefaultAzureCredential` which supports:
- Managed Identity (recommended for Azure deployments)
- Azure CLI credentials (for local development)
- Environment variables (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)
- Service principal

**Signing credential store by setup:**

| Setup | Signing Credential Store | Key Storage |
|-------|-------------------------|-------------|
| EF stores only (development) | `DevelopmentSigningCredentialStore` | Development key |
| `AddSigningKeys()` | `SigningCredentialStore` | Database, encrypted with Data Protection |
| `AddSigningKeys()` + `AddOlusoAzureKeyVault()` | `SigningCredentialStore` | Azure Key Vault (or local when it is not the default) |

### Key Rotation

Rotation is built in. `SigningKeyMaintenanceBackgroundService` runs every hour and calls
`ISigningKeyService.ProcessScheduledRotationsAsync()`, which marks expired keys, creates the next key ahead of
expiry and cleans up. Rotated-out keys stay in the JWKS until they expire, so tokens they signed keep validating.

To trigger maintenance from an external scheduler instead, set `Oluso:RunSigningKeyMaintenanceInProcess` to
`false` and call `POST /api/system/maintenance/signing-keys`. Both paths are idempotent.

To rotate on demand:

```csharp
await signingKeyService.RotateKeysAsync();              // Platform/tenant keys
await signingKeyService.RotateKeysAsync("my-client-id"); // One client's keys
```

### Per-Client Keys

For scenarios where different OAuth clients need separate signing keys:

```csharp
// Generate key for specific client
await signingKeyService.GenerateKeyAsync(new GenerateKeyRequest
{
    TenantId = "tenant-123",
    ClientId = "my-client-id",
    KeyType = SigningKeyType.EC,
    Algorithm = "ES256",
    Name = "my-client-signing-key"
});

// Get signing credentials for specific client
var credentials = await signingKeyService.GetSigningCredentialsAsync("my-client-id");
```

### JWKS Endpoint

Oluso automatically exposes a JWKS (JSON Web Key Set) endpoint for token validation. Clients can fetch public keys from:

```
GET /.well-known/jwks
```

The JWKS includes all active keys (current and those still within their validity after rotation). The URL is
also published as `jwks_uri` in `/.well-known/openid-configuration`.

### Custom Key Material Provider

For other HSM or key management systems (AWS KMS, HashiCorp Vault, etc.), implement `IKeyMaterialProvider`:

```csharp
public class AwsKmsProvider : IKeyMaterialProvider
{
    public KeyStorageProvider ProviderType => KeyStorageProvider.AwsKms;

    public async Task<KeyMaterialResult> GenerateKeyAsync(
        KeyGenerationParams request,
        CancellationToken cancellationToken = default)
    {
        // Create key in AWS KMS
        var createResponse = await _kmsClient.CreateKeyAsync(new CreateKeyRequest
        {
            KeySpec = "RSA_2048",
            KeyUsage = "SIGN_VERIFY"
        });

        return new KeyMaterialResult
        {
            KeyId = createResponse.KeyMetadata.KeyId,
            KeyVaultUri = createResponse.KeyMetadata.Arn,
            PublicKeyData = await GetPublicKeyFromKms(createResponse.KeyMetadata.KeyId)
        };
    }

    public async Task<SigningCredentials?> GetSigningCredentialsAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        // Return credentials that sign via KMS
        var kmsSecurityKey = new AwsKmsSecurityKey(_kmsClient, key.KeyVaultUri);
        return new SigningCredentials(kmsSecurityKey, key.Algorithm);
    }

    // Also: GetPublicKeyAsync, GetJsonWebKeyAsync, DeleteKeyAsync, IsAvailableAsync
}

// Register
builder.Services.AddOluso(builder.Configuration)
    .AddSigningKeys()
    .AddKeyMaterialProvider<AwsKmsProvider>();
```

---

## Enterprise Modules

Each module has its own database context. Register its storage with the module's `Add*ForProvider(provider,
connectionString)` method so `MigrateOlusoDatabaseAsync()` creates its tables (see
[migrations.md](./migrations.md)).

### FIDO2/WebAuthn (Oluso.Enterprise.Fido2)

Add FIDO2/Passkey support for passwordless authentication:

```csharp
using Oluso.Enterprise.Fido2;

builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddFido2(fido2 => fido2
        .WithRelyingParty("example.com", "Example App")
        .WithOrigins("https://example.com")
        .RequireUserVerification()
        .PreferPlatformAuthenticator());

// Credential storage
builder.Services.AddFido2ForProvider(provider, connectionString);
```

`AddFido2()` without arguments binds `Oluso:Fido2` (`RelyingPartyId`, `RelyingPartyName`, `Origins`, ...).

**Important:** FIDO2 requires session middleware. Add to your pipeline:

```csharp
app.UseAuthentication();
app.UseOluso();
app.UseAuthorization();
app.UseSession(); // Required for FIDO2 registration/assertion state

app.MapRazorPages();
app.MapControllers();
```

The `AddFido2()` method automatically registers session services, but your app must call `app.UseSession()` in the middleware pipeline.

**Step Handlers:**

| Step Type | Description |
|-----------|-------------|
| `fido2_login` | Passwordless login with passkeys |
| `fido2_register` | Register new passkeys (post-authentication) |
| `webauthn` | Combined passkey step |

`local_login` and `composite_login` can also offer a passkey button with `"showPasskey": true`.

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "login",
      "type": "local_login",
      "configuration": {
        "showPasskey": true
      }
    },
    {
      "id": "passkey_setup",
      "type": "fido2_register",
      "optional": true,
      "configuration": {
        "promptNewUsers": true,
        "requireResidentKey": true,
        "allowSkip": true
      }
    }
  ]
}
```

**Views** (in `Views/Shared/Journey/`):
- `_Fido2Login.cshtml` / `_Fido2Assertion.cshtml` - Passkey login UI
- `_Fido2RegistrationPrompt.cshtml` / `_Fido2Registration.cshtml` - Passkey registration UI

**API Endpoints:**

The FIDO2 module exposes REST endpoints for credential management:

| Endpoint | Description | Auth |
|----------|-------------|------|
| `POST /api/fido2/register/options` | Begin passkey registration | User |
| `POST /api/fido2/register/complete` | Complete passkey registration | User |
| `POST /api/fido2/assert/options` | Begin passkey assertion | Anonymous |
| `POST /api/fido2/assert/complete` | Complete passkey assertion | Anonymous |
| `GET /api/fido2/credentials` | List user's passkeys | User |
| `DELETE /api/fido2/credentials/{id}` | Delete a passkey | User |
| `GET /api/account/passkeys` | List passkeys (Account API) | User |
| `GET /api/admin/fido2/users/{userId}/credentials` | List user's passkeys (admin) | AdminApi |
| `DELETE /api/admin/fido2/users/{userId}/credentials/{credentialId}` | Delete user's passkey (admin) | AdminApi |

**Configuration Options:**

```csharp
.AddFido2(fido2 => fido2
    .WithRelyingParty("example.com", "Example App", icon: null)
    .WithOrigins("https://example.com", "https://app.example.com")
    .WithTimeout(60000)                    // Ceremony timeout in ms
    .RequireUserVerification()             // Require biometric/PIN
    .RequireResidentKey()                  // Require discoverable credentials
    .PreferPlatformAuthenticator()         // Prefer Touch ID, Windows Hello, etc.
    .WithAttestation("none")               // none, indirect, direct, enterprise
    .WithMaxCredentialsPerUser(10)         // Limit credentials per user
    .StoreAttestationData(false));         // Store attestation for enterprise
```

---

### SAML (Oluso.Enterprise.Saml)

Add SAML 2.0 Identity Provider (and Service Provider) support for enterprise SSO:

```csharp
using Oluso.Enterprise.Saml;

builder.Services
    .AddSaml(builder.Configuration)
    .AddSamlForProvider(provider, connectionString);

// ...
app.MapSamlEndpoints();
```

**Configuration (appsettings.json):**
```json
{
  "Oluso": {
    "Saml": {
      "IdentityProvider": {
        "Enabled": true,
        "EntityId": "https://auth.example.com",
        "BaseUrl": "https://auth.example.com",
        "SigningCertificate": {
          "FilePath": "certs/saml-signing.pfx",
          "Password": "your-password"
        }
      },
      "ServiceProvider": {
        "EntityId": "https://auth.example.com",
        "BaseUrl": "https://auth.example.com"
      }
    }
  }
}
```

The IdP signing and encryption certificates can also be generated or uploaded through the Admin API
(`/api/admin/saml/service-providers/idp-certificate/...`).

**Endpoints:**

| Endpoint | Description |
|----------|-------------|
| `GET /saml/idp/metadata` | IdP metadata document |
| `GET/POST /saml/idp/sso` | IdP Single Sign-On endpoint |
| `GET/POST /saml/idp/slo` | IdP Single Logout endpoint |
| `GET /saml/metadata` | SP metadata document |
| `GET /saml/login/{idpName}` | Start sign-in at an upstream SAML IdP |
| `POST /saml/acs` | SP Assertion Consumer Service |
| `POST /saml/slo` | SP Single Logout |
| `GET /api/admin/saml/service-providers` | List configured SPs |
| `POST /api/admin/saml/service-providers` | Create SP configuration |
| `PUT /api/admin/saml/service-providers/{id}` | Update SP configuration |
| `DELETE /api/admin/saml/service-providers/{id}` | Delete SP configuration |

---

### SCIM Provisioning (Oluso.Enterprise.Scim)

Add SCIM 2.0 user provisioning for automated user management:

```csharp
using Oluso.Enterprise.Scim;

builder.Services
    .AddScim(options =>
    {
        options.BasePath = "/scim/v2";
        options.MaxResults = 200;
        options.SoftDeleteUsers = true;
        options.LogRetention = TimeSpan.FromDays(90);
    })
    .AddScimForProvider(provider, connectionString);

// In middleware pipeline, after UseAuthentication
app.UseScim();
```

SCIM clients (and their bearer tokens) are managed through `/api/admin/scim/clients`; outbound provisioning to
downstream SCIM services through `/api/admin/scim/providers`.

**API Endpoints:**

| Endpoint | Description |
|----------|-------------|
| `GET /scim/v2/Users` | List users |
| `POST /scim/v2/Users` | Create user |
| `GET /scim/v2/Users/{id}` | Get user |
| `PUT /scim/v2/Users/{id}` | Replace user |
| `PATCH /scim/v2/Users/{id}` | Update user |
| `DELETE /scim/v2/Users/{id}` | Delete user |
| `GET /scim/v2/Groups` | List groups |
| `POST /scim/v2/Groups` | Create group |
| `POST /scim/v2/Bulk` | Bulk operations |
| `GET /scim/v2/ServiceProviderConfig` | SCIM service config |
| `GET /scim/v2/ResourceTypes` | SCIM resource types |
| `GET /scim/v2/Schemas` | SCIM schemas |

---

### LDAP Authentication (Oluso.Enterprise.Ldap)

Add LDAP directory authentication:

```csharp
using Oluso.Enterprise.Ldap;

builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneyEngine()
    .AddLdap(ldap => ldap
        .WithServer("ldap.example.com", 389)
        .WithBaseDn("dc=example,dc=com")
        .WithServiceAccount("cn=admin,dc=example,dc=com", "password")
        .WithUserSearch(search => search.AuthenticationFilter = "(&(objectClass=inetOrgPerson)(uid={0}))")
        .WithGroupSearch(search => search.Filter = "(objectClass=groupOfNames)"));

builder.Services.AddLdapForProvider(provider, connectionString);
```

`AddLdap()` without arguments binds `Oluso:Ldap`. The module adds the `ldap` journey step.

**Or with LDAP Server (expose Oluso users over LDAP):**

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddLdap()
    .AddLdapServer(server => server
        .WithPort(389)
        .WithBaseDn("dc=example,dc=com")
        .WithAdmin("cn=admin,dc=example,dc=com", "password")
        .AllowAnonymousBind(false));
```

`builder.Services.AddLdapServer(builder.Configuration)` binds the same settings from `Oluso:LdapServer`.

---

## Complete Example

These examples show complete `Program.cs` configurations for common scenarios. Copy the appropriate example as a starting point for your application. `samples/Oluso.Sample/Program.cs` is the full reference.

### Single-Tenant Setup

Best for: Internal apps, single-customer deployments, or apps where all users share the same identity configuration.

```csharp
using Oluso;
using Oluso.Account;
using Oluso.Admin;
using Oluso.EntityFramework;
using Oluso.Messaging;
using Oluso.UI;
using Oluso.Webhooks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("OlusoDb")!;
var provider = builder.Configuration.GetValue<string>("Oluso:Database:Provider", "Sqlite")!;

var olusoBuilder = builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddSigningKeys()
    .AddAdminApi(mvc => mvc.AddOlusoAdmin().AddOlusoAccount())
    .AddEntityFrameworkStoresForProvider(provider, connectionString);

builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddOlusoWebhooks();
builder.Services.AddWebhookRetryProcessing();

builder.Services.AddControllers().ApplyOlusoConventions(olusoBuilder);
builder.Services.AddOlusoUI();
builder.Services.AddRazorPages();
builder.Services.AddCors(options => options.AddPolicy("Oluso", _ => { }));

var app = builder.Build();

await app.MigrateOlusoDatabaseAsync();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("Oluso");
app.UseAuthentication();
app.UseOluso();
app.UseOlusoUI();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapOlusoRealtime();

app.Run();
```

### Multi-Tenant Setup with Enterprise Modules

Best for: SaaS applications, white-label platforms, or any scenario where different customers need their own OAuth configurations. Tenants are resolved from the subdomain, each tenant configures its own external providers in the Admin UI, and journey state is kept in Redis.

```csharp
using Oluso;
using Oluso.Account;
using Oluso.Admin;
using Oluso.EntityFramework;
using Oluso.Enterprise.Fido2;
using Oluso.Enterprise.Saml;
using Oluso.Enterprise.Scim;
using Oluso.Messaging;
using Oluso.UI;
using Oluso.Webhooks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("OlusoDb")!;
var provider = builder.Configuration.GetValue<string>("Oluso:Database:Provider", "Sqlite")!;

// Distributed cache for journey state across instances
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

var olusoBuilder = builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy(options => options.ResolutionStrategy = TenantResolutionStrategy.Subdomain)
    .AddUserJourneyEngine()
    .AddSigningKeys()
    .AddAdminApi(mvc => mvc.AddOlusoAdmin().AddOlusoAccount())
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddUserJourneys(journeys => journeys.UseDistributedCache())
    .AddFido2();

builder.Services.AddFido2ForProvider(provider, connectionString);
builder.Services
    .AddSaml(builder.Configuration)
    .AddSamlForProvider(provider, connectionString)
    .AddScim()
    .AddScimForProvider(provider, connectionString);

builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddOlusoWebhooks();
builder.Services.AddWebhookRetryProcessing();

builder.Services.AddControllers().ApplyOlusoConventions(olusoBuilder);
builder.Services.AddOlusoUI();
builder.Services.AddRazorPages();
builder.Services.AddCors(options => options.AddPolicy("Oluso", _ => { }));

var app = builder.Build();

await app.MigrateOlusoDatabaseAsync();

app.UseStaticFiles();
app.UseRouting();
app.UseCors("Oluso");
app.UseAuthentication();
app.UseOluso();
app.UseOlusoUI();
app.UseAuthorization();
app.UseSession(); // FIDO2

app.MapRazorPages();
app.MapControllers();
app.MapOlusoRealtime();
app.MapSamlEndpoints();
app.UseScim();

app.Run();
```

With more than one instance, also set `ConnectionStrings:RealtimeRedis` (or `ConnectionStrings:Redis`) so the
realtime hub uses a Redis backplane, and share the Data Protection key ring between instances.

### Configuration (appsettings.json)

Store sensitive credentials in environment variables, Azure Key Vault, or a secrets manager for production. This example shows the structure for development:

```json
{
  "ConnectionStrings": {
    "OlusoDb": "Host=localhost;Database=oluso;Username=app;Password=secret",
    "Redis": "localhost:6379"
  },
  "Oluso": {
    "IssuerUri": "https://auth.example.com",
    "Database": { "Provider": "PostgreSQL" },
    "Cors": {
      "Origins": [ "https://admin.example.com", "https://account.example.com" ]
    },
    "Fido2": {
      "RelyingPartyId": "example.com",
      "RelyingPartyName": "Example"
    },
    "Messaging": {
      "Email": {
        "Provider": "SendGrid",
        "FromAddress": "noreply@example.com",
        "FromName": "Example",
        "SendGrid": { "ApiKey": "your-sendgrid-api-key" }
      }
    }
  }
}
```

---

## Events, Webhooks, and Audit Logging

Oluso provides a unified event system for monitoring authentication flows, integrating with external systems, and maintaining compliance audit logs.

### Event Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  OlusoEvent (raised by step handlers, services)                     │
│                              ↓                                      │
│  IOlusoEventService.RaiseAsync()                                    │
│                              ↓                                      │
│  ┌────────────────┬────────────────┬────────────────┐               │
│  │ LoggerEventSink│ AuditEventSink │ WebhookEventSink│ Custom Sinks │
│  └────────────────┴────────────────┴────────────────┘               │
│        ↓                  ↓                ↓                        │
│   Console/Logs     AuditLog DB     HTTP Endpoints                   │
└─────────────────────────────────────────────────────────────────────┘
```

### Enable Events

`AddOluso` registers the event service (`AddOlusoEvents()`), so there is nothing to enable. The sinks come from
the features you add:

| Sink | Registered by |
|------|---------------|
| `AuditEventSink` | `AddEntityFrameworkStores*` (persists to the `AuditLogs` table) |
| `WebhookEventSink` | `AddOlusoWebhooks()` |
| `LoggerEventSink` | `builder.Services.AddLoggerEventSink()` (logs to `ILogger`) |
| Your sink | `builder.Services.AddEventSink<TSink>()` |

Event options (`OlusoEventOptions`: `ContinueOnSinkError`, `IncludeCategories`, `ExcludeCategories`,
`CaptureActivityId`) are set with `builder.Services.Configure<OlusoEventOptions>(...)`.

### Event Categories

| Category | Events |
|----------|--------|
| `Authentication` | UserSignedIn, UserSignInFailed, UserSignedOut |
| `User` | UserRegistered, UserUpdated, UserDeleted, UserEmailVerified, UserPasswordChanged |
| `Security` | ConsentGranted, ConsentDenied, UserLockedOut, MfaCompleted |
| `Token` | TokenIssued, TokenRevoked |

Other categories: `Client`, `Role`, `Resource`, `Settings`, `Admin`, `System`, `Team`, `Conversation`.

### Core Events

```csharp
// Login events
UserSignedInEvent        // Successful login
UserSignInFailedEvent    // Failed login attempt
UserSignedOutEvent       // User logged out
UserLockedOutEvent       // Account locked

// User management events
UserRegisteredEvent      // New user created
UserUpdatedEvent         // Profile updated
UserDeletedEvent         // Account deleted
UserPasswordChangedEvent // Password changed
UserEmailVerifiedEvent   // Email verified
UserRoleAssignedEvent    // Role assigned in a tenant
UserRoleRemovedEvent     // Role removed in a tenant

// Security events
ConsentGrantedEvent      // OAuth consent granted
ConsentDeniedEvent       // OAuth consent denied
MfaCompletedEvent        // MFA verification result (success/fail)

// Token events
TokenIssuedEvent         // Access/ID token issued
TokenRevokedEvent        // Token revoked
```

### Custom Events

Enterprise packages can define their own events:

```csharp
public class PasskeyRegisteredEvent : OlusoEvent
{
    public override string Category => EventCategories.Security;
    public override string? WebhookEventType => "passkey.registered";

    public required string SubjectId { get; init; }
    public required string CredentialId { get; init; }
    public required string DeviceType { get; init; }
}
```

### Custom Event Sinks

```csharp
public class SlackNotificationSink : IOlusoEventSink
{
    public string Name => "Slack";

    public async Task HandleAsync(OlusoEvent evt, CancellationToken ct = default)
    {
        if (evt is UserLockedOutEvent lockout)
        {
            await _slackClient.PostMessageAsync(
                $"User {lockout.SubjectId} locked out: {lockout.Reason}");
        }
    }
}

// Register
builder.Services.AddEventSink<SlackNotificationSink>();
```

---

## Webhooks

Webhooks allow external systems to receive real-time notifications when events occur. Each tenant can configure their own webhook endpoints.

### Enable Webhooks

Webhooks are registered on `IServiceCollection`:

```csharp
using Oluso.Webhooks;

builder.Services.AddOlusoWebhooks(options =>
{
    options.TimeoutSeconds = 30;
    options.MaxRetries = 5;
    options.RequireHttps = true;
});

// Retry failed deliveries with the in-process hosted service
builder.Services.AddWebhookRetryProcessing();
```

`AddOlusoWebhooks` also registers the webhook event sink. Endpoints are managed per tenant in the Admin UI.
Without a retry processor, failed deliveries are recorded but not retried. For several instances, use
`AddQueueBasedWebhookRetries(...)` from `Syndew.Oluso.Enterprise.AzureWebhooks` instead of
`AddWebhookRetryProcessing()`.
The core webhook events are always available; `AddCoreWebhookEvents()` is kept for compatibility and adds
nothing.

### Webhook Events

| Event Type | Description | Category |
|------------|-------------|----------|
| `user.created` | New user registered | User |
| `user.updated` | User profile updated | User |
| `user.deleted` | User account deleted | User |
| `user.deactivated` | User account deactivated, including SCIM deprovisioning | User |
| `user.reactivated` | Deactivated user account reactivated | User |
| `user.email_verified` | Email verified | User |
| `user.password_changed` | Password changed | User |
| `user.locked_out` | Account locked | Security |
| `user.role_assigned` | Role given to a user in a tenant | Role |
| `user.role_removed` | Role taken from a user in a tenant | Role |
| `auth.login_success` | Successful login | Authentication |
| `auth.login_failed` | Failed login attempt | Authentication |
| `auth.logout` | User logged out | Authentication |
| `auth.token_issued` | Token issued | Authentication |
| `auth.token_revoked` | Token revoked | Authentication |
| `security.consent_granted` | OAuth consent granted | Security |
| `security.consent_revoked` | OAuth consent denied | Security |

Admin changes (`admin.*`), OIDC protocol events (`oidc.*`), journey submissions (`journey.submission_created`)
and team events (`team.*`) are also available; the Admin UI lists every registered event type.

### Webhook Payload

```json
{
  "id": "evt_abc123",
  "event_type": "user.created",
  "tenant_id": "tenant_xyz",
  "timestamp": "2024-01-15T10:30:00Z",
  "api_version": "2026-01-01",
  "origin": {
    "kind": "service_client",
    "id": "pay1",
    "name": null,
    "ip_address": "203.0.113.7"
  },
  "data": {
    "subject_id": "user_123",
    "email": "user@example.com",
    "username": "johndoe"
  },
  "metadata": {
    "client_id": "my-app"
  }
}
```

`origin` says what caused the event, whoever it was. User events are the same
(`user.created`, `user.updated`, `user.deleted`, `user.role_assigned`,
`user.role_removed`) whether an admin, a Service API client, SCIM or a journey
made the change; read `origin` to tell them apart. The admin API used to
raise its own copies (`admin.user_created`, `admin.user_updated`,
`admin.user_deleted`, `admin.user_role_assigned`, `admin.user_role_removed`,
`admin.password_reset`); they are gone, and existing webhook subscriptions on
them were moved to the core events on upgrade.

| `origin.kind` | `origin.id` |
|---------------|-------------|
| `admin` | The admin's user id (admin API) |
| `service_client` | The client_id of a client_credentials token (Service API) |
| `scim` | The inbound SCIM client id |
| `journey` | The journey policy id |
| `user` | The signed-in user's id, acting on their own account |
| `anonymous` | none: an unauthenticated request |
| `system` | The job or source, when known: background work and startup |

The same values are stored on audit log rows (`OriginKind`, `OriginId`,
`OriginName`; filter with `originKind` / `originId`).

#### Not echoing an integration's own changes

An integration that calls the Service API and also receives webhooks would be
told about its own writes and may write again in response. Link the webhook
endpoint to the integration's client (**Owning client** on the endpoint) and
keep **Don't send changes this client made itself** on: events whose origin is
that `service_client` are then not delivered to the endpoint. Other endpoints,
and changes made by anyone else, are unaffected.

Outbound SCIM has the same guard: set **Same system as inbound client** on a
SCIM provider and changes that inbound client provisions are not pushed back
to that provider.

### Webhook Security

Each delivery carries `X-Webhook-ID`, `X-Webhook-Event` and `X-Webhook-Timestamp` headers. When the endpoint
has a signing secret, payloads are signed with HMAC-SHA256 over `{X-Webhook-Timestamp}.{body}` and sent in
`X-Webhook-Signature` as `v1={hex}`. Verify the signature:

```csharp
var signature = request.Headers["X-Webhook-Signature"].ToString();
var timestamp = request.Headers["X-Webhook-Timestamp"].ToString(); // ISO 8601, use as sent

using var reader = new StreamReader(request.Body);
var payload = await reader.ReadToEndAsync();
var signedPayload = $"{timestamp}.{payload}";

using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(endpointSecret));
var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
var expected = $"v1={Convert.ToHexString(hash).ToLowerInvariant()}";

if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expected)))
    return Unauthorized();
```

### Webhook Retry Policy

With a retry processor registered, failed deliveries are retried with exponential backoff, up to `MaxRetries` (default 5):

| Attempt | Delay |
|---------|-------|
| 1 | 1 minute |
| 2 | 5 minutes |
| 3 | 30 minutes |
| 4 | 2 hours |
| 5 | 8 hours |

### Custom Webhook Events (Enterprise)

Enterprise packages can register their own webhook events:

```csharp
public class Fido2WebhookEventProvider : IWebhookEventProvider
{
    public string ProviderId => "fido2";
    public string DisplayName => "FIDO2/Passkeys";

    public IReadOnlyList<WebhookEventDefinition> GetEventDefinitions() => new[]
    {
        new WebhookEventDefinition
        {
            EventType = "passkey.registered",
            Category = "Security",
            DisplayName = "Passkey Registered",
            Description = "A new passkey was registered for a user"
        },
        new WebhookEventDefinition
        {
            EventType = "passkey.deleted",
            Category = "Security",
            DisplayName = "Passkey Deleted",
            Description = "A passkey was removed from a user account"
        }
    };
}

// Register
builder.Services.AddWebhookEventProvider<Fido2WebhookEventProvider>();
```

---

## Audit Logging

Audit logs provide a compliance-ready record of all security-relevant actions in the system.

### Enable Audit Logging

The EF stores register `AuditEventSink`, so every auditable event is written to the `AuditLogs` table with no
extra setup:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddUserJourneyEngine();
```

The Admin API exposes the log at `/api/admin/auditlogs` (query, `/{id}`, `/user/{subjectId}`,
`/resource/{resourceType}/{resourceId}`, `/export` and `DELETE /purge`).

### Audit Log Schema

| Column | Description |
|--------|-------------|
| `Id` | Unique log entry ID |
| `TenantId` | Tenant the entry belongs to |
| `Timestamp` | When the event occurred |
| `EventType` | Event type (e.g., "UserSignedIn") |
| `Category` | Event category (Authentication, User, Security) |
| `Action` | High-level action (Login, Logout, Register, etc.) |
| `Success` | Whether the action succeeded |
| `SubjectId` | User who performed the action |
| `SubjectName` | Username/display name |
| `SubjectEmail` | User's email |
| `ResourceType` | Type of resource affected |
| `ResourceId` | ID of affected resource |
| `ResourceName` | Name of affected resource |
| `ClientId` | OAuth client involved |
| `IpAddress` | Client IP address |
| `UserAgent` | Client user agent |
| `Details` | JSON with additional context |
| `ErrorMessage` | Error details for failures |
| `Reason` | Reason given for the action |
| `ActivityId` | Correlation ID for distributed tracing |
| `OriginKind` / `OriginId` / `OriginName` | What caused the event (see [Webhook Payload](#webhook-payload)) |

### Querying Audit Logs

```csharp
public class AuditController : ControllerBase
{
    private readonly IAuditLogService _auditService;
    private readonly ITenantContext _tenantContext;

    public AuditController(IAuditLogService auditService, ITenantContext tenantContext)
    {
        _auditService = auditService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs([FromQuery] AuditLogQuery query)
    {
        var result = await _auditService.QueryAsync(query);
        return Ok(result);
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserActivity(string userId)
    {
        var logs = await _auditService.GetBySubjectAsync(_tenantContext.TenantId, userId, limit: 100);
        return Ok(logs);
    }

    [HttpGet("resource/{type}/{id}")]
    public async Task<IActionResult> GetResourceHistory(string type, string id)
    {
        var logs = await _auditService.GetByResourceAsync(_tenantContext.TenantId, type, id, limit: 50);
        return Ok(logs);
    }
}
```

### Query Options

```csharp
var query = new AuditLogQuery
{
    TenantId = "tenant-123",          // Filter by tenant
    Category = "Authentication",      // Filter by category
    EventType = "UserSignedIn",       // Filter by event type
    SubjectId = "user_123",           // Filter by user
    ClientId = "my-app",              // Filter by client
    OriginKind = "service_client",    // Filter by origin
    Success = false,                  // Only failures
    StartDate = DateTime.UtcNow.AddDays(-7),
    EndDate = DateTime.UtcNow,
    SearchTerm = "john",              // Search in names/emails
    Page = 1,
    PageSize = 50,
    SortBy = "Timestamp",
    SortDescending = true
};
```

### Log Retention

Audit logs are not purged automatically. Purge them from the Admin API (`DELETE /api/admin/auditlogs/purge`)
or on a schedule:

```csharp
// Purge logs older than 90 days
public class AuditLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditLogCleanupService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            var cutoff = DateTime.UtcNow.AddDays(-90);
            await auditService.PurgeOldLogsAsync(tenantId: null, cutoff, stoppingToken);
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
```

---

## Licensing

Oluso includes a feature gating system. Licensed modules (SAML, LDAP, Azure Key Vault, OpenTelemetry) check
an `ILicenseValidator` when one is registered; the public build registers none, so they run without a license.
Extensions (like billing) can add their own feature providers.

### License Tiers

`LicenseTier` values: `Community`, `Starter`, `Professional`, `Enterprise`, `Development`. Feature keys are in
`LicensedFeatures` (for example `fido2`, `saml`, `scim`, `ldap`, `webhooks`, `audit-logging`, `telemetry`,
`keyvault`).

### Feature Gating

`AddOluso` registers `IFeatureGate`, with the organization and tenant feature providers. The sample also adds
per-user module seats:

```csharp
builder.Services.AddOrganizationFeatureProvider();   // Organization-level entitlements
builder.Services.AddTenantFeatureProvider();         // Tenant-level, within organization bounds
builder.Services.AddUserModuleAccessFeatureProvider(); // Per-user module seats (when MaxSeats is set)
```

The `IFeatureGate` interface provides runtime feature validation:

```csharp
public class MyService
{
    private readonly IFeatureGate _featureGate;

    public MyService(IFeatureGate featureGate) => _featureGate = featureGate;

    public async Task<IActionResult> ConfigureSaml()
    {
        var result = await _featureGate.CheckFeatureAsync(LicensedFeatures.Saml);
        if (!result.IsAllowed)
        {
            return new ObjectResult(result.Message) { StatusCode = StatusCodes.Status403Forbidden };
        }

        // Configure SAML...
    }
}
```

`RequireFeatureAsync(featureKey)` throws instead of returning a result.

### License Claims

`builder.Services.AddLicenseClaims()` adds license information to tokens via `LicenseClaimsProvider`:

| Claim | Description |
|-------|-------------|
| `license_tier` | License tier (community, professional, enterprise) |
| `license_expires` | Expiration as Unix timestamp |
| `licensed_features` | Array of enabled feature keys |

### Custom Feature Providers

Extend feature availability with custom providers (e.g., for billing integration):

```csharp
public class CustomFeatureProvider : IFeatureAvailabilityProvider
{
    public int Priority => 100;

    public async Task<bool> IsFeatureAvailableAsync(
        string featureKey,
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // Check tenant subscription, usage limits, etc.
        return await CheckFeatureAccess(featureKey, tenantId);
    }

    public async Task<IDictionary<string, object>> GetFeatureClaimsAsync(
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // Return additional claims for tokens
        return new Dictionary<string, object>
        {
            ["custom_tier"] = "premium"
        };
    }
}

// Register
builder.Services.AddFeatureProvider<CustomFeatureProvider>();
```

Providers are checked in descending `Priority`; the first that denies a feature wins.

---

## OpenTelemetry Integration

Oluso provides comprehensive OpenTelemetry support for observability with metrics, distributed tracing, and logging.

### Quick Start

```csharp
using Oluso.Telemetry.OpenTelemetry;

builder.Services.AddOlusoOpenTelemetry(options =>
{
    options.ServiceName = "MyAuthServer";
    options.OtlpEndpoint = "http://localhost:4317"; // Any OTLP collector
    options.EnableConsoleExporter = true; // For development
});
```

### Package Structure

| Package | Description |
|---------|-------------|
| `Syndew.Oluso.Telemetry.Abstractions` | Vendor-neutral interfaces for telemetry |
| `Syndew.Oluso.Telemetry.OpenTelemetry` | OpenTelemetry implementation, telemetry API and database logger |

### Configuration Options

```csharp
builder.Services.AddOlusoOpenTelemetry(options =>
{
    // Service identification
    options.ServiceName = "MyAuthServer";
    options.ServiceVersion = "1.0.0";

    // Feature toggles
    options.EnableMetrics = true;
    options.EnableTracing = true;
    options.EnableLogs = true;

    // Export configuration
    options.OtlpEndpoint = "http://collector:4317";
    options.LogsOtlpEndpoint = null;        // Defaults to OtlpEndpoint
    options.TempoEndpoint = null;           // Separate trace endpoint
    options.LokiEndpoint = null;            // Separate log endpoint
    options.EnableConsoleExporter = false;  // Development only

    // Additional resource attributes
    options.ResourceAttributes = new Dictionary<string, object>
    {
        ["deployment.environment"] = "production",
        ["service.namespace"] = "auth"
    };

    // Advanced customization
    options.ConfigureMetrics = builder =>
    {
        builder.AddRuntimeInstrumentation();
    };

    options.ConfigureTracing = builder =>
    {
        builder.AddEntityFrameworkCoreInstrumentation();
    };

    // Development/testing only
    options.SkipLicenseValidation = false;
});
```

The sample enables OpenTelemetry only when `Oluso:Telemetry:OtlpEndpoint` or `Oluso:Telemetry:TempoEndpoint` is
set, and otherwise registers the no-op implementation.

To keep application logs in the database for the Admin UI log viewer, add the database logger:

```csharp
builder.Logging.AddDatabaseLogger(options =>
{
    options.MinimumLevel = LogLevel.Information;
    options.ExcludeCategories.Add("Microsoft.EntityFrameworkCore");
});
```

### Metrics

Oluso tracks the following metrics via `IOlusoMetrics` (meter name `Oluso`):

| Metric | Type | Description |
|--------|------|-------------|
| `oluso.tokens.issued` | Counter | Tokens issued by grant type |
| `oluso.tokens.failed` | Counter | Failed token requests |
| `oluso.tokens.introspected` / `oluso.tokens.revoked` | Counter | Introspection and revocation |
| `oluso.authentication.success` | Counter | Successful authentications |
| `oluso.authentication.failures` | Counter | Failed authentication attempts |
| `oluso.authentication.external` | Counter | External IdP authentications |
| `oluso.mfa.challenges` | Counter | MFA challenges |
| `oluso.consent.granted` / `oluso.consent.denied` | Counter | Consent decisions |
| `oluso.sessions.active` | Gauge | Current active sessions |
| `oluso.request.duration` | Histogram | Request duration in ms |
| `oluso.database.duration` | Histogram | DB operation duration |

### Tracing

Distributed tracing uses the `Oluso` activity source and captures request flows with these span names:

| Span | Kind | Description |
|------|------|-------------|
| `Oluso.Token` | Server | Token endpoint requests |
| `Oluso.Authorize` | Server | Authorization endpoint |
| `Oluso.LocalLogin` | Internal | Local authentication |
| `Oluso.ExternalLogin` | Client | External IdP authentication |
| `Oluso.Journey` | Internal | User journey execution |
| `Oluso.JourneyStep` | Internal | One journey step |

### Custom Telemetry

Inject `IOlusoTelemetry` to add custom metrics/traces:

```csharp
public class MyService
{
    private readonly IOlusoTelemetry _telemetry;

    public MyService(IOlusoTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    public async Task DoWorkAsync()
    {
        using var activity = _telemetry.Tracing.StartActivity("MyService.DoWork");

        try
        {
            // Work...
            _telemetry.Metrics.TenantOperation("tenant-123", "custom_operation");
        }
        catch (Exception ex)
        {
            _telemetry.Tracing.RecordException(ex);
            throw;
        }
    }
}
```

### No-Op Fallback

When OpenTelemetry is not configured, register the no-op implementation so `IOlusoTelemetry` still resolves:

```csharp
builder.Services.AddOlusoNullTelemetry(); // NullOlusoTelemetry
```

---

## Telemetry Dashboard (Admin UI)

The `@oluso/plugin-telemetry` Admin UI plugin (`libs/plugins/telemetry`) provides a dashboard for viewing logs,
traces, and metrics. It is shown to super admins.

### Installation

The Admin UI app registers it at start-up:

```typescript
import { TELEMETRY_PLUGIN } from '@oluso/plugins/telemetry';

pluginService.registerAdminPlugin(TELEMETRY_PLUGIN);
```

The backend side is the telemetry API in `Oluso.Telemetry.OpenTelemetry`, enabled by
`AddOlusoOpenTelemetry`.

### Features

| Page | Path | Description |
|------|------|-------------|
| Dashboard | `/telemetry` | Overview metrics with charts |
| Logs | `/telemetry/logs` | Searchable application logs with live tail |
| Traces | `/telemetry/traces` | Distributed trace viewer |

Audit logs have their own page in the Admin UI, backed by `/api/admin/auditlogs`.

### Dashboard Metrics

The dashboard displays:
- **Tokens Issued**: Total tokens issued in the time range
- **Active Users**: Currently active users
- **Auth Success Rate**: Percentage of successful authentications
- **Avg Response Time**: Average request duration

### Log Viewer

- Filter by level (trace, debug, info, warn, error, fatal)
- Filter by category
- Full-text search
- Live tail mode (auto-refresh)
- Expandable log details with properties and exceptions
- Click-through to related traces

### Trace Viewer

- Hierarchical span visualization
- Span durations
- Error highlighting
- Filter by operation

### API Endpoints

The telemetry UI uses these admin API endpoints:

| Endpoint | Description |
|----------|-------------|
| `GET /api/admin/telemetry/metrics/overview` | Dashboard metrics |
| `GET /api/admin/telemetry/metrics/{metricName}` | Time series data |
| `GET /api/admin/telemetry/logs` | Application logs |
| `GET /api/admin/telemetry/logs/categories` | Log categories |
| `GET /api/admin/telemetry/traces` | Trace list |
| `GET /api/admin/telemetry/traces/{traceId}` | Trace detail |
