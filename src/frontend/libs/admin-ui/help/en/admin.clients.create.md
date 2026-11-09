---
title: Create Client
relatedTopics:
  - Clients | admin.clients
  - API Scopes | admin.api-scopes
  - Roles | admin.roles
---

Register a new OAuth 2.0 / OpenID Connect client application. The creation form has five sections, each configuring a different aspect of the client.

## 1. Basic Information

- **Client ID** (required) — a unique identifier used in OAuth requests. Cannot be changed after creation. Use lowercase with hyphens, e.g. `my-web-app`.
- **Client Name** — a display name shown in consent screens and the admin portal.
- **Description** — an internal note about the client's purpose.
- **Client URI** — the application's homepage URL.
- **Logo URI** — a URL to the client's logo, shown on consent screens.

## 2. Authentication

**Grant Types** — select which OAuth 2.0 flows this client supports:

- **Authorization Code** — standard flow for web and mobile apps. Recommended for most use cases.
- **Client Credentials** — for server-to-server communication with no user context.
- **Refresh Token** — enables token renewal without re-authentication.
- **Device Code** — for input-constrained devices (TVs, CLIs).
- **Implicit** and **Resource Owner Password** — legacy flows, use only if required.

**Security Settings:**

- **Require Client Secret** — enable for confidential clients (server-side apps). Disable for public clients (SPAs, mobile).
- **Require PKCE** — strongly recommended for public clients. Prevents authorization code interception attacks.
- **Allow Plain Text PKCE** — not recommended. Only enable if clients cannot support S256 challenge method.

## 3. URIs & CORS

- **Redirect URIs** — where the authorization server sends users after authentication. Must exactly match what the client sends in the request.
- **Post Logout Redirect URIs** — where users are redirected after signing out.
- **CORS Origins** — origins allowed to make cross-origin requests to the token endpoint. Required for browser-based apps.

## 4. Scopes

Select which scopes the client can request:

- **openid** — required for OpenID Connect flows
- **profile** — access to user profile claims (name, picture, etc.)
- **email** — access to email and email_verified claims
- **offline_access** — enables refresh tokens
- Add custom scopes for API-specific permissions

## 5. Access Control

Restrict who can authenticate with this client:

- **Required Permission** — users must have this permission (via a role) to use the client.
- **Allowed Roles** — only users with these roles can authenticate.
- **Allowed Users** — allow specific users regardless of role.
- **Identity Provider Restrictions** — limit which external providers (Google, Azure AD, etc.) can be used.

Leave access control empty to allow all authenticated users.
