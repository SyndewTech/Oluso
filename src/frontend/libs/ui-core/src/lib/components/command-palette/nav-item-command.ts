import type { NavigationCommand } from './command-registry.service';

/** The parts of a sidebar entry a "Go to …" command is made from. */
export interface CommandNavItem {
  id: string;
  label: string;
  path: string;
  icon?: string;
  queryParams?: Record<string, unknown>;
  permission?: string;
}

/** The group shells put "Go to …" commands for nav items in. */
export const NAVIGATION_COMMAND_GROUP = 'Navigation';

/**
 * The "Go to …" command for a sidebar entry. Shells make one per nav item, and a module that registers its own
 * command for an entry uses this too, so both land on the same id and group and the palette lists it once.
 */
export function navItemCommand(item: CommandNavItem, app: string | null): NavigationCommand {
  return {
    kind: 'navigation',
    id: `nav-${item.id}`,
    label: `Go to ${item.label}`,
    group: NAVIGATION_COMMAND_GROUP,
    icon: item.icon,
    route: item.path,
    queryParams: item.queryParams,
    permission: item.permission,
    app,
  };
}
