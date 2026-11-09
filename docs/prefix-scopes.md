# Prefix scopes

Most API scopes have a fixed name. A **prefix scope** stands for every scope value that starts with its name
followed by a part the client chooses, the **suffix**. The authorization server grants the full value unchanged, and
the resource server reads the suffix back from the token's scope. Typical uses:

- a per-device or per-installation scope (`device:<id>`), where the client picks the id and the resource server ties
  the tokens to it;
- a scope narrowed to one resource (`files:read:<folder>`, `project:<project-id>`), where a client asks for access to
  just the thing it is about to use;
- a protocol that defines its own templated scopes, such as the Matrix device scope (see [matrix.md](./matrix.md)).

## How a prefix scope behaves

An API scope is a prefix scope when it has the property `match` = `prefix`. In the admin UI, tick **Prefix scope**
when you create or edit the API scope; the suffix rule fields appear underneath.

- **Recognised values.** A requested scope `<name><suffix>` is recognised when the routed tenant (or the platform)
  has an enabled prefix scope `<name>` and the suffix is valid (next section).
- **The bare name is not a scope.** Requesting `device:` with nothing after it is an unknown scope.
- **Client allowance.** A client whose allowed scopes include the prefix scope's name may request any valid value of
  it. Listing one full value (e.g. `device:ABC`) allows only that value.
- **Exact names win.** An ordinary API scope whose name happens to start with a prefix scope's name keeps its own
  rules (its own allowance, no suffix check). When two prefix scopes match, the longer name wins.
- **Feature gates.** A `required_feature` property on the prefix scope applies to all of its values.
- **What is granted.** The full requested value: it is stored in the authorization code and the grant, reported in the
  token response's `scope`, in the access token's `scope` claim and in introspection, and kept on refresh. User claims
  on the prefix scope are added as for any API scope, and a resource listing the prefix scope is inferred as the
  audience.
- **Refresh.** A refresh may repeat the value or leave `scope` out. Asking for a different suffix is asking for a
  scope beyond the original grant, so it fails with `invalid_scope` (RFC 6749 §6).
- **Invalid values.** A value whose suffix isn't valid is left out of the grant (reason `invalid_value` in the
  ScopesDropped audit event), or refused with `invalid_scope` for clients with `strict_scopes=true`.
- **Consent** shows the prefix scope's display name and description, never the client-chosen suffix. The user
  consents to the full value.
- **Discovery** lists a prefix scope as `<name>*` (e.g. `device:*`) in `scopes_supported` when *Show in Discovery* is
  on. The bare name would advertise a value that can't be granted; the trailing `*` is the usual wildcard notation
  for "the rest is yours to choose". `scopes_supported` is advisory (RFC 8414), so turn *Show in Discovery* off if
  your clients would misread it.
- **No scope requested.** When a grant falls back to the client's allowed scopes (client credentials, password), a
  prefix scope's name is not issued: there is no suffix to issue it with.
- **Dynamic client registration.** A registration may name a value of a prefix scope that registration allows (e.g.
  `device:ABC` when the DCR allowed scopes list `device:`); it is registered as asked, so the client is allowed that
  one value. To be allowed any suffix, the client registers the prefix scope's name itself.

Nothing is seeded: a tenant sets up the prefix scopes it needs.

## The suffix rule

A suffix is valid when all of these hold:

1. **It is not empty.**
2. **It uses only scope-token characters.** RFC 6749 §3.3 defines a scope token as `1*( %x21 / %x23-5B / %x5D-7E )`:
   printable ASCII except space, `"` and `\`. That is the default: `:`, `/`, `?`, `@`, `*` and so on are all fine. It
   is also the outer bound for every prefix scope. Scopes are sent space-separated, so a space can never be part of
   one, and a request with `"`, `\` or a non-ASCII character in a scope is refused as malformed before any prefix
   matching.
3. **It is at most the maximum length**: the scope's `suffix_max_length` property (admin UI: *Maximum suffix length*),
   or, when it isn't set, the tenant's default: *Settings → Protocol → Prefix Scopes → Default maximum suffix
   length for prefix scopes* (1–2048, default 256; also `prefixScopeSuffixMaxLength` on the tenant's protocol
   configuration). RFC 6749 sets no limit, but the value is client-chosen and is copied into authorization URLs,
   codes, stored grants, every access token and audit records; 256 fits identifiers, URNs and most URLs.
4. **It matches the scope's pattern, if it has one**: the `suffix_pattern` property (admin UI: *Allowed suffix
   pattern*), a .NET regular expression anchored to the whole suffix (`[0-9]+` accepts `42`, not `42a`). A pattern
   narrows rule 2; it can't widen it (`.*` still can't let a `"` through). Patterns run with a 100 ms match timeout,
   and a suffix that hits the timeout is treated as invalid, so a pathological pattern can't stall sign-in. The admin
   API refuses a pattern that doesn't compile.

Use a pattern when the resource server expects a particular shape, for example `[0-9]+` for numeric ids,
`[a-z0-9-]{3,63}` for slugs, or `[A-Za-z0-9._~-]+` for the URL-safe RFC 3986 unreserved characters.

In the admin API (`/api/admin/api-scopes`) the prefix settings are `isPrefix`, `suffixPattern` and `suffixMaxLength`.
On update they are applied together when `isPrefix` is sent: an empty `suffixPattern` or a null `suffixMaxLength`
removes that property, and `isPrefix: false` removes both. Leaving `isPrefix` out leaves all three as they are.

## Example: per-project access

An API serves several projects and wants tokens that reach only one of them. Create `project:` as a prefix scope with
display name "Access one of your projects" and **Allowed suffix pattern** `[0-9a-f]{8}` (the API's project ids), and
allow it to the client. The client asks for `project:1a2b3c4d`; the token's `scope` carries that value, and the API
checks it against the project in the request path. Asking for `project:../x` or `project:1A2B3C4D` is left out (or
refused for a strict client). The consent screen says "Access one of your projects".

## Worked example: Matrix clients

The Matrix Client-Server API defines a prefix scope of its own, `urn:matrix:client:device:<device_id>`, alongside the
ordinary scope `urn:matrix:client:api:*`. Setting both up, and everything else a Matrix homeserver needs from
Oluṣọ, is in [matrix.md](./matrix.md).
