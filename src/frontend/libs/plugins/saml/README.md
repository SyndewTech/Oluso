# @syndew/oluso-plugin-saml

SAML 2.0 administration for the Oluso admin portal: a SAML dashboard, the service providers that sign in through Oluso (with their details and certificates), and a settings tab for the identity provider.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs the SAML module (`Syndew.Oluso.Enterprise.Saml`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-saml
```

Peer dependencies (npm installs them with the package): `@angular/common`, `@angular/core`, `@angular/forms`, `@angular/router`, `rxjs`, `@syndew/oluso-admin-ui`, `@syndew/oluso-ui-core`.

## Use

Register the plugin with the `PluginService` when the admin app starts:

```ts
import { inject, provideAppInitializer } from '@angular/core';
import { PluginService } from '@syndew/oluso-ui-core';
import { SAML_PLUGIN } from '@syndew/oluso-plugin-saml';

// in the admin app's providers
provideAppInitializer(() => {
  const plugins = inject(PluginService);
  plugins.registerAdminPlugin(SAML_PLUGIN);
  plugins.markInitialized();
})
```

Register every plugin before `markInitialized()`.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
