import { TestBed } from '@angular/core/testing';
import { ActivationStart, Router, provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { of } from 'rxjs';
import {
  APP_SWITCH_GROUP,
  CommandRegistryService,
  SWITCH_APP_COMMAND_ID,
  type ActionCommand,
  type AnyCommand,
  type NavigationCommand,
} from './command-registry.service';

@Component({ standalone: true, template: '' })
class BlankComponent {}

function nav(id: string, change: Partial<NavigationCommand> = {}): NavigationCommand {
  return { kind: 'navigation', id, label: `Go to ${id}`, group: 'Navigation', route: `/${id}`, ...change };
}

/** Apps live under their own first segment, the way a shell's resolver maps them. */
const byFirstSegment = (url: string) => {
  const first = url.split('/').filter(Boolean)[0]?.split(/[?#]/)[0];
  return first && ['sales', 'projects', 'hr'].includes(first) ? first : null;
};

describe('CommandRegistryService', () => {
  let registry: CommandRegistryService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: BlankComponent }])],
    });
    registry = TestBed.inject(CommandRegistryService);
    router = TestBed.inject(Router);
  });

  const ids = (commands: AnyCommand[]) => commands.map((c) => c.id);

  async function goTo(url: string) {
    await router.navigateByUrl(url);
  }

  describe('app scope', () => {
    beforeEach(() => {
      registry.setAppResolver(byFirstSegment);
      registry.register([nav('sales-contacts', { app: 'sales' }), nav('projects-tasks', { app: 'projects' }), nav('theme', { app: null })]);
    });

    it('lists only the current app\'s commands and the global ones', async () => {
      await goTo('/sales/contacts');
      expect(ids(registry.filter(''))).toEqual(['sales-contacts', 'theme']);

      await goTo('/projects/tasks/42');
      expect(ids(registry.filter(''))).toEqual(['projects-tasks', 'theme']);
    });

    it('keeps other apps\' commands out of searches too', async () => {
      await goTo('/sales/contacts');
      expect(ids(registry.filter('tasks'))).toEqual([]);
    });

    it('lists only global commands outside every app', async () => {
      await goTo('/profile');
      expect(ids(registry.filter(''))).toEqual(['theme']);
    });

    it('scopes dynamic searches the same way', async () => {
      registry.register({ kind: 'dynamic', id: 'projects-search', group: 'Tasks', label: 'Search tasks', app: 'projects', search: () => of([]) });
      registry.register({ kind: 'dynamic', id: 'records', group: 'Records', label: 'Search records', app: null, search: () => of([]) });

      await goTo('/sales/contacts');
      expect(ids(registry.getDynamicGroups())).toEqual(['records']);
      await goTo('/projects');
      expect(ids(registry.getDynamicGroups())).toEqual(['projects-search', 'records']);
    });

    it('gives the register call\'s app to commands without their own', async () => {
      registry.register([nav('hr-leave'), nav('hr-shared', { app: null })], { app: 'hr' });

      await goTo('/hr');
      expect(ids(registry.filter('hr'))).toEqual(['hr-leave', 'hr-shared']);
      await goTo('/sales');
      expect(ids(registry.filter('hr'))).toEqual(['hr-shared']);
    });
  });

  describe('commands registered without an app', () => {
    it('are global when registered before any app is known, as at bootstrap', async () => {
      registry.register(nav('legacy'));
      registry.setAppResolver(byFirstSegment);

      await goTo('/sales');
      expect(ids(registry.filter('legacy'))).toEqual(['legacy']);
      await goTo('/projects');
      expect(ids(registry.filter('legacy'))).toEqual(['legacy']);
    });

    it('belong to the app the user is in when they are registered', async () => {
      registry.setAppResolver(byFirstSegment);
      await goTo('/sales/contacts');
      registry.register(nav('sales-page-action'));

      expect(registry.commands().find((c) => c.id === 'sales-page-action')?.app).toBe('sales');
      await goTo('/projects');
      expect(ids(registry.filter('sales-page-action'))).toEqual([]);
    });

    it('belong to the app being opened when a page registers while it loads', async () => {
      registry.setAppResolver(byFirstSegment);
      await goTo('/sales');
      let registeredApp: string | null | undefined;
      const sub = router.events.subscribe((event) => {
        if (event instanceof ActivationStart && registeredApp === undefined) {
          registry.register(nav('projects-page'));
          registeredApp = registry.commands().find((c) => c.id === 'projects-page')?.app;
        }
      });

      await goTo('/projects/tasks');
      sub.unsubscribe();
      expect(registeredApp).toBe('projects');
    });

    it('are global when registered on a page no app claims', async () => {
      registry.setAppResolver(byFirstSegment);
      await goTo('/profile');
      registry.register(nav('profile-action'));

      await goTo('/sales');
      expect(ids(registry.filter('profile-action'))).toEqual(['profile-action']);
    });
  });

  describe('switching apps', () => {
    const opened: string[] = [];
    beforeEach(() => {
      opened.length = 0;
      registry.setAppResolver(byFirstSegment);
      registry.register([nav('sales-contacts', { app: 'sales' }), nav('projects-tasks', { app: 'projects' })]);
      registry.setApps([
        { id: 'sales', label: 'Pipeline', description: 'Contacts and deals', open: () => opened.push('sales') },
        { id: 'projects', label: 'Projects', open: () => opened.push('projects') },
      ]);
    });

    it('offers a global "Switch app…" that asks the palette to list the apps', async () => {
      await goTo('/sales');
      const switchApp = registry.filter('').find((c) => c.id === SWITCH_APP_COMMAND_ID) as ActionCommand;
      expect(switchApp.group).toBe(APP_SWITCH_GROUP);
      expect(switchApp.keepOpen).toBe(true);

      registry.execute(switchApp);
      expect(registry.requestedQuery()).toBe('>app ');
    });

    it('lists the apps, not their commands, for a >app query, the current one last', async () => {
      await goTo('/sales');
      const apps = registry.filter('>app');
      expect(apps.map((c) => c.label)).toEqual(['Open Projects', 'Pipeline (current)']);
      expect(apps.every((c) => c.group === APP_SWITCH_GROUP)).toBe(true);

      registry.execute(apps[0]);
      expect(opened).toEqual(['projects']);
    });

    it('narrows the app list by the text after the prefix', async () => {
      await goTo('/projects');
      expect(registry.filter('>app deals').map((c) => c.id)).toEqual(['app-sales']);
    });

    it('offers matching apps in a normal search without their commands', async () => {
      await goTo('/projects');
      expect(ids(registry.filter('pipeline'))).toEqual(['app-sales']);
      expect(ids(registry.filter('contacts'))).toEqual(['app-sales']);
    });

    it('hides "Switch app…" when there is nowhere else to go', async () => {
      registry.setApps([{ id: 'sales', label: 'Pipeline', open: () => undefined }]);
      await goTo('/sales');
      expect(ids(registry.filter(''))).not.toContain(SWITCH_APP_COMMAND_ID);
    });
  });

  describe('permissions', () => {
    it('hides commands the registered checker refuses, unless the caller passes its own', () => {
      registry.register([nav('open', { app: null }), nav('locked', { app: null, permission: 'secret' })]);
      registry.setPermissionChecker((perm) => perm !== 'secret');

      expect(ids(registry.filter(''))).toEqual(['open']);
      expect(ids(registry.filter('', () => true))).toEqual(['open', 'locked']);
    });
  });

  describe('execute', () => {
    it('keeps query params on a navigation command with a path route', () => {
      const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
      registry.execute(nav('x', { route: '/sales/objects/site', queryParams: { new: '1' } }));
      expect(navigate).toHaveBeenCalledWith(['/sales/objects/site'], { queryParams: { new: '1' } });
    });

    it('navigates by URL when there are no query params', () => {
      const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      registry.execute(nav('x', { route: '/projects/tasks' }));
      expect(navigateByUrl).toHaveBeenCalledWith('/projects/tasks');
    });
  });
});
