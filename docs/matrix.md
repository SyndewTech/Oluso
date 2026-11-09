# Matrix homeservers

Oluṣọ can be the OAuth 2.0 authorization server for a Matrix homeserver, using the OAuth 2.0 API of the Matrix
Client-Server API. Matrix clients then sign in with Oluṣọ accounts, on the tenant the homeserver trusts, and the
homeserver accepts the tokens Oluṣọ issues. It is protocol support, like SAML or SCIM: any homeserver and any
Matrix client that implement that API can use it.

What Oluṣọ provides:

| Matrix needs | Oluṣọ |
|--------------|-------|
| The Matrix scopes, including a device id the client chooses | `urn:matrix:client:api:*` as an API scope and `urn:matrix:client:device:` as a [prefix scope](./prefix-scopes.md); the unstable MSC2967 names too |
| Clients register themselves at every login (MSC2966) | Dynamic client registration with the Matrix rules for client metadata and redirect URIs; PKCE required |
| Native clients on a loopback redirect | `http://localhost` registered without a port, any port accepted at `/authorize` |
| A refresh token with every login, without `offline_access` | *Refresh-implying scopes*, the Matrix API scopes by default |
| Sign in with QR (MSC4108) | The device authorization grant, with a verification page that describes the Matrix scopes in plain words |
| Account creation from the client | `prompt=create`, listed in discovery |
| A user ID for the account | The `preferred_username` claim (the username, or the email when there is none) |
| Revoking a token ends the login | Revocation revokes the whole authorization: every grant of that user, client and session |
| The homeserver hears when a session ends | Resource-server back-channel logout: a logout token for each ended session that held a Matrix scope |

The rest of this page sets up a tenant for Matrix, and describes what the homeserver receives.

## Setting up a tenant

The Matrix Client-Server API (v1.18, "OAuth 2.0 API" → "Scope") defines two scope tokens:

| Scope | Meaning |
| --- | --- |
| `urn:matrix:client:api:*` | Full access to the Client-Server API. |
| `urn:matrix:client:device:<device_id>` | Allocates `<device_id>` and ties it to the tokens. Exactly one per login request. |

In the tenant that the Matrix homeserver trusts (admin UI → **API Scopes**):

1. **Create `urn:matrix:client:api:*`** as an ordinary API scope (leave *Prefix scope* off). The `*` is a literal
   character of the name, not a wildcard. Display name e.g. "Use your Matrix account".
2. **Create `urn:matrix:client:device:`** with **Prefix scope** on. Keep the trailing `:`. Display name e.g. "Sign
   in this device". Clients choose the device id after the `:`. The Matrix spec says device ids SHOULD use the
   RFC 3986 unreserved characters (Client-Server API, OAuth 2.0 API, "Device ID allocation"), but don't enforce
   that: matrix-rust-sdk (Element X) signs in with QR as a device whose id is its unpadded base64 Curve25519 key,
   which may contain `+` and `/`. To accept any scope-token character, as most homeservers do, set the **Allowed
   suffix pattern** to `[\x21\x23-\x5B\x5D-\x7E]+`. Without a pattern, any scope-token characters are accepted, up
   to the tenant's default length (256 unless changed).
3. **Unstable names (only if your clients still use them).** Clients written against MSC2967 before the scopes were
   stable request `urn:matrix:org.matrix.msc2967.client:api:*` and
   `urn:matrix:org.matrix.msc2967.client:device:<device_id>`. Create those the same way (the first ordinary, the
   second a prefix scope ending in `:`) and allow them alongside the stable ones. The homeserver must accept both
   forms.
4. **Enable dynamic client registration for Matrix clients** (admin UI → **Settings** → *Dynamic Client
   Registration*). Matrix clients register themselves at the start of every login:
   - turn on **Enable DCR** and **Allow Open Registration** (Matrix clients have no initial access token);
   - keep **Require PKCE** on (the Matrix spec requires S256 PKCE);
   - **Allowed Scopes**: `openid`, `urn:matrix:client:api:*`, `urn:matrix:client:device:` (and the unstable names
     from step 3 if used); `offline_access` isn't needed (see *Refresh tokens* below);
   - **Allowed Grant Types**: `authorization_code`, `refresh_token`, and
     `urn:ietf:params:oauth:grant-type:device_code` if you support the device authorization grant.
   Registered clients are allowed the scopes they asked for, or all of the allowed scopes when they asked for none.
   A client that names one device value at registration is allowed only that value; one that names
   `urn:matrix:client:device:` (or no scope) may choose a device id at every login.
5. For a pre-registered (non-DCR) Matrix client, add `urn:matrix:client:api:*` and `urn:matrix:client:device:` to
   its allowed scopes on the client's **Scopes** tab.

## What the homeserver sees

For a login that requested `openid urn:matrix:client:api:* urn:matrix:client:device:AbCdEf1234`:

- token response: `"scope": "openid urn:matrix:client:api:* urn:matrix:client:device:AbCdEf1234"`;
- access token (JWT): the `scope` claim holds the same values as a JSON array, one string per value (a single
  string when only one scope was granted);
- introspection: `"scope": "openid urn:matrix:client:api:* urn:matrix:client:device:AbCdEf1234"`;
- after a refresh: the same scope, device value unchanged.

**Refresh tokens.** Matrix clients never ask for `offline_access`, but the Matrix spec has the server issue a
refresh token with every login. A grant (authorization code or device code) that carries a *refresh-implying
scope* gets a refresh token, with the client's usual rotation, when the client is allowed the `refresh_token` grant
(and the tenant, if it limits grant types, allows it too). The refresh-implying scopes are a tenant protocol
setting, `refreshImplyingScopes` on the tenant's protocol configuration (`PUT /api/admin/tenants/{id}/protocol-configuration`):
unset means `urn:matrix:client:api:*` and `urn:matrix:org.matrix.msc2967.client:api:*`, `[]` turns it off and
`resetRefreshImplyingScopes: true` puts the default back. `offline_access` works as before for every other client.

The homeserver takes the device id from the single `urn:matrix:client:device:` value (and the unstable form, if
supported). Oluṣọ does not enforce "exactly one device scope"; the homeserver should refuse a token with none or
several.

## Audience

Create a resource (admin API `/api/admin/resources`, or the resources page of the admin UI) with URI `urn:matrix:client` (or whatever the homeserver is
configured to expect) that lists the Matrix scopes. A token with a Matrix scope then carries `aud`
`urn:matrix:client` (RFC 9068 §3, inferred from the scopes).

## Dynamic client registration rules (MSC2966)

A client that will hold a Matrix scope (it asks for one, or asks for none and the tenant's registration scopes
include one) must follow the Matrix spec's registration rules, else registration answers `invalid_client_metadata`
or `invalid_redirect_uri`:

- `client_uri` is required and https; `logo_uri`, `policy_uri` and `tos_uri`, if given, are https on the
  `client_uri` host or a subdomain of it; no URI carries user info;
- `application_type` is `web` (the default) or `native` (any client may send it; it is stored and returned);
- web redirect URIs: https on the `client_uri` host or a subdomain;
- native redirect URIs: a private-use scheme starting with the reversed `client_uri` host and no authority
  (`app.example:/oauth-callback` for `https://example.app`), a loopback `http://localhost`, `http://127.0.0.1` or
  `http://[::1]` **without** a port (any port is accepted at `/authorize`; the path and query must match), or https
  as for web;
- PKCE is required for such a client.

A client registering only non-redirect grants (for example the device code and refresh token grants) needs no
redirect URIs. The consent page and the device verification page show the client's name, logo, privacy policy and
terms (https links only).

## Sign in with QR (device authorization grant)

MSC4108 has the new device get its tokens with the OAuth 2.0 device authorization grant (RFC 8628):

- allow `urn:ietf:params:oauth:grant-type:device_code` for the tenant (protocol settings, if it limits grant types)
  and for the client (or in the DCR grant types, step 4). The metadata then lists the grant and
  `device_authorization_endpoint`; it leaves both out when the tenant doesn't allow the grant;
- the device authorization response carries `verification_uri` (`{issuer}/device`) and `verification_uri_complete`
  (`{issuer}/device?user_code=…`), a code lifetime (the client's *Device code lifetime*, default 300 s) and a
  5 second polling interval;
- the verification page signs the user in first if needed (the tenant's journey or standard pages, following the
  tenant's and the device client's journey settings), shows the code, the client, what the device asks for in plain
  words (for Matrix: "Sign in to your chat account on a new device" and the Matrix device id), and that this signs
  in the *other* device; Approve or Deny;
- the token endpoint answers `authorization_pending` until then (`slow_down`, adding 5 seconds to the interval,
  when the device polls too fast), `access_denied` after a denial, `expired_token` after the code expires, and once
  approved issues the tokens (with the device scope and, through the Matrix API scope, a refresh token). The code is
  redeemed once;
- each approved device gets a session id (`sid`) of its own, not the approving browser's, so revoking or signing
  out one device leaves the others and the browser session alone.

`prompt_values_supported` lists `none`, `login`, `consent` and `create`.

## Revocation and logout

- Revoking a refresh token, a reference token or a JWT access token at `/connect/revocation` revokes the whole
  authorization: every grant of the same user, client and session. A JWT is verified first (signature, access
  token type, issued to the revoking client).
- A homeserver that keeps its own Matrix sessions hears when one ends: register its client as a
  *resource-server logout watcher* (`ResourceServerLogoutOptions`) with a
  back-channel logout URI. On end-session, admin session revocation and the account portal's session, all-sessions
  and connected-app revocation, it gets a logout token (`typ: logout+jwt`, `aud` its client id, the session's `sid`)
  for every ended session whose grants held a Matrix API scope.

  Register the watcher in the host, with the client id of the homeserver's client (one that has a back-channel
  logout URI) and the scopes whose sessions it keeps:

  ```csharp
  using Oluso.Core.Protocols;
  using Oluso.Core.Services;

  builder.Services.Configure<ResourceServerLogoutOptions>(logout =>
      logout.Watchers.Add(new ResourceServerLogoutWatcher
      {
          ClientId = "matrix-homeserver",
          Scopes = new[] { MatrixScopes.Api, MatrixScopes.UnstableApi },
      }));
  ```

