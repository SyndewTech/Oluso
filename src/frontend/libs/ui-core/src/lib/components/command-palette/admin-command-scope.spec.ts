import type { NavItem } from '../../models/nav-item.model';
import { resolveCommandApp } from './command-app-area';
import { adminCommandAppId, adminCommandAreas } from './admin-command-scope';

describe('admin command scope', () => {
  const pluginNav: NavItem[] = [
    { label: 'Inventory', path: '/inventory', children: [{ label: 'Items', path: '/inventory/items' }] },
    { label: 'Projects', path: '/projects', children: [{ label: 'Boards', path: '/projects/boards' }] },
    { label: 'LDAP', path: '/settings/ldap', mergeIntoPath: '/settings' },
  ];

  it('names an area after its first path segment', () => {
    expect(adminCommandAppId('/inventory/items?x=1')).toBe('inventory');
    expect(adminCommandAppId('/')).toBeNull();
  });

  it('gives each plugin section an area and leaves merged items to the core admin', () => {
    const areas = adminCommandAreas(pluginNav);
    expect(areas.map((a) => a.app)).toEqual(['inventory', 'projects']);
    expect(resolveCommandApp('/inventory/items/9', areas)).toBe('inventory');
    expect(resolveCommandApp('/projects/tasks', areas)).toBe('projects');
    expect(resolveCommandApp('/settings/ldap', areas)).toBeNull();
    expect(resolveCommandApp('/users', areas)).toBeNull();
  });
});
