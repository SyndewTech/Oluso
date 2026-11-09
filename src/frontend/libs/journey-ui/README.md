# @syndew/oluso-journey-ui

`<oluso-journey>`: an Angular component that runs a user journey session your backend started for the signed-in user. It draws the steps with `@syndew/oluso-sdk`. Every step goes through the app's `HttpClient` (via `httpClientFetch` from `@syndew/oluso-auth`), so it carries the user's token, tenant and DPoP proof.

Only apps that render journeys need this package (and `@syndew/oluso-sdk`). `@syndew/oluso-auth` doesn't depend on either.

## Install

```bash
npm install @syndew/oluso-journey-ui @syndew/oluso-sdk
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-auth` and `@syndew/oluso-sdk`.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [JavaScript SDK](https://www.npmjs.com/package/@syndew/oluso-sdk)
