import { Injectable, inject, computed } from '@angular/core';
import { PluginService, NavItem } from '@syndew/oluso-ui-core';
import { AccountAuthService } from './account-auth.service';

/**
 * Account-specific plugin service.
 * Filters account plugin nav items by tenant feature flags and, where an item
 * declares `requires`, by the per-user facts modules add to /api/account/me.
 */
@Injectable({
  providedIn: 'root',
})
export class AccountPluginService {
  private readonly pluginService = inject(PluginService);
  private readonly authService = inject(AccountAuthService);

  /**
   * Core account nav items that are always shown (profile, security, etc.)
   */
  private readonly coreNavItems: NavItem[] = [
    { id: 'profile', path: '/profile', label: 'Profile', icon: 'user', order: 0 },
    { id: 'security', path: '/security', label: 'Security', icon: 'lock', order: 10 },
    { id: 'sessions', path: '/sessions', label: 'Sessions', icon: 'computer-desktop', order: 20 },
    { id: 'connected-apps', path: '/connected-apps', label: 'Connected Apps', icon: 'squares-2x2', order: 30 },
    { id: 'auth-requests', path: '/authentication-requests', label: 'Auth Requests', icon: 'shield-check', order: 40 },
  ];

  /**
   * All navigation items: core items + filtered plugin items.
   * Plugin items with a `feature` property are only shown if the tenant has that
   * feature; items with `requires` only when that user-level flag is true.
   * Children are also filtered recursively.
   */
  /** The account's own items, the same for everyone. */
  readonly coreNavigationItems: readonly NavItem[] = this.coreNavItems;

  readonly navigationItems = computed(() => {
    const pluginItems = this.filterByFeature(this.pluginService.accountNavItems());
    return [...this.coreNavItems, ...pluginItems];
  });

  /**
   * Only the plugin nav items (without core), filtered by feature.
   */
  readonly pluginNavigationItems = computed(() => {
    return this.filterByFeature(this.pluginService.accountNavItems());
  });

  private filterByFeature(items: NavItem[], parentFeature?: string): NavItem[] {
    return items
      .filter((item) => {
        // Every plugin item is owned by a module: it must name its feature (or sit
        // under a parent that does) and the tenant must have it. An item without
        // one is hidden rather than shown to every tenant.
        const feature = item.feature ?? parentFeature;
        if (!feature || !this.authService.hasFeature(feature)) {
          return false;
        }
        if (item.requires && !this.authService.hasExtensionFlag(item.requires)) {
          return false;
        }
        return true;
      })
      .map((item) => {
        if (item.children && item.children.length > 0) {
          return { ...item, children: this.filterByFeature(item.children, item.feature ?? parentFeature) };
        }
        return item;
      });
  }
}
