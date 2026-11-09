---
title: Identity Providers
relatedTopics:
  - Clients | admin.clients
  - Users | admin.users
  - Settings | admin.settings
---

Configure external authentication providers to enable single sign-on (SSO) and social login. Users can authenticate with a third-party identity provider instead of (or in addition to) a local username and password.

## Provider List

The table shows all configured providers with:

- **Provider** — icon, display name, and scheme identifier
- **Type** — the protocol or service (Google, Microsoft, OIDC, SAML2, LDAP, etc.)
- **Status** — Enabled or Disabled
- **Clients** — how many clients are configured to use this provider

Click a row to view or edit the provider's configuration. Use the play/pause button to quickly enable or disable a provider without opening the detail page.

## Supported Provider Types

| Type | Protocol | Use Case |
|---|---|---|
| **Google** | OAuth 2.0 | Google Workspace / Gmail users |
| **Microsoft** | OAuth 2.0 | Azure AD / Microsoft 365 users |
| **GitHub** | OAuth 2.0 | Developer teams using GitHub |
| **Facebook** | OAuth 2.0 | Consumer-facing social login |
| **Apple** | OAuth 2.0 | Apple ID sign-in |
| **LinkedIn / Twitter** | OAuth 2.0 | Professional / social networks |
| **OIDC** | OpenID Connect | Any standards-compliant IdP |
| **OAuth2** | OAuth 2.0 | Custom OAuth providers |
| **SAML2** | SAML 2.0 | Enterprise SSO (Okta, ADFS, etc.) |
| **LDAP** | LDAP/LDAPS | On-premises Active Directory |

## Adding a Provider

Click **Add Provider**, select the provider type, and configure the required credentials (client ID, secret, endpoints). Each provider type has its own setup instructions shown on the configuration page.
