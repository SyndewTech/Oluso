---
title: Create Platform Client
relatedTopics:
  - Platform Clients | admin.platform.clients
  - Create Client | admin.clients.create
  - Clients | admin.clients
---

Register a new platform-level OAuth 2.0 client. Platform clients operate across all tenants, making them suitable for admin dashboards, shared services, and internal tooling.

## Form Sections

The creation form has the same sections as a regular client:

1. **Basic** — Client ID (required, unique), Client Name, Description, Client URI, Logo URI, Enabled
2. **Authentication** — Grant types (Authorization Code, Client Credentials, etc.), security settings (require secret, require PKCE)
3. **URIs & CORS** — Redirect URIs, post-logout redirect URIs, CORS origins
4. **Scopes** — Which scopes this client can request (openid, profile, email, custom scopes)
5. **Access Control** — Required permission, allowed roles, allowed users, identity provider restrictions

## Recommended Setup

For a typical admin portal or internal tool:

- **Grant Type:** Authorization Code
- **Require PKCE:** Yes
- **Scopes:** `openid profile email`
- **Redirect URI:** Your application's callback URL

## Tips

- The Client ID cannot be changed after creation — choose a descriptive, stable identifier
- Platform clients appear on all tenants' login screens if no identity provider restrictions are set
- See the Client Details help for a full explanation of each configuration tab
