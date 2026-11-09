# @syndew/oluso-plugin-telemetry

Telemetry for the Oluso admin portal: a dashboard, plus the server's logs and traces.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs telemetry (`Syndew.Oluso.Telemetry.OpenTelemetry`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-telemetry
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core` and `@syndew/oluso-admin-ui`.

## Use

Register the plugin with the `PluginService` before the admin portal starts:

```ts
import { PluginService } from '@syndew/oluso-ui-core';
import { TELEMETRY_PLUGIN } from '@syndew/oluso-plugin-telemetry';

pluginService.registerAdminPlugin(TELEMETRY_PLUGIN);
pluginService.markInitialized();
```

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
