# Authentication context (acr)

An ID token's `acr` claim says how strongly the user was authenticated (OpenID Connect Core 1.0 section 2). Oluso asserts one of two values. They are absolute URIs, as the specification recommends, and they mean the same thing in every deployment of Oluso, whatever the product is branded as.

| Value | Meaning |
|-------|---------|
| `https://oluso.io/acr/sfa` | Single factor: the user signed in with one authentication factor, such as a password, a one-time code sent by email or SMS, a passkey, or a sign-in at an external identity provider that did not report multi-factor authentication. |
| `https://oluso.io/acr/mfa` | Multi-factor: the user signed in with two or more independent factors, for example a password followed by an authenticator app code. The ID token's `amr` claim contains `mfa` (RFC 8176). |

Both are listed in `acr_values_supported` in the discovery document.

## How the value is chosen

The value comes from the authentication methods recorded for the sign-in, which are also returned in `amr`. When `amr` contains `mfa`, the value is `https://oluso.io/acr/mfa`, and otherwise it is `https://oluso.io/acr/sfa`. When a sign-in step records an `acr` of its own, that value is used instead. A token issued without a recorded sign-in method carries no `acr`.

The same value is returned in the ID token from the authorization endpoint, the token endpoint and every refresh, because it describes the original sign-in, not the token request.

## Requesting a value

A client may send `acr_values` on the authorization request. In OpenID Connect this requests `acr` as a voluntary claim (section 3.1.2.1). Oluso always returns the value the sign-in actually met, which may not be one of the requested values. A client that needs multi-factor authentication checks that the ID token's `acr` is `https://oluso.io/acr/mfa` and refuses the sign-in otherwise.

To make multi-factor authentication happen for every sign-in to a tenant, turn on **Require Multi-Factor Authentication** in the tenant's settings (see "Requiring MFA" in the user guide). For one client, add a multi-factor step to the user journey that client uses. The request's `acr_values` is passed into the journey as its `acrValues` value, so a condition step can route a request that sends `acr_values=https://oluso.io/acr/mfa` to the multi-factor step (see "ACR Values" in the user guide).

## Requiring a value

On a tenant that turns on the `claims` request parameter (OpenID Connect Core 1.0 section 5.5; **Settings > Protocol > Claims Request Parameter** in the admin portal, shown as `claims_parameter_supported` in discovery), a client can make `acr` an essential claim:

```json
{ "id_token": { "acr": { "essential": true, "values": ["https://oluso.io/acr/mfa"] } } }
```

The sign-in must then meet one of the listed values (section 5.5.1.1):

- A user whose session does not meet them signs in again. When the request has no `acr_values`, the listed values are passed into the journey as its `acrValues` value in their place, so a condition step can route to a multi-factor step that meets them.
- When the sign-in still does not meet them, no code or token is issued, and the client receives the error `unmet_authentication_requirements` on its redirect URI.
- With `prompt=none` a session that does not meet them returns `login_required`, as no sign-in can be shown.

An essential `acr` without `value` or `values`, or one that is not essential, is a request for the claim like `acr_values`, and is always answered with the value the sign-in met.
