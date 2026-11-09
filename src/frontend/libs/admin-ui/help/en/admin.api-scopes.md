---
title: API Scopes
relatedTopics:
  - Resources | admin.resources
  - Clients | admin.clients
---

API scopes define the permissions that clients can request when accessing protected resources. They appear on the user consent screen and control what data or operations an access token grants.

## Scope List

The table shows all registered scopes with:

- **Name** — the scope identifier used in OAuth requests (e.g., `read:orders`, `profile`)
- **Display Name** — human-readable label shown on consent screens
- **Status** — Enabled or Disabled
- **Required** — whether the scope is always included, even if the client doesn't request it

Click a row to view or edit the scope's details.

## Creating a Scope

Click **Add API Scope** and provide:

- **Name** (required) — the unique identifier used in `scope` parameters
- **Display Name** — what users see on the consent screen
- **Description** — additional context shown during consent
- **Enabled** — whether clients can request this scope
- **Required** — automatically include in every token
- **Emphasize** — highlight this scope on the consent screen
- **Show in Discovery** — include in the `.well-known/openid-configuration` document

## Tips

- Use descriptive names like `read:users` or `manage:billing` rather than generic names
- Required scopes are always granted — use sparingly for essential permissions
- Disabled scopes are rejected if a client requests them
