---
title: Module Seats
relatedTopics:
  - Users | admin.users
  - Roles & Permissions | admin.roles
  - Subscription | admin.billing.subscriptions
---

Manage which users have access to each licensed module. Each module enforces its own seat limit based on your subscription, and only users assigned to a module can sign in to its features.

## Module List

The page lists every module enabled for your tenant. Each card shows:

- **Module name** and short description
- **Seats used** vs **seats licensed** (e.g. 12 / 25)
- **Status** — whether the module is at capacity or has free seats

Click a module to open its detail view and manage its assigned users.

## Module Detail

Selecting a module reveals:

- **Assigned Users** — users who currently consume a seat on this module
- **Add User** — search across all tenant users and grant a seat
- **Remove** — revoke a user's seat (frees it for someone else)

When all licensed seats are taken, **Add User** is disabled until you remove someone or upgrade the plan.

## Adding a User

1. Click **Add User** on the module card.
2. Search by name or email.
3. Select the user and confirm. The seat is consumed immediately and the user gains access on next sign-in.

## Removing a User

Click the **Remove** action next to a user, then confirm. The user keeps their account and other module seats — only this module's access is revoked.

## Tips

- Seat counts are enforced at the API layer, not just the UI — a removed user is locked out immediately.
- If you're at capacity, check **Last Login** in the user list to identify dormant users you can reclaim seats from.
- Module seat consumption is reflected on the billing **Subscription** page so you can see usage trending into the next renewal.
