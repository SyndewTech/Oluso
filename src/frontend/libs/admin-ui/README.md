# @syndew/oluso-admin-ui

The Oluso admin portal as an Angular library: the routes, pages and services for administering an Oluso server. That covers tenants, users and roles, clients, user journeys, signing keys and security settings.

Mount it in your own Angular app to host the admin portal yourself, rebrand it, or extend it with admin plugins such as `@syndew/oluso-plugin-saml`.

## Install

```bash
npm install @syndew/oluso-admin-ui
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core` and `@syndew/oluso-auth`.

## Use

```ts
import { provideRouter } from '@angular/router';
import { ADMIN_ROUTES, provideAdminAuth } from '@syndew/oluso-admin-ui';

providers: [provideRouter(ADMIN_ROUTES), provideAdminAuth()]
```

At runtime the portal reads `window.__OLUSO_CONFIG__` (from a `config.js` next to `index.html`) for the server URL. The `admin-standalone` app in the repository is a complete example.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Hosting the portals](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
