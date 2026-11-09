---
title: Resource Details
relatedTopics:
  - Resources | admin.resources
  - API Scopes | admin.api-scopes
  - Clients | admin.clients
---

View and edit the full configuration of a protected resource.

## Basic Information

- **Resource URI** — the unique API identifier (read-only after creation)
- **Display Name** — label shown on consent screens
- **Description** — context for administrators
- **Status** — Enabled or Disabled

## Settings

- **Show in Discovery Document** — include this resource in the `.well-known/openid-configuration` response so clients can discover it
- **Created / Updated** — timestamps

## Allowed Scopes

The API scopes associated with this resource. When a client requests a token for this resource, only these scopes can be granted.

Click **Add Scope** to associate an existing API scope with this resource. The modal shows all unassigned scopes — select one or more and save.

Remove a scope by clicking the remove button next to it. This does not delete the scope itself, only the association.

## User Claims

Claims included in access tokens when this resource is the audience. For example, a user management API might include `role` and `department` claims.

Click **Edit Claims** to:

- Add custom claim types by name
- Remove existing claims
- Pick from common suggestions: `sub`, `email`, `name`, `role`, `preferred_username`, etc.

## Tips

- Scopes and claims work together — scopes control what the client is allowed to do, claims provide identity context
- A scope can be shared across multiple resources if the same permission applies to several APIs
