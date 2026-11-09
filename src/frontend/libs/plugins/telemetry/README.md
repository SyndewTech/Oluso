# @syndew/oluso-plugin-telemetry

Telemetry for the Oluso admin portal: a dashboard, plus the server's logs and traces.

It plugs into the Oluso admin portal (`@syndew/oluso-admin-ui`) and needs telemetry (`Syndew.Oluso.Telemetry.OpenTelemetry`) on the Oluso server.

## Install

```bash
npm install @syndew/oluso-plugin-telemetry
```

Peer dependencies (npm installs them with the package): `@angular/common`, `@angular/core`, `@angular/forms`, `@angular/router`, `rxjs`, `@syndew/oluso-admin-ui`, `@syndew/oluso-ui-core`.

## Use

Register the plugin with the `PluginService` when the admin app starts:

```ts
import { inject, provideAppInitializer } from '@angular/core';
import { PluginService } from '@syndew/oluso-ui-core';
import { TELEMETRY_PLUGIN } from '@syndew/oluso-plugin-telemetry';

// in the admin app's providers
provideAppInitializer(() => {
  const plugins = inject(PluginService);
  plugins.registerAdminPlugin(TELEMETRY_PLUGIN);
  plugins.markInitialized();
})
```

Register every plugin before `markInitialized()`.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
