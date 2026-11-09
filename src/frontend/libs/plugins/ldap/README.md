# @syndew/oluso-plugin-ldap

LDAP administration for the Oluso admin portal: an LDAP dashboard, service accounts for applications that bind over LDAP, and a settings tab for the built-in LDAP server.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs the LDAP module (`Syndew.Oluso.Enterprise.Ldap`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-ldap
```

Peer dependencies (npm installs them with the package): `@angular/common`, `@angular/core`, `@angular/forms`, `@angular/router`, `rxjs`, `@syndew/oluso-admin-ui`, `@syndew/oluso-ui-core`.

## Use

Register the plugin with the `PluginService` when the admin app starts:

```ts
import { inject, provideAppInitializer } from '@angular/core';
import { PluginService } from '@syndew/oluso-ui-core';
import { LDAP_PLUGIN } from '@syndew/oluso-plugin-ldap';

// in the admin app's providers
provideAppInitializer(() => {
  const plugins = inject(PluginService);
  plugins.registerAdminPlugin(LDAP_PLUGIN);
  plugins.markInitialized();
})
```

Register every plugin before `markInitialized()`.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
