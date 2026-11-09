# @syndew/oluso-auth

Authentication for Angular apps that sign in with an Oluso server:

- the auth provider contract, with silent renew and remember-me storage
- an auth store and `AuthService`
- `authGuard` and `permissionGuard` for routes
- HTTP interceptors: bearer token, token refresh and retry, tenant header and DPoP proofs
- tenant single sign-on helpers

The Oluso admin, workspace and account portals use it, and so can your own Angular apps.

## Install

```bash
npm install @syndew/oluso-auth
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core`.

## Use

```ts
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor } from '@syndew/oluso-auth';

provideHttpClient(withInterceptors([authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor]));
```

Protect routes with `authGuard`, or `permissionGuard` for a permission the signed-in user must hold.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
