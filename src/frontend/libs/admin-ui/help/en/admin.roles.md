---
title: Roles & Permissions
relatedTopics:
  - Users | admin.users
  - Grants | admin.grants
---

Roles define sets of permissions that control what users can see and do in the admin portal. Assign roles to users to grant them the appropriate level of access.

## Built-in Roles

- **SuperAdmin** - full access to all platform features across all organizations
- **OrgAdmin** - full access within a single organization
- **Custom roles** - create your own with fine-grained permissions

## Creating a Role

Click **Add Role** and:

1. Give the role a descriptive **Name** (e.g., "Team Manager", "Report Viewer")
2. Select the **Permissions** this role should grant
3. Save the role

## Permission Categories

Permissions are organized by module:

- **Core** - users, clients, resources, roles, settings
- Each enabled module contributes its own permission category for its features and records

## Assigning Roles

Navigate to a user's detail page and add roles in the **Roles** section. Users can hold multiple roles; their effective permissions are the union of all assigned role permissions.

## Moving Roles Between Environments

Tick the roles to copy and click **Export**. The file carries each role's name, display name, description, permissions, entitlements and other claims, along with the definitions of the entitlements it grants. Who holds a role is not included. Only this organization's own roles can be exported; built-in, platform and module roles are set up by each environment.

In the other environment, click **Import** and choose the file. A preview shows what will happen to each role before anything is saved:

- **Create**: no role here has that name yet.
- **Skip**: a role with that name exists. Tick **Replace roles that already exist** to update it instead; its permissions, entitlements and claims then become the file's.
- **Can't import**: the name is reserved, belongs to a platform role, or appears twice in the file.

Entitlements the file uses that this organization doesn't have are created from their definitions in the file, which needs permission to manage entitlements. Permissions this environment doesn't know, or that you aren't allowed to grant, and reserved claims are left out and listed in the preview. Imported roles are checked and audited the same way as roles saved by hand.
