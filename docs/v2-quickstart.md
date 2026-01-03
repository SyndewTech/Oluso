# Oluso v2 Quick Start Guide

This guide covers the modular setup options for Oluso Identity Server v2.

## Table of Contents

- [Project Structure](#project-structure)
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
├── backend/                    # .NET projects
│   ├── Oluso/                  # Core identity server
│   ├── Oluso.Core/             # Domain entities and interfaces
│   ├── Oluso.EntityFramework/  # EF Core stores
│   ├── Oluso.Admin/            # Admin API controllers
│   ├── Oluso.Account/          # Account API (end-user self-service)
│   ├── Oluso.UI/               # Razor views and UI components
│   └── Oluso.Enterprise/       # Enterprise add-ons
│       ├── Fido2/              # FIDO2/Passkeys
│       ├── Saml/               # SAML IdP
│       ├── Scim/               # SCIM provisioning
│       └── Ldap/               # LDAP authentication
│
├── frontend/                   # React/TypeScript projects
│   ├── package.json            # npm workspace root
│   ├── AdminUI/                # Admin dashboard SPA
│   ├── AccountUI/              # End-user self-service portal
│   ├── ui-core/                # Shared components (@oluso/ui-core)
│   └── libs/                   # UI plugin packages
│       ├── fido2-ui/           # @oluso/fido2-ui
│       ├── saml-ui/            # @oluso/saml-ui
│       ├── scim-ui/            # @oluso/scim-ui
│       └── telemetry-ui/       # @oluso/telemetry-ui
```

### Frontend Workspaces

The frontend uses npm workspaces. Run commands from `src/frontend/`:

```bash
cd src/frontend
npm install              # Install all workspace dependencies
npm run build -w AdminUI # Build AdminUI
npm run dev -w AdminUI   # Dev server for AdminUI
```

### UI Plugin Architecture

Both Admin and Account UIs use a plugin system for extensibility. Plugins are defined in `@oluso/ui-core`:

```typescript
import { AdminUIPlugin, AccountUIPlugin } from '@oluso/ui-core';

// Admin plugin (for AdminUI)
const samlPlugin: AdminUIPlugin = {
  id: 'saml',
  name: 'SAML',
  navigation: [{ id: 'saml', name: 'SAML Apps', href: '/saml', icon: ShieldCheckIcon }],
  routes: [{ path: '/saml', component: SamlAppsPage }],
};

// Account plugin (for AccountUI - end-user self-service)
const passkeysPlugin: AccountUIPlugin = {
  id: 'passkeys',
  name: 'Passkeys',
  navigation: [{ label: 'Passkeys', path: '/security/passkeys', group: 'settings' }],
  routes: [{ path: '/security/passkeys', component: PasskeysPage }],
};
```

---

## Basic Setup

### Minimal Configuration

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneysWithDefaults();
```

Configuration in `appsettings.json`:
```json
{
  "Oluso": {
    "IssuerUri": "https://auth.example.com"
  }
}
```

### With Entity Framework

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneysWithDefaults();
```

### With Custom Options

```csharp
builder.Services.AddOluso(builder.Configuration, options =>
{
    options.IssuerUri = "https://auth.example.com";
    options.AutoMigrate = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddUserJourneysWithDefaults();
```

---

## User Journey Engine

The User Journey Engine provides a flexible, policy-driven authentication flow system.

### Enable with All Built-in Steps

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddUserJourneysWithDefaults();
```

This registers all built-in step handlers:

**UI Steps:**
- `local_login` - Username/password authentication
- `composite_login` - Combined local and external login
- `signup` - User registration
- `mfa` - Multi-factor authentication
- `consent` - OAuth scope consent
- `password_reset` - Forgot password flow
- `password_change` - In-journey password change
- `external_login` - Social/external identity providers
- `passwordless_email` - Email OTP/magic links
- `passwordless_sms` - SMS OTP
- `link_account` - Account linking
- `terms_acceptance` - Terms acceptance step
- `dynamic_form` - Custom form collection
- `claims_collection` - Claims gathering
- `captcha` - CAPTCHA verification

**Logic Steps (no UI):**
- `condition` - Conditional flow control
- `branch` - Branching logic
- `transform` - Data transformation
- `api_call` - External API calls
- `webhook` - Webhook notifications
- `create_user` - User creation
- `update_user` - User profile update

### Selective Step Registration

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

        // Or add all built-in steps at once
        // journeys.AddBuiltInSteps();

        // Or add all including logic steps
        // journeys.AddAllBuiltInSteps();
    });
```

---

## Step Handlers

### Local Login

Username/password authentication with lockout support.

```csharp
journeys.AddLocalLogin();
```

**View:** `Views/Journey/_LocalLogin.cshtml`

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
        "allowForgotPassword": true
      }
    }
  ]
}
```

### Sign Up

User registration with email verification and terms acceptance.

```csharp
journeys.AddSignUp();
```

**View:** `Views/Journey/_SignUp.cshtml`

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
        "allowedEmailDomains": "example.com,company.org",
        "termsUrl": "https://example.com/terms",
        "privacyUrl": "https://example.com/privacy"
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
- `Views/Journey/_MfaVerify.cshtml` - Code entry
- `Views/Journey/_MfaSetup.cshtml` - Method selection
- `Views/Journey/_MfaTotpSetup.cshtml` - Authenticator app setup
- `Views/Journey/_MfaEmailSetup.cshtml` - Email verification setup
- `Views/Journey/_MfaRecoveryCodes.cshtml` - Recovery codes display

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "mfa",
      "type": "mfa",
      "configuration": {
        "required": true,
        "methods": ["totp", "email"]
      }
    }
  ]
}
```

**Custom MFA Service:**
```csharp
builder.Services.AddScoped<IMfaService, CustomMfaService>();
```

### Consent

OAuth scope consent screen for client applications.

```csharp
journeys.AddConsent();
```

**View:** `Views/Journey/_Consent.cshtml`

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "consent",
      "type": "consent"
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
- `Views/Journey/_PasswordResetRequest.cshtml` - Email entry
- `Views/Journey/_PasswordResetVerify.cshtml` - Code verification
- `Views/Journey/_PasswordResetNewPassword.cshtml` - New password entry

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

### External Login

Social and enterprise identity provider authentication.

```csharp
journeys.AddExternalLogin();
```

**View:** `Views/Journey/_ExternalLogin.cshtml`

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "external",
      "type": "external_login",
      "configuration": {
        "providers": ["google", "microsoft", "github"],
        "autoRedirect": false,
        "autoProvision": true
      }
    }
  ]
}
```

---

## State Storage

### In-Memory (Default, Development Only)

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

// Then use it for journey state
builder.Services.AddOluso()
    .AddUserJourneys(journeys =>
    {
        journeys.AddBuiltInSteps();
        journeys.UseDistributedCache();
    });
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
    public Task<JourneyState?> GetAsync(string journeyId, CancellationToken ct) { ... }
    public Task SaveAsync(JourneyState state, CancellationToken ct) { ... }
    public Task DeleteAsync(string journeyId, CancellationToken ct) { ... }
    public Task<IEnumerable<JourneyState>> GetByUserAsync(string userId, CancellationToken ct) { ... }
    public Task CleanupExpiredAsync(CancellationToken ct) { ... }
}

// Register
journeys.UseStateStore<DatabaseJourneyStateStore>();
```

---

## Views and UI

### Option 1: Default Views (Oluso.UI Package)

Install the `Oluso.UI` NuGet package for ready-to-use views:

```xml
<PackageReference Include="Oluso.UI" Version="1.0.0" />
```

Views are automatically discovered from the Razor Class Library.

### Option 2: Custom Views (Recommended for Production)

Create your own views in your host application:

```
YourApp/
└── Views/
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

### Implement IMfaService

```csharp
public class CustomMfaService : IMfaService
{
    public Task<MfaSetupResult> GenerateTotpSetupAsync(string userId, CancellationToken ct)
    {
        // Generate TOTP shared key and QR code URI
        var key = GenerateSecretKey();
        var uri = $"otpauth://totp/YourApp:{userId}?secret={key}&issuer=YourApp";

        return Task.FromResult(MfaSetupResult.Success(key, uri));
    }

    public Task<bool> VerifyTotpCodeAsync(string userId, string code, CancellationToken ct)
    {
        // Verify TOTP code
        return Task.FromResult(ValidateTotpCode(userId, code));
    }

    public Task<bool> SendVerificationCodeAsync(string userId, string provider, CancellationToken ct)
    {
        // Send code via email/SMS
        return Task.FromResult(true);
    }

    public Task<MfaEnableResult> EnableMfaAsync(string userId, string provider, CancellationToken ct)
    {
        // Enable MFA and generate recovery codes
        var recoveryCodes = GenerateRecoveryCodes(10);
        return Task.FromResult(MfaEnableResult.Success(recoveryCodes));
    }

    // ... implement other methods
}

// Register
builder.Services.AddScoped<IMfaService, CustomMfaService>();
```

---

## External Authentication

External authentication allows users to sign in using their existing accounts from third-party identity providers like Google, Microsoft, GitHub, and others. This eliminates the need for users to create and remember yet another password while leveraging the security infrastructure of established providers.

Oluso provides comprehensive external authentication support with:

- **Built-in Social Providers**: Google, Microsoft, Facebook, Twitter/X, GitHub, LinkedIn, and Apple Sign In
- **Multi-Tenant Support**: Each tenant can have their own OAuth credentials configured in the database
- **Proxy Mode**: Act as a federation broker without storing user data locally
- **Token Caching**: Cache external tokens for subsequent API calls to upstream providers
- **Claim Transformation**: Filter and transform claims from external providers

### Quick Start: Static Providers

For simple single-tenant setups where all users share the same OAuth app credentials, configure providers directly in your startup code:

```csharp
builder.Services.AddOluso(configuration)
    .AddExternalAuthentication()
    .AddGoogle(
        clientId: configuration["Auth:Google:ClientId"],
        clientSecret: configuration["Auth:Google:ClientSecret"])
    .AddGitHub(
        clientId: configuration["Auth:GitHub:ClientId"],
        clientSecret: configuration["Auth:GitHub:ClientSecret"])
    .AddMicrosoftAccount(
        clientId: configuration["Auth:Microsoft:ClientId"],
        clientSecret: configuration["Auth:Microsoft:ClientSecret"]);
```

### Supported Providers

Oluso includes built-in support for the most popular OAuth providers. Each provider requires credentials from their developer console:

| Provider | Method | Required Parameters | Developer Console |
|----------|--------|---------------------|-------------------|
| Google | `AddGoogle()` | ClientId, ClientSecret | [Google Cloud Console](https://console.cloud.google.com/) |
| Microsoft | `AddMicrosoftAccount()` | ClientId, ClientSecret | [Azure Portal](https://portal.azure.com/) |
| Facebook | `AddFacebook()` | AppId, AppSecret | [Meta Developer Portal](https://developers.facebook.com/) |
| Twitter/X | `AddTwitter()` | ConsumerKey, ConsumerSecret | [Twitter Developer Portal](https://developer.twitter.com/) |
| GitHub | `AddGitHub()` | ClientId, ClientSecret | [GitHub Developer Settings](https://github.com/settings/developers) |
| LinkedIn | `AddLinkedIn()` | ClientId, ClientSecret | [LinkedIn Developer Portal](https://developer.linkedin.com/) |
| Apple | `AddApple()` | ClientId, TeamId, KeyId, PrivateKey | [Apple Developer Portal](https://developer.apple.com/) |

### Custom Scopes and Options

Each provider method accepts an optional configuration callback to customize scopes, token handling, and other provider-specific options:

```csharp
builder.Services.AddOluso(configuration)
    .AddGoogle(
        clientId: "...",
        clientSecret: "...",
        configure: opt =>
        {
            opt.Scopes.Add("https://www.googleapis.com/auth/calendar.readonly");
            opt.SaveTokens = true;
        });
```

### Generic OAuth/OIDC Providers

Beyond the built-in providers, you can integrate any OAuth 2.0 or OpenID Connect compliant identity provider. This is useful for enterprise SSO systems, custom identity providers, or providers not included in the built-in list:

```csharp
// OAuth 2.0 provider
builder.Services.AddOluso(configuration)
    .AddOAuthProvider("custom-oauth", "Custom Provider", options =>
    {
        options.ClientId = "...";
        options.ClientSecret = "...";
        options.AuthorizationEndpoint = "https://provider.example.com/authorize";
        options.TokenEndpoint = "https://provider.example.com/token";
        options.UserInformationEndpoint = "https://provider.example.com/userinfo";
    });

// OpenID Connect provider
builder.Services.AddOluso(configuration)
    .AddOidcProvider("custom-oidc", "Enterprise SSO", options =>
    {
        options.ClientId = "...";
        options.ClientSecret = "...";
        options.Authority = "https://sso.enterprise.com";
        options.ResponseType = "code";
    });
```

---

## Multi-Tenant External Providers

In multi-tenant SaaS applications, different tenants often need their own OAuth credentials. For example:

- **White-label scenarios**: Each customer wants their own branded Google/Microsoft login
- **Enterprise requirements**: Corporate clients require authentication through their own Azure AD tenant
- **Compliance**: Some tenants may need their OAuth apps registered in specific regions

With static provider configuration, all tenants share the same OAuth credentials. Dynamic provider configuration solves this by loading credentials per-tenant from your database at runtime.

### When to Use Dynamic Providers

| Scenario | Use Static | Use Dynamic |
|----------|------------|-------------|
| Single-tenant app | ✅ | |
| All tenants share OAuth apps | ✅ | |
| Tenants have own OAuth apps | | ✅ |
| White-label/branded login | | ✅ |
| Enterprise SSO per tenant | | ✅ |

### Enable Dynamic Providers

```csharp
builder.Services.AddOluso(configuration)
    .AddDynamicExternalProviders()
    .AddExternalProviderStore<EfExternalProviderStore>();
```

### Implement IExternalProviderConfigStore

You need to implement `IExternalProviderConfigStore` to load provider configurations from your database. This interface is called whenever authentication is initiated, and Oluso will cache the results to minimize database lookups.

Here's a complete example using Entity Framework Core:

```csharp
public class EfExternalProviderStore : IExternalProviderConfigStore
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public EfExternalProviderStore(
        ApplicationDbContext dbContext,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ExternalProviderDefinition>> GetEnabledProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        return await _dbContext.ExternalProviders
            .Where(p => p.Enabled)
            .Where(p => p.TenantId == tenantId || p.TenantId == null)
            .OrderByDescending(p => p.TenantId) // Tenant-specific first
            .Select(p => new ExternalProviderDefinition
            {
                Id = p.Id,
                TenantId = p.TenantId,
                Scheme = p.Scheme,
                DisplayName = p.DisplayName,
                ProviderType = p.ProviderType,
                ClientId = p.ClientId,
                ClientSecret = _encryptor.Decrypt(p.ClientSecretEncrypted),
                Scopes = p.Scopes,
                // ... other properties
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ExternalProviderDefinition?> GetBySchemeAsync(
        string scheme,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        // Try tenant-specific first, fall back to global
        var provider = await _dbContext.ExternalProviders
            .Where(p => p.Scheme == scheme && p.Enabled)
            .Where(p => p.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);

        provider ??= await _dbContext.ExternalProviders
            .Where(p => p.Scheme == scheme && p.Enabled)
            .Where(p => p.TenantId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (provider == null) return null;

        return new ExternalProviderDefinition
        {
            Id = provider.Id,
            TenantId = provider.TenantId,
            Scheme = provider.Scheme,
            ProviderType = provider.ProviderType,
            ClientId = provider.ClientId,
            ClientSecret = _encryptor.Decrypt(provider.ClientSecretEncrypted),
            // ... map other properties
        };
    }
}
```

### ExternalProviderDefinition Properties

The `ExternalProviderDefinition` class contains all the information needed to configure an OAuth provider at runtime:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | string | Unique identifier for this configuration |
| `TenantId` | string? | Tenant this config belongs to (null = global/fallback) |
| `Scheme` | string | Authentication scheme name (e.g., "Google", "Microsoft") |
| `ProviderType` | string | Provider type for handler selection (see supported types below) |
| `ClientId` | string | OAuth Client ID from the provider's developer console |
| `ClientSecret` | string | OAuth Client Secret (always store encrypted in database!) |
| `Scopes` | string[] | OAuth scopes to request (e.g., ["openid", "profile", "email"]) |
| `ProxyMode` | bool | Enable federation broker mode (see Proxy Mode section) |
| `StoreUserLocally` | bool | Whether to create local user records on login |
| `AutoProvisionUsers` | bool | Auto-create users on first external login |

**Supported Provider Types:** `Google`, `Microsoft`, `Facebook`, `Twitter`, `GitHub`, `LinkedIn`, `Apple`, `Oidc`, `OAuth`

### Database Schema Example

Here's a recommended database schema for storing external provider configurations. Note that `ClientSecretEncrypted` should use your application's encryption mechanism:

```sql
CREATE TABLE ExternalProviders (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NULL,
    Scheme NVARCHAR(100) NOT NULL,
    DisplayName NVARCHAR(200),
    ProviderType NVARCHAR(50) NOT NULL,
    Enabled BIT NOT NULL DEFAULT 1,
    ClientId NVARCHAR(500) NOT NULL,
    ClientSecretEncrypted NVARCHAR(1000) NOT NULL,
    Scopes NVARCHAR(1000),
    ProxyMode BIT NOT NULL DEFAULT 0,
    StoreUserLocally BIT NOT NULL DEFAULT 1,
    AutoProvisionUsers BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2
);

-- Example: Global Google provider
INSERT INTO ExternalProviders (Id, TenantId, Scheme, ProviderType, ClientId, ...)
VALUES (NEWID(), NULL, 'Google', 'Google', 'global-google-client-id', ...);

-- Example: Tenant-specific Google provider (overrides global)
INSERT INTO ExternalProviders (Id, TenantId, Scheme, ProviderType, ClientId, ...)
VALUES (NEWID(), 'tenant-abc-id', 'Google', 'Google', 'tenant-abc-google-client-id', ...);
```

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

### Cache Configuration

Provider configurations are cached in memory to avoid database lookups on every authentication request. The cache is tenant-aware, so changing one tenant's configuration doesn't affect others:

```csharp
builder.Services.AddOluso(configuration)
    .AddDynamicExternalProviders(options =>
    {
        options.CacheDurationMinutes = 5; // Default: 5 minutes
        options.AllowTenantOverrides = true; // Tenant configs override global
    });
```

To invalidate the cache (e.g., after admin updates provider config):

```csharp
public class ExternalProviderAdminService
{
    private readonly DynamicOptionsCache _optionsCache;
    private readonly DynamicSchemeCache _schemeCache;

    public async Task UpdateProviderAsync(string tenantId, ExternalProvider provider)
    {
        // Update in database
        await _dbContext.SaveChangesAsync();

        // Invalidate caches for this tenant
        _optionsCache.InvalidateForTenant(tenantId);
        _schemeCache.InvalidateForTenant(tenantId);
    }
}
```

---

## Proxy Mode (Federation Broker)

By default, when a user authenticates via an external provider (e.g., Google), Oluso creates a local user record and links it to the external identity. However, some scenarios require **Proxy Mode** where Oluso acts purely as a federation broker—authenticating users without storing any local data.

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
1. User authenticates with external IdP (e.g., corporate Azure AD)
2. Oluso receives the authentication response with claims
3. **No local user is created** — claims are passed through directly
4. Oluso issues its own tokens containing the external claims
5. External tokens can optionally be cached for upstream API calls

### Enable Proxy Mode

To enable proxy mode, set `ProxyMode = true` in your provider configuration. You'll typically also set `StoreUserLocally = false`:

```csharp
new ExternalProviderDefinition
{
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
| `amr` | `["external", "{provider}", "proxy"]` |

Plus all claims from the external IdP (filtered by `ProxyIncludeClaims`/`ProxyExcludeClaims`).

### Token Caching for Proxy Mode

In proxy mode, you may need to make API calls to the upstream identity provider on behalf of the user (e.g., fetching additional profile data, accessing Microsoft Graph, or calling Google APIs). Enable `CacheExternalTokens` to store the external access token for later use:

```csharp
// Retrieve cached tokens in your application
public class UserInfoProxyService
{
    private readonly IExternalAuthService _externalAuth;

    public async Task<ExternalUserInfo> GetUserInfoAsync(string sessionKey)
    {
        var tokens = await _externalAuth.GetCachedTokensAsync(sessionKey);

        if (tokens == null)
            throw new InvalidOperationException("No cached tokens");

        // Use external access token to call upstream IdP
        var response = await _httpClient.GetAsync(
            "https://external-idp.com/userinfo",
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken));

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
    public async Task<IReadOnlyList<ExternalProviderInfo>> GetAvailableProvidersAsync(
        CancellationToken ct)
    {
        // Return providers available for current tenant
    }

    public async Task<ExternalProviderConfig?> GetProviderConfigAsync(
        string provider,
        CancellationToken ct)
    {
        // Return configuration including proxy mode settings
    }

    public async Task<ExternalChallengeResult> ChallengeAsync(
        string provider,
        string returnUrl,
        CancellationToken ct)
    {
        // Initiate OAuth challenge
    }

    public async Task<ExternalLoginResult?> GetExternalLoginResultAsync(
        CancellationToken ct)
    {
        // Process OAuth callback, extract claims and tokens
    }

    public async Task<string?> FindUserByLoginAsync(
        string provider,
        string providerKey,
        CancellationToken ct)
    {
        // Find local user by external login
    }

    public async Task<ExternalLoginOperationResult> LinkLoginAsync(
        string userId,
        string provider,
        string providerKey,
        string? displayName,
        CancellationToken ct)
    {
        // Link external login to existing user
    }

    public async Task<ExternalLoginOperationResult> UnlinkLoginAsync(
        string userId,
        string provider,
        string providerKey,
        CancellationToken ct)
    {
        // Unlink external login from user
    }

    public async Task CacheExternalTokensAsync(
        string sessionKey,
        ExternalTokenData tokens,
        TimeSpan? expiry,
        CancellationToken ct)
    {
        // Cache tokens for proxy mode
    }

    public async Task<ExternalTokenData?> GetCachedTokensAsync(
        string sessionKey,
        CancellationToken ct)
    {
        // Retrieve cached tokens
    }
}

// Register
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
        "allowLinking": true
      }
    }
  ]
}
```

| Option | Description | Default |
|--------|-------------|---------|
| `providers` | List of allowed provider schemes | All enabled |
| `autoRedirect` | Auto-redirect if only one provider | `false` |
| `autoProvision` | Create user on first login | `true` |
| `allowLinking` | Allow linking to existing account | `true` |

**View:** `Views/Journey/_ExternalLogin.cshtml`

### Combining with Local Login

A common pattern is to offer both local (username/password) and external login options. You can combine them in a single step or use branching:

```json
{
  "steps": [
    {
      "id": "login",
      "type": "local_login",
      "configuration": {
        "allowExternalProviders": true
      },
      "branches": {
        "external": "external_auth"
      }
    },
    {
      "id": "external_auth",
      "type": "external_login",
      "configuration": {
        "providers": ["Google", "Microsoft"]
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

Replace the default ASP.NET Identity user store with your own:

```csharp
public class LdapUserService : IOlusoUserService
{
    public async Task<UserValidationResult> ValidateCredentialsAsync(
        string username, string password, CancellationToken ct)
    {
        // Validate against LDAP
        var ldapUser = await _ldapClient.AuthenticateAsync(username, password);

        if (ldapUser == null)
            return UserValidationResult.Failed("invalid_credentials", "Invalid username or password");

        return UserValidationResult.Success(new OlusoUserInfo
        {
            Id = ldapUser.Dn,
            Username = ldapUser.SamAccountName,
            Email = ldapUser.Email,
            EmailConfirmed = true,
            FirstName = ldapUser.GivenName,
            LastName = ldapUser.Surname,
            DisplayName = ldapUser.DisplayName
        });
    }

    public async Task<OlusoUserInfo?> FindByIdAsync(string userId, CancellationToken ct)
    {
        // Lookup user in LDAP by DN
        return await _ldapClient.FindByDnAsync(userId);
    }

    // ... implement other methods
}

// Register
builder.Services.AddOluso()
    .AddUserService<LdapUserService>();
```

---

## Custom Step Handlers

### Create a Custom Step

```csharp
public class CaptchaStepHandler : IStepHandler
{
    public string StepType => "captcha";

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken ct)
    {
        var response = context.GetInput("captcha_response");

        if (string.IsNullOrEmpty(response))
        {
            return StepHandlerResult.ShowUi("Journey/_Captcha", new CaptchaViewModel
            {
                SiteKey = _options.SiteKey
            });
        }

        var isValid = await _captchaService.ValidateAsync(response);

        if (!isValid)
        {
            return StepHandlerResult.ShowUi("Journey/_Captcha", new CaptchaViewModel
            {
                SiteKey = _options.SiteKey,
                ErrorMessage = "Please complete the captcha"
            });
        }

        return StepHandlerResult.Continue();
    }
}
```

### Register Custom Step

```csharp
builder.Services.AddOluso()
    .AddUserJourneys(journeys =>
    {
        journeys.AddBuiltInSteps();
        journeys.AddStepHandler<CaptchaStepHandler>();
    });
```

### Custom Authentication Steps

If your custom step performs user authentication (verifies credentials and establishes identity), you **must** call `SetAuthenticated()` to ensure a session cookie is issued when the journey completes.

```csharp
public class CustomAuthStepHandler : IStepHandler
{
    public string StepType => "custom_auth";

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken ct)
    {
        var username = context.GetInput("username");
        var token = context.GetInput("token");

        // Validate credentials...
        var user = await _authService.ValidateAsync(username, token);

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
| `idp` | Identity provider for external logins | `"google"`, `"saml:okta"` |

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

### Define a Policy

```csharp
var loginPolicy = new JourneyPolicy
{
    Id = "default-login",
    Name = "Default Login Flow",
    Type = JourneyType.Login,
    Steps = new List<JourneyPolicyStep>
    {
        new() { Id = "login", Type = "local_login", Order = 1 },
        new() { Id = "mfa", Type = "mfa", Order = 2, Optional = true },
        new() { Id = "consent", Type = "consent", Order = 3 }
    }
};
```

### Register Policies

```csharp
builder.Services.AddOluso()
    .AddUserJourneys(journeys =>
    {
        journeys.AddBuiltInSteps();
        journeys.UsePolicyStore<DatabasePolicyStore>();
    });
```

### Policy with Branches

```csharp
var loginPolicy = new JourneyPolicy
{
    Id = "login-with-registration",
    Steps = new List<JourneyPolicyStep>
    {
        new()
        {
            Id = "login",
            Type = "local_login",
            Order = 1,
            Branches = new Dictionary<string, string>
            {
                ["register"] = "signup"
            }
        },
        new() { Id = "signup", Type = "signup", Order = 2 },
        new() { Id = "mfa", Type = "mfa", Order = 3 }
    }
};
```

---

## Signing Keys

Oluso provides a pluggable signing key infrastructure for token signing (JWT access tokens, ID tokens) and verification. Keys can be stored locally with encryption at rest, or in external HSM-backed services like Azure Key Vault.

### Quick Start: Local Keys

For simple deployments, keys are stored encrypted in your database using ASP.NET Core Data Protection:

```csharp
builder.Services.AddOluso(configuration)
    .AddSigningKeys();
```

This provides:
- Automatic key generation (RS256 by default)
- Key encryption at rest using Data Protection
- Automatic key rotation support
- JWKS endpoint for token validation

### Signing Key Options

```csharp
builder.Services.AddOluso(configuration)
    .AddSigningKeys(options =>
    {
        options.DefaultAlgorithm = "RS256";  // or ES256, RS384, etc.
        options.DefaultKeySize = 2048;       // RSA key size
        options.DefaultKeyLifetimeDays = 365;
        options.AutoGenerateIfMissing = true;
        options.EnableKeyRotation = true;
        options.RotationOverlapDays = 7;     // Grace period for old keys
    });
```

### Available Algorithms

| Algorithm | Type | Key Size | Use Case |
|-----------|------|----------|----------|
| RS256 | RSA | 2048+ | Standard, widely compatible |
| RS384 | RSA | 2048+ | Higher security |
| RS512 | RSA | 2048+ | Maximum RSA security |
| ES256 | EC | P-256 | Fast, compact tokens |
| ES384 | EC | P-384 | Higher security EC |
| ES512 | EC | P-521 | Maximum EC security |

### Azure Key Vault (Enterprise)

For production deployments requiring HSM-backed keys where the private key never leaves the vault:

```bash
# Install the package
dotnet add package Oluso.Enterprise.AzureKeyVault
```

```csharp
builder.Services.AddOluso(configuration)
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
  "AzureKeyVault": {
    "VaultUri": "https://your-vault.vault.azure.net/"
  }
}
```

**Use Key Vault as the default provider for new keys:**

```csharp
builder.Services.AddOluso(configuration)
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

**Development vs Production:**

| Environment | Signing Credential Store | Key Storage |
|-------------|-------------------------|-------------|
| Development (no Key Vault) | `DevelopmentSigningCredentialStore` | In-memory RSA key |
| Production with `AddOlusoAzureKeyVault()` | `SigningCredentialStore` | Azure Key Vault (or local fallback) |

### Key Rotation

Oluso supports automatic key rotation with configurable overlap periods:

```csharp
builder.Services.AddOluso(configuration)
    .AddSigningKeys(options =>
    {
        options.EnableKeyRotation = true;
        options.RotationOverlapDays = 7;  // Old key stays valid for verification
    });

// Schedule rotation (e.g., in a background service)
public class KeyRotationService : BackgroundService
{
    private readonly ISigningKeyService _signingKeyService;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _signingKeyService.ProcessScheduledRotationsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
```

### Per-Client Keys

For scenarios where different OAuth clients need separate signing keys:

```csharp
// Generate key for specific client
await signingKeyService.GenerateKeyAsync(new GenerateKeyRequest
{
    TenantId = "tenant-123",
    ClientId = "my-client-id",
    Algorithm = "ES256",
    Name = "my-client-signing-key"
});

// Get signing credentials for specific client
var credentials = await signingKeyService.GetSigningCredentialsAsync("my-client-id");
```

### JWKS Endpoint

Oluso automatically exposes a JWKS (JSON Web Key Set) endpoint for token validation. Clients can fetch public keys from:

```
GET /.well-known/jwks.json
```

The JWKS includes all active keys (current and those in rotation overlap period).

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

    // ... implement other methods
}

// Register
builder.Services.AddSingleton<IKeyMaterialProvider, AwsKmsProvider>();
```

---

## Enterprise Modules

### FIDO2/WebAuthn (Oluso.Enterprise.Fido2)

Add FIDO2/Passkey support for passwordless authentication:

```csharp
builder.Services.AddOluso(configuration)
    .AddUserJourneys(journeys => journeys.AddBuiltInSteps())
    .AddFido2(fido2 => fido2
        .WithRelyingParty("example.com", "Example App")
        .WithOrigins("https://example.com")
        .RequireUserVerification()
        .PreferPlatformAuthenticator());
```

**Important:** FIDO2 requires session middleware. Add to your pipeline:

```csharp
app.UseAuthentication();
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

**Policy Configuration:**
```json
{
  "steps": [
    {
      "id": "login",
      "type": "local_login",
      "configuration": {
        "allowPasskeyLogin": true
      }
    },
    {
      "id": "passkey_setup",
      "type": "fido2_register",
      "optional": true,
      "configuration": {
        "promptNewUsers": true,
        "requireResidentKey": true
      }
    }
  ]
}
```

**Views:**
- `Views/Journey/_Fido2Assertion.cshtml` - Passkey login UI
- `Views/Journey/_Fido2Registration.cshtml` - Passkey registration UI

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
| `GET /api/admin/fido2/users/{userId}/credentials` | List user's passkeys (admin) | AdminApi |
| `DELETE /api/admin/fido2/users/{userId}/credentials/{id}` | Delete user's passkey (admin) | AdminApi |

**Configuration Options:**

```csharp
.AddFido2(fido2 => fido2
    .WithRelyingParty("example.com", "Example App", iconUrl: null)
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

### SAML IdP (Oluso.Enterprise.Saml)

Add SAML 2.0 Identity Provider support for enterprise SSO:

```csharp
builder.Services.AddSaml(builder.Configuration);

builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneysWithDefaults();
```

**Configuration (appsettings.json):**
```json
{
  "Saml": {
    "EntityId": "https://auth.example.com/saml",
    "SigningCertificatePath": "certs/saml-signing.pfx",
    "SigningCertificatePassword": "your-password"
  }
}
```

**API Endpoints:**

| Endpoint | Description |
|----------|-------------|
| `GET /saml/metadata` | SAML metadata document |
| `POST /saml/sso` | Single Sign-On endpoint |
| `GET /saml/slo` | Single Logout endpoint |
| `GET /api/admin/saml/service-providers` | List configured SPs |
| `POST /api/admin/saml/service-providers` | Create SP configuration |
| `PUT /api/admin/saml/service-providers/{id}` | Update SP configuration |
| `DELETE /api/admin/saml/service-providers/{id}` | Delete SP configuration |

---

### SCIM Provisioning (Oluso.Enterprise.Scim)

Add SCIM 2.0 user provisioning for automated user management:

```csharp
builder.Services.AddScim();
builder.Services.AddScimDbContext<ApplicationDbContext>();

builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneysWithDefaults();

// In middleware pipeline
app.UseScim();
```

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
| `GET /scim/v2/ServiceProviderConfig` | SCIM service config |
| `GET /scim/v2/Schemas` | SCIM schemas |

---

### LDAP Authentication (Oluso.Enterprise.Ldap)

Add LDAP directory authentication:

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddLdap(ldap => ldap
        .WithServer("ldap.example.com", 389)
        .WithBaseDn("dc=example,dc=com")
        .WithBindCredentials("cn=admin,dc=example,dc=com", "password")
        .WithUserSearchFilter("(uid={0})")
        .WithGroupSearchFilter("(memberUid={0})"))
    .AddUserJourneysWithDefaults();
```

**Or with LDAP server (acting as an LDAP IdP):**

```csharp
builder.Services.AddOluso(builder.Configuration)
    .AddLdapServer(server => server
        .WithPort(389)
        .WithBaseDn("dc=example,dc=com")
        .EnableAnonymousBind(false));
```

---

## Complete Example

These examples show complete `Program.cs` configurations for common scenarios. Copy the appropriate example as a starting point for your application.

### Single-Tenant Setup

Best for: Internal apps, single-customer deployments, or apps where all users share the same identity configuration.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Add distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

// Add Oluso with static external providers
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneys(journeys =>
    {
        journeys.AddBuiltInSteps();
        journeys.UseDistributedCache();
        journeys.UsePolicyStore<EfJourneyPolicyStore>();
    })
    // External authentication with static providers
    .AddExternalAuthentication()
    .AddGoogle(
        builder.Configuration["Auth:Google:ClientId"]!,
        builder.Configuration["Auth:Google:ClientSecret"]!)
    .AddGitHub(
        builder.Configuration["Auth:GitHub:ClientId"]!,
        builder.Configuration["Auth:GitHub:ClientSecret"]!)
    .AddMicrosoftAccount(
        builder.Configuration["Auth:Microsoft:ClientId"]!,
        builder.Configuration["Auth:Microsoft:ClientSecret"]!);

// Add MFA service
builder.Services.AddScoped<IMfaService, TotpMfaService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapOlusoEndpoints();

app.Run();
```

### Multi-Tenant Setup with Dynamic Providers

Best for: SaaS applications, white-label platforms, or any scenario where different customers need their own OAuth configurations.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Add distributed cache (required for multi-tenant state management)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

// Add Oluso with multi-tenancy and dynamic external providers
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddMultiTenancy(options =>
    {
        // Resolve tenant from subdomain (e.g., acme.yourapp.com -> tenant "acme")
        options.ResolutionStrategy = TenantResolutionStrategy.Host;
    })
    .AddUserJourneys(journeys =>
    {
        journeys.AddBuiltInSteps();
        journeys.UseDistributedCache();  // Redis-backed for multi-instance deployments
        journeys.UsePolicyStore<EfJourneyPolicyStore>();
    })
    // Dynamic external providers: each tenant loads OAuth config from database
    .AddDynamicExternalProviders(options =>
    {
        options.CacheDurationMinutes = 5;
        options.AllowTenantOverrides = true;
    })
    .AddExternalProviderStore<EfExternalProviderStore>();

// Add MFA service
builder.Services.AddScoped<IMfaService, TotpMfaService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapOlusoEndpoints();

app.Run();
```

### Configuration (appsettings.json)

Store sensitive credentials in environment variables, Azure Key Vault, or a secrets manager for production. This example shows the structure for development:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=oluso;Username=app;Password=secret",
    "Redis": "localhost:6379"
  },
  "Oluso": {
    "IssuerUri": "https://auth.example.com"
  },
  "Auth": {
    "Google": {
      "ClientId": "your-google-client-id.apps.googleusercontent.com",
      "ClientSecret": "your-google-client-secret"
    },
    "GitHub": {
      "ClientId": "your-github-client-id",
      "ClientSecret": "your-github-client-secret"
    },
    "Microsoft": {
      "ClientId": "your-microsoft-client-id",
      "ClientSecret": "your-microsoft-client-secret"
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

Events are registered on `IServiceCollection`:

```csharp
// Add the event system
builder.Services.AddOlusoEvents();

// Add event sinks
builder.Services.AddLoggerEventSink();           // Logs to ILogger
builder.Services.AddEventSink<AuditEventSink>(); // Persists to database
builder.Services.AddWebhookEventSink();          // Dispatches to webhook endpoints

// Then add Oluso
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneysWithDefaults();
```

### Event Categories

| Category | Events |
|----------|--------|
| `Authentication` | UserSignedIn, UserSignInFailed, UserSignedOut |
| `User` | UserRegistered, UserUpdated, UserDeleted, UserEmailVerified, UserPasswordChanged |
| `Security` | ConsentGranted, ConsentDenied, UserLockedOut, MfaCompleted |
| `Token` | TokenIssued, TokenRevoked |

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

// Security events
ConsentGrantedEvent      // OAuth consent granted
ConsentDeniedEvent       // OAuth consent denied
MfaCompletedEvent        // MFA verification result (success/fail)

// Token events
TokenIssuedEvent         // Access/ID token issued
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
// Add webhook support
builder.Services.AddOlusoWebhooks();
builder.Services.AddCoreWebhookEvents();

// Then add Oluso
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneysWithDefaults();
```

### Webhook Events

| Event Type | Description | Category |
|------------|-------------|----------|
| `user.created` | New user registered | User |
| `user.updated` | User profile updated | User |
| `user.deleted` | User account deleted | User |
| `user.email_verified` | Email verified | User |
| `user.password_changed` | Password changed | User |
| `user.locked_out` | Account locked | Security |
| `auth.login_success` | Successful login | Authentication |
| `auth.login_failed` | Failed login attempt | Authentication |
| `auth.logout` | User logged out | Authentication |
| `auth.token_issued` | Token issued | Authentication |
| `security.consent_granted` | OAuth consent granted | Security |
| `security.consent_revoked` | OAuth consent denied | Security |

### Webhook Payload

```json
{
  "id": "evt_abc123",
  "event_type": "user.created",
  "timestamp": "2024-01-15T10:30:00Z",
  "tenant_id": "tenant_xyz",
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

### Webhook Security

Payloads are signed with HMAC-SHA256. Verify the signature:

```csharp
var signature = request.Headers["X-Webhook-Signature"];
var timestamp = request.Headers["X-Webhook-Timestamp"];

var payload = await request.Content.ReadAsStringAsync();
var signedPayload = $"{timestamp}.{payload}";

using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(endpointSecret));
var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
var expected = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";

if (signature != expected)
    return Unauthorized();
```

### Webhook Retry Policy

Failed deliveries are retried with exponential backoff:

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

```csharp
// Add event system with audit sink
builder.Services.AddOlusoEvents();
builder.Services.AddEventSink<AuditEventSink>();  // Persists events to AuditLogs table

// Then add Oluso with EF stores
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneysWithDefaults();
```

### Audit Log Schema

| Column | Description |
|--------|-------------|
| `Id` | Unique log entry ID |
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
| `ClientId` | OAuth client involved |
| `IpAddress` | Client IP address |
| `UserAgent` | Client user agent |
| `Details` | JSON with additional context |
| `ErrorMessage` | Error details for failures |
| `ActivityId` | Correlation ID for distributed tracing |

### Querying Audit Logs

```csharp
public class AuditController : ControllerBase
{
    private readonly IAuditLogService _auditService;

    [HttpGet]
    public async Task<IActionResult> GetLogs([FromQuery] AuditLogQuery query)
    {
        var result = await _auditService.QueryAsync(query);
        return Ok(result);
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserActivity(string userId)
    {
        var logs = await _auditService.GetBySubjectAsync(userId, limit: 100);
        return Ok(logs);
    }

    [HttpGet("resource/{type}/{id}")]
    public async Task<IActionResult> GetResourceHistory(string type, string id)
    {
        var logs = await _auditService.GetByResourceAsync(type, id, limit: 50);
        return Ok(logs);
    }
}
```

### Query Options

```csharp
var query = new AuditLogQuery
{
    Category = "Authentication",      // Filter by category
    EventType = "UserSignedIn",       // Filter by event type
    SubjectId = "user_123",           // Filter by user
    ClientId = "my-app",              // Filter by client
    Success = false,                  // Only failures
    StartDate = DateTime.UtcNow.AddDays(-7),
    EndDate = DateTime.UtcNow,
    SearchTerm = "john",              // Search in names/emails
    Page = 1,
    PageSize = 50,
    SortBy = "timestamp",
    SortDescending = true
};
```

### Log Retention

```csharp
// Purge logs older than 90 days
public class AuditLogCleanupService : BackgroundService
{
    private readonly IAuditLogService _auditService;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var cutoff = DateTime.UtcNow.AddDays(-90);
            await _auditService.PurgeOldLogsAsync(cutoff, stoppingToken);
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
```

---

## Licensing

Oluso includes a flexible licensing system for feature gating. The platform license uses offline RSA signature validation, while extensions (like billing) can add online subscription-based features.

### Quick Start

Licensing is registered on `IServiceCollection`:

```csharp
// With a license key
builder.Services.AddOlusoLicensing(builder.Configuration["Oluso:LicenseKey"]);

// Or for community/development
builder.Services.AddOlusoCommunityLicense();
builder.Services.AddOlusoDevelopmentLicense(); // For development only

// Then add Oluso
builder.Services.AddOluso(builder.Configuration)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserJourneysWithDefaults();
```

### License Tiers

| Tier | Features |
|------|----------|
| Community | Core OIDC, local authentication, basic MFA |
| Professional | + External providers, webhooks, audit logging |
| Enterprise | + FIDO2, SAML, SCIM, LDAP, custom journeys |

### Feature Gating

The `IFeatureGate` interface provides runtime feature validation:

```csharp
public class MyService
{
    private readonly IFeatureGate _featureGate;

    public async Task<IActionResult> ConfigureSaml()
    {
        if (!await _featureGate.IsEnabledAsync(Features.Saml))
        {
            return Forbid("SAML requires an Enterprise license");
        }

        // Configure SAML...
    }
}
```

### License Claims

License information is added to tokens via `LicenseClaimsProvider`:

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

---

## Migration from v1

If upgrading from IdentityServer.Core:

1. Replace `IdentityServer.Core` references with `Oluso.Core`
2. Replace `IdentityServer.Endpoints` with `Oluso`
3. Update namespaces from `IdentityServer.*` to `Oluso.*`
4. Replace `IJourneyStepHandler` with `IStepHandler`
5. Replace `StepResult` with `StepHandlerResult`
6. Update step type strings (e.g., `StepType.LocalLogin` to `"local_login"`)

See [Migration Guide](./migration-v1-to-v2.md) for detailed instructions.

---

## OpenTelemetry Integration

Oluso provides comprehensive OpenTelemetry support for observability with metrics, distributed tracing, and logging.

### Quick Start

```csharp
builder.Services.AddOluso(configuration)
    .AddOlusoOpenTelemetry(options =>
    {
        options.ServiceName = "MyAuthServer";
        options.OtlpEndpoint = "http://localhost:4317"; // Jaeger, Tempo, etc.
        options.EnableConsoleExporter = true; // For development
    });
```

### Package Structure

| Package | Description |
|---------|-------------|
| `Oluso.Telemetry.Abstractions` | Vendor-neutral interfaces for telemetry |
| `Oluso.Telemetry.OpenTelemetry` | OpenTelemetry implementation |

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

    // Export configuration
    options.OtlpEndpoint = "http://collector:4317";
    options.EnableConsoleExporter = false; // Development only

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
});
```

### Metrics

Oluso tracks the following metrics via `IOlusoMetrics`:

| Metric | Type | Description |
|--------|------|-------------|
| `oluso.tokens.issued` | Counter | Tokens issued by grant type |
| `oluso.tokens.failed` | Counter | Failed token requests |
| `oluso.authentication.success` | Counter | Successful authentications |
| `oluso.authentication.failures` | Counter | Failed authentication attempts |
| `oluso.sessions.active` | Gauge | Current active sessions |
| `oluso.request.duration` | Histogram | Request duration in ms |
| `oluso.database.duration` | Histogram | DB operation duration |

### Tracing

Distributed tracing captures request flows with these span names:

| Span | Kind | Description |
|------|------|-------------|
| `Oluso.Token` | Server | Token endpoint requests |
| `Oluso.Authorize` | Server | Authorization endpoint |
| `Oluso.LocalLogin` | Internal | Local authentication |
| `Oluso.ExternalLogin` | Client | External IdP authentication |
| `Oluso.Journey` | Internal | User journey execution |
| `Oluso.Database` | Client | Database operations |

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

When OpenTelemetry is not configured, a no-op implementation is used:

```csharp
// Automatically falls back to NullOlusoTelemetry
builder.Services.AddOluso(configuration)
    .EnsureTelemetryServices(); // Registers no-op if nothing else configured
```

---

## Telemetry Dashboard (Admin UI)

The `@oluso/telemetry-ui` package provides a React-based dashboard for viewing logs, traces, and metrics.

### Installation

```typescript
import { createTelemetryPlugin } from '@oluso/telemetry-ui';

const telemetryPlugin = createTelemetryPlugin({
  apiClient: axiosInstance,
});

// Register with admin UI
adminUI.registerPlugin(telemetryPlugin);
```

### Features

| Page | Path | Description |
|------|------|-------------|
| Dashboard | `/telemetry` | Overview metrics with charts |
| Logs | `/telemetry/logs` | Searchable application logs with live tail |
| Traces | `/telemetry/traces` | Distributed trace viewer |
| Audit Logs | `/telemetry/audit` | Security audit trail |

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
- Duration bar chart showing timing
- Span tags and events
- Error highlighting
- Operation breakdown

### Audit Log Viewer

- Filter by category (Authentication, User, Security, Token)
- Date range filtering
- User activity tracking
- Export to CSV
- Detailed event properties

### API Endpoints

The telemetry UI requires these admin API endpoints:

| Endpoint | Description |
|----------|-------------|
| `GET /api/admin/telemetry/metrics/overview` | Dashboard metrics |
| `GET /api/admin/telemetry/metrics/{name}` | Time series data |
| `GET /api/admin/telemetry/logs` | Application logs |
| `GET /api/admin/telemetry/traces` | Trace list |
| `GET /api/admin/telemetry/traces/{id}` | Trace detail |
| `GET /api/admin/telemetry/audit` | Audit logs |

### Reusable Components

Export components for custom dashboards:

```typescript
import {
  MetricCard,
  SimpleBarChart,
  SimpleLineChart,
  LogViewer,
  TraceViewer,
  AuditLogViewer,
} from '@oluso/telemetry-ui';

// Use in custom pages
<MetricCard
  title="Active Users"
  value={1234}
  icon={UserGroupIcon}
  color="green"
/>
```
