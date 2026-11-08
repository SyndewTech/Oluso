---
title: Organizations
relatedTopics:
  - Tenants | admin.tenants
  - Users | admin.users
  - Platform Clients | admin.platform.clients
---

Organizations are the top-level grouping in a multi-tenant deployment. Each organization can have its own tenants, members, and feature entitlements. This page is only visible to Super Admins.

## Organization List

The table shows all organizations with:

- **Name** — organization name and description
- **Slug** — the URL-friendly identifier
- **Tenants** — how many tenants belong to this organization
- **Status** — Enabled or Disabled
- **Created** — when the organization was created

Use the search bar to filter by name. Click a row to open the organization's detail page.

## Creating an Organization

Click **Add Organization** and provide:

- **Name** (required) — display name for the organization
- **Slug** (required) — unique URL identifier (lowercase, hyphens allowed)
- **Description** — optional summary
- **Website URL** — the organization's website
- **Max Members** — limit on how many users can be added as members
- **Max Tenants** — limit on how many tenants this organization can create

## Tips

- Each organization operates independently — users, clients, and data are isolated between organizations
- Disabling an organization does not delete its tenants or data, but prevents access
- Use the settings icon on each row to jump directly to the organization's detail page
