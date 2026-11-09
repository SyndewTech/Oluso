# Oluso Identity Server

A multi-tenant identity server built on .NET 10, with OAuth 2.0 / OpenID Connect, SAML 2.0, SCIM 2.0, LDAP and FIDO2/passkeys, a user journey engine, and Angular admin, account and workspace portals.

## Features

### Core
- **OAuth 2.0 / OpenID Connect** - Authorization code with PKCE, client credentials, refresh tokens, device authorization (RFC 8628), token exchange (RFC 8693), CIBA (poll, ping and push), and resource owner password for legacy clients
- **Sender-constrained tokens** - DPoP (RFC 9449) and mutual-TLS certificate-bound tokens (RFC 8705)
- **Pushed Authorization Requests** (RFC 9126), resource indicators (RFC 8707), dynamic client registration (RFC 7591), introspection, revocation and back-channel logout
- **Prefix scopes** - API scopes whose suffix the client chooses, such as `device:<id>` (see [docs/prefix-scopes.md](docs/prefix-scopes.md))
- **Matrix homeservers** - The OAuth 2.0 authorization server for a Matrix homeserver: Matrix scopes, client self-registration, sign in with QR, refresh tokens and session-end notices as the Matrix spec expects (see [docs/matrix.md](docs/matrix.md))
- **Multi-tenancy** - Tenant isolation, per-tenant signing keys, branding and settings, resolved by subdomain, verified custom domain or `X-Tenant-Id` header
- **Organizations** - Group tenants under an organization, with organization sign-in and organization- and tenant-level feature entitlements
- **User journeys** - Configurable sign-in, sign-up, password reset, profile and data-collection flows built from steps, with WebAssembly (Extism) plugins that reload when the file changes
- **Dynamic providers** - External identity providers (Google, Microsoft, GitHub, Apple, Facebook, LinkedIn, X, any OIDC or OAuth 2.0 provider) configured per tenant in the database
- **MFA** - TOTP, email and SMS codes, recovery codes, passkeys
- **Events and webhooks** - Event sinks for logs, audit and webhooks; webhooks signed with HMAC-SHA256 or with the tenant's issuer key, with retries
- **Messaging** - Email (SendGrid), SMS (Twilio, Infobip) and WhatsApp (Meta Cloud API)

### Enterprise add-ons
- **SAML 2.0** - Identity Provider and Service Provider
- **SCIM 2.0** - Inbound provisioning (Users, Groups, Bulk) and outbound provisioning to downstream SCIM services
- **LDAP** - Authenticate against LDAP / Active Directory with group mapping and user sync, or expose users through the built-in LDAP server
- **FIDO2 / passkeys** - WebAuthn registration and sign-in, as a journey step or a second factor
- **Business verification** - KYB checks as a journey step (Dojah, Youverify)
- **Azure integrations** - Key Vault (including HSM-backed signing keys), Blob Storage file uploads, a shared Data Protection key ring (Blob Storage, wrapped with Key Vault), queue-based webhook retries
- **Cloudflare** - Custom hostnames for tenant domains
- **OpenTelemetry** - Traces, metrics and logs over OTLP

## Quick Start

### Prerequisites

| To | You need |
|----|----------|
| Try it in Docker | Docker with Compose v2 |
| Run the server from source | .NET SDK 10.0.102 or later (`global.json`) |
| Run or build the portals | Node.js 22 and npm |

### Which path?

| You want to | Start with |
|-------------|-----------|
| See Oluso working, with the portals | [Docker](#docker) |
| Work on Oluso itself | [From source](#from-source) |
| Add Oluso to your own server | [Packages](#packages) and [docs/v2-quickstart.md](docs/v2-quickstart.md) |

### Docker

Runs the sample server with the admin, workspace and account portals behind Caddy, from the repository root:

```bash
docker compose up --build
```

The first build takes a few minutes. Then open `https://localhost:8443/admin` (accept the development certificate warning) and sign in as `superadmin@oluso.local` / `SuperAdmin123!`. The workspace and account portals are under `/workspace` and `/account`, and the other [seeded accounts](#test-credentials) work too.

Data is kept in volumes, so it survives rebuilds. To run on PostgreSQL, add the override, which starts a `postgres` container next to Oluso:

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up --build
```

[docs/docker.md](docs/docker.md) covers the rest: what runs, volumes and backups, SQL Server or your own database, configuration, seeding without the test accounts, running the server image on its own, running a single instance on a domain, production with several instances, and troubleshooting.

### From source

The sample server runs every public module against SQLite, with development defaults in `samples/Oluso.Sample/appsettings.json`:

```bash
cd samples/Oluso.Sample
dotnet run
```

It listens on `http://localhost:5050`, which serves a landing page linking to the discovery document, the OIDC, SAML and LDAP test pages, and Swagger at `/swagger`. The database is migrated and seeded on startup, and the background jobs (signing-key rotation, expired-record cleanup, custom-domain checks, webhook retries, SCIM outbound provisioning) run in the same process.

To run the portals against it, in a second terminal:

```bash
cd src/frontend
npm ci
npx nx serve account-standalone     # http://localhost:4200
npx nx serve admin-standalone       # http://localhost:4201
npx nx serve workspace-standalone   # http://localhost:4202
```

Sign in to the admin portal at `http://localhost:4201` as `superadmin@oluso.local` / `SuperAdmin123!`.

### Running the tests

From the repository root:

```bash
dotnet test Oluso.sln                                     # backend: unit, integration and end-to-end tests

(cd src/frontend && npm ci \
  && npx nx run-many --target=build --all \
  && npx nx run-many --target=test --all --watch=false)   # portals and libraries

(cd sdk/js && npm ci && npm test -- --run)                # JavaScript SDK
```

The integration tests start the sample server in memory, so they need no database or running services.

## Packages

NuGet packages are published with the `Syndew.` ID prefix, for example:

```bash
dotnet add package Syndew.Oluso
dotnet add package Syndew.Oluso.EntityFramework
dotnet add package Syndew.Oluso.UI
dotnet add package Syndew.Oluso.Admin
dotnet add package Syndew.Oluso.Account
dotnet add package Syndew.Oluso.Enterprise.Saml
```

Every project under `src/backend` is published this way (`Syndew.Oluso.Core`, `Syndew.Oluso.Enterprise.Fido2`, `Syndew.Oluso.Enterprise.Scim`, `Syndew.Oluso.Telemetry.OpenTelemetry` and so on). Namespaces and assembly names stay `Oluso.*`.

npm packages are published under `@syndew/oluso-*`:

| Package | Description |
|---------|-------------|
| `@syndew/oluso-sdk` | JavaScript/TypeScript SDK: journey forms, OIDC and embedded sign-in, React components, drop-in embed scripts |
| `@syndew/oluso-ui-core` | Shared Angular components |
| `@syndew/oluso-auth` | Angular authentication helpers |
| `@syndew/oluso-admin-ui` | Admin portal |
| `@syndew/oluso-account-ui` | End-user account portal |
| `@syndew/oluso-workspace-ui` | Workspace portal |
| `@syndew/oluso-journey-ui` | Journey host component |
| `@syndew/oluso-plugin-fido2`, `@syndew/oluso-plugin-ldap`, `@syndew/oluso-plugin-saml`, `@syndew/oluso-plugin-scim`, `@syndew/oluso-plugin-telemetry`, `@syndew/oluso-plugin-verification` | Admin UI plugins for the enterprise add-ons |

## Project Structure

```
├── src/
│   ├── backend/
│   │   ├── Oluso/                      # Identity server: protocols, journeys, messaging, webhooks
│   │   ├── Oluso.Core/                 # Domain models, interfaces, events, licensing
│   │   ├── Oluso.EntityFramework/      # EF Core stores (SQLite, SQL Server, PostgreSQL)
│   │   ├── Oluso.UI/                   # Razor sign-in and journey pages
│   │   ├── Oluso.Admin/                # Admin API
│   │   ├── Oluso.Account/              # End-user account API
│   │   ├── Oluso.Telemetry.*/          # Telemetry abstractions and OpenTelemetry pipeline
│   │   └── Oluso.Enterprise/           # Fido2, Ldap, Saml, Scim, Verification, Azure*, Cloudflare
│   └── frontend/                       # Angular / Nx workspace
│       ├── apps/                       # admin-, account- and workspace-standalone
│       └── libs/                       # admin-ui, account-ui, workspace-ui, journey-ui, ui-core, auth, plugins
├── sdk/js/                             # @syndew/oluso-sdk
├── samples/
│   ├── Oluso.Sample/                   # Complete sample server
│   ├── Oluso.TestClient/               # Sample OIDC client
│   └── plugins/hello-plugin/           # Sample WebAssembly journey plugin (Rust)
├── templates/                          # Production configuration template
├── deploy/                             # Dockerfile and Caddy configuration
└── tests/                              # Unit, integration, end-to-end and enterprise module tests
```

## Minimal Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("OlusoDb")!;
var provider = builder.Configuration.GetValue<string>("Oluso:Database:Provider", "Sqlite")!;

var oluso = builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddSigningKeys()
    .AddAdminApi(mvc => mvc.AddOlusoAdmin().AddOlusoAccount())
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddFido2(fido2 => fido2.WithRelyingParty("localhost", "My App"))
    .AddLdap();

// Enterprise add-ons
builder.Services
    .AddSaml(builder.Configuration)
    .AddSamlForProvider(provider, connectionString)
    .AddScim()
    .AddScimForProvider(provider, connectionString)
    .AddFido2ForProvider(provider, connectionString);

builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddOlusoWebhooks();

builder.Services.AddControllers().ApplyOlusoConventions(oluso);
builder.Services.AddOlusoUI();
builder.Services.AddRazorPages();

var app = builder.Build();

await app.MigrateOlusoDatabaseAsync();

app.UseRouting();
app.UseAuthentication();
app.UseOluso();
app.UseOlusoUI();
app.UseAuthorization();
app.UseSession();

app.MapRazorPages();
app.MapControllers();
app.MapSamlEndpoints();
app.UseScim();
app.Run();
```

See [samples/Oluso.Sample/Program.cs](samples/Oluso.Sample/Program.cs) for a complete setup and [docs/v2-quickstart.md](docs/v2-quickstart.md) for the options.

## Configuration

All configuration is under the `Oluso` section in `appsettings.json`:

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

`Oluso:IssuerUri` is required. A production template is in [templates/appsettings.Production.json](templates/appsettings.Production.json).

### Database Providers

Set `Oluso:Database:Provider` and use `AddEntityFrameworkStoresForProvider` to pick the database:

| Provider | Value | Connection String Example |
|----------|-------|---------------------------|
| SQLite | `Sqlite` | `Data Source=Oluso.db` |
| SQL Server | `SqlServer` | `Server=...;Database=Oluso;...` |
| PostgreSQL | `PostgreSQL` | `Host=...;Database=Oluso;...` |

The value is not case-sensitive, and `mssql`, `postgres` and `npgsql` are accepted too. The connection string is `ConnectionStrings:OlusoDb`. As environment variables these are `Oluso__Database__Provider` and `ConnectionStrings__OlusoDb`.

## Test Credentials

The sample seeds these accounts, clients and passwords for local development. They are public, so before the sample is reachable by anyone else, turn them off with `Sample:SeedTestData=false` and set your own administrator (see [Seeding](docs/docker.md#seeding)), or change them.

### Accounts

| Email | Password | Role | Sign in at |
|-------|----------|------|------------|
| `superadmin@oluso.local` | `SuperAdmin123!` | Super Admin (platform) | Admin portal |
| `orgadmin@oluso.local` | `OrgAdmin123!` | Organization Admin | Admin portal |
| `admin@default.local` | `TenantAdmin123!` | Tenant Admin (`default` tenant) | Admin portal |
| `testuser@example.com` | `Password123!` | User (`default` tenant) | Account and workspace portals |

| Portal | `dotnet run` | `docker compose up` |
|--------|--------------|---------------------|
| Admin | `http://localhost:4201` | `https://localhost:8443/admin` |
| Workspace | `http://localhost:4202` | `https://localhost:8443/workspace` |
| Account | `http://localhost:4200` | `https://localhost:8443/account` |

### Clients

| Client ID | Secret | Grant types |
|-----------|--------|-------------|
| `test-client` | `test-secret` | Authorization code, refresh token |
| `interactive-test-client` | `test-secret` | Authorization code, refresh token (redirect `http://localhost:9876/callback`) |
| `cc-client` | `test-secret` | Client credentials |
| `ropc-client` | `test-secret` | Resource owner password, refresh token |
| `device-client` | none (public) | Device code, refresh token |
| `admin-portal`, `workspace-portal`, `account-ui` | none (public, PKCE) | Authorization code, used by the portals |

### Database (PostgreSQL override)

`docker-compose.postgres.yml` creates the database `oluso` with user `oluso` and password `oluso-dev`. It is only reachable from the other containers (`docker compose exec postgres psql -U oluso -d oluso`). Set `POSTGRES_PASSWORD` before the first start to use your own; the password is fixed when the `postgres_data` volume is created.

## Endpoints

| Endpoint | Description |
|----------|-------------|
| `/.well-known/openid-configuration` | OIDC Discovery |
| `/.well-known/oauth-authorization-server` | OAuth 2.0 Authorization Server Metadata |
| `/.well-known/jwks` | Signing keys (JWKS) |
| `/connect/authorize` | Authorization endpoint |
| `/connect/par` | Pushed authorization requests |
| `/connect/token` | Token endpoint |
| `/connect/userinfo` | UserInfo endpoint |
| `/connect/introspect` | Token introspection |
| `/connect/revocation` | Token revocation |
| `/connect/endsession` | End session (logout) |
| `/connect/deviceauthorization` | Device authorization |
| `/connect/ciba` | Backchannel authentication (CIBA) |
| `/connect/register` | Dynamic client registration (when enabled) |
| `/saml/idp/metadata` | SAML IdP metadata |
| `/saml/metadata` | SAML SP metadata |
| `/scim/v2/Users`, `/scim/v2/Groups` | SCIM provisioning API |
| `/api/admin/*` | Admin API |
| `/api/account/*` | Account API |
| `/api/service/*` | Service API for machine clients |

In development the sample serves Swagger UI at `/swagger` with separate Admin, Account, Service and Other API documents.

## Documentation

- [Running in Docker](docs/docker.md)
- [Quick start and setup options](docs/v2-quickstart.md)
- [User guide](docs/USER_GUIDE.md)
- [Feature matrix](docs/FEATURE_MATRIX.md)
- [Prefix scopes](docs/prefix-scopes.md)
- [Matrix homeservers](docs/matrix.md)
- [Database migrations](docs/migrations.md)

## License

Copyright (c) 2025-2026 Syndew Technology Ltd. All rights reserved.

Oluso is released under the Oluso Community License. The core identity server is free for personal, open source, non-profit, educational and evaluation use and for eligible small businesses. SAML 2.0, SCIM 2.0 and the LDAP server are licensed add-ons, and a commercial license is required above the community thresholds or to offer Oluso as a hosted service.

See [LICENSE](LICENSE) for the full terms, and [oluso.io/license](https://oluso.io/license) for eligibility.
