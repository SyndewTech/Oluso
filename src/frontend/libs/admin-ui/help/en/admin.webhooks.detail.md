---
title: Webhook
relatedTopics:
  - Webhooks | admin.webhooks
  - Clients | admin.clients
  - Signing Keys | admin.signing-keys
---

Create or edit one webhook endpoint. Existing endpoints have two tabs: **Settings** and **Deliveries**.

## Basics

The name, an optional description, the URL that receives the POST requests, and whether the endpoint is active. Turn an endpoint off to pause deliveries without losing its configuration.

## Events

Events are grouped by module. Search by name or event type, and use the checkbox on a group to select or clear all of its events. Restricted events need the permission they name; without it you can't subscribe to them.

## Authentication

How each delivery proves it comes from Oluṣọ:

- **HMAC signature (shared secret)** - each delivery is signed with a `whsec_` secret in the `X-Webhook-Signature` header. The secret is shown once, when the webhook is created or the secret is rotated.
- **Signed by this tenant's issuer (no shared secret)** - each delivery carries a short-lived token signed with this tenant's issuer key. The receiver checks it against the issuer's public keys (its JWKS); nothing secret is shared. The page shows the JWKS URL to give the receiver.
- **Static credential** - a fixed bearer token, username and password (Basic), or API key (`X-API-Key`), for receivers that only check a header. Deliveries are also HMAC signed. Credentials are stored encrypted and never shown again; leave the field blank to keep the current one.

## Owning client

The client this endpoint belongs to. Events this client causes itself (for example comments it writes through the Service API) are not sent back to it when **Skip own client events** is on. With issuer-signed delivery, the token names this client as its subject.

## Sensitive data

When the selected events include restricted ones, their confidential fields are withheld unless you authorise the endpoint. Authorising needs each restricted event's permission and records your name and the time.

## Headers and version

Extra headers sent with every delivery. Headers the delivery sets itself (such as `Authorization` and the `X-Webhook-` headers) can't be overridden. Don't put secrets in custom headers.

## Deliveries

Delivery counts, the recent delivery history, and a retry button for failed deliveries. Use **Send test event** to try the endpoint.
