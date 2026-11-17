---
title: Platform Roles
relatedTopics:
  - Roles | admin.roles
  - Users | admin.users
  - Organizations | admin.organizations
---

Platform roles belong to no single organization: SuperAdmin, SystemAdmin, OrgAdmin and each module's administrator role. An organization's Roles page only shows who holds them in that organization. This page shows every holder in every organization. It is only visible to Super Admins.

## Roles

The list on the left shows each platform role and how many people hold it.

- **Every organization**: SuperAdmin and SystemAdmin apply in every organization, whichever organization the assignment was made in.
- **Follows membership**: OrgAdmin is held by every Owner and Admin of an organization. It changes with their membership on the organization page, not here.
- Every other platform role applies only in the organization it was assigned in, so one person can hold it in several.

## Holders

Select a role to see who holds it, the organization each assignment applies in, the person's home organization when that is different, and when and by whom it was assigned. Search narrows the list by person or organization.

## Assigning a role

Click **Assign**, choose the organization, then the person. Only people whose home is that organization, or who are members of it, can be chosen. For SuperAdmin and SystemAdmin the organization is only where the assignment is recorded.

## Removing a role

Click **Remove** on a holder. For a role that applies in one organization, only that assignment goes; their assignments elsewhere stay. Removing SuperAdmin or SystemAdmin takes it away everywhere.

You can't remove SuperAdmin or SystemAdmin from yourself, and the last SuperAdmin keeps the role until someone else has it.

Assigning and removing record the same audit events, and send the same notices to a module's other administrators, as changing roles on the Users page.
