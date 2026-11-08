---
title: Platform Clients
relatedTopics:
  - Clients | admin.clients
  - Organizations | admin.organizations
---

Platform clients are system-level OAuth 2.0 applications that operate across all tenants and organizations. Unlike regular tenant-scoped clients, platform clients are available globally. This page is only visible to Super Admins.

## Client List

The table shows all platform clients with:

- **Client ID** — the unique OAuth identifier
- **Name** — display name of the client
- **Status** — Enabled or Disabled
- **Grant Types** — which OAuth 2.0 flows the client supports
- **Created** — when the client was registered

Click **Edit** to open the client detail page, or **Delete** to remove it.

## Creating a Platform Client

Click **Add Platform Client** to open the client creation form. The form is the same as for regular clients but the client will be registered at the platform level rather than scoped to a single tenant.

## Platform vs Tenant Clients

| | Platform Clients | Tenant Clients |
|---|---|---|
| **Scope** | Available across all tenants | Scoped to a single tenant |
| **Managed by** | Super Admins only | Tenant admins |
| **Use case** | Admin portals, shared services, internal tools | Customer-facing apps, third-party integrations |
| **Visibility** | This page | Clients page |

## Tips

- Use platform clients for applications that need to authenticate users across multiple tenants (e.g., the admin portal itself)
- Platform clients follow the same configuration options as regular clients — grant types, scopes, URIs, etc.
