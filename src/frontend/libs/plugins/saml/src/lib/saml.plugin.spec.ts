import { Route } from '@angular/router';
import { NavItem } from '@syndew/oluso-ui-core';
import { SAML_PLUGIN } from './saml.plugin';

/** Every full route path the plugin declares, e.g. '/saml/dashboard' or '/saml/items/:id'. */
function routePaths(routes: Route[], parent = ''): string[] {
  return routes.flatMap((route) => {
    const path = [parent, route.path].filter((p) => p !== undefined && p !== '').join('/');
    return [path, ...routePaths(route.children ?? [], path)];
  });
}

function navPaths(items: NavItem[]): string[] {
  return items
    .flatMap((item) => [item.path, ...navPaths(item.children ?? [])])
    .filter((p): p is string => !!p);
}

function declares(paths: string[], link: string): boolean {
  const segments = link.replace(/^\//, '').split('/');
  return paths.some((path) => {
    const parts = path.split('/');
    return (
      parts.length === segments.length &&
      parts.every((part, i) => part.startsWith(':') || part === segments[i])
    );
  });
}

function leafRoutes(routes: Route[]): Route[] {
  return routes.flatMap((route) => (route.children ? leafRoutes(route.children) : [route]));
}

describe('SAML_PLUGIN', () => {
  const routes = SAML_PLUGIN.routes ?? [];
  const paths = routePaths(routes);

  it('registers its pages under /saml', () => {
    expect(routes.map((r) => r.path)).toEqual(['saml']);
  });

  it('only links to routes it declares', () => {
    const links = navPaths(SAML_PLUGIN.navItems ?? []);
    expect(links.length).toBeGreaterThan(0);
    for (const link of links) {
      expect(declares(paths, link), `nav link ${link}`).toBe(true);
    }
  });

  it('gives every route a page or a redirect', () => {
    for (const route of leafRoutes(routes)) {
      expect(
        route.component ?? route.loadComponent ?? route.redirectTo,
        `route '${route.path}'`,
      ).toBeDefined();
    }
  });

  it('lazy-loads a component for every lazy route', async () => {
    for (const route of leafRoutes(routes)) {
      if (!route.loadComponent) continue;
      const component = await route.loadComponent();
      expect(typeof component, `route '${route.path}'`).toBe('function');
    }
  });

  it('gives each settingsTabs entry a unique id and a component', () => {
    const tabs = SAML_PLUGIN.settingsTabs ?? [];
    expect(tabs.length).toBeGreaterThan(0);
    expect(new Set(tabs.map((t) => t.id)).size).toBe(tabs.length);
    for (const tab of tabs) {
      expect(tab.component, `tab ${tab.id}`).toBeDefined();
    }
  });
});
