import type { WorkspaceApp, WorkspaceNavItem } from '../../models/workspace-plugin.model';
import type { CommandAppArea } from './command-app-area';

/** Nav items without a category are self-service ones, as the workspace sidebar treats them. */
const DEFAULT_CATEGORY = 'self-service';

/**
 * The workspace app a nav item belongs to: the first app (in the order given) whose categories hold the item's,
 * which is the app whose sidebar shows it. Null when no app shows it.
 */
export function workspaceAppForNavItem(item: Pick<WorkspaceNavItem, 'category'>, apps: readonly WorkspaceApp[]): string | null {
  const category = item.category || DEFAULT_CATEGORY;
  return apps.find((app) => app.categories?.includes(category))?.id ?? null;
}

/** Each internal workspace app's area: its default route and the paths of the nav items its sidebar shows. */
export function workspaceCommandAreas(apps: readonly WorkspaceApp[], navItems: readonly WorkspaceNavItem[]): CommandAppArea[] {
  return apps
    .filter((app) => !app.type || app.type === 'internal')
    .map((app) => ({
      app: app.id,
      paths: [
        ...(app.defaultRoute ? [app.defaultRoute] : []),
        ...navItems.filter((item) => workspaceAppForNavItem(item, apps) === app.id).map((item) => item.path),
      ],
    }));
}
