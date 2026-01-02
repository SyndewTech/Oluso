// ============ Components ============
export * from './components';

// ============ Contexts ============
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
} from './contexts/PluginContext';

// ============ Theme ============
export { ThemeProvider, useTheme, type ThemeConfig } from './theme/ThemeProvider';

// ============ Registry ============
export {
  getRegistry,
  registerPlugin,
  registerFieldRenderer,
  registerCellRenderer,
  registerActionButtons,
  registerDetailSection,
} from './registry/ExtensionRegistry';

// ============ Types ============
export type {
  // Admin Plugin types
  AdminUIPlugin,
  AdminNavItem,
  AdminRoute,
  PageSlot,
  PageSlotProps,
  DashboardWidget,
  DashboardWidgetProps,
  FilterOptions,
  PluginRegistrationOptions,
  IconComponent,
  // Detail page tab types
  DetailPageTab,
  DetailPageTabProps,
  UserDetailTab,
  UserDetailTabProps,
  UserDetailData,
  // Tenant settings tab types
  TenantSettingsTab,
  TenantSettingsTabProps,
  TenantData,
  // Settings tab types (SuperAdmin global settings)
  SettingsTab,
  SettingsTabProps,
  // Account Plugin types
  AccountUIPlugin,
  AccountNavItem,
  AccountRoute,
  AccountPluginOptions,
} from './types/plugin';

// Plugin helpers
export { defineAccountPlugin, defineAdminPlugin } from './types/plugin';

export type {
  // Renderer types
  FieldRenderer,
  FieldRendererProps,
  CellRenderer,
  CellRendererProps,
  ActionButton,
  DetailSection,
  DetailSectionProps,
} from './types/renderers';
