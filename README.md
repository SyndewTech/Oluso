# Oluso Identity Server

A modern, enterprise-grade identity server built on .NET 8 with full OAuth 2.0/OIDC compliance, SAML 2.0, SCIM 2.0, and LDAP support.

## Features

### Core
- **OAuth 2.0 / OpenID Connect** - Full protocol compliance including PKCE, DPoP, PAR, CIBA
- **Multi-tenancy** - Complete tenant isolation with custom domains
- **User Journeys** - Customizable authentication flows with WASM plugin support
- **Dynamic Providers** - Database-driven external identity provider configuration

### Enterprise Add-ons
- **SAML 2.0** - Identity Provider and Service Provider modes
- **SCIM 2.0** - User and group provisioning
- **LDAP Server** - Expose users via LDAP for legacy applications
- **FIDO2/Passkeys** - WebAuthn passwordless authentication
- **Azure Key Vault** - HSM-backed signing keys

## Quick Start

```bash
cd samples/Oluso.Sample
dotnet run
```

Visit `http://localhost:5050` to explore the sample application.

## Project Structure

```
├── src/
│   ├── backend/
│   │   ├── Oluso/                 # Core identity server
│   │   ├── Oluso.Core/            # Domain models and interfaces
│   │   ├── Oluso.EntityFramework/ # EF Core data access
│   │   ├── Oluso.Enterprise/      # Enterprise features (SAML, SCIM, LDAP, FIDO2)
│   │   ├── Oluso.Admin/           # Admin API controllers
│   │   └── Oluso.Account/         # User account API
│   └── frontend/
│       ├── admin-ui/              # Admin dashboard (React)
│       └── account-ui/            # User account portal (React)
├── samples/
│   └── Oluso.Sample/              # Complete sample application
└── tests/
    ├── Oluso.Tests/               # Unit tests
    └── Oluso.Integration.Tests/   # Integration tests
```

## Minimal Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddAdminApi(mvc => mvc.AddOlusoAdmin().AddOlusoAccount())
    .AddEntityFrameworkStores(options => options.UseSqlite(connectionString))
    .AddFido2(fido2 => fido2.WithRelyingParty("localhost", "My App"))
    .AddLdap()
    .AddDynamicExternalProviders()
    .AddSigningKeys();

// Enterprise add-ons
builder.Services.AddLdapServer(builder.Configuration);
builder.Services.AddSaml(builder.Configuration);
builder.Services.AddScim();

var app = builder.Build();

app.UseOluso();
app.MapControllers();
app.Run();
```

## Configuration

All configuration is centralized under the `Oluso` section in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "OlusoDb": "Data Source=Oluso.db"
  },
  "Oluso": {
    "IssuerUri": "https://auth.yourdomain.com",
    "Database": { "Provider": "Sqlite" },
    "Urls": { "BaseUrl": "https://auth.yourdomain.com" },
    "Fido2": { "RelyingPartyId": "yourdomain.com" },
    "LdapServer": { "Enabled": true },
    "Saml": { "IdentityProvider": { "Enabled": true } },
    "Scim": { "Enabled": true },
    "Messaging": {
      "Email": { "Provider": "SendGrid" },
      "Sms": { "Provider": "Twilio" }
    }
  }
}
```

### Database Providers

Set `Oluso:Database:Provider` to automatically configure the database:

| Provider | Value | Connection String Example |
|----------|-------|---------------------------|
| SQLite | `Sqlite` | `Data Source=Oluso.db` |
| SQL Server | `SqlServer` | `Server=...;Database=Oluso;...` |
| PostgreSQL | `PostgreSQL` | `Host=...;Database=Oluso;...` |

## Test Credentials

The sample application seeds these accounts:

| Email | Password | Role |
|-------|----------|------|
| `superadmin@localhost` | `Admin123!` | Super Admin |
| `admin@localhost` | `Admin123!` | Admin |
| `testuser@example.com` | `Password123!` | User |

## Endpoints

| Endpoint | Description |
|----------|-------------|
| `/.well-known/openid-configuration` | OIDC Discovery |
| `/connect/authorize` | Authorization endpoint |
| `/connect/token` | Token endpoint |
| `/connect/userinfo` | UserInfo endpoint |
| `/saml/idp/metadata` | SAML IdP metadata |
| `/scim/v2/Users` | SCIM Users API |
| `/api/admin/*` | Admin API |

## License

Copyright (c) 2025-2026 Syndew Technology Ltd. All rights reserved.

This software is proprietary. A commercial license is required for production use.

See [LICENSE](LICENSE) for details or visit [oluso.io/license](https://oluso.io/license) for full terms.
