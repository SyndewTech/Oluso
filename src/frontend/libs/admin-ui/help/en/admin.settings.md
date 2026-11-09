---
title: Settings
relatedTopics:
  - Signing Keys | admin.signing-keys
  - Clients | admin.clients
---

Configure platform-wide settings for authentication, tokens, security, and CORS. Changes are saved per-tenant.

## General

- **Use Journey-Based Flow** — switch between the journey builder UI and standalone login/register pages
- **Allow Self Registration** — let users create their own accounts
- **Require Email Verification** — new users must verify their email before they can sign in
- **Require Terms Acceptance** — show a terms checkbox during registration
- **Terms of Service / Privacy Policy URLs** — links shown on the registration page
- **Allowed Email Domains** — restrict self-registration to specific domains (comma-separated)

## Tokens

Default lifetimes applied to new clients. Individual clients can override these values.

| Setting | Default | Description |
|---|---|---|
| **Access Token Lifetime** | 3600s (1 hour) | How long access tokens are valid |
| **Identity Token Lifetime** | 300s (5 minutes) | How long ID tokens are valid |
| **Refresh Token Lifetime** | 2592000s (30 days) | How long refresh tokens can be used |

## Security

- **Require HTTPS** — reject non-TLS requests
- **Emit Static Claims** — include standard claims in all tokens
- **Enable Backchannel Logout** — support server-to-server logout notifications

## CORS

Control which origins can make cross-origin requests to the token and authorization endpoints.

- **Allow All Origins** — accept requests from any origin (not recommended for production)
- **Allowed Origins** — comma-separated list of permitted origins

## Dynamic Client Registration

Enable RFC 7591 for programmatic client registration:

- **Enable DCR** — turn on the registration endpoint
- **Allow Open Registration** — anyone can register clients without an initial access token
- **Require PKCE** — dynamically registered clients must use PKCE
- **Allowed Scopes / Grant Types** — restrict what registered clients can request


## WhatsApp

Connects the organisation's WhatsApp Business number (Meta Cloud API) for every module that sends notifications. Credentials are stored encrypted.

- **Setup** — phone number ID, access token, WhatsApp Business Account ID (for templates), app secret (for webhooks) and a default country used to complete local phone numbers. **Test connection** calls Meta with your credentials and records the verified name and number.
- **Webhook** — paste the callback URL and verify token into Meta's app configuration and subscribe to the *messages* field. Inbound messages open the 24-hour reply window, STOP and START replies update consent, and delivery receipts (sent, delivered, read, failed) update the notification log.
- **Templates** — Meta only accepts free-form text within 24 hours of a person's last message; anything the organisation starts must use an approved template. Sync templates here, then pick them in a workflow's Send WhatsApp step.
- **Contacts & consent** — every number messaged or received appears with its consent state and whether its reply window is open. Opted-out numbers are never messaged until they opt back in (by replying START or by an administrator).
- **Send a test** — sends one template or text message with the saved credentials.

## Business verification

Shown when the business verification module is installed. Configures the providers the **Business Verification** journey step uses. Needs the *Manage business verification providers* permission.

- **Providers** — Dojah (App ID and secret key) and Youverify (API secret key). Choose Sandbox while testing (test data, not billed) or Live. Secrets are stored encrypted, never shown again, and cleared when you switch environment. **Test saved credentials** calls the provider's free endpoint (Dojah's wallet balance, Youverify's supported countries); it never buys a lookup.
- **Hosted verification (Dojah)** — for the step's *hosted* mode, also save the public key, the EasyOnboard widget ID of your KYB flow and the webhook secret, and give Dojah the **Webhook URL** shown on the provider (subscribe the *kyc_widget* service to it). The URL is on this organisation's own domain.
- **Country routing** — which enabled provider verifies businesses in each country. There is no fallback: a country without a provider can't be verified.
- **Reuse registry results** — within this many days, verifying the same registration number again reuses the earlier registry result instead of paying for a new lookup. 0 always looks up again.

