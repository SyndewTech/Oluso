# @syndew/oluso-plugin-scim

SCIM 2.0 provisioning for the Oluso admin portal: the SCIM clients that provision users into Oluso, their attribute mappings, and the outbound providers Oluso provisions users to.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs the SCIM module (`Syndew.Oluso.Enterprise.Scim`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-scim
```

Peer dependencies (npm installs them with the package): `@angular/common`, `@angular/core`, `@angular/forms`, `@angular/router`, `rxjs`, `@syndew/oluso-admin-ui`, `@syndew/oluso-ui-core`.

## Use

Register the plugin with the `PluginService` when the admin app starts:

```ts
import { inject, provideAppInitializer } from '@angular/core';
import { PluginService } from '@syndew/oluso-ui-core';
import { SCIM_PLUGIN } from '@syndew/oluso-plugin-scim';

// in the admin app's providers
provideAppInitializer(() => {
  const plugins = inject(PluginService);
  plugins.registerAdminPlugin(SCIM_PLUGIN);
  plugins.markInitialized();
})
```

Register every plugin before `markInitialized()`.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
