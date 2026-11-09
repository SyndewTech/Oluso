---
title: Tenants
relatedTopics:
  - Organizations | admin.organizations
  - Settings | admin.settings
---

Tenants provide data isolation within an organization. Each tenant has its own users, clients, settings, and feature set. This page is visible to Organization Admins.

## Tenants Tab

The table lists all tenants in your organization:

- **Name**: the tenant's display name
- **Identifier**: the unique slug used in URLs and API requests
- **Environment**: Development, Staging, or Production
- **Status**: Enabled or Disabled
- **Created**: when the tenant was created

Use the search bar to filter by name. Click the settings icon to open a tenant's configuration page.

### Creating a Tenant

Click **Add Tenant** and provide:

- **Name** and **Display Name**
- **Identifier** (required): unique slug, cannot be changed later
- **Environment**: Development, Staging, or Production
- **Description**
- **Custom Domain**: optional vanity domain for this tenant. It's added to the tenant's **Domains** for verification; add the DNS TXT record shown there and verify it before the domain is used.

The new tenant gets its Administrator and User roles. If setting it up fails, nothing is left behind and you see the reason. Assign someone the Administrator role from the organisation's members.

## Settings Tab

Organization-level settings shared across all tenants:

- **Name** and **Slug** (read-only)
- **Description**: update the organization's description
- **Website URL**: the organization's website

## Tips

- Use the Environment field to distinguish between dev, staging, and production tenants
- Each tenant is fully isolated: users in one tenant cannot access another unless explicitly granted access
- Disabling a tenant blocks all logins without deleting data
