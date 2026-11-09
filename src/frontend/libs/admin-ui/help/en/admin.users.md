---
title: Users
relatedTopics:
  - Roles & Permissions | admin.roles
  - Grants | admin.grants
  - Settings | admin.settings
---

Manage user accounts across your identity platform. From here you can create, edit, disable, and delete users, as well as manage their roles and credentials.

## User List

The table shows all registered users with:

- **User**: avatar, display name, and username
- **Email**: address with a verification indicator
- **Status**: Active or Disabled, with a Locked badge if the account is locked out
- **Roles**: assigned roles (shows the first two, with a +N count for additional)
- **Last Login**: the most recent authentication timestamp

Click any row to open the user's detail page.

## Creating a User

Click **Add User** and fill in:

- **Email** (required)
- **Username** (required)
- **Password**: either **Email an invite** (the default), which emails the user a link to set their own password, or **Set a password now**
- **First Name** and **Last Name**
- **Phone Number**
- **Active**: whether the account is immediately usable
- **Email Verified**: skip the email verification step

### Invites

An invite email comes from your organisation and carries a link to set a password on your organisation's own sign-in page. The email says how long the link lasts: your tenant's account-link lifetime (24 hours unless you've changed it). The password must meet your tenant's password policy, and the page shows its rules.

If the email can't be sent (for example, email isn't set up), the user is still created and you're told why. Until the user sets a password, **Resend invite** in their row sends a fresh link.

## Quick Actions

Each row has action buttons for common operations without opening the detail page:

- **Edit**: update the user's profile
- **Manage Roles**: assign or remove roles
- **Reset Password**: set a new password
- **Resend invite**: send the set-password invite again (only for users who haven't set a password yet)
- **Unlock**: clear a locked account (only appears if locked)
- **Delete**: permanently remove the user

## Tips

- Use the search bar to find users by name or email
- Disabling a user prevents login without deleting their account or data
- Locked accounts are typically caused by too many failed login attempts. Use the Unlock action or configure lockout policy in Settings
