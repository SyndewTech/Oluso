import { Injectable, inject, signal, computed, effect } from '@angular/core';
import { Router } from '@angular/router';
import { PluginService, WorkspaceApp, WorkspaceNavItem } from '@oluso/ui-core';
import { WorkspaceAuthService } from './workspace-auth.service';
import { WorkspaceAppsConfigService } from './workspace-apps-config.service';

const STORAGE_KEY = 'workspace_selected_app';

/**
 * Built-in Home app - always available
 */
const HOME_APP: WorkspaceApp = {
  id: 'home',
  name: 'Home',
  icon: 'home',
  description: 'Dashboard & Profile',
  categories: ['dashboard', 'self-service'],
  defaultRoute: '/dashboard',
  type: 'internal',
  order: 0,
  color: 'slate',
};

/**
 * Admin app - available to users with admin permissions
 */
const ADMIN_APP: WorkspaceApp = {
  id: 'admin',
  name: 'Admin',
  icon: 'cog',
  description: 'System Administration',
  type: 'admin',
  externalUrl: '/admin',
  order: 900,
  color: 'indigo',
  permission: 'admin.access',
};

/**
 * App Switcher Service
 *
 * Manages the app switcher panel state, available apps, and app-filtered navigation.
 * Persists the selected app and panel state to localStorage.
 */
@Injectable({
  providedIn: 'root',
})
export class AppSwitcherService {
  private readonly router = inject(Router);
  private readonly pluginService = inject(PluginService);
  private readonly authService = inject(WorkspaceAuthService);
  private readonly appsConfig = inject(WorkspaceAppsConfigService);

  // State signals
  private readonly _selectedAppId = signal<string>(this.loadSelectedAppId());
  private readonly _isPanelOpen = signal<boolean>(false);

  // Public readonly signals
  readonly selectedAppId = this._selectedAppId.asReadonly();
  readonly isPanelOpen = this._isPanelOpen.asReadonly();

  /**
   * All available apps grouped by type
   */
  readonly availableApps = computed<WorkspaceApp[]>(() => {
    const pluginApps = this.pluginService.workspaceApps();
    const config = this.appsConfig.settings();

    // Filter plugin apps: permissions + tenant enable/disable
    const filteredPluginApps = pluginApps
      .map(app => ({ ...app, type: app.type || 'internal' as const }))
      .filter((app) => this.filterByPermissions(app))
      .filter((app) => config.builtInApps[app.id] !== false);

    // Custom external apps from tenant config
    const customApps: WorkspaceApp[] = (config.customApps || []).map(ca => ({
      id: `custom-${ca.id}`,
      name: ca.name,
      icon: ca.icon,
      description: ca.description,
      externalUrl: ca.externalUrl,
      type: 'external' as const,
      order: ca.order,
      color: (ca.color || 'slate') as WorkspaceApp['color'],
    }));

    // Add admin app if user has permission, using configured URL if available
    const adminApps: WorkspaceApp[] = [];
    if (this.filterByPermissions(ADMIN_APP)) {
      adminApps.push({
        ...ADMIN_APP,
        externalUrl: this.appsConfig.getAdminUrl(),
      });
    }

    return [HOME_APP, ...filteredPluginApps, ...customApps, ...adminApps].sort(
      (a, b) => (a.order ?? 100) - (b.order ?? 100)
    );
  });

  /**
   * Internal workspace apps only
   */
  readonly internalApps = computed<WorkspaceApp[]>(() =>
    this.availableApps().filter((app) => app.type === 'internal' || !app.type)
  );

  /**
   * Admin apps
   */
  readonly adminApps = computed<WorkspaceApp[]>(() =>
    this.availableApps().filter((app) => app.type === 'admin')
  );

  /** Resolved URL for the admin app (tenant override > host config > /admin). */
  getAdminUrl(): string {
    return this.appsConfig.getAdminUrl();
  }

  /** Resolved URL of the Account app's profile page (tenant override > host config > /account). */
  getAccountProfileUrl(): string {
    return this.appsConfig.getAccountUrl().replace(/\/$/, '') + '/profile';
  }

  /**
   * External apps
   */
  readonly externalApps = computed<WorkspaceApp[]>(() =>
    this.availableApps().filter((app) => app.type === 'external')
  );

  /**
   * Currently selected app
   */
  readonly selectedApp = computed<WorkspaceApp | undefined>(() => {
    const appId = this._selectedAppId();
    return this.internalApps().find((app) => app.id === appId) ?? HOME_APP;
  });

  /**
   * Navigation items for the currently selected app
   */
  readonly currentAppNavItems = computed<WorkspaceNavItem[]>(() => {
    const selectedApp = this.selectedApp();
    if (!selectedApp || !selectedApp.categories) return [];

    const allNavItems = this.pluginService.workspaceNavItems();
    const appCategories = selectedApp.categories;

    return allNavItems
      .filter((item) => {
        const category = item.category || 'self-service';
        return appCategories.includes(category);
      })
      .filter((item) => this.filterNavItemByPermissions(item))
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  });

  constructor() {
    // Persist selected app to localStorage whenever it changes
    effect(() => {
      const appId = this._selectedAppId();
      this.saveSelectedAppId(appId);
    });
  }

  /**
   * Select an internal app and optionally navigate to its default route
   */
  selectApp(appId: string, navigate = true): void {
    const app = this.internalApps().find((a) => a.id === appId);
    if (!app) return;

    this._selectedAppId.set(appId);
    this._isPanelOpen.set(false);

    if (navigate && app.defaultRoute) {
      this.router.navigateByUrl(app.defaultRoute);
    }
  }

  /**
   * Open an external or admin app
   */
  openExternalApp(app: WorkspaceApp): void {
    if (app.externalUrl) {
      if (app.type === 'admin') {
        // Admin apps open in same tab (same origin)
        window.location.href = app.externalUrl;
      } else {
        // External apps open in new tab
        window.open(app.externalUrl, '_blank', 'noopener,noreferrer');
      }
    }
    this._isPanelOpen.set(false);
  }

  /**
   * Toggle the app switcher panel
   */
  togglePanel(): void {
    this._isPanelOpen.update((open) => !open);
  }

  /**
   * Open the app switcher panel
   */
  openPanel(): void {
    this._isPanelOpen.set(true);
  }

  /**
   * Close the app switcher panel
   */
  closePanel(): void {
    this._isPanelOpen.set(false);
  }

  /**
   * Load selected app ID from localStorage
   */
  private loadSelectedAppId(): string {
    if (typeof window === 'undefined') return 'home';

    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored || 'home';
    } catch {
      return 'home';
    }
  }

  /**
   * Save selected app ID to localStorage
   */
  private saveSelectedAppId(appId: string): void {
    if (typeof window === 'undefined') return;

    try {
      localStorage.setItem(STORAGE_KEY, appId);
    } catch {
      // Ignore storage errors
    }
  }

  /**
   * Filter apps by user permissions, features, and manager status
   */
  private filterByPermissions(app: WorkspaceApp): boolean {
    // Gates run coarsest-first: managerOnly (role flag) → feature
    // (tenant) → requiresSeat (user) → permission (per-action). A
    // tenant turning the module's feature off should hide the app even
    // if individual users still carry leftover permissions / seats.
    if (app.managerOnly && !this.authService.isManager()) {
      return false;
    }
    if (app.feature && !this.authService.hasFeature(app.feature)) {
      return false;
    }
    if (app.requiresSeat && !this.authService.hasModuleSeat(app.requiresSeat)) {
      return false;
    }
    if (app.permission && !this.authService.hasPermission(app.permission)) {
      return false;
    }
    return true;
  }

  /**
   * Filter nav items by permissions
   */
  private filterNavItemByPermissions(item: WorkspaceNavItem): boolean {
    // Same coarsest-first order as filterByPermissions above —
    // managerOnly → feature → requiresSeat → permission.
    if (item.managerOnly && !this.authService.isManager()) {
      return false;
    }
    if (item.feature && !this.authService.hasFeature(item.feature)) {
      return false;
    }
    if (item.requiresSeat && !this.authService.hasModuleSeat(item.requiresSeat)) {
      return false;
    }
    if (item.permission && !this.authService.hasPermission(item.permission)) {
      return false;
    }
    if (item.anyPermission && !this.authService.hasAnyPermission(item.anyPermission)) {
      return false;
    }
    return true;
  }
}
