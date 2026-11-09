# @syndew/oluso-workspace-ui

The Oluso workspace portal as an Angular library: the signed-in home for an organization's people. It has a layout with an app switcher, route guards for managers and for module access, conversation threads, and a replaceable landing page.

Mount it in your own Angular app and add pages with workspace plugins.

## Install

```bash
npm install @syndew/oluso-workspace-ui
```

Peer dependencies: `@angular/core` and `@angular/common` (Angular 21), `@syndew/oluso-ui-core` and `@syndew/oluso-auth`.

## Use

```ts
import { provideRouter } from '@angular/router';
import { WORKSPACE_ROUTES, provideWorkspaceAuth } from '@syndew/oluso-workspace-ui';

providers: [provideRouter(WORKSPACE_ROUTES), provideWorkspaceAuth()]
```

At runtime the portal reads `window.__OLUSO_CONFIG__` (from a `config.js` next to `index.html`) for the server URL. The `workspace-standalone` app in the repository is a complete example.

## Links

- [Oluso on GitHub](https://github.com/SyndewTech/Oluso)
- [Hosting the portals](https://github.com/SyndewTech/Oluso/blob/main/docs/docker.md)
