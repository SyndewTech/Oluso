# @syndew/oluso-plugin-fido2

Passkeys (FIDO2 / WebAuthn) for the Oluso portals:

- **admin portal** (`FIDO2_PLUGIN`): a passkeys dashboard, every registered credential, and a passkeys tab on each user
- **account portal** (`FIDO2_ACCOUNT_PLUGIN`): a page where users register, rename and remove their own passkeys

It needs the FIDO2 module (`Syndew.Oluso.Enterprise.Fido2`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-fido2
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core` and `@syndew/oluso-admin-ui`.

## Use

```ts
import { PluginService } from '@syndew/oluso-ui-core';
import { FIDO2_PLUGIN, FIDO2_ACCOUNT_PLUGIN } from '@syndew/oluso-plugin-fido2';

// admin portal
pluginService.registerAdminPlugin(FIDO2_PLUGIN);
// account portal
pluginService.registerAccountPlugin(FIDO2_ACCOUNT_PLUGIN);
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
