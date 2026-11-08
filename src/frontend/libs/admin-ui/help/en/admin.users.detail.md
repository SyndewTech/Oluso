---
title: User Details
relatedTopics:
  - Users | admin.users
  - Roles & Permissions | admin.roles
---

View and manage all aspects of a user account. The detail page is organized into tabs.

## Profile

Core identity information: username, email, first name, last name, display name, phone number, and timestamps for creation, last update, and last login. A checkmark next to the email or phone indicates it has been verified.

## Security

Account security status and actions:

- **Account Status** — Active or Disabled
- **Lockout Status** — whether the account is currently locked, with an **Unlock** button
- **Failed Login Attempts** — count of consecutive failures
- **Reset Password** — set a new password for the user

## Roles

Lists all roles assigned to the user as badges. Click **Manage Roles** to add or remove roles from a checkbox list. A user's effective permissions are the union of all assigned roles.

## Sessions

Active authentication sessions for this user. Each row shows the client, IP address, user agent, and expiration time. You can revoke individual sessions or use **Revoke All** to terminate every active session.

## MFA

Multi-factor authentication status. If MFA is enabled, you can **Reset MFA** to clear the user's authenticator registration, requiring them to re-enroll on next login.

## Claims

Custom claims attached to the user's identity. These are included in tokens issued for this user. Use **Add Claim** to create new type/value pairs. Common claims include `department`, `employee_id`, or `cost_center`.

## External Logins

Linked external authentication providers (Google, Microsoft, GitHub, etc.). You can remove a linked provider, which will prevent the user from signing in with that provider until they re-link it.
