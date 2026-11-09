# @syndew/oluso-plugin-saml

SAML 2.0 administration for the Oluso admin portal: a SAML dashboard, the service providers that sign in through Oluso (with their details and certificates), and a settings tab for the identity provider.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs the SAML module (`Syndew.Oluso.Enterprise.Saml`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-saml
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core` and `@syndew/oluso-admin-ui`.

## Use

Register the plugin with the `PluginService` before the admin portal starts:

```ts
import { PluginService } from '@syndew/oluso-ui-core';
import { SAML_PLUGIN } from '@syndew/oluso-plugin-saml';

pluginService.registerAdminPlugin(SAML_PLUGIN);
pluginService.markInitialized();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
