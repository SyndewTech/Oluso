---
title: Organization Details
relatedTopics:
  - Organizations | admin.organizations
  - Tenants | admin.tenants
---

Manage a single organization's settings, features, tenants, and members. The detail page is organized into tabs.

## General

Two sections side by side:

- **Identity** — Name, Slug (read-only), Description, and Enabled toggle
- **Configuration** — Logo URL, Website URL, Max Members, and Max Tenants limits

Changes are saved when you click **Save Changes**.

## Features

A grid of feature cards representing the modules and capabilities available to this organization. Each card shows:

- **Feature name** and description
- **Granted at / Expires at** dates (if time-limited)
- **Toggle** to enable or disable the feature

Enabling a feature here makes it available for the organization's tenants to activate. Disabling it hides the feature from all tenants under this organization.

## Tenants

A table of all tenants belonging to this organization, showing Name, Identifier, Status, and Created date.

Click **Add Tenant** to create a new tenant with:

- **Name** and **Display Name**
- **Identifier** — unique tenant slug
- **Environment** — Development, Staging, or Production
- **Description**

Use the settings icon on each row to jump to that tenant's configuration page.

## Members

Organization members are users with administrative access to this organization. The table shows each member's User, Role, and Joined date.

Click **Add Member** and provide:

- **User ID** — the user to add
- **Role** — Member, Admin, or Owner

Owners have full control over the organization. Admins can manage tenants and members. Members have read access.
