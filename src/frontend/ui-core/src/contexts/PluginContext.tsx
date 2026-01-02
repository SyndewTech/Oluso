import { createContext, useContext, useMemo, useSyncExternalStore, type ReactNode } from 'react';
import { getRegistry } from '../registry/ExtensionRegistry';
import type {
  AdminUIPlugin,
  AdminNavItem,
  AdminRoute,
  PageSlot,
  DashboardWidget,
  TenantSettingsTab,
  SettingsTab,
  UserDetailTab,
  FilterOptions,
} from '../types/plugin';
import type { FieldRenderer, ActionButton, DetailSection } from '../types/renderers';

/**
 * Plugin context value
 */
interface PluginContextValue {
  /** All registered plugins */
  plugins: AdminUIPlugin[];
  /** Get filtered navigation items */
  getNavigation: (options?: FilterOptions) => AdminNavItem[];
  /** Get filtered routes */
  getRoutes: (options?: FilterOptions) => AdminRoute[];
  /** Get page slots for a path */
  getPageSlots: (path: string, position: PageSlot['position'], options?: FilterOptions) => PageSlot[];
  /** Get dashboard widgets */
  getDashboardWidgets: (options?: FilterOptions) => DashboardWidget[];
  /** Get user detail tabs */
  getUserDetailTabs: (options?: FilterOptions) => UserDetailTab[];
  /** Get tenant settings tabs */
  getTenantSettingsTabs: (options?: FilterOptions) => TenantSettingsTab[];
  /** Get settings tabs (SuperAdmin global settings) */
  getSettingsTabs: (options?: FilterOptions) => SettingsTab[];
  /** Get field renderer by type */
  getFieldRenderer: (type: string) => FieldRenderer | undefined;
  /** Get all field renderers */
  getAllFieldRenderers: () => FieldRenderer[];
  /** Get action buttons for entity type */
  getActionButtons: (entityType: string, options?: FilterOptions) => ActionButton[];
  /** Get detail sections for entity type */
  getDetailSections: (entityType: string, options?: FilterOptions) => DetailSection[];
  /** Check if plugin is registered */
  hasPlugin: (id: string) => boolean;
}

const PluginContext = createContext<PluginContextValue | null>(null);

/**
 * Hook to access plugin functionality
 */
export function usePlugins(): PluginContextValue {
  const context = useContext(PluginContext);
  if (!context) {
    throw new Error('usePlugins must be used within a PluginProvider');
  }
  return context;
}

/**
 * Hook to subscribe to registry changes
 * Returns the version number which is a stable primitive value
 */
function useRegistrySubscription() {
  const registry = getRegistry();
  return useSyncExternalStore(
    callback => registry.subscribe(callback),
    () => registry.getVersion(),
    () => registry.getVersion()
  );
}

interface PluginProviderProps {
  children: ReactNode;
  /** Initial plugins to register */
  plugins?: AdminUIPlugin[];
}

/**
 * Provider that gives access to plugin registry
 */
export function PluginProvider({ children, plugins: initialPlugins = [] }: PluginProviderProps) {
  const registry = getRegistry();

  // Register initial plugins on mount
  useMemo(() => {
    initialPlugins.forEach(plugin => {
      if (!registry.hasPlugin(plugin.id)) {
        registry.registerPlugin(plugin);
      }
    });
  }, []);

  // Subscribe to registry changes
  useRegistrySubscription();

  const value = useMemo<PluginContextValue>(() => ({
    plugins: registry.getPlugins(),
    getNavigation: (options) => registry.getNavigation(options),
    getRoutes: (options) => registry.getRoutes(options),
    getPageSlots: (path, position, options) => registry.getPageSlots(path, position, options),
    getDashboardWidgets: (options) => registry.getDashboardWidgets(options),
    getUserDetailTabs: (options) => registry.getUserDetailTabs(options),
    getTenantSettingsTabs: (options) => registry.getTenantSettingsTabs(options),
    getSettingsTabs: (options) => registry.getSettingsTabs(options),
    getFieldRenderer: (type) => registry.getFieldRenderer(type),
    getAllFieldRenderers: () => registry.getAllFieldRenderers(),
    getActionButtons: (entityType, options) => registry.getActionButtons(entityType, options),
    getDetailSections: (entityType, options) => registry.getDetailSections(entityType, options),
    hasPlugin: (id) => registry.hasPlugin(id),
  }), [registry]);

  return (
    <PluginContext.Provider value={value}>
      {children}
    </PluginContext.Provider>
  );
}

// ============ Convenience Hooks ============

/**
 * Hook to get filtered navigation
 */
export function useNavigation(options: FilterOptions = {}) {
  const { getNavigation } = usePlugins();
  return useMemo(() => getNavigation(options), [getNavigation, options]);
}

/**
 * Hook to get filtered routes
 */
export function useRoutes(options: FilterOptions = {}) {
  const { getRoutes } = usePlugins();
  return useMemo(() => getRoutes(options), [getRoutes, options]);
}

/**
 * Hook to get page slots
 */
export function usePageSlots(path: string, position: PageSlot['position'], options: FilterOptions = {}) {
  const { getPageSlots } = usePlugins();
  return useMemo(() => getPageSlots(path, position, options), [getPageSlots, path, position, options]);
}

/**
 * Hook to get dashboard widgets
 */
export function useDashboardWidgets(options: FilterOptions = {}) {
  const { getDashboardWidgets } = usePlugins();
  return useMemo(() => getDashboardWidgets(options), [getDashboardWidgets, options]);
}

/**
 * Hook to get user detail tabs
 */
export function useUserDetailTabs(options: FilterOptions = {}) {
  const { getUserDetailTabs } = usePlugins();
  return useMemo(() => getUserDetailTabs(options), [getUserDetailTabs, options]);
}

/**
 * Hook to get tenant settings tabs
 */
export function useTenantSettingsTabs(options: FilterOptions = {}) {
  const { getTenantSettingsTabs } = usePlugins();
  return useMemo(() => getTenantSettingsTabs(options), [getTenantSettingsTabs, options]);
}

/**
 * Hook to get settings tabs (for system/global settings like SAML IdP)
 */
export function useSettingsTabs(options: FilterOptions = {}) {
  const { getSettingsTabs } = usePlugins();
  return useMemo(() => getSettingsTabs(options), [getSettingsTabs, options]);
}

/**
 * Hook to get action buttons
 */
export function useActionButtons(entityType: string, options: FilterOptions = {}) {
  const { getActionButtons } = usePlugins();
  return useMemo(() => getActionButtons(entityType, options), [getActionButtons, entityType, options]);
}

/**
 * Hook to get detail sections
 */
export function useDetailSections(entityType: string, options: FilterOptions = {}) {
  const { getDetailSections } = usePlugins();
  return useMemo(() => getDetailSections(entityType, options), [getDetailSections, entityType, options]);
}

export default PluginContext;
