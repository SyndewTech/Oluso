---
title: Resources
relatedTopics:
  - API Scopes | admin.api-scopes
  - Clients | admin.clients
---

Resources represent protected APIs that clients can request access to. They follow the OAuth 2.0 Resource Indicators specification (RFC 8707), allowing clients to specify which API they intend to call when requesting tokens.

## Resource List

The table shows all registered resources with:

- **Resource URI** — the unique identifier for the API (e.g., `https://api.example.com`)
- **Display Name** — a human-readable label
- **Status** — Enabled or Disabled
- **Scopes** — how many API scopes are associated with this resource
- **Created** — when the resource was registered

Click a row to view or edit the resource's details.

## Creating a Resource

Click **Add Resource** and provide:

- **Resource URI** (required) — the API's base URI, used as the `resource` parameter in OAuth requests
- **Display Name** — shown on consent screens and in the admin UI
- **Description** — additional context for administrators
- **Enabled** — whether clients can request this resource
- **Show in Discovery** — include in the `.well-known/openid-configuration` response

## How Resources Work

1. Register the resource with its URI
2. Associate API scopes with the resource
3. Clients include `resource=https://api.example.com` in their token request
4. The issued access token is audience-restricted to that resource

## Tips

- Each resource should represent a distinct API or microservice
- The resource URI should match the audience (`aud`) claim your API validates
- Associate scopes to control fine-grained permissions within each resource
