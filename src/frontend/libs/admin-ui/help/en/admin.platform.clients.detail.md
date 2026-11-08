---
title: Platform Client Details
relatedTopics:
  - Platform Clients | admin.platform.clients
  - Client Details | admin.clients.detail
---

View and edit a platform-level OAuth 2.0 client. The configuration tabs are identical to tenant-scoped clients — see the Client Details help for a full explanation of each section.

## Tabs

- **Basic** — Client ID (read-only), name, description, URIs, enabled status
- **Authentication** — Grant types, PKCE settings, client secrets management
- **URIs & CORS** — Redirect URIs, post-logout URIs, allowed CORS origins
- **Scopes** — Allowed scopes for this client
- **Access Control** — Permission requirements, role and user restrictions, identity provider restrictions
- **Tokens** — Access token, identity token, and authorization code lifetimes
- **Logout** — Front-channel and back-channel logout URIs
- **Claims & Properties** — Custom client claims, properties, and claim prefix settings
- **Advanced** — SSO settings, UI flow, device flow, PAR, and additional security options (JAR, DPoP, JWT ID)

## Client Secrets

Manage secrets in the **Authentication** tab. Secrets are hashed on creation and cannot be viewed again — copy the value immediately when generating a new one.

## Key Difference from Tenant Clients

The only difference is scope: platform clients are available across all tenants. All configuration options, token behavior, and security settings work the same way.
