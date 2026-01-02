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
  PluginRegistrationOptions,
} from '../types/plugin';
import type {
  FieldRenderer,
  CellRenderer,
  ActionButton,
  DetailSection,
} from '../types/renderers';

type Listener = () => void;

/**
 * Central registry for all Admin UI extensions.
 * Singleton pattern - use getRegistry() to access.
 */
class ExtensionRegistry {
  private plugins = new Map<string, AdminUIPlugin>();
  private fieldRenderers = new Map<string, FieldRenderer>();
  private cellRenderers = new Map<string, CellRenderer>();
  private actionButtons = new Map<string, ActionButton[]>();
  private detailSections: DetailSection[] = [];
  private listeners = new Set<Listener>();

  // Version number for useSyncExternalStore - must be a stable reference
  private version = 0;
  // Cached plugins array - only rebuilt when version changes
  private cachedPlugins: AdminUIPlugin[] = [];

  // ============ Plugin Management ============

  /**
   * Register a plugin
   */
  registerPlugin(plugin: AdminUIPlugin, options: PluginRegistrationOptions = {}): void {
    if (this.plugins.has(plugin.id) && !options.override) {
      console.warn(`Plugin "${plugin.id}" is already registered. Use override option to replace.`);
      return;
    }

    // Check dependencies
    if (plugin.dependencies?.length) {
      const missing = plugin.dependencies.filter(dep => !this.plugins.has(dep));
      if (missing.length > 0) {
        console.warn(`Plugin "${plugin.id}" has missing dependencies: ${missing.join(', ')}`);
      }
    }

    this.plugins.set(plugin.id, plugin);
    plugin.initialize?.();
    this.notify();
  }

  /**
   * Unregister a plugin
   */
  unregisterPlugin(pluginId: string): void {
    const plugin = this.plugins.get(pluginId);
    if (plugin) {
      plugin.destroy?.();
      this.plugins.delete(pluginId);
      this.notify();
    }
  }

  /**
   * Get all registered plugins (returns cached array for useSyncExternalStore)
   */
  getPlugins(): AdminUIPlugin[] {
    return this.cachedPlugins;
  }

  /**
   * Get the current version number for useSyncExternalStore
   */
  getVersion(): number {
    return this.version;
  }

  /**
   * Check if a plugin is registered
   */
  hasPlugin(pluginId: string): boolean {
    return this.plugins.has(pluginId);
  }

  /**
   * Get navigation items from all plugins
   */
  getNavigation(options: FilterOptions = {}): AdminNavItem[] {
    const { isSuperAdmin = false, hasFeature = () => true } = options;

    return this.getPlugins()
      .filter(plugin => this.pluginMeetsRequirements(plugin, options))
      .flatMap(plugin => plugin.navigation ?? [])
      .filter(item => {
        if (item.superAdminOnly && !isSuperAdmin) return false;
        if (item.feature && !hasFeature(item.feature)) return false;
        return true;
      })
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  }

  /**
   * Get routes from all plugins
   */
  getRoutes(options: FilterOptions = {}): AdminRoute[] {
    const { isSuperAdmin = false, hasFeature = () => true } = options;

    return this.getPlugins()
      .filter(plugin => this.pluginMeetsRequirements(plugin, options))
      .flatMap(plugin => plugin.routes ?? [])
      .filter(route => {
        if (route.superAdminOnly && !isSuperAdmin) return false;
        if (route.feature && !hasFeature(route.feature)) return false;
        return true;
      });
  }

  /**
   * Get page slots for a specific path
   */
  getPageSlots(path: string, position: PageSlot['position'], options: FilterOptions = {}): PageSlot[] {
    return this.getPlugins()
      .filter(plugin => this.pluginMeetsRequirements(plugin, options))
      .flatMap(plugin => plugin.slots ?? [])
      .filter(slot => {
        if (slot.position !== position) return false;
        const patterns = Array.isArray(slot.pages) ? slot.pages : [slot.pages];
        return patterns.some(pattern => this.matchPath(path, pattern));
      })
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  }

  /**
   * Get dashboard widgets
   */
  getDashboardWidgets(options: FilterOptions = {}): DashboardWidget[] {
    const { hasFeature = () => true } = options;

    return this.getPlugins()
      .filter(plugin => this.pluginMeetsRequirements(plugin, options))
      .flatMap(plugin => plugin.widgets ?? [])
      .filter(widget => !widget.feature || hasFeature(widget.feature))
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  }

  /**
   * Get user detail tabs from all plugins
   */
  getUserDetailTabs(options: FilterOptions = {}): UserDetailTab[] {
    const { isSuperAdmin = false, hasFeature = () => true } = options;

    return this.getPlugins()
      .filter(plugin => this.pluginMeetsRequirements(plugin, options))
      .flatMap(plugin => plugin.userDetailTabs ?? [])
      .filter(tab => {
        if (tab.superAdminOnly && !isSuperAdmin) return false;
        if (tab.feature && !hasFeature(tab.feature)) return false;
        return true;
      })
      .sort((a, b) => (a.order ?? 1000) - (b.order ?? 1000));
  }

  /**
   * Get tenant settings tabs from all plugins
   */
  getTenantSettingsTabs(options: FilterOptions = {}): TenantSettingsTab[] {
    const { isSuperAdmin = false, hasFeature = () => true } = options;

    return this.getPlugins()
      .filter(plugin => this.pluginMeetsRequirements(plugin, options))
      .flatMap(plugin => plugin.tenantSettingsTabs ?? [])
      .filter(tab => {
        if (tab.superAdminOnly && !isSuperAdmin) return false;
        if (tab.feature && !hasFeature(tab.feature)) return false;
        return true;
      })
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  }

  /**
   * Get settings tabs from all plugins (SuperAdmin only - for system/global settings)
   */
  getSettingsTabs(options: FilterOptions = {}): SettingsTab[] {
    const { hasFeature = () => true } = options;

    return this.getPlugins()
      .filter(plugin => this.pluginMeetsRequirements(plugin, options))
      .flatMap(plugin => plugin.settingsTabs ?? [])
      .filter(tab => !tab.feature || hasFeature(tab.feature))
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  }

  // ============ Field Renderers ============

  /**
   * Register a custom field renderer
   */
  registerFieldRenderer(renderer: FieldRenderer): void {
    if (this.fieldRenderers.has(renderer.type)) {
      console.warn(`Field renderer "${renderer.type}" is already registered`);
      return;
    }
    this.fieldRenderers.set(renderer.type, renderer);
    this.notify();
  }

  /**
   * Get a field renderer by type
   */
  getFieldRenderer(type: string): FieldRenderer | undefined {
    return this.fieldRenderers.get(type);
  }

  /**
   * Get all field renderers
   */
  getAllFieldRenderers(): FieldRenderer[] {
    return Array.from(this.fieldRenderers.values());
  }

  // ============ Cell Renderers ============

  /**
   * Register a custom cell renderer
   */
  registerCellRenderer(renderer: CellRenderer): void {
    if (this.cellRenderers.has(renderer.type)) {
      console.warn(`Cell renderer "${renderer.type}" is already registered`);
      return;
    }
    this.cellRenderers.set(renderer.type, renderer);
    this.notify();
  }

  /**
   * Get a cell renderer by type
   */
  getCellRenderer(type: string): CellRenderer | undefined {
    return this.cellRenderers.get(type);
  }

  // ============ Action Buttons ============

  /**
   * Register action buttons for an entity type
   */
  registerActionButtons(entityType: string, buttons: ActionButton[]): void {
    const existing = this.actionButtons.get(entityType) ?? [];
    this.actionButtons.set(entityType, [...existing, ...buttons]);
    this.notify();
  }

  /**
   * Get action buttons for an entity type
   */
  getActionButtons(entityType: string, options: FilterOptions = {}): ActionButton[] {
    const { hasFeature = () => true } = options;
    const buttons = this.actionButtons.get(entityType) ?? [];
    return buttons.filter(btn => !btn.feature || hasFeature(btn.feature));
  }

  // ============ Detail Sections ============

  /**
   * Register a detail section
   */
  registerDetailSection(section: DetailSection): void {
    this.detailSections.push(section);
    this.detailSections.sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
    this.notify();
  }

  /**
   * Get detail sections for an entity type
   */
  getDetailSections(entityType: string, options: FilterOptions = {}): DetailSection[] {
    const { hasFeature = () => true } = options;
    return this.detailSections
      .filter(section => section.entityTypes.includes(entityType))
      .filter(section => !section.feature || hasFeature(section.feature));
  }

  // ============ Subscription ============

  /**
   * Subscribe to registry changes
   */
  subscribe(listener: Listener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  private notify(): void {
    // Increment version and rebuild cached plugins array
    this.version++;
    this.cachedPlugins = Array.from(this.plugins.values());
    this.listeners.forEach(listener => listener());
  }

  // ============ Helpers ============

  private pluginMeetsRequirements(plugin: AdminUIPlugin, options: FilterOptions): boolean {
    const { hasFeature = () => true } = options;
    if (plugin.requiredFeatures?.length) {
      return plugin.requiredFeatures.every(f => hasFeature(f));
    }
    return true;
  }

  private matchPath(path: string, pattern: string): boolean {
    if (pattern === '*') return true;
    if (pattern.endsWith('/*')) {
      const prefix = pattern.slice(0, -2);
      return path === prefix || path.startsWith(prefix + '/');
    }
    return path === pattern;
  }
}

// Singleton instance
let registryInstance: ExtensionRegistry | null = null;

/**
 * Get the global extension registry instance
 */
export function getRegistry(): ExtensionRegistry {
  if (!registryInstance) {
    registryInstance = new ExtensionRegistry();
  }
  return registryInstance;
}

// Convenience exports for direct registration
export const registerPlugin = (plugin: AdminUIPlugin, options?: PluginRegistrationOptions) =>
  getRegistry().registerPlugin(plugin, options);

export const registerFieldRenderer = (renderer: FieldRenderer) =>
  getRegistry().registerFieldRenderer(renderer);

export const registerCellRenderer = (renderer: CellRenderer) =>
  getRegistry().registerCellRenderer(renderer);

export const registerActionButtons = (entityType: string, buttons: ActionButton[]) =>
  getRegistry().registerActionButtons(entityType, buttons);

export const registerDetailSection = (section: DetailSection) =>
  getRegistry().registerDetailSection(section);
