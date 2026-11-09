import type { NavItem } from '../../models/nav-item.model';
import type { CommandAppArea } from './command-app-area';

/** The app id of an admin area: the first segment of its path, e.g. 'emr' for '/emr'. */
export function adminCommandAppId(path: string): string | null {
  return path.split(/[?#]/)[0].split('/').find((s) => s.length > 0) ?? null;
}

function paths(item: NavItem): string[] {
  return [item.path, ...(item.children ?? []).flatMap(paths)];
}

/**
 * The admin portal's app areas: one per top-level section a plugin adds, named after its path's first segment, so a
 * module's admin commands declare the same id as its workspace app where the two share a path ('emr', 'accounting').
 * Items a plugin merges into a core menu stay part of the core admin, which belongs to no app.
 */
export function adminCommandAreas(pluginNavItems: readonly NavItem[]): CommandAppArea[] {
  const areas = new Map<string, string[]>();
  for (const item of pluginNavItems) {
    if (item.mergeIntoPath) continue;
    const app = adminCommandAppId(item.path);
    if (!app) continue;
    areas.set(app, [...(areas.get(app) ?? []), ...paths(item)]);
  }
  return Array.from(areas, ([app, appPaths]) => ({ app, paths: appPaths }));
}
