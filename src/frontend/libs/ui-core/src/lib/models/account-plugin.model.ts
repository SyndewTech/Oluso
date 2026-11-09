import { Route } from '@angular/router';
import { BasePlugin } from './base-plugin.model';
import { NavItem } from './nav-item.model';
import { PageSlot } from './page-slot.model';
import { TabDefinition } from './tab-definition.model';

export interface AccountPlugin extends BasePlugin {
  routes?: Route[];
  navItems?: NavItem[];
  pageSlots?: PageSlot[];
  profileTabs?: TabDefinition[];
  securityTabs?: TabDefinition[];
  /**
   * Where a signed-in user this plugin serves lands when they didn't ask for a page. The first registered plugin
   * whose `feature` the tenant has and whose `requires` flag the account carries wins; otherwise the profile page.
   */
  home?: AccountPluginHome;
}

export interface AccountPluginHome {
  path: string;
  /** Tenant feature the home needs. */
  feature?: string;
  /** Account extension flag the home needs, as "{module}.{flag}" (e.g. a module's own users only). */
  requires?: string;
}
