import { Component, inject, signal, computed, HostListener, OnInit, OnDestroy, effect, afterNextRender, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule, RouterOutlet, NavigationEnd } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Subject, takeUntil, filter, map } from 'rxjs';
import { AuthStore } from '@syndew/oluso-auth';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { PluginService, NavItem, PermissionsService, IconComponent, HelpPanelService, HelpPanelComponent, FeedbackService, FeedbackFabComponent, FeedbackPanelComponent, CommandPaletteComponent, ShortcutsHelpComponent, KeyboardShortcutsService, CommandRegistryService, DynamicCommandGroup, adminCommandAppId, adminCommandAreas, resolveCommandApp } from '@syndew/oluso-ui-core';
import { PermissionService } from '../../services/permission.service';
import { ContextService } from '../../services/context.service';
import { TenantSwitcherComponent } from '../../components/tenant-switcher/tenant-switcher.component';
import { ToastContainerComponent } from '@syndew/oluso-ui-core';
import { UserMenuComponent } from '../../components/user-menu/user-menu.component';
import { GlobalSearchComponent } from '../../components/global-search/global-search.component';
import { workspaceHref } from '../../components/global-search/admin-search-ui';
import { AdminSearchService, MIN_SEARCH_LENGTH } from '../../services/admin-search.service';
import { ADMIN_BRAND } from '../../branding/admin-brand.token';
import { ADMIN_NAV_MODES } from '../../branding/admin-nav-modes.token';

/**
 * Main sidebar navigation items for the admin dashboard
 * Matching the React AdminUI navigation structure
 */
const CORE_NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', path: '/dashboard', icon: 'home', order: 0 },
  { label: 'Organizations', path: '/organizations', icon: 'building', permission: 'organizations.read', superAdminOnly: true, order: 10 },
  { label: 'Platform Clients', path: '/platform/clients', icon: 'globe', permission: 'platform.clients.read', superAdminOnly: true, order: 11 },
  { label: 'My Organization', path: '/tenants', icon: 'building', permission: 'tenants.read', orgAdminOnly: true, order: 10 },
  {
    label: 'Applications',
    path: '/clients',
    icon: 'squares',
    order: 20,
    children: [
      { label: 'Clients', path: '/clients', icon: 'window', permission: 'clients.read', order: 0 },
      { label: 'Resources', path: '/resources', icon: 'server', permission: 'resources.read', order: 10 },
      { label: 'API Scopes', path: '/api-scopes', icon: 'key', permission: 'scopes.read', order: 20 },
      { label: 'Identity Resources', path: '/identity-resources', icon: 'identification', permission: 'identity_resources.read', order: 30 },
    ],
  },
  {
    label: 'Federation',
    path: '/identity-providers',
    icon: 'link',
    order: 30,
    children: [
      { label: 'Identity Providers', path: '/identity-providers', icon: 'globe', permission: 'identity_providers.read', order: 0 },
    ],
  },
  {
    label: 'User Management',
    path: '/users',
    icon: 'user-group',
    order: 35,
    children: [
      { label: 'Users', path: '/users', icon: 'user', permission: 'users.read', order: 0 },
      { label: 'Roles', path: '/roles', icon: 'shield', permission: 'roles.read', order: 10 },
      { label: 'Entitlements', path: '/entitlements', icon: 'key', permission: 'entitlements.read', order: 12 },
      { label: 'Teams', path: '/teams', icon: 'user-group', permission: 'teams.read', order: 15 },
      { label: 'Grants', path: '/grants', icon: 'clock', permission: 'grants.read', order: 20 },
      { label: 'Module Seats', path: '/module-seats', icon: 'puzzle-piece', permission: 'module_seats.read', order: 30 },
    ],
  },
  {
    label: 'Experience',
    path: '/experience',
    icon: 'sparkles',
    order: 40,
    children: [
      { label: 'User Journeys', path: '/experience/journeys', icon: 'map', permission: 'journeys.read', order: 0 },
      { label: 'Data Collection', path: '/experience/submissions', icon: 'inbox', permission: 'submissions.read', order: 10 },
      { label: 'Plugins', path: '/experience/plugins', icon: 'puzzle', permission: 'plugins.read', order: 20 },
    ],
  },
  { label: 'Signing Keys', path: '/signing-keys', icon: 'lock', permission: 'signing_keys.read', order: 46 },
  { label: 'Beta Feedback', path: '/feedback', icon: 'chat-bubble-left', superAdminOnly: true, order: 85 },
  { label: 'Activity Logs', path: '/audit-logs', icon: 'clipboard', permission: 'audit_logs.read', order: 80 },
  { label: 'Settings', path: '/settings', icon: 'cog', permission: 'settings.read', order: 100 },
  { label: 'Terminology', path: '/terminology', icon: 'globe', permission: 'settings.read', order: 101 },
  { label: 'Blueprints', path: '/blueprints', icon: 'rocket-launch', permission: 'settings.read', order: 102 },
];

@Component({
  selector: 'admin-main-layout',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, RouterOutlet, TenantSwitcherComponent, UserMenuComponent, IconComponent, ToastContainerComponent, HelpPanelComponent, FeedbackFabComponent, FeedbackPanelComponent, CommandPaletteComponent, ShortcutsHelpComponent, GlobalSearchComponent],
  templateUrl: './main-layout.component.html',
})
export class MainLayoutComponent implements OnInit, OnDestroy {
  readonly brand = inject(ADMIN_BRAND);
  readonly navModesConfig = inject(ADMIN_NAV_MODES);
  readonly authStore = inject(AuthStore);
  private readonly contextService = inject(ContextService);
  private readonly oidcService = inject(OidcSecurityService);
  private readonly pluginService = inject(PluginService);
  private readonly permissionsService = inject(PermissionsService);
  private readonly permissionService = inject(PermissionService);
  private readonly router = inject(Router);
  readonly helpService = inject(HelpPanelService);
  readonly feedbackService = inject(FeedbackService);
  /** Global command palette + shortcut help open state is owned by this service,
   *  which also listens for ⌘K and `?` keydowns. Injected here so the template
   *  can bind `[open]="shortcutsService.commandPaletteOpen()"`. */
  readonly shortcutsService = inject(KeyboardShortcutsService);
  private readonly commandRegistry = inject(CommandRegistryService);
  private readonly adminSearch = inject(AdminSearchService);
  private unregisterSearchCommand: (() => void) | null = null;
  private readonly destroy$ = new Subject<void>();

  readonly sidebarOpen = signal(true);

  /** Currently selected nav mode id (only meaningful when ADMIN_NAV_MODES is provided). */
  readonly selectedModeId = signal<string | null>(this.initialModeId());

  /** Resolved active mode (or null when modes aren't configured). */
  readonly activeMode = computed(() => {
    const cfg = this.navModesConfig;
    if (!cfg) return null;
    // If no plugin has registered a grouped item, the suite-style modes are
    // empty — fall back to the platform-style mode so a stale 'modules'
    // localStorage selection doesn't trap the user on an empty pane.
    const hasGrouped = this.navItems().some((item) => !!item.group);
    if (!hasGrouped) {
      return cfg.modes.find((m) => m.filter === 'ungrouped' || m.filter === 'all') ?? cfg.modes[0] ?? null;
    }
    const id = this.selectedModeId();
    return cfg.modes.find((m) => m.id === id) ?? cfg.modes[0] ?? null;
  });

  /**
   * Tabs only render when the host has both a multi-mode config AND at least
   * one nav item carries a `group` value (so the suite/grouped tab isn't an
   * empty mode). Without this, reference admins that don't tag plugins with
   * navGroup would see a "Modules" tab leading to an empty pane.
   */
  readonly showNavModeTabs = computed(() => {
    const cfg = this.navModesConfig;
    if (!cfg || cfg.modes.length < 2) return false;
    return this.navItems().some((item) => !!item.group);
  });

  readonly user = this.authStore.user;
  readonly currentTenantName = computed(() =>
    this.contextService.adminContext()?.currentTenant?.name ?? null
  );
  readonly currentOrgName = computed(() =>
    this.contextService.adminContext()?.currentOrganization?.name ?? null
  );
  readonly screenWidth = signal(typeof window !== 'undefined' ? window.innerWidth : 1024);
  /** Track which nav items are expanded (by path) */
  readonly expandedItems = signal<Set<string>>(new Set());

  /** Expose permissions loaded state */
  readonly permissionsLoaded = this.permissionsService.isLoaded;
  readonly permissionsFromCache = this.permissionsService.isFromCache;

  private readonly navItemsEffect = effect(() => {
    // Watch for navItems changes (e.g., after plugins load) and expand parents
    // Using effect as a class field ensures proper injection context
    const items = this.navItems();
    // Only run after initial render to avoid blocking the router
    // Untracked: expanding reads and writes expandedItems, which would make this
    // effect depend on its own write and re-run forever (a tenant switch resets
    // permissions, navItems recomputes, and the tab locks up before the reload).
    if (items.length > 0 && this.hasRendered) {
      untracked(() => this.expandParentsForCurrentRoute());
    }
  });

  private hasRendered = false;

  constructor() {
    // Mark as rendered after first render cycle
    afterNextRender(() => {
      this.hasRendered = true;
      // Trigger initial expansion after render
      this.expandParentsForCurrentRoute();
    });
  }

  ngOnInit(): void {
    this.feedbackService.portal.set('admin');
    this.registerSearchCommand();
    this.scopeCommandPalette();

    // Fetch permissions on mount if not already loaded.
    // The login flow and app initializer both call getMyPermissions() after
    // initFromLoginResponse(), so by the time this component mounts,
    // permissions (including explicitGrantPermissions) are already populated.
    if (!this.permissionsService.isLoaded()) {
      this.permissionService.getMyPermissions().subscribe({
        error: (err) => console.error('Failed to load permissions:', err),
      });
    }

    // Load admin context for org/tenant names in the top bar
    this.contextService.getContext().pipe(takeUntil(this.destroy$)).subscribe();

    // Expand parents for current route on init
    this.expandParentsForCurrentRoute();

    // Subscribe to route changes to expand parents when navigating
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntil(this.destroy$)
      )
      .subscribe(() => {
        this.expandParentsForCurrentRoute();
      });
  }

  ngOnDestroy(): void {
    this.unregisterSearchCommand?.();
    this.commandRegistry.setAppResolver(null);
    this.commandRegistry.setApps([]);
    this.commandRegistry.setPermissionChecker(null);
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Expand parent nav items that contain the current active route */
  private expandParentsForCurrentRoute(): void {
    const currentUrl = this.router.url.split('?')[0]; // Remove query params
    const items = this.navItems();
    const newExpanded = new Set(this.expandedItems());

    for (const item of items) {
      if (item.children && item.children.length > 0) {
        // Check if any child path matches the current URL
        const hasActiveChild = item.children.some((child) => {
          // Exact match or starts with child path (for nested routes)
          if (currentUrl === child.path || currentUrl.startsWith(child.path + '/')) {
            return true;
          }
          // Check grandchildren for nested nav items
          if (child.children) {
            return child.children.some(
              (grandchild) => currentUrl === grandchild.path || currentUrl.startsWith(grandchild.path + '/')
            );
          }
          return false;
        });
        if (hasActiveChild) {
          newExpanded.add(item.path);
        }

        // Also expand nested children if they have an active grandchild
        for (const child of item.children) {
          if (child.children && child.children.length > 0) {
            const hasActiveGrandchild = child.children.some(
              (grandchild) => currentUrl === grandchild.path || currentUrl.startsWith(grandchild.path + '/')
            );
            if (hasActiveGrandchild) {
              newExpanded.add(child.path);
            }
          }
        }
      }
    }

    this.expandedItems.set(newExpanded);
  }

  @HostListener('window:resize')
  onResize(): void {
    this.screenWidth.set(window.innerWidth);
  }

  /** Check if we're on mobile screen */
  isMobile(): boolean {
    return this.screenWidth() < 1024;
  }

  /** Check if user is a super admin */
  isSuperAdmin(): boolean {
    const currentUser = this.user();
    return currentUser?.roles?.includes('SuperAdmin') || false;
  }

  /** Get user's initial for avatar */
  getUserInitial(): string {
    const currentUser = this.user();
    return currentUser?.displayName?.[0] || currentUser?.userName?.[0] || currentUser?.email?.[0] || 'U';
  }

  /** Combined navigation items from core + plugins, with plugin items merged into existing menus */
  readonly navItems = computed(() => {
    const pluginNavItems = this.pluginService.adminNavItems();
    const isSuperAdmin = this.isSuperAdmin();

    // Start with core items (deep clone to avoid mutation)
    const mergedItems: NavItem[] = CORE_NAV_ITEMS.map((item) => ({
      ...item,
      children: item.children ? [...item.children] : undefined,
    }));

    // Process plugin nav items
    for (const pluginItem of pluginNavItems) {
      if (pluginItem.mergeIntoPath) {
        // Find the target parent item to merge into
        const targetParent = mergedItems.find((item) => item.path === pluginItem.mergeIntoPath);
        if (targetParent) {
          // Add the plugin item itself as a child of the target parent (preserving grouping)
          const itemToAdd: NavItem = {
            ...pluginItem,
            mergeIntoPath: undefined, // Clear mergeIntoPath to avoid re-processing
          };
          targetParent.children = [...(targetParent.children || []), itemToAdd];
        } else {
          // Target not found, add as top-level item
          mergedItems.push(pluginItem);
        }
      } else {
        // Check if a nav item with same path already exists
        const existingItem = mergedItems.find((item) => item.path === pluginItem.path);
        if (existingItem && pluginItem.children) {
          // Merge children into existing item
          existingItem.children = [...(existingItem.children || []), ...pluginItem.children];
        } else {
          // Add as new top-level item
          mergedItems.push(pluginItem);
        }
      }
    }

    // Sort children within each item
    for (const item of mergedItems) {
      if (item.children) {
        item.children.sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
      }
    }

    // Filter based on user role and permissions:
    // - superAdminOnly items: only visible to super admins
    // - orgAdminOnly items: only visible to non-super-admins (org admins)
    // - permission: check if user has required permission
    const visible = mergedItems.filter((item) => {
      if (item.superAdminOnly && !isSuperAdmin) return false;
      if (item.orgAdminOnly && (isSuperAdmin || !this.authStore.currentOrganization()?.id)) return false;
      if (item.permission && !this.permissionsService.hasPermission(item.permission)) return false;
      // For items with children, check if any child is visible
      if (item.children && item.children.length > 0) {
        const visibleChildren = item.children.filter((child) => this.hasChildPermission(child));
        if (visibleChildren.length === 0) return false;
      }
      return true;
    });

    // Cluster grouped items together so the section header isn't fragmented by
    // an ungrouped item slipping between two members of the same group. Each
    // group's position is anchored to its earliest member's order.
    const groupAnchor = new Map<string | null, number>();
    for (const item of visible) {
      const g = item.group ?? null;
      const o = item.order ?? 100;
      const anchor = groupAnchor.get(g);
      if (anchor === undefined || anchor > o) groupAnchor.set(g, o);
    }
    return visible.sort((a, b) => {
      const ga = a.group ?? null;
      const gb = b.group ?? null;
      if (ga !== gb) return (groupAnchor.get(ga) ?? 100) - (groupAnchor.get(gb) ?? 100);
      return (a.order ?? 100) - (b.order ?? 100);
    });
  });

  /**
   * Nav items clustered by `group`. Consecutive items sharing a group label
   * become one section with a header rendered above the first item.
   * Ungrouped items have `group: null` and render with no header.
   */
  readonly groupedNavItems = computed(() => {
    const all = this.navItems();
    const mode = this.activeMode();
    const items = mode
      ? all.filter((item) => {
          if (mode.filter === 'all') return true;
          if (mode.filter === 'grouped') return !!item.group;
          if (mode.filter === 'ungrouped') return !item.group;
          return true;
        })
      : all;

    const groups: Array<{ group: string | null; items: NavItem[] }> = [];
    for (const item of items) {
      const group = item.group ?? null;
      const last = groups[groups.length - 1];
      if (last && last.group === group) {
        last.items.push(item);
      } else {
        groups.push({ group, items: [item] });
      }
    }
    return groups;
  });

  /** Check if a child nav item should be visible based on permission */
  private hasChildPermission(child: NavItem): boolean {
    const isSuperAdmin = this.isSuperAdmin();
    if (child.superAdminOnly && !isSuperAdmin) return false;
    if (child.orgAdminOnly && isSuperAdmin) return false;
    if (child.permission && !this.permissionsService.hasPermission(child.permission)) return false;
    // A section shows only while something inside it does.
    if (child.children?.length && !child.children.some((c) => this.hasChildPermission(c))) return false;
    return true;
  }

  toggleSidebar(): void {
    this.sidebarOpen.update((open) => !open);
  }

  /**
   * Collapse the sidebar after a leaf nav link is clicked — mobile only,
   * since desktop's sidebar is permanently visible via lg:translate-x-0
   * regardless of the signal. Without this the mobile menu stays open
   * over the destination page after navigation.
   */
  onNavLinkClick(): void {
    if (this.isMobile()) {
      this.sidebarOpen.set(false);
    }
  }

  setMode(id: string): void {
    if (!this.navModesConfig) return;
    this.selectedModeId.set(id);
    if (typeof localStorage !== 'undefined') {
      const key = this.navModesConfig.storageKey ?? 'oluso.admin.navMode';
      localStorage.setItem(key, id);
    }
  }

  private readonly _sanitizer = inject(DomSanitizer);
  private readonly _iconHtmlCache = new Map<string, SafeHtml>();
  /** Returns a memoised SafeHtml for a mode's inline SVG icon markup. */
  sanitizedIconHtml(html: string): SafeHtml {
    const cached = this._iconHtmlCache.get(html);
    if (cached) return cached;
    const safe = this._sanitizer.bypassSecurityTrustHtml(html);
    this._iconHtmlCache.set(html, safe);
    return safe;
  }

  private initialModeId(): string | null {
    const cfg = this.navModesConfig;
    if (!cfg || cfg.modes.length === 0) return null;
    if (typeof localStorage !== 'undefined') {
      const key = cfg.storageKey ?? 'oluso.admin.navMode';
      const stored = localStorage.getItem(key);
      if (stored && cfg.modes.some((m) => m.id === stored)) return stored;
    }
    return cfg.defaultMode ?? cfg.modes[0].id;
  }

  /** Check if a nav item has visible children */
  hasChildren(item: NavItem): boolean {
    return !!item.children && this.getVisibleChildren(item).length > 0;
  }

  /** Check if a child nav item has its own children (for nested expansion) */
  childHasChildren(child: NavItem): boolean {
    return !!child.children && child.children.length > 0 && child.children.some((c) => this.hasChildPermission(c));
  }

  /** Get visible children filtered by permissions */
  getVisibleChildren(item: NavItem): NavItem[] {
    if (!item.children) return [];
    return item.children.filter((child) => this.hasChildPermission(child));
  }

  /** Get visible grandchildren filtered by permissions */
  getVisibleGrandChildren(child: NavItem): NavItem[] {
    if (!child.children) return [];
    return child.children.filter((grandchild) => this.hasChildPermission(grandchild));
  }

  /** Check if a nav item is expanded */
  isExpanded(item: NavItem): boolean {
    return this.expandedItems().has(item.path);
  }

  /** Check if any child route is active (for parent highlighting) */
  isParentActive(item: NavItem): boolean {
    if (!item.children || item.children.length === 0) return false;
    const currentUrl = this.router.url;
    return currentUrl.startsWith(item.path);
  }

  /** Toggle expansion of a nav item */
  toggleExpand(item: NavItem, event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.expandedItems.update((set) => {
      const newSet = new Set(set);
      if (newSet.has(item.path)) {
        newSet.delete(item.path);
      } else {
        newSet.add(item.path);
      }
      return newSet;
    });
  }

  /** Navigate to a path and expand parent if needed */
  navigateTo(path: string): void {
    this.router.navigate([path]);
  }

  logout(): void {
    this.oidcService.logoff().subscribe();
  }

  /** Each plugin's admin section is an app to the ⌘K palette; the core admin pages belong to none. */
  private readonly commandAreas = computed(() => adminCommandAreas(this.pluginService.adminNavItems()));

  /**
   * Keeps the ⌘K palette to the section the user is in, so a module's admin commands only show inside its section,
   * and checks commands' permissions the way the sidebar does.
   */
  private scopeCommandPalette(): void {
    this.commandRegistry.setPermissionChecker((perm) => this.permissionsService.hasPermission(perm));
    this.commandRegistry.setAppResolver((url) => resolveCommandApp(url, this.commandAreas()));
  }

  /** "Switch app…" lists the plugin sections the sidebar shows the user, and the core admin to come back to. */
  private readonly paletteAppsEffect = effect(() => {
    const areaIds = new Set(this.commandAreas().map((area) => area.app));
    const sections = this.navItems()
      .map((item) => ({ item, id: adminCommandAppId(item.path) }))
      .filter((entry): entry is { item: NavItem; id: string } => !!entry.id && areaIds.has(entry.id));
    const seen = new Set<string>();
    this.commandRegistry.setApps([
      { id: 'core-admin', label: 'Administration', icon: 'home', open: () => this.router.navigateByUrl('/dashboard') },
      ...sections
        .filter(({ id }) => !seen.has(id) && !!seen.add(id))
        .map(({ item, id }) => ({
          id,
          label: item.label,
          icon: item.icon,
          open: () => this.router.navigateByUrl(item.children?.[0]?.path ?? item.path),
        })),
    ]);
  });

  /** The ⌘K palette searches the same records as the header box. */
  private registerSearchCommand(): void {
    const group: DynamicCommandGroup = {
      kind: 'dynamic',
      id: 'admin-global-search',
      label: 'Search records',
      group: 'Records',
      app: null,
      icon: 'search',
      minQueryLength: MIN_SEARCH_LENGTH,
      search: (query) => this.adminSearch.search(query, { limit: 4 }).pipe(
        map((response) => response.groups.flatMap((g) => g.items.map((item) => ({
          id: `${g.providerId}:${item.id}`,
          label: item.title,
          description: [g.displayName, item.subtitle].filter(Boolean).join(' · '),
          icon: item.icon ?? g.icon,
          onSelect: () => {
            if (item.route) { this.router.navigateByUrl(item.route); return; }
            const href = workspaceHref(item.workspaceRoute);
            if (href) window.location.assign(href);
          },
        })))),
      ),
    };
    this.unregisterSearchCommand = this.commandRegistry.register([group]);
  }
}
