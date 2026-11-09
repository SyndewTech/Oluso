# @syndew/oluso-account-ui

The Oluso account portal as an Angular library: the self-service pages where users manage their own profile, password, security settings and sessions.

Mount it in your own Angular app, replace its landing page through the `ACCOUNT_LANDING_COMPONENT` token, or add pages with account plugins (`@syndew/oluso-plugin-fido2` adds passkeys).

## Install

```bash
npm install @syndew/oluso-account-ui
```

Peer dependencies (npm installs them with the package): `@angular/common`, `@angular/core`, `@angular/forms`, `@angular/router`, `angular-auth-oidc-client`, `rxjs`, `@syndew/oluso-auth`, `@syndew/oluso-ui-core`.

## Use

```ts
import { provideRouter } from '@angular/router';
import { ACCOUNT_ROUTES, provideAccountAuth } from '@syndew/oluso-account-ui';

providers: [provideRouter(ACCOUNT_ROUTES), provideAccountAuth()]
```

At runtime the portal reads `window.__OLUSO_CONFIG__` (from a `config.js` next to `index.html`) for the server URL. The `account-standalone` app in the repository is a complete example.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Hosting the portals](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
