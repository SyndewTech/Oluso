---
title: API Scope Details
relatedTopics:
  - API Scopes | admin.api-scopes
  - Resources | admin.resources
  - Clients | admin.clients
---

View and edit the full configuration of an API scope.

## Basic Information

- **Name** — the unique scope identifier (read-only after creation)
- **Display Name** — label shown to users on the consent screen
- **Description** — additional explanation shown during consent
- **Status** — Enabled or Disabled

## Settings

- **Required** — if enabled, this scope is always included in tokens regardless of whether the client explicitly requests it
- **Emphasize on Consent** — visually highlight this scope on the consent page to draw the user's attention
- **Show in Discovery Document** — include this scope in the `.well-known/openid-configuration` response so clients can discover it automatically
- **Prefix scope** — the scope matches any scope value that starts with its name, followed by a part the client chooses (the suffix). With the prefix scope `device:`, a client allowed it may request `device:abc123`, and that full value is granted and put in the token. The consent screen shows the scope's display name, not the suffix. When ticked, two optional fields appear:
  - **Allowed suffix pattern** — a regular expression the whole suffix must match (it is anchored). Leave it empty to allow any characters a scope can contain: printable ASCII except space, `"` and `\`. A pattern can only narrow that set. It must compile; a broken pattern is refused on save.
  - **Maximum suffix length** — the longest suffix accepted. Leave it empty for the default of 256 characters.

  For example, Matrix clients choose their device id under `urn:matrix:client:device:`; the pattern `[\x21\x23-\x5B\x5D-\x7E]+` accepts any scope-token character, which Element X needs for its base64 device ids. See `docs/prefix-scopes.md`.

## User Claims

Claims are pieces of user information included in access tokens when this scope is granted. For example, a `profile` scope might include the `name`, `family_name`, and `given_name` claims.

Click **Edit Claims** to:

- Add custom claim types by name
- Remove existing claims
- Pick from common suggestions: `email`, `sub`, `name`, `given_name`, `family_name`, `preferred_username`, `role`, etc.

## Tips

- Adding a claim to a scope means any client granted that scope will receive the claim in the access token
- Keep scopes focused — prefer multiple narrow scopes over one broad scope
- The `openid` scope is required for all OpenID Connect flows and should not be disabled
