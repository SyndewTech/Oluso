---
title: Signing Keys
relatedTopics:
  - Settings | admin.settings
  - Clients | admin.clients
---

Manage the cryptographic keys used to sign tokens (JWTs). Proper key management ensures that tokens cannot be forged and that key rotation happens without downtime.

## Key Table

Each key shows:

- **Name / Key ID** — identifier and optional client assignment
- **Type / Algorithm** — RSA, EC, or Symmetric with the specific algorithm and key size
- **Status** — Active (can sign), Retired (verify only), or Expired
- **Expires** — expiration date, with a warning for keys expiring soon
- **Usage** — how many signatures the key has produced and when it was last used

## Key Actions

- **Generate Key** — create a new signing key with a chosen algorithm, key size, and lifetime
- **Rotate Keys** — retire the current active key and activate the next one
- **Revoke** — immediately retire an active key (requires a reason)
- **Delete** — permanently remove an inactive key

## Automatic Rotation

Click **Rotation Settings** to configure automatic key rotation:

- **Key Lifetime** — how long a key stays active before rotation
- **Rotation Lead Time** — how far in advance to generate the next key
- **Grace Period** — how long retired keys continue to validate existing tokens
- **Maximum Keys** — limit on how many keys are retained

## How Rotation Works

1. A new key is generated before the active key expires (lead time)
2. The old key is retired — it can still verify tokens but no longer signs new ones
3. After the grace period, the retired key can be safely deleted
4. Clients that cache the JWKS document will pick up new keys on their next refresh

## Tips

- Always keep at least one active key — without it, no tokens can be issued
- RSA 2048+ or EC P-256 are recommended for production
- The JWKS endpoint (`.well-known/jwks`) automatically publishes all active and retired keys
