---
title: Client Details
relatedTopics:
  - Clients | admin.clients
  - API Scopes | admin.api-scopes
  - Identity Providers | admin.identity-providers
---

View and edit the full configuration of an OAuth 2.0 / OpenID Connect client. The detail page has nine tabs covering all aspects of the client.

## Basic

Core identity of the client — name, description, URIs, and enabled status. The **Client ID** is read-only after creation.

## Authentication

Configure grant types and security settings. Manage **Client Secrets** here — secrets are hashed on creation and cannot be viewed again, so copy the value immediately when generating a new one. You cannot delete the last remaining secret.

## Tokens

Control token lifetimes:

- **Access Token Lifetime** — how long access tokens are valid (default: 3600s / 1 hour)
- **Identity Token Lifetime** — how long ID tokens are valid (default: 3600s)
- **Authorization Code Lifetime** — how long auth codes can be exchanged (default: 300s / 5 minutes)

Shorter lifetimes improve security but increase token refresh frequency.

## URIs & CORS

Manage redirect URIs, post-logout redirect URIs, and CORS origins. Redirect URIs must exactly match what the client sends — no wildcards.

## Scopes

Toggle common scopes (openid, profile, email, etc.) and add custom scopes. Only scopes listed here can be requested by the client.

## Logout

Configure front-channel and back-channel logout URIs for single sign-out support.

- **Front-Channel** — the server renders a hidden iframe to this URL during logout
- **Back-Channel** — the server makes a direct HTTP POST to this URL

## Access Control

Restrict authentication to specific roles, users, or identity providers. Set a **Required Permission** to gate access via the role-based permission system.

## Claims & Properties

- **Client Claims** — custom claims added to tokens issued for this client (e.g., `department: engineering`)
- **Client Properties** — key-value metadata stored with the client
- **Claims Prefix** — string prepended to client claim types (default: `client_`)
- **Always Send Client Claims** — include client claims in all token types, not just client credentials tokens

## Advanced

- **SSO Settings** — control local login and SSO session lifetime
- **UI Flow** — choose the authentication UI flow (journey-based or standalone pages)
- **Device Flow** — configure user code type and device code lifetime
- **PAR** — require Pushed Authorization Requests for enhanced security
- **Security** — enable JAR (Request Objects), DPoP (Proof of Possession), and JWT ID claims
