/**
 * Plugin Context
 *
 * This module re-exports the unified plugin context from @oluso/ui-core.
 * All plugin registration and access should go through the shared registry.
 */

// Re-export everything from the core ui-core plugin context
export {
  PluginProvider,
  usePlugins,
  useNavigation,
  useRoutes,
  usePageSlots,
  useDashboardWidgets,
  useUserDetailTabs,
  useTenantSettingsTabs,
  useSettingsTabs,
  useActionButtons,
  useDetailSections,
} from '@oluso/ui-core';

// Re-export types for convenience
export type {
  AdminUIPlugin,
  AdminNavItem,
  AdminRoute,
  IconComponent,
  FilterOptions,
  UserDetailTab,
  UserDetailData,
  UserDetailTabProps,
  DetailPageTab,
  DetailPageTabProps,
  PageSlot,
  PageSlotProps,
  DashboardWidget,
  DashboardWidgetProps,
  SettingsTab,
  SettingsTabProps,
  TenantSettingsTab,
  TenantSettingsTabProps,
  TenantData,
} from '@oluso/ui-core';
