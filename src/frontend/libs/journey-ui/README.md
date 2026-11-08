# @oluso/journey-ui

`<oluso-journey>`: runs a journey session your backend started for the signed-in user, drawn by `@oluso/sdk`. Every step goes through the app's `HttpClient` (via `httpClientFetch` from `@oluso/auth`), so it carries the user's token, tenant and DPoP proof.

Only apps that render journeys need this package (and `@oluso/sdk`); `@oluso/auth` does not depend on either.
