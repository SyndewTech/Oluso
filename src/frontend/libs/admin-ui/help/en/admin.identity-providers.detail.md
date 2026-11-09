---
title: Identity Provider Details
relatedTopics:
  - Identity Providers | admin.identity-providers
  - Clients | admin.clients
---

Configure or view the full settings of an external authentication provider. The form adapts to the selected provider type.

## Basic Information

- **Provider Type** — the protocol (set at creation, cannot be changed)
- **Scheme Name** — unique identifier used in authentication URLs (set at creation)
- **Display Name** — label shown on the login page
- **Enabled** — toggle the provider on or off

## Provider Configuration

Each provider type has specific fields:

- **Google / Microsoft / GitHub / Facebook** — Client ID and Client Secret from the provider's developer console, plus provider-specific options (hosted domain, tenant ID, allowed organizations, etc.)
- **OIDC** — Authority URL, client credentials, scopes, and optional endpoint overrides for non-standard providers
- **OAuth2** — Manual endpoint configuration (authorization, token, userinfo) with custom claim mappings
- **SAML2** — Entity ID, metadata URL, SSO service URL, signing certificate, and security settings
- **LDAP** — Server address, bind credentials, search base DN, user filter, and attribute mappings

## Proxy Mode (Federation Broker)

Advanced settings for pass-through federation:

- **Proxy Mode** — forward tokens from the external provider without re-issuing
- **Store User Details Locally** — cache user profile data
- **Token Pass-through** — include the external provider's access or ID token in your tokens
- **Claim Filtering** — control which claims from the external provider are included or excluded

## Setup Instructions

Each provider type shows context-specific setup steps, including:

- The **Callback URL** to register with the external provider
- The **Discovery URL** for OIDC providers
- **ACS URL** and **Entity ID** for SAML2 providers

## Testing

Click **Test Connection** to verify that the provider is reachable and credentials are valid before enabling it for users.
