import { Injectable, signal, computed, Injector, runInInjectionContext, inject, Type } from '@angular/core';
import { Route, Router } from '@angular/router';
import { Plugin } from '../models/plugin.type';
import { AdminPlugin } from '../models/admin-plugin.model';
import { AccountPlugin } from '../models/account-plugin.model';
import {
  WorkspacePlugin,
  WorkspaceNavItem,
  WorkspaceQuickAction,
  WorkspaceStatItem,
  WorkspaceDashboardWidget,
  WorkspaceApp,
} from '../models/workspace-plugin.model';
import { NavItem } from '../models/nav-item.model';
import { DashboardWidget } from '../models/dashboard-widget.model';
import { TabDefinition } from '../models/tab-definition.model';
import { PageSlot } from '../models/page-slot.model';
import { ActionButton } from '../models/action-button.model';
import { DetailSection } from '../models/detail-section.model';
import { PermissionsService } from './permissions.service';
import { FeatureService } from './feature.service';
import { ModuleSeatService } from './module-seat.service';

/**
 * Plugin Service
 *
 * Central service for managing plugins in the application.
 * Uses Angular signals for reactive state management.
 *
 * This service mirrors the functionality of the React PluginContext
 * from the existing frontend codebase.
 */
@Injectable({
  providedIn: 'root',
})
export class PluginService {
  private readonly router = inject(Router);
  private readonly permissionsService = inject(PermissionsService);
  private readonly featureService = inject(FeatureService);
  private readonly seatService = inject(ModuleSeatService);
  private readonly _adminPlugins = signal<AdminPlugin[]>([]);
  private readonly _accountPlugins = signal<AccountPlugin[]>([]);
  private readonly _workspacePlugins = signal<WorkspacePlugin[]>([]);
  private readonly _initialized = signal<boolean>(false);

  /**
   * Filter items by permission and feature fields.
   * If permission is set, checks PermissionsService.hasPermission().
   * If feature is set, checks FeatureService.hasFeature().
   * Items without permission/feature pass through unfiltered.
   *
   * <param name="strict" /> When true, items with a feature gate are
   * HIDDEN until features have loaded. Used for dashboard widgets / quick
   * actions / stat items that fire API calls on mount — being permissive
   * there causes a brief flash where the widget renders, hits 403, then
   * disappears once features arrive. For nav items the permissive default
   * is fine (they don't fire APIs and benefit from rendering immediately).
   */
  private filterByAccess<T extends { permission?: string; feature?: string; requiresSeat?: string }>(
    items: T[],
    strict = false,
  ): T[] {
    return items.filter(item => {
      // Gates run coarsest-first so a tenant turning a feature off
      // short-circuits before we even consider user-level perms /
      // seats. Concretely: admin disables a module for the whole tenant
      // → every contribution from it disappears immediately, regardless of
      // leftover permissions or seats still attached to users.
      //   1. feature   — tenant-level "module licensed"
      //   2. seat      — per-user "this user is allowed to use it"
      //   3. permission — per-action "this user can do X inside it"
      // (1) and (2) honour an "is loaded?" gate so we don't flash the
      // item before features/seats arrive from /me.
      if (item.feature) {
        if (!this.featureService.isLoaded()) return !strict;
        if (!this.featureService.hasFeature(item.feature)) return false;
      }
      if (item.requiresSeat) {
        if (!this.seatService.isLoaded()) return !strict;
        if (!this.seatService.hasSeat(item.requiresSeat)) return false;
      }
      if (item.permission && !this.permissionsService.hasPermission(item.permission)) return false;
      return true;
    });
  }

  /** Read-only signals for plugin access */
  readonly adminPlugins = this._adminPlugins.asReadonly();
  readonly accountPlugins = this._accountPlugins.asReadonly();
  readonly workspacePlugins = this._workspacePlugins.asReadonly();
  readonly initialized = this._initialized.asReadonly();

  /** Computed routes from all admin plugins */
  readonly adminRoutes = computed<Route[]>(() =>
    this._adminPlugins().flatMap((plugin) => plugin.routes ?? [])
  );

  /** Computed routes from all account plugins */
  readonly accountRoutes = computed<Route[]>(() =>
    this._accountPlugins().flatMap((plugin) => plugin.routes ?? [])
  );

  /** Computed routes from all workspace plugins */
  readonly workspaceRoutes = computed<Route[]>(() =>
    this._workspacePlugins().flatMap((plugin) => plugin.routes ?? [])
  );

  /** Computed navigation items from admin plugins */
  readonly adminNavItems = computed<NavItem[]>(() =>
    this._adminPlugins()
      .flatMap((plugin) => plugin.navItems ?? [])
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
  );

  /** Computed navigation items from account plugins */
  readonly accountNavItems = computed<NavItem[]>(() =>
    this._accountPlugins()
      .flatMap((plugin) => plugin.navItems ?? [])
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
  );

  /** Computed navigation items from workspace plugins */
  readonly workspaceNavItems = computed<WorkspaceNavItem[]>(() =>
    this._workspacePlugins()
      .flatMap((plugin) => plugin.navigation ?? plugin.navItems ?? [])
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
  );

  /** Computed quick actions from workspace plugins (filtered by permission + feature) */
  readonly workspaceQuickActions = computed<WorkspaceQuickAction[]>(() =>
    this.filterByAccess(
      this._workspacePlugins()
        .flatMap((plugin) => plugin.quickActions ?? []),
      true,
    )
  );

  /** Computed stat items from workspace plugins (filtered by permission + feature) */
  readonly workspaceStatItems = computed<WorkspaceStatItem[]>(() =>
    this.filterByAccess(
      this._workspacePlugins()
        .flatMap((plugin) => plugin.statItems ?? [])
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100)),
      true,
    )
  );

  /** Computed dashboard widgets from admin plugins (filtered by permission + feature) */
  readonly adminDashboardWidgets = computed<DashboardWidget[]>(() =>
    this.filterByAccess(
      this._adminPlugins()
        .flatMap((plugin) => plugin.dashboardWidgets ?? [])
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100)),
      true,
    )
  );

  /** Computed dashboard widgets from workspace plugins (filtered by permission + feature) */
  readonly workspaceDashboardWidgets = computed<WorkspaceDashboardWidget[]>(() =>
    this.filterByAccess(
      this._workspacePlugins()
        .flatMap((plugin) => plugin.dashboardWidgets ?? [])
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100)),
      true,
    )
  );

  /**
   * Computed status widget components from workspace plugins.
   * Rendered by the shell in a persistent overlay region; each widget
   * component owns its own visibility and behaviour.
   */
  readonly workspaceStatusWidgets = computed<Type<unknown>[]>(() =>
    this._workspacePlugins().flatMap((plugin) => plugin.statusWidgets ?? [])
  );

  /** Computed top-bar widget components from workspace plugins; each owns its own visibility. */
  readonly workspaceHeaderWidgets = computed<Type<unknown>[]>(() =>
    this._workspacePlugins().flatMap((plugin) => plugin.headerWidgets ?? [])
  );

  /** Computed workspace apps from plugins that define an app (filtered by permission + feature) */
  readonly workspaceApps = computed<WorkspaceApp[]>(() =>
    this.filterByAccess(
      this._workspacePlugins()
        .filter((plugin) => plugin.app)
        .map((plugin) => plugin.app!)
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
    )
  );

  /** Computed user detail tabs from admin plugins (filtered by permission + feature) */
  readonly userDetailTabs = computed<TabDefinition[]>(() =>
    this.filterByAccess(
      this._adminPlugins()
        .flatMap((plugin) => plugin.userDetailTabs ?? [])
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
    )
  );

  /** Computed tenant settings tabs from admin plugins (filtered by permission + feature) */
  readonly tenantSettingsTabs = computed<TabDefinition[]>(() =>
    this.filterByAccess(
      this._adminPlugins()
        .flatMap((plugin) => plugin.tenantSettingsTabs ?? [])
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
    )
  );

  /** Computed settings tabs from admin plugins (filtered by permission + feature) */
  readonly settingsTabs = computed<TabDefinition[]>(() =>
    this.filterByAccess(
      this._adminPlugins()
        .flatMap((plugin) => plugin.settingsTabs ?? [])
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
    )
  );

  /** Computed action buttons from admin plugins (filtered by permission + feature) */
  readonly actionButtons = computed<ActionButton[]>(() =>
    this.filterByAccess(
      this._adminPlugins()
        .flatMap((plugin) => plugin.actionButtons ?? [])
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
    )
  );

  /** Computed detail sections from admin plugins (filtered by permission + feature) */
  readonly detailSections = computed<DetailSection[]>(() =>
    this.filterByAccess(
      this._adminPlugins()
        .flatMap((plugin) => plugin.detailSections ?? [])
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
    )
  );

  constructor(private injector: Injector) {}

  /**
   * Register an admin plugin
   */
  registerAdminPlugin(plugin: AdminPlugin): void {
    this._adminPlugins.update((plugins) => [...plugins, plugin]);
    this.addPluginRoutes(plugin.routes);
    this.initializePlugin(plugin);
  }

  /**
   * Register an account plugin
   */
  registerAccountPlugin(plugin: AccountPlugin): void {
    this._accountPlugins.update((plugins) => [...plugins, plugin]);
    this.addPluginRoutes(plugin.routes);
    this.initializePlugin(plugin);
  }

  /**
   * Register a workspace plugin
   */
  registerWorkspacePlugin(plugin: WorkspacePlugin): void {
    this._workspacePlugins.update((plugins) => [...plugins, plugin]);
    this.addPluginRoutes(plugin.routes);
    this.initializePlugin(plugin);
  }

  /**
   * Replaces a registered workspace plugin's sidebar navigation. Some navigation is known only at runtime, such as
   * entries for objects a tenant defines for itself, so a plugin registers its fixed items and sets the full list
   * once it has loaded the rest. Does nothing when no workspace plugin has the name.
   */
  setWorkspaceNavigation(pluginName: string, navigation: WorkspaceNavItem[]): void {
    this._workspacePlugins.update((plugins) =>
      plugins.some((plugin) => plugin.name === pluginName)
        ? plugins.map((plugin) => (plugin.name === pluginName ? { ...plugin, navigation } : plugin))
        : plugins
    );
  }

  /**
   * Register multiple admin plugins at once
   */
  registerAdminPlugins(plugins: AdminPlugin[]): void {
    plugins.forEach((plugin) => this.registerAdminPlugin(plugin));
  }

  /**
   * Register multiple account plugins at once
   */
  registerAccountPlugins(plugins: AccountPlugin[]): void {
    plugins.forEach((plugin) => this.registerAccountPlugin(plugin));
  }

  /**
   * Register multiple workspace plugins at once
   */
  registerWorkspacePlugins(plugins: WorkspacePlugin[]): void {
    plugins.forEach((plugin) => this.registerWorkspacePlugin(plugin));
  }

  /**
   * Get page slots for a specific page
   */
  getPageSlots(pageId: string, slotId?: string): PageSlot[] {
    const allSlots = [
      ...this._adminPlugins().flatMap((p) => p.pageSlots ?? []),
      ...this._accountPlugins().flatMap((p) => p.pageSlots ?? []),
      ...this._workspacePlugins().flatMap((p) => p.pageSlots ?? []),
    ];

    return this.filterByAccess(
      allSlots
        .filter((slot) => slot.pageId === pageId && (!slotId || slot.slotId === slotId))
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
    );
  }

  /**
   * Get detail sections for a specific entity type
   */
  getDetailSections(entityType: string): DetailSection[] {
    return this.detailSections().filter((section) => section.entityType === entityType);
  }

  /**
   * Get action buttons for a specific action context
   */
  getActionButtons(actionContext: string): ActionButton[] {
    return this.actionButtons().filter((button) => button.action.startsWith(actionContext));
  }

  /**
   * Check if a plugin is registered
   */
  isPluginRegistered(pluginName: string): boolean {
    return [
      ...this._adminPlugins(),
      ...this._accountPlugins(),
      ...this._workspacePlugins(),
    ].some((p) => p.name === pluginName);
  }

  /**
   * Get a plugin by name
   */
  getPlugin(pluginName: string): Plugin | undefined {
    return [
      ...this._adminPlugins(),
      ...this._accountPlugins(),
      ...this._workspacePlugins(),
    ].find((p) => p.name === pluginName);
  }

  /**
   * Mark the plugin system as initialized
   */
  markInitialized(): void {
    this._initialized.set(true);
  }

  /**
   * Initialize a plugin
   */
  private initializePlugin(plugin: Plugin): void {
    if (plugin.initialize) {
      runInInjectionContext(this.injector, () => {
        const result = plugin.initialize!();
        if (result instanceof Promise) {
          result.catch((error) => {
            console.error(`Failed to initialize plugin ${plugin.name}:`, error);
          });
        }
      });
    }
  }

  /**
   * Add plugin routes to the router configuration.
   * Plugin routes are added as children of the main layout route.
   */
  private addPluginRoutes(routes?: Route[]): void {
    if (!routes || routes.length === 0) {
      return;
    }

    const currentConfig = this.router.config;
    // Find the main layout route (first route with children)
    const mainLayoutRoute = currentConfig.find((route) => route.children);

    if (mainLayoutRoute && mainLayoutRoute.children) {
      // Add plugin routes to the children
      mainLayoutRoute.children.push(...routes);
      // Reset the router config to apply changes
      this.router.resetConfig(currentConfig);
    }
  }
}
