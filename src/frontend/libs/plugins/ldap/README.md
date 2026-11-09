# @syndew/oluso-plugin-ldap

LDAP administration for the Oluso admin portal: an LDAP dashboard, service accounts for applications that bind over LDAP, and a settings tab for the built-in LDAP server.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs the LDAP module (`Syndew.Oluso.Enterprise.Ldap`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-ldap
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core` and `@syndew/oluso-admin-ui`.

## Use

Register the plugin with the `PluginService` before the admin portal starts:

```ts
import { PluginService } from '@syndew/oluso-ui-core';
import { LDAP_PLUGIN } from '@syndew/oluso-plugin-ldap';

pluginService.registerAdminPlugin(LDAP_PLUGIN);
pluginService.markInitialized();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
