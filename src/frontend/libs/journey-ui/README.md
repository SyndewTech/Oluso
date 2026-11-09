# @syndew/oluso-journey-ui

`<oluso-journey>`: runs a journey session your backend started for the signed-in user, drawn by `@syndew/oluso-sdk`. Every step goes through the app's `HttpClient` (via `httpClientFetch` from `@syndew/oluso-auth`), so it carries the user's token, tenant and DPoP proof.

Only apps that render journeys need this package (and `@syndew/oluso-sdk`); `@syndew/oluso-auth` does not depend on either.
