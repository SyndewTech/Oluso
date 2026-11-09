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
  return first && ['crm', 'emr', 'hr'].includes(first) ? first : null;
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
      registry.register([nav('crm-contacts', { app: 'crm' }), nav('emr-patients', { app: 'emr' }), nav('theme', { app: null })]);
    });

    it('lists only the current app\'s commands and the global ones', async () => {
      await goTo('/crm/contacts');
      expect(ids(registry.filter(''))).toEqual(['crm-contacts', 'theme']);

      await goTo('/emr/patients/42');
      expect(ids(registry.filter(''))).toEqual(['emr-patients', 'theme']);
    });

    it('keeps other apps\' commands out of searches too', async () => {
      await goTo('/crm/contacts');
      expect(ids(registry.filter('patients'))).toEqual([]);
    });

    it('lists only global commands outside every app', async () => {
      await goTo('/profile');
      expect(ids(registry.filter(''))).toEqual(['theme']);
    });

    it('scopes dynamic searches the same way', async () => {
      registry.register({ kind: 'dynamic', id: 'emr-search', group: 'Patients', label: 'Search patients', app: 'emr', search: () => of([]) });
      registry.register({ kind: 'dynamic', id: 'records', group: 'Records', label: 'Search records', app: null, search: () => of([]) });

      await goTo('/crm/contacts');
      expect(ids(registry.getDynamicGroups())).toEqual(['records']);
      await goTo('/emr');
      expect(ids(registry.getDynamicGroups())).toEqual(['emr-search', 'records']);
    });

    it('gives the register call\'s app to commands without their own', async () => {
      registry.register([nav('hr-leave'), nav('hr-shared', { app: null })], { app: 'hr' });

      await goTo('/hr');
      expect(ids(registry.filter('hr'))).toEqual(['hr-leave', 'hr-shared']);
      await goTo('/crm');
      expect(ids(registry.filter('hr'))).toEqual(['hr-shared']);
    });
  });

  describe('commands registered without an app', () => {
    it('are global when registered before any app is known, as at bootstrap', async () => {
      registry.register(nav('legacy'));
      registry.setAppResolver(byFirstSegment);

      await goTo('/crm');
      expect(ids(registry.filter('legacy'))).toEqual(['legacy']);
      await goTo('/emr');
      expect(ids(registry.filter('legacy'))).toEqual(['legacy']);
    });

    it('belong to the app the user is in when they are registered', async () => {
      registry.setAppResolver(byFirstSegment);
      await goTo('/crm/contacts');
      registry.register(nav('crm-page-action'));

      expect(registry.commands().find((c) => c.id === 'crm-page-action')?.app).toBe('crm');
      await goTo('/emr');
      expect(ids(registry.filter('crm-page-action'))).toEqual([]);
    });

    it('belong to the app being opened when a page registers while it loads', async () => {
      registry.setAppResolver(byFirstSegment);
      await goTo('/crm');
      let registeredApp: string | null | undefined;
      const sub = router.events.subscribe((event) => {
        if (event instanceof ActivationStart && registeredApp === undefined) {
          registry.register(nav('emr-page'));
          registeredApp = registry.commands().find((c) => c.id === 'emr-page')?.app;
        }
      });

      await goTo('/emr/patients');
      sub.unsubscribe();
      expect(registeredApp).toBe('emr');
    });

    it('are global when registered on a page no app claims', async () => {
      registry.setAppResolver(byFirstSegment);
      await goTo('/profile');
      registry.register(nav('profile-action'));

      await goTo('/crm');
      expect(ids(registry.filter('profile-action'))).toEqual(['profile-action']);
    });
  });

  describe('switching apps', () => {
    const opened: string[] = [];
    beforeEach(() => {
      opened.length = 0;
      registry.setAppResolver(byFirstSegment);
      registry.register([nav('crm-contacts', { app: 'crm' }), nav('emr-patients', { app: 'emr' })]);
      registry.setApps([
        { id: 'crm', label: 'Pulse', description: 'Contacts and deals', open: () => opened.push('crm') },
        { id: 'emr', label: 'Clinic', open: () => opened.push('emr') },
      ]);
    });

    it('offers a global "Switch app…" that asks the palette to list the apps', async () => {
      await goTo('/crm');
      const switchApp = registry.filter('').find((c) => c.id === SWITCH_APP_COMMAND_ID) as ActionCommand;
      expect(switchApp.group).toBe(APP_SWITCH_GROUP);
      expect(switchApp.keepOpen).toBe(true);

      registry.execute(switchApp);
      expect(registry.requestedQuery()).toBe('>app ');
    });

    it('lists the apps, not their commands, for a >app query, the current one last', async () => {
      await goTo('/crm');
      const apps = registry.filter('>app');
      expect(apps.map((c) => c.label)).toEqual(['Open Clinic', 'Pulse (current)']);
      expect(apps.every((c) => c.group === APP_SWITCH_GROUP)).toBe(true);

      registry.execute(apps[0]);
      expect(opened).toEqual(['emr']);
    });

    it('narrows the app list by the text after the prefix', async () => {
      await goTo('/emr');
      expect(registry.filter('>app deals').map((c) => c.id)).toEqual(['app-crm']);
    });

    it('offers matching apps in a normal search without their commands', async () => {
      await goTo('/emr');
      expect(ids(registry.filter('pulse'))).toEqual(['app-crm']);
      expect(ids(registry.filter('contacts'))).toEqual(['app-crm']);
    });

    it('hides "Switch app…" when there is nowhere else to go', async () => {
      registry.setApps([{ id: 'crm', label: 'Pulse', open: () => undefined }]);
      await goTo('/crm');
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
      registry.execute(nav('x', { route: '/emr/patients' }));
      expect(navigateByUrl).toHaveBeenCalledWith('/emr/patients');
    });
  });
});
