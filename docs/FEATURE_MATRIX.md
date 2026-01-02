# Identity Server Feature Matrix

## Core Identity & Authentication

| Feature | Status | Description |
|---------|--------|-------------|
| OAuth 2.0 / OpenID Connect | ✅ | Full specification compliance |
| Authorization Code + PKCE | ✅ | Secure flow for web and mobile applications |
| Client Credentials | ✅ | Machine-to-machine authentication |
| Device Authorization Flow | ✅ | Smart TV, CLI, and IoT device support |
| Refresh Token Rotation | ✅ | OneTimeOnly and ReUse rotation modes |
| Token Exchange (RFC 8693) | ✅ | Delegation and impersonation scenarios |
| Pushed Authorization Requests (PAR) | ✅ | Enhanced security for authorization requests |
| DPoP (RFC 9449) | ✅ | Sender-constrained access tokens |
| Reference Tokens | ✅ | Opaque token support with introspection |
| JWT Access Tokens | ✅ | Configurable per client |

## Multi-Factor Authentication

| Feature | Status | Description |
|---------|--------|-------------|
| TOTP (Authenticator Apps) | ✅ | Google Authenticator, Microsoft Authenticator, etc. |
| SMS/Phone Verification | ✅ | One-time codes via SMS |
| Email Verification | ✅ | One-time codes via email |
| FIDO2/WebAuthn | ✅ | Security keys and passkeys as MFA (add-on) |
| MFA Per-Client Configuration | ✅ | Require MFA for specific applications |
| Step-up Authentication | ✅ | Elevate authentication level on demand |

## Multi-Tenancy

| Feature | Status | Description |
|---------|--------|-------------|
| Full Tenant Isolation | ✅ | Complete data separation between tenants |
| Per-Tenant Branding | ✅ | Custom logos, colors, and themes |
| Per-Tenant Signing Keys | ✅ | Isolated cryptographic keys |
| Subdomain Resolution | ✅ | tenant.yourdomain.com |
| Path-Based Resolution | ✅ | yourdomain.com/tenant |
| Header-Based Resolution | ✅ | X-Tenant-ID header support |
| Tenant-Scoped Users & Clients | ✅ | Users and clients belong to specific tenants |

## User Journey Engine

Build complex authentication flows visually—similar to Azure AD B2C Custom Policies, but easier.

| Feature | Status | Description |
|---------|--------|-------------|
| Visual Policy Builder | ✅ | Design flows in Admin UI |
| Local Login Step | ✅ | Username/password authentication |
| External IdP Step | ✅ | Federate to external providers |
| MFA Step | ✅ | Add multi-factor at any point |
| Consent Step | ✅ | User consent collection |
| Conditional Steps | ✅ | Branch logic based on claims or context |
| Claims Transformation | ✅ | Modify claims during the flow |
| Claims Collection | ✅ | Gather additional user information |
| API Call Steps | ✅ | Call external APIs mid-journey |
| WebAssembly Plugins | ✅ | Extend with custom WASM modules (Extism) |
| Hot-Reload Plugins | ✅ | Update plugins without restart |

## External Identity Providers

| Feature | Status | Description |
|---------|--------|-------------|
| OIDC Federation | ✅ | Connect to any OpenID Connect provider |
| OAuth 2.0 Providers | ✅ | Google, Facebook, GitHub, etc. |
| Dynamic Provider Registration | ✅ | Add providers via Admin UI or API |
| Database-Stored Configuration | ✅ | No config file changes needed |
| Proxy Mode | ✅ | Pass-through external tokens directly |
| Auto-Provisioning | ✅ | Create local users on first login |
| Configurable OIDC Endpoints | ✅ | Custom authorization/token/userinfo URLs |
| Claims Mapping | ✅ | Map external claims to internal schema |

## Client Access Control

| Feature | Status | Description |
|---------|--------|-------------|
| Role-Based Client Access | ✅ | Restrict client access by user role |
| User-Based Client Access | ✅ | Allow/deny specific users per client |
| Allowed Origins (CORS) | ✅ | Configure allowed origins per client |
| Redirect URI Validation | ✅ | Strict redirect URI matching |
| Post-Logout Redirect URIs | ✅ | Control logout destinations |

## Enterprise Add-Ons

Premium features available with licensed tiers.

| Feature | Status | License Tier | Description |
|---------|--------|--------------|-------------|
| LDAP/Active Directory | ✅ | Starter+ | Authenticate against LDAP directories |
| SAML 2.0 Service Provider | ✅ | Starter+ | Accept SAML assertions |
| SAML 2.0 Identity Provider | ✅ | Starter+ | Issue SAML assertions |
| OpenTelemetry Metrics | ✅ | Starter+ | Prometheus, Datadog, etc. |
| OpenTelemetry Tracing | ✅ | Starter+ | Jaeger, Zipkin, etc. |
| **FIDO2/WebAuthn (Passkeys)** | ✅ | Professional+ | Passwordless authentication with biometrics/security keys |
| **Audit Logging** | ✅ | Professional+ | Persistent, queryable security audit trail |

## Observability & Events

| Feature | Status | Description |
|---------|--------|-------------|
| Events/Hooks System | ✅ | React to authentication events |
| Authentication Events | ✅ | Login success/failure, logout, MFA |
| Token Events | ✅ | Token issued, refreshed, revoked |
| Session Events | ✅ | Session created, ended, extended |
| Client Events | ✅ | Client authentication success/failure |
| Logger Event Sink | ✅ | Write events to application logs |
| Webhook Event Sink | ✅ | POST events to external URLs |
| Batching Event Sink | ✅ | High-throughput event batching |
| Custom Event Sinks | ✅ | Implement your own destinations |
| SIEM Integration | ✅ | Send to Splunk, ELK, etc. via sinks |

## Admin UI

| Feature | Status | Description |
|---------|--------|-------------|
| Client Management | ✅ | Create, edit, delete OAuth clients |
| User Management | ✅ | Manage users, roles, and claims |
| Tenant Management | ✅ | Configure tenant settings and branding |
| Identity Provider Management | ✅ | Add and configure external IdPs |
| User Journey Policy Editor | ✅ | Visual flow designer |
| API Scope Management | ✅ | Define and manage API scopes |
| Persisted Grants View | ✅ | Monitor active tokens and consents |

## Security Features

| Feature | Status | Description |
|---------|--------|-------------|
| PKCE Required | ✅ | Enforce PKCE for public clients |
| DPoP Support | ✅ | Proof-of-possession tokens |
| PAR Support | ✅ | Server-side request storage |
| Token Encryption | ✅ | Encrypt sensitive token data |
| Key Rotation | ✅ | Automatic signing key rotation |
| Secure Defaults | ✅ | Security-first configuration |
| Account Lockout | ✅ | Brute-force protection |
| Password Policies | ✅ | Configurable password requirements |

---

## Licensing Model

### Tiers

| Tier | Revenue Threshold | Price | Features |
|------|-------------------|-------|----------|
| **Community** | < $1M annual revenue | **Free** | All core features |
| **Starter** | $1M+ annual revenue | $499/year | Core + LDAP, SAML, Telemetry |
| **Professional** | Any | $1,499/year | Starter + Priority Support |
| **Enterprise** | Any | Custom | Professional + Custom SLA, Training |

### What's Included Free

- Full OAuth 2.0 / OpenID Connect implementation
- Multi-tenancy with tenant isolation
- User Journey Engine with visual builder
- External IdP federation
- MFA support
- Events/Hooks system
- Admin UI
- All security features

### Paid Add-Ons (Starter+)

- LDAP/Active Directory integration
- SAML 2.0 SP/IdP support
- OpenTelemetry integration

---

## Competitive Comparison

### vs Duende IdentityServer

| Capability | This Product | Duende IdentityServer |
|------------|--------------|----------------------|
| **Starting Price** | Free (< $1M revenue) | $1,500/year minimum |
| **Multi-Tenancy** | Built-in, first-class | DIY implementation |
| **User Journeys** | Visual builder included | Manual code required |
| **Admin UI** | Included | Separate purchase ($$$) |
| **Plugin System** | WebAssembly hot-reload | None |
| **External IdP Proxy** | Built-in proxy mode | Manual implementation |
| **PAR Support** | ✅ | ✅ |
| **DPoP Support** | ✅ | ✅ |
| **SAML Support** | Add-on ($499+) | Add-on ($2,000+) |

### vs Auth0

| Capability | This Product | Auth0 |
|------------|--------------|-------|
| **Deployment** | Self-hosted | Cloud only (mostly) |
| **Pricing Model** | Revenue-based | Per MAU |
| **Data Residency** | Full control | Limited regions |
| **Customization** | Full source access | Limited |
| **User Journeys** | Visual + code + WASM | Actions (JS only) |
| **Multi-Tenancy** | Native | Organizations add-on |

### vs Keycloak

| Capability | This Product | Keycloak |
|------------|--------------|----------|
| **Technology** | .NET 8 | Java |
| **Performance** | Optimized for .NET | Heavy JVM footprint |
| **User Journeys** | Visual builder | Authentication flows |
| **Plugin System** | WebAssembly | Java SPIs |
| **Commercial Support** | Available | Red Hat (expensive) |
| **Learning Curve** | .NET developers friendly | Steep |

---

## Roadmap / Coming Soon

| Feature | Priority | Status |
|---------|----------|--------|
| SCIM 2.0 Provisioning | Medium | Planned |
| Session Management UI | Medium | Planned |
| Built-in Rate Limiting | Medium | Planned |
| Passwordless Email/SMS | Medium | Planned |
| Risk-Based Authentication | Low | Researching |

---

## Technical Specifications

### Supported Platforms

- .NET 8.0+
- Linux, Windows, macOS
- Docker / Kubernetes
- Azure App Service, AWS ECS, GCP Cloud Run

### Database Support

- SQL Server
- PostgreSQL
- MySQL
- SQLite (development)
- In-Memory (testing)

### Standards Compliance

- OAuth 2.0 (RFC 6749)
- OAuth 2.0 Token Revocation (RFC 7009)
- OAuth 2.0 Token Introspection (RFC 7662)
- OAuth 2.0 Device Authorization Grant (RFC 8628)
- OAuth 2.0 Token Exchange (RFC 8693)
- OAuth 2.0 Pushed Authorization Requests (RFC 9126)
- OAuth 2.0 DPoP (RFC 9449)
- OpenID Connect Core 1.0
- OpenID Connect Discovery 1.0
- OpenID Connect Dynamic Client Registration 1.0
- OpenID Connect Session Management 1.0
- OpenID Connect Front-Channel Logout 1.0
- OpenID Connect Back-Channel Logout 1.0
- SAML 2.0 (add-on)

---

## Quick Start

```csharp
// Program.cs
builder.Services.AddIdentityServer()
    .AddMultiTenancy()
    .AddUserJourneyEngine()
    .AddIdentityServerEvents()
    .AddEntityFrameworkStores();

// Add external providers dynamically
builder.Services.AddDynamicExternalProviders();

// Optional: Add enterprise features
builder.Services.AddIdentityServerLdap();
builder.Services.AddIdentityServerSaml();
builder.Services.AddIdentityServerOpenTelemetry();
builder.Services.AddFido2(builder.Configuration); // Passkeys (Professional+)
```

---

*Last updated: December 2025*
