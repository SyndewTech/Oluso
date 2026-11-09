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

Peer dependencies (npm installs them with the package): `@angular/common`, `@angular/core`, `@angular/router`, `rxjs`, `@syndew/oluso-ui-core`.

## Use

```ts
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { tenantInterceptor, authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor } from '@syndew/oluso-auth';

provideHttpClient(withInterceptors([tenantInterceptor, authRefreshInterceptor, tokenRefreshRetryInterceptor, dpopInterceptor]));
```

The interceptors and guards use the app's `AuthProvider`. The portal libraries register one (`provideAdminAuth()`, `provideAccountAuth()`, `provideWorkspaceAuth()`); in your own app, provide a class that extends `AuthProvider`:

```ts
import { AuthProvider } from '@syndew/oluso-auth';

providers: [{ provide: AuthProvider, useClass: MyAuthProvider }]
```

Protect routes with `authGuard`, or `permissionGuard` for a permission the signed-in user must hold.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
