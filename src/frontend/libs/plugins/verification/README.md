# @syndew/oluso-plugin-verification

Business verification (KYB) for the Oluso admin portal: the businesses being verified and their details, a review queue for checks that need a person, and a settings tab for the verification providers.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs the verification module (`Syndew.Oluso.Enterprise.Verification`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-verification
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core` and `@syndew/oluso-admin-ui`.

## Use

Register the plugin with the `PluginService` before the admin portal starts:

```ts
import { PluginService } from '@syndew/oluso-ui-core';
import { VERIFICATION_PLUGIN } from '@syndew/oluso-plugin-verification';

pluginService.registerAdminPlugin(VERIFICATION_PLUGIN);
pluginService.markInitialized();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
