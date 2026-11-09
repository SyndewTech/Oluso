---
title: License
relatedTopics:
  - Settings | admin.settings
  - Module Seats | admin.module-seats
  - Subscription | admin.billing.subscriptions
---

View and manage the license key that activates your tenant's enabled modules and feature flags.

## License Status

The page displays the current license state:

- **Tier** — the plan associated with the licence (e.g. Starter, Pro, Enterprise)
- **Issued** — date the license was generated
- **Expires** — expiry date, with warning indicators as the date approaches
- **Modules** — list of modules unlocked by this license
- **Seat caps** — per-module seat limits where applicable

## Updating the License

Paste a new license key into the **License Key** field and click **Apply**. The key is validated server-side before being stored. If valid:

- New modules become visible in navigation immediately
- Seat caps update to the new tier
- Expiry warnings are recalculated

If invalid, the existing license remains active and an error explains why (expired, signature mismatch, wrong tenant ID, malformed).

## Removing a License

Clearing the field and saving reverts to the default trial/free tier. Any modules outside that tier are hidden until a valid licence is reapplied.

## Tips

- Keep your license key in a password manager — it's a one-time issuance from the platform owner.
- Set a calendar reminder a month before expiry to request a renewal key from your account manager.
- If a module disappears from navigation, check this page first — an expired or downgraded license is the most common cause.
