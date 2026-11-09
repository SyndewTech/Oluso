import { Injectable, inject, computed } from '@angular/core';
import {
  PluginService,
  WorkspacePlugin,
  WorkspaceNavItem,
  WorkspaceDashboardWidget,
} from '@syndew/oluso-ui-core';
import { WorkspaceAuthService } from './workspace-auth.service';

/**
 * Workspace-specific plugin service
 * Extends ui-core's PluginService with workspace-specific filtering logic
 */
@Injectable({
  providedIn: 'root',
})
export class WorkspacePluginService {
  private readonly pluginService = inject(PluginService);
  private readonly authService = inject(WorkspaceAuthService);

  readonly isInitialized = this.pluginService.initialized;

  // Filtered navigation items based on permissions, features, and manager status
  readonly navigationItems = computed(() => {
    return this.filterByPermissions(this.pluginService.workspaceNavItems());
  });

  // Filtered dashboard widgets
  readonly dashboardWidgets = computed(() => {
    return this.filterByPermissions(this.pluginService.workspaceDashboardWidgets());
  });

  // Filtered stat items
  readonly statItems = computed(() => {
    return this.filterByPermissions(this.pluginService.workspaceStatItems());
  });

  // Filtered quick actions
  readonly quickActions = computed(() => {
    return this.filterByPermissions(this.pluginService.workspaceQuickActions());
  });

  /**
   * Register a workspace plugin
   */
  registerPlugin(plugin: WorkspacePlugin): void {
    this.pluginService.registerWorkspacePlugin(plugin);
  }

  /**
   * Initialize all registered plugins
   */
  async initializePlugins(): Promise<void> {
    // Plugins are initialized on registration in ui-core's PluginService
  }

  /**
   * Mark the plugin service as initialized
   */
  markInitialized(): void {
    this.pluginService.markInitialized();
  }

  /**
   * Filter items by user permissions, features, and manager status
   */
  private filterByPermissions<
    T extends {
      permission?: string;
      anyPermission?: string[];
      feature?: string;
      requiresSeat?: string;
      managerOnly?: boolean;
    }
  >(items: T[]): T[] {
    return items.filter((item) => {
      // Coarsest-first ordering: managerOnly → feature → requiresSeat
      // → permission. A tenant disabling the feature short-circuits
      // before we look at user-level seats / permissions, so admins
      // can hide a module everywhere in one move regardless of
      // leftover user-level grants.
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
    });
  }

  /**
   * Get navigation items for a specific category
   */
  getNavigationByCategory(category: string): WorkspaceNavItem[] {
    return this.navigationItems().filter((item) => item.category === category);
  }

  /**
   * Get dashboard widgets for a specific slot
   */
  getWidgetsBySlot(slot: 'welcome' | 'stats' | 'actions' | 'content'): WorkspaceDashboardWidget[] {
    return this.dashboardWidgets().filter((widget) => widget.slot === slot);
  }
}
