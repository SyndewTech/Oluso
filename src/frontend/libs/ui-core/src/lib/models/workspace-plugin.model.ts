import { Route } from '@angular/router';
import { Type } from '@angular/core';
import { BasePlugin } from './base-plugin.model';
import { DashboardWidget } from './dashboard-widget.model';
import { PageSlot } from './page-slot.model';
import type { AnyCommand } from '../components/command-palette/command-registry.service';

/**
 * Workspace app definition for the app switcher
 * Each app represents a distinct functional area
 */
export interface WorkspaceApp {
  /** Unique identifier (the module key) */
  id: string;
  /** Display name (shown in the app switcher) */
  name: string;
  /** Icon identifier */
  icon: string;
  /** Short description for tooltip */
  description?: string;
  /** Nav item categories this app owns (for internal apps) */
  categories?: string[];
  /** Route when app is selected (for internal apps) */
  defaultRoute?: string;
  /** External URL (for external apps) */
  externalUrl?: string;
  /** App type: internal (workspace), admin, or external */
  type?: 'internal' | 'admin' | 'external';
  /** Sort order in switcher */
  order?: number;
  /** Feature flag requirement — tenant-level "module is licensed" gate. */
  feature?: string;
  /**
   * Per-user module-seat requirement. Even when `feature` is true (tenant
   * has the module), the user only sees the app if they hold a seat for
   * this key. Mirrors the backend's RequireModuleSeat gate.
   */
  requiresSeat?: string;
  /** Permission requirement */
  permission?: string;
  /** Manager-only visibility */
  managerOnly?: boolean;
  /** Color theme for the app */
  color?: 'slate' | 'blue' | 'green' | 'purple' | 'orange' | 'red' | 'indigo' | 'teal' | 'pink';
}

/**
 * Navigation item for workspace sidebar
 */
export interface WorkspaceNavItem {
  id: string;
  label: string;
  path: string;
  /** Optional query params merged into the route when the nav link is clicked. */
  queryParams?: Record<string, string | number | boolean | null>;
  icon: string;
  category?: 'dashboard' | 'self-service' | 'team' | 'approvals' | 'tools' | 'settings' | string;
  /** Group label for sidebar section headers (items with the same group are rendered together) */
  group?: string;
  order?: number;
  permission?: string;
  anyPermission?: string[];
  feature?: string;
  /** Per-user module-seat requirement (see {@link WorkspaceApp.requiresSeat}). */
  requiresSeat?: string;
  managerOnly?: boolean;
  badge?: number | (() => number);
  children?: WorkspaceNavItem[];
}

/**
 * Quick action for workspace dashboard
 */
export interface WorkspaceQuickAction {
  id: string;
  label: string;
  description?: string;
  icon: string;
  path: string;
  permission?: string;
  anyPermission?: string[];
  feature?: string;
  /** Per-user module-seat requirement (see {@link WorkspaceApp.requiresSeat}). */
  requiresSeat?: string;
  managerOnly?: boolean;
  color?: 'primary' | 'secondary' | 'success' | 'warning' | 'danger';
}

/**
 * Stat item for workspace dashboard
 */
export interface WorkspaceStatItem {
  id: string;
  label: string;
  value: number | string | (() => number | string);
  icon: string;
  href?: string;
  order?: number;
  permission?: string;
  feature?: string;
  /** Per-user module-seat requirement (see {@link WorkspaceApp.requiresSeat}). */
  requiresSeat?: string;
  managerOnly?: boolean;
  color?: 'default' | 'primary' | 'success' | 'warning' | 'danger';
}

/**
 * Dashboard widget for workspace (extends base dashboard widget)
 */
export interface WorkspaceDashboardWidget extends DashboardWidget {
  slot?: 'welcome' | 'stats' | 'actions' | 'content';
  category?: 'personal' | 'team' | 'approvals' | 'analytics';
  managerOnly?: boolean;
}

/**
 * Workspace plugin definition
 */
export interface WorkspacePlugin extends BasePlugin {
  routes?: Route[];
  navigation?: WorkspaceNavItem[];
  quickActions?: WorkspaceQuickAction[];
  statItems?: WorkspaceStatItem[];
  dashboardWidgets?: WorkspaceDashboardWidget[];
  pageSlots?: PageSlot[];
  /**
   * Persistent status widget components rendered by the workspace shell in a
   * fixed overlay region (bottom-right, above the routed page). Widgets live
   * for the lifetime of the shell — they survive route changes — so plugins
   * can surface long-running background activity (uploads, recordings, sync
   * progress). Each component decides its own visibility (render nothing when
   * there is nothing to show).
   */
  statusWidgets?: Type<unknown>[];
  /**
   * Compact components rendered by the workspace shell in its top bar, before help and the Action Center (e.g. a
   * picker for the context the plugin's pages work in). Like status widgets they live as long as the shell, and each
   * decides its own visibility, typically showing only on its own app's routes.
   */
  headerWidgets?: Type<unknown>[];
  /** If provided, plugin registers as a distinct app in the app switcher */
  app?: WorkspaceApp;
  /**
   * The plugin's own ⌘K palette commands (actions, flows, dynamic search). The workspace shell registers them in
   * the plugin's app (a command may name another app, or `null` for every app), and only while the user can open
   * that app; navigation items already become "Go to …" commands without being listed here.
   */
  commands?: AnyCommand[];
  /** @deprecated Use navigation instead */
  navItems?: WorkspaceNavItem[];
}
