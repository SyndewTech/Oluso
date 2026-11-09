---
title: Identity Resources
relatedTopics:
  - API Scopes | admin.api-scopes
  - Resources | admin.resources
  - Clients | admin.clients
---

Identity resources are the OIDC identity scopes — `openid`, `profile`, `email`, `phone`, and any custom scopes you define. Each one declares the set of user claims it releases into the ID token and the userinfo response when a client is granted that scope.

## Resource List

The table shows all configured identity resources with:

- **Name** — the scope name clients request (e.g., `profile`, `email`)
- **Display Name** — human-readable label shown on consent screens
- **Status** — Enabled or Disabled
- **Claims** — the number of user claims this scope releases

Click a row to view or edit the resource.

## Standard scopes and overrides

The standard OIDC scopes have built-in default claim sets (for example `profile` releases `name`, `given_name`, `family_name`, and `picture`). Those defaults apply automatically — you do not have to configure anything.

If you add user claims to a standard scope here, your configured list **replaces** the built-in default for that scope. This lets you tighten or redefine exactly what a standard scope releases.

## Creating a Resource

Click **Add Identity Resource** and provide:

- **Name** (required) — the scope name used in `scope` parameters
- **Display Name** — what users see on the consent screen
- **Description** — additional context shown during consent
- **Enabled** — whether clients can request this scope
- **Required** — automatically include in every token
- **Emphasize** — highlight this scope on the consent screen
- **Show in Discovery** — include in the `.well-known/openid-configuration` document

## User Claims

Use **Edit Claims** to manage which claims the scope releases. Custom claim types resolve from the user's standard profile fields or custom properties; if a user has no value for a claim, it is simply omitted.

## Tips

- Platform claims such as `tenant_id` and `role` are always issued and are not controlled here.
- Standard OIDC scopes cannot be deleted.
