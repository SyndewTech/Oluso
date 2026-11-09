---
title: Grants & Sessions
relatedTopics:
  - Users | admin.users
  - Clients | admin.clients
  - Activity Logs | admin.audit-logs
---

Manage persisted grants and active user sessions. This page gives you direct control over the tokens and sessions that keep users authenticated.

## Persisted Grants

Persisted grants are long-lived tokens stored by the authorization server, including refresh tokens, authorization codes, and reference tokens.

The table shows:

- **Type** — the grant type (RefreshToken, AuthorizationCode, ReferenceToken, etc.)
- **Client** — the application the grant was issued to
- **Subject** — the user the grant belongs to
- **Created** — when the grant was issued
- **Expires** — when the grant will expire

Click **Revoke** to immediately invalidate a grant. This forces the user to re-authenticate with that client.

## Active Sessions

Server-side authentication sessions that track logged-in users.

The table shows:

- **Subject** — the user's identifier
- **Display Name** — the user's name
- **Scheme** — the authentication scheme used (e.g., local, Google, OIDC)
- **Created / Renewed / Expires** — session timestamps

Click **Terminate** to end a specific session, or use this page to audit which users are currently signed in.

## When to Use This

- **Compromised account** — revoke all grants and terminate sessions for a user
- **Client rotation** — revoke grants for an old client after migrating to a new one
- **Debugging** — verify that tokens and sessions are being created as expected
