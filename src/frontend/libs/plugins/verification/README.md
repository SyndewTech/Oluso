# @syndew/oluso-plugin-verification

Business verification (KYB) for the Oluso admin portal: the businesses being verified and their details, a review queue for checks that need a person, and a settings tab for the verification providers.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs the verification module (`Syndew.Oluso.Enterprise.Verification`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-verification
```

Peer dependencies (npm installs them with the package): `@angular/common`, `@angular/core`, `@angular/forms`, `@angular/router`, `rxjs`, `@syndew/oluso-admin-ui`, `@syndew/oluso-ui-core`.

## Use

Register the plugin with the `PluginService` when the admin app starts:

```ts
import { inject, provideAppInitializer } from '@angular/core';
import { PluginService } from '@syndew/oluso-ui-core';
import { VERIFICATION_PLUGIN } from '@syndew/oluso-plugin-verification';

// in the admin app's providers
provideAppInitializer(() => {
  const plugins = inject(PluginService);
  plugins.registerAdminPlugin(VERIFICATION_PLUGIN);
  plugins.markInitialized();
})
```

Register every plugin before `markInitialized()`.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
