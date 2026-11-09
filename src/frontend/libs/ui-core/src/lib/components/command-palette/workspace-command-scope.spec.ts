import type { WorkspaceApp, WorkspaceNavItem } from '../../models/workspace-plugin.model';
import { resolveCommandApp } from './command-app-area';
import { workspaceAppForNavItem, workspaceCommandAreas } from './workspace-command-scope';

const app = (id: string, categories: string[], defaultRoute: string, type: WorkspaceApp['type'] = 'internal'): WorkspaceApp => ({
  id,
  name: id,
  icon: 'squares-2x2',
  categories,
  defaultRoute,
  type,
});

const item = (id: string, path: string, category?: string): WorkspaceNavItem => ({ id, label: id, path, icon: 'x', category });

describe('workspace command scope', () => {
  const apps = [
    app('home', ['dashboard', 'self-service'], '/dashboard'),
    app('sales', ['sales'], '/sales/contacts'),
    app('hr', ['hr', 'team'], '/leave'),
    { ...app('admin', [], ''), type: 'admin' as const, externalUrl: '/admin' },
  ];
  const navItems = [
    item('profile', '/profile'),
    item('contacts', '/sales/contacts', 'sales'),
    item('vehicles', '/sales/objects/vehicle', 'sales'),
    item('payroll', '/payroll', 'hr'),
    item('team', '/team', 'team'),
    item('orphan', '/calendar', 'tools'),
  ];

  it('gives a nav item to the app whose sidebar shows it', () => {
    expect(workspaceAppForNavItem(navItems[0], apps)).toBe('home');
    expect(workspaceAppForNavItem(navItems[3], apps)).toBe('hr');
    expect(workspaceAppForNavItem(navItems[4], apps)).toBe('hr');
    expect(workspaceAppForNavItem(navItems[5], apps)).toBeNull();
  });

  it('maps URLs to the internal app that owns them', () => {
    const areas = workspaceCommandAreas(apps, navItems);
    expect(areas.map((a) => a.app)).toEqual(['home', 'sales', 'hr']);
    expect(resolveCommandApp('/payroll/runs/3', areas)).toBe('hr');
    expect(resolveCommandApp('/sales/objects/vehicle/42', areas)).toBe('sales');
    expect(resolveCommandApp('/sales/settings/objects', areas)).toBe('sales');
    expect(resolveCommandApp('/profile', areas)).toBe('home');
    expect(resolveCommandApp('/calendar', areas)).toBeNull();
  });
});
