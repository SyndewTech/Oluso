import { resolveCommandApp, type CommandAppArea } from './command-app-area';
import { navItemCommand } from './nav-item-command';

describe('resolveCommandApp', () => {
  const areas: CommandAppArea[] = [
    { app: 'home', paths: ['/dashboard', '/profile'] },
    { app: 'sales', paths: ['/sales/contacts', '/sales/deals'] },
    { app: 'hr', paths: ['/leave', '/team', '/approvals/pending'] },
    { app: 'approval', paths: ['/approvals'] },
  ];

  it('finds the app whose path the URL starts with', () => {
    expect(resolveCommandApp('/sales/contacts/123?tab=notes', areas)).toBe('sales');
    expect(resolveCommandApp('/leave/requests', areas)).toBe('hr');
    expect(resolveCommandApp('/dashboard', areas)).toBe('home');
  });

  it('prefers the longest matching path', () => {
    expect(resolveCommandApp('/approvals/pending/7', areas)).toBe('hr');
    expect(resolveCommandApp('/approvals/history', areas)).toBe('approval');
  });

  it('matches whole segments only', () => {
    expect(resolveCommandApp('/teams', areas)).toBeNull();
  });

  it('falls back to an app sharing the first segment', () => {
    expect(resolveCommandApp('/sales/objects/vehicle', areas)).toBe('sales');
  });

  it('is null where no app claims the URL', () => {
    expect(resolveCommandApp('/search?q=x', areas)).toBeNull();
    expect(resolveCommandApp('/', areas)).toBeNull();
  });
});

describe('navItemCommand', () => {
  it('makes a "Go to" command keyed by the nav item', () => {
    expect(navItemCommand({ id: 'crm-contacts', label: 'Contacts', path: '/sales/contacts', icon: 'users', permission: 'crm.contacts.read' }, 'sales')).toEqual({
      kind: 'navigation',
      id: 'nav-crm-contacts',
      label: 'Go to Contacts',
      group: 'Navigation',
      icon: 'users',
      route: '/sales/contacts',
      queryParams: undefined,
      permission: 'crm.contacts.read',
      app: 'sales',
    });
  });
});
