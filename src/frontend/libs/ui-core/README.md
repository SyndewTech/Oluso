# @syndew/oluso-ui-core

The shared foundation of the Oluso Angular portals (admin, workspace and account) and their plugins:

- the plugin model and `PluginService`, which admin, workspace and account plugins register with
- the `API_BASE_URL` and HTTP context tokens
- terminology, locale, currency and country services
- shared components, models and utilities

Every other `@syndew/oluso-*` Angular package builds on it.

## Install

```bash
npm install @syndew/oluso-ui-core
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21).

## Use

```ts
import { PluginService, API_BASE_URL, provideAppTitle } from '@syndew/oluso-ui-core';
```

A plugin is an `AdminPlugin`, `WorkspacePlugin` or `AccountPlugin` object with its routes, navigation items and settings tabs. Register it with `PluginService` when the app starts; see `@syndew/oluso-plugin-fido2` for a complete example.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Writing a frontend plugin](https://github.com/SyndewTech/Oluso/tree/main/plugin-templates/frontend-plugins)
- [Getting started](https://github.com/SyndewTech/Oluso/blob/main/docs/v2-quickstart.md)
