---
title: Clients
relatedTopics:
  - Create Client | admin.clients.create
  - Resources | admin.resources
  - API Scopes | admin.api-scopes
  - Identity Providers | admin.identity-providers
---

Clients represent OAuth 2.0 / OpenID Connect applications that authenticate users through your identity platform. Each client corresponds to an application — a web app, mobile app, API, or service — that needs to verify user identity or access protected resources.

## Client List

The table shows all registered clients with their:

- **Client ID** — the unique identifier used in OAuth flows
- **Name** — a human-readable display name
- **Status** — whether the client is enabled or disabled
- **Grant Types** — which OAuth 2.0 flows the client supports
- **Created** — when the client was registered

Click any row to view and edit the client's full configuration.

## Adding a Client

Click **Add Client** to register a new application. You will need to configure at minimum:

1. A unique **Client ID**
2. One or more **Grant Types** (e.g., Authorization Code for web apps)
3. **Redirect URIs** where authorization responses are sent
4. **Scopes** the client is allowed to request

## Common Grant Types

| Grant Type | Use Case |
|---|---|
| **Authorization Code** | Web and mobile apps with a backend |
| **Client Credentials** | Server-to-server / machine-to-machine |
| **Refresh Token** | Long-lived sessions without re-authentication |
| **Device Code** | Smart TVs, CLI tools, IoT devices |
| **Implicit** | Legacy SPAs (prefer Authorization Code + PKCE) |

## Tips

- Use **Authorization Code with PKCE** for single-page applications instead of the Implicit flow
- Enable **Require PKCE** for public clients (SPAs, mobile apps) to prevent authorization code interception
- Keep client secrets secure — they are hashed and cannot be retrieved after creation
- Use **Access Control** settings to restrict which users and roles can authenticate with a client
