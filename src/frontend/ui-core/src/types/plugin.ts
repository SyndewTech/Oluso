import type { ComponentType, LazyExoticComponent } from 'react';

/**
 * Icon component type - compatible with Heroicons
 */
export type IconComponent = ComponentType<{ className?: string }>;

/**
 * Navigation item from a plugin
 */
export interface AdminNavItem {
  id: string;
  name: string;
  href: string;
  icon: IconComponent;
  /** Feature flag required to show this item */
  feature?: string;
  /** Navigation group */
  group?: 'main' | 'resources' | 'security' | 'billing' | 'settings';
  /** Sort order within group */
  order?: number;
  /** Only visible to super admins */
  superAdminOnly?: boolean;
  /** Permission required to view this item (e.g., "users.read") */
  permission?: string;
  /** Any of these permissions grants access (OR logic) */
  anyPermission?: string[];
  /** Child navigation items */
  children?: AdminNavItem[];
  /** Badge to show (e.g., count) */
  badge?: string | number;
}

/**
 * Route from a plugin
 */
export interface AdminRoute {
  path: string;
  component: ComponentType<unknown> | LazyExoticComponent<ComponentType<unknown>>;
  /** Feature flag required */
  feature?: string;
  /** Only accessible by super admins */
  superAdminOnly?: boolean;
  /** Permission required to access this route */
  permission?: string;
  /** Any of these permissions grants access (OR logic) */
  anyPermission?: string[];
  /** Requires authentication (default true) */
  requiresAuth?: boolean;
  /** Nested routes */
  children?: AdminRoute[];
  /** Layout to use */
  layout?: 'default' | 'fullscreen' | 'minimal';
}

/**
 * Page slot for injecting content
 */
export interface PageSlot {
  id: string;
  /** Pages where this slot appears (glob pattern) */
  pages: string | string[];
  /** Slot position */
  position: 'header' | 'header-actions' | 'footer' | 'sidebar' | 'before-content' | 'after-content';
  /** Component to render */
  component: ComponentType<PageSlotProps>;
  /** Sort order */
  order?: number;
}

export interface PageSlotProps {
  /** Current page path */
  pagePath: string;
  /** Page-specific context data */
  context?: Record<string, unknown>;
}

/**
 * Dashboard widget
 */
export interface DashboardWidget {
  id: string;
  name: string;
  /** Widget size */
  size: 'small' | 'medium' | 'large' | 'full';
  /** Component to render */
  component: ComponentType<DashboardWidgetProps>;
  /** Feature flag required */
  feature?: string;
  /** Sort order */
  order?: number;
}

export interface DashboardWidgetProps {
  /** Refresh trigger */
  onRefresh?: () => void;
}

/**
 * Detail page tab for extensible detail pages (e.g., User Details)
 */
export interface DetailPageTab<TData = unknown> {
  /** Unique tab identifier */
  id: string;
  /** Tab label */
  label: string;
  /** Icon for the tab */
  icon?: IconComponent;
  /** Component to render in the tab panel */
  component: ComponentType<DetailPageTabProps<TData>>;
  /** Feature flag required */
  feature?: string;
  /** Sort order (built-in tabs use increments of 100: 100, 200, 300...) */
  order?: number;
  /** Only visible to super admins */
  superAdminOnly?: boolean;
  /** Badge to show (e.g., count) - can be static or computed from data */
  badge?: string | number | ((data: TData) => string | number | undefined);
}

export interface DetailPageTabProps<TData = unknown> {
  /** The entity data being displayed */
  data: TData;
  /** Callback to refresh the data */
  onRefresh?: () => void;
  /** Whether the tab content is currently visible */
  isActive?: boolean;
}

/**
 * User detail page specific tab
 */
export type UserDetailTab = DetailPageTab<UserDetailData>;

/**
 * User detail page tab props
 */
export interface UserDetailTabProps extends DetailPageTabProps<UserDetailData> {
  /** User ID for convenience */
  userId: string;
}

/**
 * User detail data structure
 */
export interface UserDetailData {
  id: string;
  userName: string;
  email: string;
  emailConfirmed: boolean;
  phoneNumber?: string;
  phoneNumberConfirmed: boolean;
  firstName?: string;
  lastName?: string;
  displayName?: string;
  profilePictureUrl?: string;
  isActive: boolean;
  twoFactorEnabled: boolean;
  lockoutEnabled: boolean;
  lockoutEnd?: string;
  accessFailedCount?: number;
  roles: string[];
  claims?: Array<{ type: string; value: string }>;
  externalLogins?: Array<{ provider: string; providerKey: string; displayName?: string }>;
  externalId?: string;
  externalProvider?: string;
  createdAt: string;
  updatedAt?: string;
  lastLoginAt?: string;
}

/**
 * Tenant settings tab for extending the Tenant Settings page
 */
export interface TenantSettingsTab {
  /** Unique tab identifier */
  id: string;
  /** Tab label */
  label: string;
  /** Icon for the tab */
  icon?: IconComponent;
  /** Component to render in the tab panel */
  component: ComponentType<TenantSettingsTabProps>;
  /** Feature flag required */
  feature?: string;
  /** Sort order (built-in tabs use increments of 100: 100, 200, 300...) */
  order?: number;
  /** Only visible to super admins */
  superAdminOnly?: boolean;
}

export interface TenantSettingsTabProps {
  /** Tenant ID */
  tenantId: string;
  /** Tenant data */
  tenant: TenantData;
  /** Callback when settings are saved */
  onSave?: () => void;
  /** Callback to notify parent of unsaved changes */
  onHasChanges?: (hasChanges: boolean) => void;
  /** Whether the tab content is currently visible */
  isActive?: boolean;
}

/**
 * Settings tab for extending the Settings page (SuperAdmin only).
 * Unlike TenantSettingsTab, these are global/system settings.
 */
export interface SettingsTab {
  /** Unique tab identifier */
  id: string;
  /** Tab label */
  label: string;
  /** Icon for the tab */
  icon?: IconComponent;
  /** Component to render in the tab panel */
  component: ComponentType<SettingsTabProps>;
  /** Feature flag required */
  feature?: string;
  /** Sort order (built-in tabs use increments of 100: 100, 200, 300...) */
  order?: number;
}

export interface SettingsTabProps {
  /** Callback when settings are saved */
  onSave?: () => void;
  /** Callback to notify parent of unsaved changes */
  onHasChanges?: (hasChanges: boolean) => void;
  /** Whether the tab content is currently visible */
  isActive?: boolean;
}

/**
 * Tenant data structure
 */
export interface TenantData {
  id: string;
  name: string;
  displayName?: string;
  identifier: string;
  description?: string;
  customDomain?: string;
  enabled: boolean;
  createdAt: string;
  updatedAt?: string;
}

/**
 * Plugin definition
 */
export interface AdminUIPlugin {
  /** Unique plugin identifier */
  id: string;
  /** Display name */
  name: string;
  /** Plugin version */
  version: string;
  /** Navigation items to add */
  navigation?: AdminNavItem[];
  /** Routes to add */
  routes?: AdminRoute[];
  /** Page slots to register */
  slots?: PageSlot[];
  /** Dashboard widgets */
  widgets?: DashboardWidget[];
  /** User detail page tabs */
  userDetailTabs?: UserDetailTab[];
  /** Tenant settings page tabs */
  tenantSettingsTabs?: TenantSettingsTab[];
  /** Settings page tabs (SuperAdmin only - for system/global settings) */
  settingsTabs?: SettingsTab[];
  /** Initialization function */
  initialize?: () => void | Promise<void>;
  /** Cleanup function */
  destroy?: () => void;
  /** Required features for this plugin */
  requiredFeatures?: string[];
  /** Plugin dependencies */
  dependencies?: string[];
}

/**
 * Plugin registration options
 */
export interface PluginRegistrationOptions {
  /** Override existing plugin with same ID */
  override?: boolean;
}

/**
 * Filter options for getting navigation/routes
 */
export interface FilterOptions {
  /** Current user is super admin */
  isSuperAdmin?: boolean;
  /** Feature check function */
  hasFeature?: (key: string) => boolean;
  /** Permission check function */
  hasPermission?: (permission: string) => boolean;
  /** Check if user has any of the specified permissions */
  hasAnyPermission?: (permissions: string[]) => boolean;
  /** Current page path (for slots) */
  currentPath?: string;
}

// ============ Account UI Plugin Types ============

/**
 * Navigation item for Account UI
 */
export interface AccountNavItem {
  /** Display label */
  label: string;
  /** Route path */
  path: string;
  /** Icon component */
  icon?: IconComponent;
  /** Badge text (e.g., "New", "3") */
  badge?: string;
  /** Navigation group */
  group?: 'main' | 'settings' | 'billing';
  /** Sort order within group */
  order?: number;
}

/**
 * Route for Account UI
 */
export interface AccountRoute {
  /** Route path */
  path: string;
  /** Component to render */
  component: ComponentType<unknown> | LazyExoticComponent<ComponentType<unknown>>;
  /** Required permission */
  permission?: string;
  /** Feature flag required */
  feature?: string;
}

/**
 * Plugin for extending the Account UI with additional pages
 */
export interface AccountUIPlugin {
  /** Unique identifier for the plugin */
  id: string;
  /** Display name of the plugin */
  name: string;
  /** Plugin version */
  version?: string;
  /** Navigation items to add to the sidebar */
  navigation?: AccountNavItem[];
  /** Routes to register */
  routes?: AccountRoute[];
  /** Optional: Feature flag that must be enabled */
  featureFlag?: string;
  /** Initialization function */
  initialize?: () => void | Promise<void>;
  /** Cleanup function */
  destroy?: () => void;
}

/**
 * Options passed to plugin factory functions
 */
export interface AccountPluginOptions {
  /** API client (axios instance or similar) */
  apiClient: unknown;
  /** Base URL for API calls */
  apiBaseUrl?: string;
}

/**
 * Helper to create an Account plugin
 */
export function defineAccountPlugin(plugin: AccountUIPlugin): AccountUIPlugin {
  return plugin;
}

/**
 * Helper to create an Admin plugin
 */
export function defineAdminPlugin(plugin: AdminUIPlugin): AdminUIPlugin {
  return plugin;
}

// ============ Workspace Portal Plugin Types ============

/**
 * Navigation item for Workspace Portal
 */
export interface WorkspaceNavItem {
  /** Unique identifier */
  id: string;
  /** Display label */
  label: string;
  /** Route path */
  path: string;
  /** Icon component */
  icon?: IconComponent;
  /** Badge text (e.g., "New", "3") or function that returns badge */
  badge?: string | number | (() => string | number | undefined);
  /** Navigation group */
  group?: 'dashboard' | 'self-service' | 'team' | 'approvals' | 'tools' | 'settings';
  /** Sort order within group */
  order?: number;
  /** Required permission */
  permission?: string;
  /** Any of these permissions grants access (OR logic) */
  anyPermission?: string[];
  /** Feature flag required */
  feature?: string;
  /** Only visible to managers */
  managerOnly?: boolean;
  /** Child navigation items */
  children?: WorkspaceNavItem[];
}

/**
 * Route for Workspace Portal
 */
export interface WorkspaceRoute {
  /** Route path */
  path: string;
  /** Component to render */
  component: ComponentType<unknown> | LazyExoticComponent<ComponentType<unknown>>;
  /** Required permission */
  permission?: string;
  /** Any of these permissions grants access (OR logic) */
  anyPermission?: string[];
  /** Feature flag required */
  feature?: string;
  /** Only accessible by managers */
  managerOnly?: boolean;
  /** Requires authentication (default true) */
  requiresAuth?: boolean;
  /** Layout to use */
  layout?: 'default' | 'fullscreen' | 'minimal';
}

/**
 * Dashboard widget slot - determines where the widget appears on the dashboard
 */
export type WorkspaceWidgetSlot = 'welcome' | 'stats' | 'actions' | 'content';

/**
 * Dashboard widget for Workspace Portal
 */
export interface WorkspaceDashboardWidget {
  /** Unique identifier - use same ID to replace a core widget */
  id: string;
  /** Display name */
  name: string;
  /** Widget size (for content slot) */
  size?: 'small' | 'medium' | 'large' | 'full';
  /** Component to render */
  component: ComponentType<WorkspaceWidgetProps>;
  /** Dashboard slot where this widget appears */
  slot?: WorkspaceWidgetSlot;
  /** Feature flag required */
  feature?: string;
  /** Required permission */
  permission?: string;
  /** Only visible to managers */
  managerOnly?: boolean;
  /** Sort order within slot (lower = first, core widgets use 0-100) */
  order?: number;
  /** Widget category for grouping within content slot */
  category?: 'personal' | 'team' | 'approvals' | 'analytics';
  /** If true, hides the default card wrapper (widget provides its own styling) */
  noWrapper?: boolean;
}

export interface WorkspaceWidgetProps {
  /** Current user context */
  user?: WorkspaceUser;
  /** Whether the current user is a manager */
  isManager?: boolean;
  /** Callback to refresh widget data */
  onRefresh?: () => void;
}

/**
 * Stat item for the stats slot (simpler than full widget)
 */
export interface WorkspaceStatItem {
  /** Unique identifier */
  id: string;
  /** Stat label */
  label: string;
  /** Stat value (number or string) */
  value: string | number | (() => string | number);
  /** Icon component */
  icon?: IconComponent;
  /** Link to navigate on click */
  href?: string;
  /** Feature flag required */
  feature?: string;
  /** Required permission */
  permission?: string;
  /** Only visible to managers */
  managerOnly?: boolean;
  /** Sort order (lower = first) */
  order?: number;
  /** Color theme */
  color?: 'default' | 'primary' | 'success' | 'warning' | 'danger';
}

/**
 * User context in Workspace Portal
 */
export interface WorkspaceUser {
  /** User ID */
  id: string;
  /** Username */
  userName: string;
  /** Email */
  email: string;
  /** Display name */
  displayName?: string;
  /** First name */
  firstName?: string;
  /** Last name */
  lastName?: string;
  /** Profile picture URL */
  profilePictureUrl?: string;
  /** Employee ID (if linked) */
  employeeId?: string;
  /** Is this user a manager */
  isManager: boolean;
  /** User's roles */
  roles: string[];
  /** User's permissions */
  permissions: string[];
  /** Current tenant ID */
  tenantId: string;
}

/**
 * Approval request types supported
 */
export type ApprovalRequestType = 'leave' | 'expense' | 'profile_change' | 'document' | 'custom';

/**
 * Quick action for the workspace dashboard
 */
export interface WorkspaceQuickAction {
  /** Unique identifier */
  id: string;
  /** Display label */
  label: string;
  /** Icon component */
  icon: IconComponent;
  /** Action handler or path to navigate */
  action: string | (() => void | Promise<void>);
  /** Description text */
  description?: string;
  /** Feature flag required */
  feature?: string;
  /** Required permission */
  permission?: string;
  /** Color theme */
  color?: 'primary' | 'secondary' | 'success' | 'warning' | 'danger';
}

/**
 * Plugin for extending the Workspace Portal
 */
export interface WorkspaceUIPlugin {
  /** Unique identifier for the plugin */
  id: string;
  /** Display name of the plugin */
  name: string;
  /** Plugin version */
  version: string;
  /** Plugin description */
  description?: string;
  /** Icon for the plugin (shown in module list) */
  icon?: IconComponent;
  /** Navigation items to add to the sidebar */
  navigation?: WorkspaceNavItem[];
  /** Routes to register */
  routes?: WorkspaceRoute[];
  /** Dashboard widgets (can target any slot: welcome, stats, actions, content) */
  widgets?: WorkspaceDashboardWidget[];
  /** Stat items for the welcome/stats area */
  statItems?: WorkspaceStatItem[];
  /** Quick actions for dashboard */
  quickActions?: WorkspaceQuickAction[];
  /** Approval request types this plugin handles */
  approvalTypes?: ApprovalRequestType[];
  /** Required features for this plugin */
  requiredFeatures?: string[];
  /** Plugin dependencies */
  dependencies?: string[];
  /** Initialization function */
  initialize?: () => void | Promise<void>;
  /** Cleanup function */
  destroy?: () => void;
}

/**
 * Options passed to workspace plugin factory functions
 */
export interface WorkspacePluginOptions {
  /** API client (axios instance) */
  apiClient: unknown;
  /** Base URL for API calls */
  apiBaseUrl?: string;
  /** Current user */
  user?: WorkspaceUser;
}

/**
 * Filter options for Workspace navigation/routes
 */
export interface WorkspaceFilterOptions {
  /** Current user is a manager */
  isManager?: boolean;
  /** Feature check function */
  hasFeature?: (key: string) => boolean;
  /** Permission check function */
  hasPermission?: (permission: string) => boolean;
  /** Check if user has any of the specified permissions */
  hasAnyPermission?: (permissions: string[]) => boolean;
  /** Current page path (for filtering) */
  currentPath?: string;
}

/**
 * Helper to create a Workspace plugin
 */
export function defineWorkspacePlugin(plugin: WorkspaceUIPlugin): WorkspaceUIPlugin {
  return plugin;
}
