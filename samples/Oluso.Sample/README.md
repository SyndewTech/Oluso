# Oluso Sample

A complete identity server sample demonstrating Oluso's enterprise features including OAuth 2.0/OIDC, SAML 2.0, SCIM, LDAP, FIDO2/Passkeys, and multi-tenancy.

## Quick Start

```bash
# From the repository root
cd samples/Oluso.Sample
dotnet run
```

The server starts at `http://localhost:5050`. Visit the homepage to explore available test endpoints.

## Features Demonstrated

- **OAuth 2.0 / OpenID Connect** - Authorization code, client credentials, device flow
- **SAML 2.0** - Both Identity Provider and Service Provider modes
- **SCIM 2.0** - User and group provisioning API
- **LDAP Server** - Expose users via LDAP protocol for legacy app integration
- **FIDO2/Passkeys** - WebAuthn passwordless authentication
- **Multi-tenancy** - Tenant isolation with custom domains
- **User Journeys** - Customizable authentication flows
- **Dynamic Providers** - Database-driven OAuth/SAML provider configuration

## Configuration

Configuration uses ASP.NET Core's standard pattern with environment-specific overrides:

- `appsettings.json` - Development defaults (SQLite, localhost URLs)
- `../../templates/appsettings.Production.json` - Production template (copy to project and customize)

### Database Provider

The database provider is auto-detected from configuration:

```json
{
  "Oluso": {
    "Database": {
      "Provider": "Sqlite"  // Options: "Sqlite", "SqlServer", "PostgreSQL"
    }
  }
}
```

## Production Checklist

Before deploying to production, update the following settings in `appsettings.Production.json`:

### Required

| Setting | Description |
|---------|-------------|
| `ConnectionStrings:OlusoDb` | Production database connection string |
| `Oluso:Database:Provider` | Set to `SqlServer` or `PostgreSQL` |
| `Oluso:IssuerUri` | Your public issuer URL (e.g., `https://auth.yourdomain.com`) |
| `Oluso:Urls:BaseUrl` | Same as IssuerUri |
| `Oluso:Fido2:RelyingPartyId` | Your domain (e.g., `yourdomain.com`) |

### Frontend URLs

| Setting | Description |
|---------|-------------|
| `Oluso:Urls:AdminUiUrl` | Admin dashboard URL |
| `Oluso:Urls:AccountUiUrl` | User account management URL |
| `Oluso:Urls:FrontendUrl` | Your main application URL |
| `Oluso:Cors:Origins` | Array of allowed CORS origins |

### Email & SMS (if using)

| Setting | Description |
|---------|-------------|
| `Oluso:Messaging:Email:Provider` | Set to `SendGrid` |
| `Oluso:Messaging:Email:SendGrid:ApiKey` | Your SendGrid API key |
| `Oluso:Messaging:Email:FromAddress` | Sender email address |
| `Oluso:Messaging:Sms:Provider` | Set to `Twilio` or `Infobip` |
| `Oluso:Messaging:Sms:Twilio:*` | Twilio credentials |

### LDAP (if using)

| Setting | Description |
|---------|-------------|
| `Oluso:LdapServer:Enabled` | Set to `true` to enable |
| `Oluso:LdapServer:AdminPassword` | Strong admin password |
| `Oluso:LdapServer:EnableSsl` | Set to `true` for production |

### SAML (if using)

| Setting | Description |
|---------|-------------|
| `Oluso:Saml:IdentityProvider:EntityId` | Your SAML IdP entity ID |
| `Oluso:Saml:ServiceProvider:EntityId` | Your SAML SP entity ID |

### Azure Integration (optional)

| Setting | Description |
|---------|-------------|
| `Oluso:AzureKeyVault:VaultUri` | Key Vault URI for HSM-backed signing keys |

## Test Credentials

The sample seeds the following test accounts:

| Email | Password | Role |
|-------|----------|------|
| `superadmin@localhost` | `Admin123!` | Super Admin |
| `admin@localhost` | `Admin123!` | Admin |
| `testuser@example.com` | `Password123!` | User |

## Test Endpoints

| Endpoint | Description |
|----------|-------------|
| `/` | Homepage with feature overview |
| `/.well-known/openid-configuration` | OIDC discovery document |
| `/test/oidc` | OIDC test harness |
| `/test/saml` | SAML test harness |
| `/test/ldap` | LDAP test harness |
| `/test/fido2` | FIDO2/Passkey test harness |
| `/scim/v2` | SCIM 2.0 API |
| `/health` | Health check endpoint |

## Project Structure

```
Oluso.Sample/
├── Controllers/
│   └── TestController.cs    # Test endpoints for LDAP, SAML, OIDC
├── wwwroot/
│   ├── index.html           # Landing page
│   └── test/                # Test harness pages
├── Program.cs               # Application startup and configuration
├── appsettings.json         # Development configuration
└── appsettings.Production.json  # Production template
```

## Documentation

For full documentation, visit the [Oluso Documentation](https://docs.oluso.dev).
