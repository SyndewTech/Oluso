# Identity Server Feature Matrix

## Core Identity & Authentication

| Feature | Status | Description |
|---------|--------|-------------|
| OAuth 2.0 / OpenID Connect | ✅ | Discovery, JWKS, authorize, token, userinfo, end session |
| Authorization Code + PKCE | ✅ | S256 by default; `plain` only when a tenant allows it |
| Client Credentials | ✅ | Machine-to-machine authentication |
| Device Authorization Flow | ✅ | RFC 8628, with a user-code verification page |
| CIBA | ✅ | Client-initiated backchannel authentication with poll, ping and push delivery; users approve requests in the account portal |
| Token Exchange (RFC 8693) | ✅ | Delegation scenarios and one-time exchange tickets |
| Resource Owner Password | ✅ | For legacy clients; removed from discovery when a tenant enforces OAuth 2.1 |
| Refresh Token Rotation | ✅ | OneTimeOnly and ReUse modes, sliding and absolute lifetimes |
| Pushed Authorization Requests (PAR) | ✅ | RFC 9126, optionally required per client or tenant |
| DPoP (RFC 9449) | ✅ | Sender-constrained access tokens with nonces and replay protection, optionally required per client |
| Mutual TLS (RFC 8705) | ✅ | `tls_client_auth`, `self_signed_tls_client_auth` and certificate-bound access tokens |
| Resource Indicators (RFC 8707) | ✅ | `resource` parameter selects the token audience |
| Prefix Scopes | ✅ | API scopes whose suffix the client chooses (e.g. `device:<id>`); see [prefix-scopes.md](prefix-scopes.md) |
| Matrix Homeservers | ✅ | Authorization server for the Matrix Client-Server API's OAuth 2.0 API: Matrix scopes, MSC2966 client registration, sign in with QR (device grant), refresh without `offline_access`, session-end notices to the homeserver; see [matrix.md](matrix.md) |
| Reference Tokens | ✅ | Opaque access tokens with introspection |
| JWT Access Tokens | ✅ | Configurable per client |
| Pairwise Subject Identifiers | ✅ | Per-client subject salt |
| Client Authentication | ✅ | `client_secret_basic`, `client_secret_post`, `client_secret_jwt`, `private_key_jwt`, `none` |
| Dynamic Client Registration | ✅ | RFC 7591, enabled per tenant |
| Token Introspection & Revocation | ✅ | RFC 7662 and RFC 7009 |
| Back-Channel Logout | ✅ | Logout tokens sent to clients' back-channel logout URIs |
| FedCM | ✅ | Browser Federated Credential Management endpoints |
| Re-authentication | ✅ | `prompt=login` and `max_age` force a fresh sign-in |

## Multi-Factor & Passwordless Authentication

| Feature | Status | Description |
|---------|--------|-------------|
| TOTP (Authenticator Apps) | ✅ | Any RFC 6238 authenticator app |
| SMS Codes | ✅ | One-time codes via SMS |
| Email Codes | ✅ | One-time codes via email |
| Recovery Codes | ✅ | Generated and managed from the account portal |
| FIDO2/WebAuthn | ✅ | Security keys and passkeys as a second factor or for passwordless sign-in (add-on) |
| Passwordless Email / SMS | ✅ | Journey steps that sign in with a code sent by email or SMS |
| MFA Per Application | ✅ | Journey policies matched to a client can include an MFA step |

## Multi-Tenancy & Organizations

| Feature | Status | Description |
|---------|--------|-------------|
| Tenant Isolation | ✅ | Users, clients, scopes, keys and settings are scoped to a tenant |
| Per-Tenant Branding | ✅ | Logos, colors and themes |
| Per-Tenant Signing Keys | ✅ | RSA or EC keys per tenant, with rotation settings |
| Per-Tenant Settings | ✅ | Password policy, lockout, allowed grants and protocol options |
| Subdomain Resolution | ✅ | tenant.yourdomain.com, with the base domain taken from the issuer |
| Custom Domains | ✅ | DNS-verified tenant domains, re-verified in the background; optional Cloudflare custom hostnames |
| Header-Based Resolution | ✅ | `X-Tenant-Id` header |
| Organizations | ✅ | Group tenants under an organization, with organization admins and organization sign-in |
| Feature Entitlements | ✅ | Organization- and tenant-level feature switches |
| Localization | ✅ | UI strings in English, Arabic, French, German, Portuguese and Spanish, with editable terminology |

## User Journey Engine

Build authentication and data-collection flows from steps in the admin UI.

| Feature | Status | Description |
|---------|--------|-------------|
| Journey Policy Editor | ✅ | Design flows in the admin UI |
| Journey Types | ✅ | Sign-in, sign-up, password reset and change, profile edit, account linking, consent, waitlists, contact forms, surveys, feedback and data collection |
| Policy Matching | ✅ | Pick a policy by conditions such as the client |
| Local Login Step | ✅ | Username/password authentication |
| External IdP Step | ✅ | Federate to external providers |
| LDAP and SAML Steps | ✅ | Sign in against a directory or a SAML IdP (add-ons) |
| Passkey Steps | ✅ | Passkey sign-in and registration (add-on) |
| MFA Step | ✅ | Add multi-factor at any point |
| Consent Step | ✅ | User consent collection |
| Sign-Up, Create and Update User | ✅ | Register and update accounts mid-flow |
| Email Verification | ✅ | Verify an address with a code |
| Terms Acceptance | ✅ | Require acceptance of terms |
| CAPTCHA | ✅ | reCAPTCHA v2/v3, hCaptcha and Cloudflare Turnstile |
| Conditions and Branches | ✅ | Branch on claims or context |
| Claims Transformation | ✅ | Modify claims during the flow |
| Dynamic Forms | ✅ | Gather additional user information |
| Assign Roles | ✅ | Grant roles during the flow |
| API Call and Webhook Steps | ✅ | Call external APIs mid-journey |
| Custom Pages | ✅ | Render custom content as a step |
| Business Verification Step | ✅ | KYB checks (add-on) |
| WebAssembly Plugins | ✅ | Custom steps as WASM modules (Extism), with a network policy |
| Hot-Reload Plugins | ✅ | Plugins reload when the `.wasm` file changes |
| Headless Journeys | ✅ | Run journeys over the public API from your own UI or the JS SDK |

## External Identity Providers

| Feature | Status | Description |
|---------|--------|-------------|
| OIDC Federation | ✅ | Connect to any OpenID Connect provider |
| Social Providers | ✅ | Google, Microsoft, GitHub, Apple, Facebook, LinkedIn, X |
| Generic OAuth 2.0 | ✅ | Custom authorization, token and userinfo endpoints |
| Dynamic Provider Registration | ✅ | Add providers via the admin UI or API |
| Database-Stored Configuration | ✅ | No config file changes needed |
| Proxy Mode | ✅ | Authenticate through an external IdP without storing the user locally |
| Auto-Provisioning | ✅ | Create local users on first login |
| Account Linking | ✅ | Link an external login to an existing account |
| Claims Mapping | ✅ | Map external claims to internal schema |

## Client Access Control

| Feature | Status | Description |
|---------|--------|-------------|
| Role-Based Client Access | ✅ | Restrict client access by user role |
| User-Based Client Access | ✅ | Allow specific users per client |
| Permission-Based Client Access | ✅ | Require a permission to use a client |
| Allowed Origins (CORS) | ✅ | Configure allowed origins per client |
| Redirect URI Validation | ✅ | Strict redirect URI matching |
| Post-Logout Redirect URIs | ✅ | Control logout destinations |
| Identity Provider Restrictions | ✅ | Limit which providers a client offers |

## Enterprise Add-Ons

| Feature | Status | Description |
|---------|--------|-------------|
| LDAP / Active Directory | ✅ | Authenticate against LDAP directories, with group mapping and user sync |
| LDAP Server | ✅ | Expose users over LDAP for legacy applications |
| SAML 2.0 Service Provider | ✅ | Accept SAML assertions from external IdPs, with single logout |
| SAML 2.0 Identity Provider | ✅ | Issue SAML assertions to registered service providers |
| SCIM 2.0 Inbound | ✅ | Users, Groups, Bulk, Schemas, ResourceTypes and ServiceProviderConfig |
| SCIM 2.0 Outbound | ✅ | Provision users and groups to downstream SCIM services, with attribute mapping, queueing and reconciliation |
| FIDO2/WebAuthn (Passkeys) | ✅ | Passwordless authentication with biometrics or security keys |
| Business Verification | ✅ | KYB checks through Dojah or Youverify |
| Azure Key Vault | ✅ | Key Vault and HSM-backed signing keys and certificates |
| Azure Blob Storage | ✅ | File uploads |
| Azure Data Protection | ✅ | One Data Protection key ring for every host, stored in Blob Storage and wrapped with Key Vault |
| Azure Queue Webhooks | ✅ | Queue-based webhook retries |
| Cloudflare | ✅ | Custom hostnames for tenant domains |
| OpenTelemetry | ✅ | Traces, metrics and logs over OTLP to any compatible backend |

## Observability & Events

| Feature | Status | Description |
|---------|--------|-------------|
| Events System | ✅ | React to authentication, token and admin events |
| Authentication Events | ✅ | Sign-in success and failure, sign-out, MFA, lockout, registration |
| Token Events | ✅ | Authorization codes, token issued, refreshed, introspected and revoked |
| Admin Events | ✅ | Changes to clients, roles, scopes, resources, providers, webhooks, keys, journeys and settings |
| Logger Event Sink | ✅ | Write events to application logs |
| Audit Event Sink | ✅ | Persistent, queryable audit log in the admin UI |
| Webhook Event Sink | ✅ | POST events to subscribed endpoints |
| Batching and Filtered Sinks | ✅ | Batch or filter events before delivery |
| Custom Event Sinks | ✅ | Implement your own destinations |
| Webhook Signing | ✅ | HMAC-SHA256 signatures, or tokens signed with the tenant's issuer key |
| Webhook Retries | ✅ | In-process retries, or queue-based with the Azure add-on |
| Telemetry Dashboard | ✅ | Logs and traces in the admin UI |

## Messaging

| Feature | Status | Description |
|---------|--------|-------------|
| Email | ✅ | SendGrid |
| SMS | ✅ | Twilio and Infobip |
| WhatsApp | ✅ | Meta Cloud API, with an inbound webhook |
| Sender Profiles | ✅ | Per-tenant sender identities |

## Admin UI

| Feature | Status | Description |
|---------|--------|-------------|
| Client Management | ✅ | Create, edit, delete OAuth clients |
| Platform Clients | ✅ | Clients shared across tenants |
| User Management | ✅ | Manage users, roles and claims |
| Roles & Permissions | ✅ | Roles with fine-grained permissions |
| Teams | ✅ | Group users into teams |
| Tenant Management | ✅ | Tenant settings, branding and domains |
| Organization Management | ✅ | Organizations and their tenants |
| Identity Provider Management | ✅ | Add and configure external IdPs |
| User Journey Policy Editor | ✅ | Flow designer and journey submissions |
| API Scope & Resource Management | ✅ | API scopes, API resources and identity resources |
| Signing Keys | ✅ | View, rotate and manage keys |
| Persisted Grants View | ✅ | Monitor active grants and consents |
| Webhooks | ✅ | Endpoints, subscriptions and delivery history |
| Plugins | ✅ | Upload and manage WASM plugins |
| Audit Logs | ✅ | Search and purge the audit log |
| Entitlements | ✅ | Feature entitlements and module seats |
| Terminology | ✅ | Rename platform terms per tenant |
| Search | ✅ | Global search across admin entities |

## Account & Workspace Portals

| Feature | Status | Description |
|---------|--------|-------------|
| Profile | ✅ | Edit profile details |
| Security | ✅ | Password, MFA, recovery codes and passkeys |
| Sessions | ✅ | View and end active sessions |
| Connected Apps | ✅ | Review and revoke app consents |
| CIBA Requests | ✅ | Approve or deny backchannel sign-in requests |
| Activity | ✅ | Recent account activity |
| Tenant Switching | ✅ | Move between the tenants a user belongs to |
| Workspace Portal | ✅ | App launcher, files, notifications and profile for signed-in users |

## Security Features

| Feature | Status | Description |
|---------|--------|-------------|
| PKCE | ✅ | Required per client; S256 only unless plain is allowed |
| OAuth 2.1 Mode | ✅ | Per tenant: drops implicit, password and `plain` PKCE |
| Key Rotation | ✅ | Background signing key rotation from per-tenant rotation settings |
| Encrypted Key Material | ✅ | Signing keys and stored secrets encrypted with ASP.NET Core Data Protection |
| Account Lockout | ✅ | Per-tenant failed-attempt limit and lockout duration |
| Password Policies | ✅ | Complexity, history and expiry per tenant |
| Breached Password Check | ✅ | Optional check against the Have I Been Pwned range API |
| Sign-In Rate Limiting | ✅ | Per-IP limits on sign-in endpoints |
| Host Validation | ✅ | Reject requests for unknown hosts (DNS rebinding protection) |

---

## Licensing Model

Oluso is released under the Oluso Community License (see [LICENSE](../LICENSE)). The core identity server is free for personal, open source, non-profit, educational and evaluation use and for eligible small businesses. A commercial license is required above the community thresholds or to offer Oluso as a hosted service.

### Tiers

All tiers have unlimited clients and users.

| Tier | Tenants | Features |
|------|---------|----------|
| **Community** | 1 | Core OAuth 2.0 / OIDC, multi-tenancy, journey engine, admin UI, account UI |
| **Starter** | 3 | Community + email support |
| **Professional** | 10 | Starter + FIDO2, telemetry, audit logging, Key Vault, webhooks |
| **Enterprise** | Unlimited | Professional + SCIM, SAML, LDAP, custom branding, priority support, SLA |

SCIM, SAML and LDAP can also be added to Starter and Professional. Pricing and eligibility: [oluso.io/pricing](https://oluso.io/pricing) and [oluso.io/license](https://oluso.io/license).

---

## Technical Specifications

### Supported Platforms

- .NET 10
- Linux, Windows, macOS
- Docker (see `deploy/` and `docker-compose.yml`)

### Database Support

- SQL Server
- PostgreSQL
- SQLite

### Standards Compliance

- OAuth 2.0 (RFC 6749)
- OAuth 2.0 Token Revocation (RFC 7009)
- OAuth 2.0 PKCE (RFC 7636)
- OAuth 2.0 Token Introspection (RFC 7662)
- OAuth 2.0 Dynamic Client Registration (RFC 7591)
- OAuth 2.0 Authorization Server Metadata (RFC 8414)
- OAuth 2.0 Device Authorization Grant (RFC 8628)
- OAuth 2.0 Mutual-TLS Client Authentication (RFC 8705)
- OAuth 2.0 Resource Indicators (RFC 8707)
- OAuth 2.0 Token Exchange (RFC 8693)
- OAuth 2.0 Pushed Authorization Requests (RFC 9126)
- OAuth 2.0 Authorization Server Issuer Identification (RFC 9207)
- OAuth 2.0 DPoP (RFC 9449)
- OpenID Connect Core 1.0
- OpenID Connect Discovery 1.0
- OpenID Connect Back-Channel Logout 1.0
- OpenID Connect CIBA Core 1.0
- SAML 2.0 (add-on)
- SCIM 2.0 (add-on)
- WebAuthn / FIDO2 (add-on)

---

## Quick Start

```csharp
// Program.cs
var oluso = builder.Services.AddOluso(builder.Configuration)
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddSigningKeys()
    .AddEntityFrameworkStoresForProvider(provider, connectionString)
    .AddDynamicExternalProviders();

// Optional: enterprise add-ons
oluso.AddFido2();                                     // Passkeys
oluso.AddLdap();                                      // LDAP / Active Directory
builder.Services.AddSaml(builder.Configuration);      // SAML 2.0 SP and IdP
builder.Services.AddScim();                           // SCIM 2.0
builder.Services.AddOlusoOpenTelemetry(otel =>
{
    otel.ServiceName = "my-identity-server";
    otel.OtlpEndpoint = "http://otel-collector:4317";
});
```

See the [README](../README.md) and [samples/Oluso.Sample](../samples/Oluso.Sample/Program.cs) for a complete setup.

---

*Last updated: November 2026*
