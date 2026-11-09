# @syndew/oluso-plugin-scim

SCIM 2.0 provisioning for the Oluso admin portal: the SCIM clients that provision users into Oluso, their attribute mappings, and the outbound providers Oluso provisions users to.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs the SCIM module (`Syndew.Oluso.Enterprise.Scim`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-scim
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core` and `@syndew/oluso-admin-ui`.

## Use

Register the plugin with the `PluginService` before the admin portal starts:

```ts
import { PluginService } from '@syndew/oluso-ui-core';
import { SCIM_PLUGIN } from '@syndew/oluso-plugin-scim';

pluginService.registerAdminPlugin(SCIM_PLUGIN);
pluginService.markInitialized();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
