---
title: Tenant Settings
relatedTopics:
  - Tenants | admin.tenants
  - Settings | admin.settings
---

Configure a tenant's identity policies, password requirements, and enabled features. Changes here apply only to this specific tenant.

## General

### Tenant Details

- **Name** and **Display Name** — how the tenant appears in the UI
- **Description** — optional context
- **Custom Domain** — vanity domain for this tenant's login pages
- **Enabled** — toggle the tenant on or off
- **Hide Embed Branding** — remove platform branding from hosted pages

### Password Requirements

Control the complexity rules for user passwords:

- **Min / Max Length** — character count bounds
- **Required Unique Characters** — minimum distinct characters
- **Require Digit** — at least one number
- **Require Lowercase / Uppercase** — case requirements
- **Require Special Character** — at least one symbol

### Password Lifecycle

- **Password History Count** — how many previous passwords to remember (prevents reuse)
- **Password Expiration Days** — force password changes after N days (0 = never)

### Account Lockout

- **Max Failed Attempts** — lock the account after this many consecutive failures
- **Lockout Duration** — how long the lockout lasts (in minutes)
- **Block Common Passwords** — reject passwords found in common breach lists

### Custom Validation

- **Custom Regex Pattern** — an additional regex that passwords must match
- **Custom Error Message** — shown to users when the regex fails

Use **Reset to Defaults** to revert all password and lockout settings, or **Save Settings** to apply changes.

## Features

A grid of feature toggles controlling which modules are available in this tenant. Features must first be enabled at the organization level before they can be activated here.

Each card shows the feature name, description, and a toggle switch.

## Tips

- Password policies apply to new passwords only — existing passwords are not retroactively validated
- Lockout protects against brute-force attacks without requiring MFA
- The custom regex is useful for industry-specific compliance (e.g., requiring passwords to contain no part of the username)
