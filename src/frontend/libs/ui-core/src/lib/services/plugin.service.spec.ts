import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PluginService } from './plugin.service';
import { PermissionsService } from './permissions.service';
import { FeatureService } from './feature.service';
import { ModuleSeatService } from './module-seat.service';
import type { WorkspaceNavItem, WorkspacePlugin } from '../models/workspace-plugin.model';

describe('PluginService workspace navigation', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: PermissionsService, useValue: { hasPermission: () => true } },
        { provide: FeatureService, useValue: { isLoaded: () => true, hasFeature: () => true } },
        { provide: ModuleSeatService, useValue: { isLoaded: () => true, hasSeat: () => true } },
      ],
    });
    return TestBed.inject(PluginService);
  }

  const item = (id: string, order: number): WorkspaceNavItem => ({ id, label: id, path: `/${id}`, icon: 'squares-2x2', order });

  it('replaces only the named plugin\'s navigation', () => {
    const plugins = setup();
    const directory: WorkspacePlugin = { name: 'Directory', version: '1', navigation: [item('contacts', 10)] };
    const teams: WorkspacePlugin = { name: 'Teams', version: '1', navigation: [item('people', 5)] };
    plugins.registerWorkspacePlugins([directory, teams]);

    plugins.setWorkspaceNavigation('Directory', [item('contacts', 10), item('vehicles', 21)]);

    expect(plugins.workspaceNavItems().map((i) => i.id)).toEqual(['people', 'contacts', 'vehicles']);
  });

  it('ignores a name no workspace plugin has', () => {
    const plugins = setup();
    plugins.registerWorkspacePlugin({ name: 'Directory', version: '1', navigation: [item('contacts', 10)] });
    const before = plugins.workspacePlugins();

    plugins.setWorkspaceNavigation('Unknown', [item('other', 1)]);

    expect(plugins.workspacePlugins()).toBe(before);
  });
});
