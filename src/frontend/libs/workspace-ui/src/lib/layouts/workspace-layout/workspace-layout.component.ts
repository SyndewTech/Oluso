import { Component, inject, signal, computed, effect, OnInit, OnDestroy } from '@angular/core';
import { CommonModule, NgComponentOutlet } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { WorkspaceNavItem, PluginService, IconComponent, HelpPanelService, HelpPanelComponent, FeedbackService, FeedbackFabComponent, FeedbackPanelComponent, ToastContainerComponent, CommandPaletteComponent, ShortcutsHelpComponent, CommandRegistryService, AnyCommand, WorkspaceApp, navItemCommand, resolveCommandApp, workspaceAppForNavItem, workspaceCommandAreas } from '@syndew/oluso-ui-core';
import { WorkspaceAuthService } from '../../services/workspace-auth.service';
import { AppSwitcherService } from '../../services/app-switcher.service';
import { ActionCenterService } from '../../services/action-center.service';
import { MentionNotificationService } from '../../services/mention-notification.service';
import { AppSwitcherComponent } from '../../components/app-switcher/app-switcher.component';
import { ActionCenterDropdownComponent } from '../../components/action-center-dropdown/action-center-dropdown.component';
import { WorkspaceTenantSwitcherComponent } from '../../components/tenant-switcher/tenant-switcher.component';

/** Home entries about the user themselves, offered in every app like the account menu they sit beside. */
const GLOBAL_NAV_ITEM_IDS = new Set(['profile', 'notifications']);

import { TimeZoneNoticeComponent } from '../../components/time-zone-notice/time-zone-notice.component';

@Component({
  selector: 'workspace-layout',
  standalone: true,
  imports: [CommonModule, NgComponentOutlet, RouterLink, RouterLinkActive, RouterOutlet, IconComponent, AppSwitcherComponent, ActionCenterDropdownComponent, WorkspaceTenantSwitcherComponent, TimeZoneNoticeComponent, HelpPanelComponent, FeedbackFabComponent, FeedbackPanelComponent, ToastContainerComponent, CommandPaletteComponent, ShortcutsHelpComponent],
  styles: [`
    :host ::ng-deep .active-nav-item {
      background-color: rgba(255, 255, 255, 0.12);
      color: white;
      font-weight: 600;
    }
    :host ::ng-deep .active-nav-item > span:first-child {
      color: white;
    }
    :host ::ng-deep .active-nav-item:hover {
      background-color: rgba(255, 255, 255, 0.18);
    }
  `],
  template: `
    <!-- Toast host — renders queued ToastService messages (mention
         notifications, system messages). Mirrors the admin shell so toasts
         work the same way across both portals. -->
    <oluso-toast-container />
    <div class="min-h-screen bg-gray-50">
      <!-- Mobile menu button -->
      <div class="lg:hidden fixed top-0 left-0 right-0 z-20 bg-slate-900 px-4 py-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-2">
            <workspace-app-switcher [collapsed]="false" />
          </div>
          <button
            type="button"
            class="p-2 text-slate-300 hover:text-white"
            (click)="mobileMenuOpen.set(!mobileMenuOpen())"
          >
            @if (mobileMenuOpen()) {
              <ui-icon name="close" class="h-6 w-6" />
            } @else {
              <ui-icon name="bars-3" class="h-6 w-6" />
            }
          </button>
        </div>
      </div>

      <!-- Mobile menu overlay -->
      @if (mobileMenuOpen()) {
        <div
          class="lg:hidden fixed inset-0 z-30 bg-black/50"
          (click)="mobileMenuOpen.set(false)"
        ></div>
      }

      <!-- Mobile sidebar -->
      <div
        class="lg:hidden fixed inset-y-0 left-0 z-40 w-64 bg-slate-900 transform transition-transform duration-200 ease-in-out"
        [class.translate-x-0]="mobileMenuOpen()"
        [class.-translate-x-full]="!mobileMenuOpen()"
      >
        <div class="h-full overflow-y-auto flex flex-col">
          <div class="p-4 border-b border-slate-700/50">
            <workspace-app-switcher [collapsed]="false" />
            @if (authService.user(); as user) {
              <p class="text-sm text-slate-400 mt-2">{{ user.displayName }}</p>
            }
          </div>
          <nav class="flex-1 px-3 py-4 overflow-y-auto">
            @for (section of groupedNavItems(); track section.label) {
              <div [class.mt-4]="!$first">
                @if (section.label) {
                  <div class="px-3 mb-1">
                    <span class="text-[11px] font-semibold uppercase tracking-wider text-slate-500">{{ section.label }}</span>
                  </div>
                }
                <div class="space-y-0.5">
                  @for (item of section.items; track item.id) {
                    <a
                      [routerLink]="item.path"
                      [queryParams]="item.queryParams ?? null"
                      routerLinkActive="active-nav-item"
                      [routerLinkActiveOptions]="{ exact: item.path === '/dashboard' }"
                      class="group flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium text-slate-300 hover:text-white hover:bg-white/8 transition-colors"
                      (click)="mobileMenuOpen.set(false)"
                    >
                      <ui-icon [name]="item.icon" class="h-5 w-5 shrink-0 text-slate-400 group-hover:text-slate-200 transition-colors" />
                      <span>{{ item.label }}</span>
                      @if (item.badge) {
                        <span class="ml-auto bg-primary-500/20 text-primary-300 text-xs font-semibold px-2 py-0.5 rounded-full">
                          {{ getBadgeValue(item.badge) }}
                        </span>
                      }
                    </a>
                  }
                </div>
              </div>
            }
          </nav>
          <div class="px-3 py-4 border-t border-slate-700/50">
            <button
              type="button"
              class="flex items-center gap-3 w-full px-3 py-2 rounded-lg text-sm font-medium text-slate-400 hover:text-white hover:bg-white/8 transition-colors"
              (click)="logout()"
            >
              <ui-icon name="logout" class="h-5 w-5 shrink-0" />
              <span>Sign Out</span>
            </button>
          </div>
        </div>
      </div>

      <div class="flex">
        <!-- Desktop sidebar -->
        <div
          class="hidden lg:flex lg:flex-col lg:fixed lg:inset-y-0 bg-slate-900 transition-all duration-200"
          [class.lg:w-64]="!collapsed()"
          [class.lg:w-16]="collapsed()"
        >
          <div class="flex-1 flex flex-col overflow-y-auto overflow-x-hidden">
            <!-- Header with App Switcher -->
            <div class="p-3 border-b border-slate-700/50 min-h-[65px]">
              <div class="flex items-center justify-between">
                <workspace-app-switcher [collapsed]="collapsed()" />
                @if (!collapsed()) {
                  <button
                    type="button"
                    class="p-1.5 rounded-md text-slate-400 hover:text-white hover:bg-white/8 transition-colors shrink-0"
                    (click)="collapsed.set(!collapsed())"
                    title="Collapse sidebar"
                  >
                    <ui-icon name="chevron-left" class="h-5 w-5" />
                  </button>
                }
              </div>
              @if (collapsed()) {
                <button
                  type="button"
                  class="p-1.5 rounded-md text-slate-400 hover:text-white hover:bg-white/8 transition-colors shrink-0 mx-auto block mt-2"
                  (click)="collapsed.set(!collapsed())"
                  title="Expand sidebar"
                >
                  <ui-icon name="chevron-right" class="h-5 w-5" />
                </button>
              }
            </div>

            <!-- Navigation - App-filtered items -->
            <nav class="flex-1 px-2 py-4 overflow-y-auto">
              @for (section of groupedNavItems(); track section.label) {
                <div [class.mt-4]="!$first">
                  @if (section.label && !collapsed()) {
                    <div class="px-3 mb-1">
                      <span class="text-[11px] font-semibold uppercase tracking-wider text-slate-500">{{ section.label }}</span>
                    </div>
                  }
                  @if (section.label && collapsed()) {
                    <div class="mx-2 mb-1 border-t border-slate-700/50"></div>
                  }
                  <div class="space-y-0.5">
                    @for (item of section.items; track item.id) {
                      <a
                        [routerLink]="item.path"
                        routerLinkActive="active-nav-item"
                        [routerLinkActiveOptions]="{ exact: item.path === '/dashboard' }"
                        class="group flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium text-slate-300 hover:text-white hover:bg-white/8 transition-colors"
                        [class.justify-center]="collapsed()"
                        [class.px-2]="collapsed()"
                        [title]="collapsed() ? item.label : ''"
                      >
                        <ui-icon [name]="item.icon" class="h-5 w-5 shrink-0 text-slate-400 group-hover:text-slate-200 transition-colors" />
                        @if (!collapsed()) {
                          <span class="truncate">{{ item.label }}</span>
                          @if (item.badge) {
                            <span class="ml-auto bg-primary-500/20 text-primary-300 text-xs font-semibold px-2 py-0.5 rounded-full">
                              {{ getBadgeValue(item.badge) }}
                            </span>
                          }
                        }
                      </a>
                    }
                  </div>
                </div>
              }
            </nav>

            <!-- Footer -->
            <div class="px-2 py-4 border-t border-slate-700/50">
              @if (authService.user(); as user) {
                @if (!collapsed() && user.isManager) {
                  <div class="px-3 mb-3">
                    <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-purple-500/20 text-purple-300">
                      Manager
                    </span>
                  </div>
                }
              }
              <button
                type="button"
                class="flex items-center gap-3 w-full px-3 py-2 rounded-lg text-sm font-medium text-slate-400 hover:text-white hover:bg-white/8 transition-colors"
                [class.justify-center]="collapsed()"
                [class.px-2]="collapsed()"
                [title]="collapsed() ? 'Sign Out' : ''"
                (click)="logout()"
              >
                <ui-icon name="logout" class="h-5 w-5 shrink-0" />
                @if (!collapsed()) {
                  <span>Sign Out</span>
                }
              </button>
            </div>
          </div>
        </div>

        <!-- Main content -->
        <div
          class="flex-1 flex flex-col min-h-screen min-w-0 transition-all duration-200"
          [class.lg:pl-64]="!collapsed() || appSwitcher.isPanelOpen()"
          [class.lg:pl-16]="collapsed() && !appSwitcher.isPanelOpen()"
          [style.paddingRight.px]="helpService.isOpen() ? helpService.width() : 0"
        >
          <!-- Top bar -->
          <header class="hidden lg:flex items-center justify-between h-14 px-6 bg-white border-b border-gray-200 sticky top-0 z-[60]">
            <div class="flex items-center gap-3 min-w-0">
              @if (authService.user(); as user) {
                <div class="flex items-center gap-2 text-sm min-w-0">
                  @if (user.organizationName) {
                    <span class="font-semibold text-gray-900 truncate">{{ user.organizationName }}</span>
                  }
                  @if (user.organizationName && user.tenantName) {
                    <span class="text-gray-300">/</span>
                  }
                  @if (user.tenantName) {
                    <span class="text-gray-500 truncate">{{ user.tenantName }}</span>
                  }
                </div>
              }
              <workspace-tenant-switcher />
            </div>
            <div class="flex items-center gap-3">
              <workspace-time-zone-notice />
              @for (widget of headerWidgets(); track $index) {
                <ng-container *ngComponentOutlet="widget" />
              }

              <!-- Help -->
              <button
                type="button"
                class="p-2 rounded-lg text-gray-500 hover:text-gray-700 hover:bg-gray-100 transition-colors"
                (click)="helpService.toggle()"
                title="Toggle help panel (?)"
              >
                <ui-icon name="question-mark-circle" class="h-5 w-5" />
              </button>

              <!-- Action Center -->
              <div class="relative">
                <button
                  type="button"
                  class="relative p-2 rounded-lg text-gray-500 hover:text-gray-700 hover:bg-gray-100 transition-colors"
                  title="Action Center"
                  (click)="actionCenter.toggle()"
                >
                  <ui-icon name="bell" class="h-5 w-5" />
                  @if (actionCenter.count() > 0) {
                    <span class="absolute top-1 right-1 flex h-4 w-4 items-center justify-center rounded-full bg-red-500 text-[10px] font-bold text-white">
                      {{ actionCenter.count() > 9 ? '9+' : actionCenter.count() }}
                    </span>
                  }
                </button>
                @if (actionCenter.isOpen()) {
                  <workspace-action-center-dropdown />
                }
              </div>

              <!-- Divider -->
              <div class="h-6 w-px bg-gray-200"></div>

              <!-- Account dropdown -->
              @if (authService.user(); as user) {
                <div class="relative">
                  <button
                    type="button"
                    class="flex items-center gap-2 p-1.5 rounded-lg hover:bg-gray-100 transition-colors"
                    (click)="accountMenuOpen.set(!accountMenuOpen())"
                  >
                    @if (user.profilePictureUrl) {
                      <img [src]="user.profilePictureUrl" [alt]="user.displayName" class="h-8 w-8 rounded-full object-cover" />
                    } @else {
                      <div class="h-8 w-8 rounded-full bg-slate-700 flex items-center justify-center text-sm font-medium text-white">
                        {{ getInitials(user.displayName) }}
                      </div>
                    }
                    <span class="text-sm font-medium text-gray-700">{{ user.firstName || user.displayName }}</span>
                    <ui-icon name="chevron-down" class="h-4 w-4 text-gray-400" />
                  </button>

                  <!-- Dropdown menu -->
                  @if (accountMenuOpen()) {
                    <div class="absolute right-0 mt-2 w-72 bg-white rounded-xl shadow-lg border border-gray-200 py-2 z-50">
                      <!-- User info -->
                      <div class="px-4 py-3 border-b border-gray-100">
                        <div class="flex items-center gap-3">
                          @if (user.profilePictureUrl) {
                            <img [src]="user.profilePictureUrl" [alt]="user.displayName" class="h-10 w-10 rounded-full object-cover" />
                          } @else {
                            <div class="h-10 w-10 rounded-full bg-slate-700 flex items-center justify-center text-sm font-bold text-white shrink-0">
                              {{ getInitials(user.displayName) }}
                            </div>
                          }
                          <div class="min-w-0">
                            <p class="text-sm font-semibold text-gray-900 truncate">{{ user.displayName }}</p>
                            <p class="text-xs text-gray-500 truncate">{{ user.email }}</p>
                          </div>
                        </div>
                        @if (user.jobTitle || user.department) {
                          <div class="mt-2 flex flex-wrap gap-1.5">
                            @if (user.jobTitle) {
                              <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-700">
                                {{ user.jobTitle }}
                              </span>
                            }
                            @if (user.department) {
                              <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-blue-50 text-blue-700">
                                {{ user.department }}
                              </span>
                            }
                          </div>
                        }
                      </div>

                      <!-- Menu items -->
                      <div class="py-1">
                        <a
                          routerLink="/profile"
                          class="flex items-center gap-3 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50 transition-colors"
                          (click)="accountMenuOpen.set(false)"
                        >
                          <ui-icon name="user" class="h-4 w-4 text-gray-400" />
                          My Profile
                        </a>
                        <a
                          [href]="appSwitcher.getAccountProfileUrl()"
                          class="flex items-center gap-3 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50 transition-colors"
                          (click)="accountMenuOpen.set(false)"
                        >
                          <ui-icon name="cog" class="h-4 w-4 text-gray-400" />
                          My account
                        </a>
                      </div>

                      <!-- Sign out -->
                      <div class="border-t border-gray-100 pt-1">
                        <button
                          type="button"
                          class="flex items-center gap-3 w-full px-4 py-2 text-sm text-red-600 hover:bg-red-50 transition-colors"
                          (click)="logout(); accountMenuOpen.set(false)"
                        >
                          <ui-icon name="logout" class="h-4 w-4" />
                          Sign Out
                        </button>
                      </div>
                    </div>
                  }
                </div>
              }
            </div>
          </header>

          <!-- Click-away overlay for dropdowns -->
          @if (accountMenuOpen() || actionCenter.isOpen()) {
            <div class="fixed inset-0 z-[9]" (click)="accountMenuOpen.set(false); actionCenter.close()"></div>
          }

          <main class="flex-1 py-6 px-4 sm:px-6 lg:px-8 pt-20 lg:pt-6">
            <router-outlet />
          </main>
        </div>
      </div>

      <!-- Plugin status widgets — persistent overlay that survives route
           changes (long-running background activity: recordings, uploads,
           sync progress). The region itself ignores pointer events; only the
           widgets rendered inside it are interactive. Sits above the feedback
           FAB so both stay reachable. -->
      @if (statusWidgets().length > 0) {
        <div class="fixed bottom-24 right-4 z-50 flex flex-col items-end gap-2 pointer-events-none max-w-[calc(100vw-2rem)]">
          @for (widget of statusWidgets(); track $index) {
            <div class="pointer-events-auto">
              <ng-container *ngComponentOutlet="widget" />
            </div>
          }
        </div>
      }

      <ui-help-panel />
      <ui-feedback-fab />
      <ui-feedback-panel />

      <!-- Global keyboard-activated overlays. Both manage their own open state
           via KeyboardShortcutsService signals (Cmd/Ctrl+K opens the palette,
           ? opens the shortcut help) — same wiring as the admin shell. -->
      <ui-command-palette />
      <ui-shortcuts-help />
    </div>
  `,
})
export class WorkspaceLayoutComponent implements OnInit, OnDestroy {
  private readonly oidcService = inject(OidcSecurityService);
  private readonly router = inject(Router);
  readonly authService = inject(WorkspaceAuthService);
  private readonly pluginService = inject(PluginService);
  readonly appSwitcher = inject(AppSwitcherService);
  readonly helpService = inject(HelpPanelService);
  readonly feedbackService = inject(FeedbackService);
  readonly actionCenter = inject(ActionCenterService);
  private readonly commandRegistry = inject(CommandRegistryService);

  // Boot the mention-toast subscriber once for the lifetime of the
  // workspace shell. The service self-subscribes to the conversation
  // realtime channel on construction; binding it as a field keeps it
  // alive without any further wiring per page.
  private readonly _mentionNotifications = inject(MentionNotificationService);

  /** Status widget components contributed by registered plugins. */
  readonly statusWidgets = this.pluginService.workspaceStatusWidgets;
  readonly headerWidgets = this.pluginService.workspaceHeaderWidgets;

  readonly mobileMenuOpen = signal(false);
  readonly collapsed = signal(false);
  readonly accountMenuOpen = signal(false);

  // Default navigation items for the Home app
  private readonly defaultNavItems: WorkspaceNavItem[] = [
    {
      id: 'dashboard',
      label: 'Dashboard',
      path: '/dashboard',
      icon: 'home',
      category: 'dashboard',
      order: 0,
    },
    {
      id: 'my-files',
      label: 'My Files',
      path: '/my-files',
      icon: 'folder',
      category: 'self-service',
      order: 5,
    },
    {
      id: 'notifications',
      label: 'Notifications',
      path: '/notifications',
      icon: 'bell',
      category: 'self-service',
      order: 8,
    },
    {
      id: 'profile',
      label: 'My Profile',
      path: '/profile',
      icon: 'user',
      category: 'self-service',
      order: 10,
    },
  ];

  /**
   * Navigation items for the currently selected app
   * Combines default items with plugin items, filtered by the selected app's categories
   */
  readonly currentAppNavItems = computed(() => {
    const selectedApp = this.appSwitcher.selectedApp();
    if (!selectedApp || !selectedApp.categories) return [];

    const pluginNavItems = this.pluginService.workspaceNavItems();
    const allItems = [...this.defaultNavItems, ...pluginNavItems];
    const appCategories = selectedApp.categories;

    return allItems
      .filter((item) => {
        const category = item.category || 'self-service';
        return appCategories.includes(category);
      })
      .filter((item) => this.filterByPermissions(item))
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  });

  /**
   * Grouped navigation items for the sidebar.
   * Items without a group go into an ungrouped section at the top.
   * Groups are derived from the already-filtered items so permission/manager
   * filtering is already applied — a group only shows if it has visible items.
   */
  readonly groupedNavItems = computed(() => {
    const items = this.currentAppNavItems();
    const groups: { label: string | null; items: WorkspaceNavItem[] }[] = [];
    const groupMap = new Map<string | null, WorkspaceNavItem[]>();

    // Preserve group ordering by first appearance
    for (const item of items) {
      const key = item.group ?? null;
      let bucket = groupMap.get(key);
      if (!bucket) {
        bucket = [];
        groupMap.set(key, bucket);
        groups.push({ label: key, items: bucket });
      }
      bucket.push(item);
    }

    return groups;
  });

  ngOnInit(): void {
    this.feedbackService.portal.set('workspace');

    // Initialize auth service if not already
    if (!this.authService.isInitialized()) {
      this.authService.initialize();
    }

    // Start action center polling (count every 60s)
    this.actionCenter.startPolling();

    this.scopeCommandPalette();
  }

  ngOnDestroy(): void {
    this.unregisterShellCommands?.();
    this.commandRegistry.setAppResolver(null);
    this.commandRegistry.setApps([]);
    this.commandRegistry.setPermissionChecker(null);
  }

  /**
   * Every app known to the shell, including ones the user can't open, so a nav item is always credited to the app
   * that owns its category rather than to whichever accessible app happens to share it.
   */
  private readonly allWorkspaceApps = computed<WorkspaceApp[]>(() => {
    const apps = [...this.appSwitcher.internalApps()];
    for (const plugin of this.pluginService.workspacePlugins()) {
      if (plugin.app && !apps.some((app) => app.id === plugin.app!.id)) apps.push({ ...plugin.app, type: plugin.app.type ?? 'internal' });
    }
    return apps;
  });

  private readonly commandAreas = computed(() =>
    workspaceCommandAreas(this.allWorkspaceApps(), [...this.defaultNavItems, ...this.pluginService.workspaceNavItems()]),
  );

  private unregisterShellCommands: (() => void) | null = null;

  /**
   * Keeps the ⌘K palette to the app the user is in: the URL picks the app (the selected app where no app claims
   * the page), "Switch app…" lists the apps the switcher shows, and each nav item the user may see becomes a
   * "Go to …" command in the app whose sidebar shows it. Nav items change at runtime (e.g. a tenant's own objects),
   * so the commands follow them. Modules register richer commands (actions, dynamic search) with their app id.
   */
  private scopeCommandPalette(): void {
    this.commandRegistry.setPermissionChecker((perm) => this.authService.hasPermission(perm));
    this.commandRegistry.setAppResolver(
      (url) => resolveCommandApp(url, this.commandAreas()) ?? this.appSwitcher.selectedApp()?.id ?? null,
    );
  }

  private readonly paletteEffect = effect(() => {
    this.commandRegistry.setApps(
      this.appSwitcher.availableApps().map((app) => ({
        id: app.id,
        label: app.name,
        description: app.description,
        icon: app.icon,
        open: () => (app.type === 'admin' || app.type === 'external' ? this.appSwitcher.openExternalApp(app) : this.appSwitcher.selectApp(app.id)),
      })),
    );

    const apps = this.allWorkspaceApps();
    const navCommands: AnyCommand[] = [...this.defaultNavItems, ...this.pluginService.workspaceNavItems()]
      .filter((item) => this.filterByPermissions(item))
      .map((item) =>
        navItemCommand(
          { ...item, queryParams: item.queryParams as Record<string, unknown> | undefined },
          GLOBAL_NAV_ITEM_IDS.has(item.id) ? null : workspaceAppForNavItem(item, apps),
        ),
      );
    const accountCommands: AnyCommand[] = [
      { kind: 'action', id: 'account-profile', group: 'Account', label: 'Open my account', icon: 'user', app: null, execute: () => window.location.assign(this.appSwitcher.getAccountProfileUrl()) },
      { kind: 'action', id: 'account-sign-out', group: 'Account', label: 'Sign out', icon: 'forward', app: null, execute: () => this.logout() },
    ];
    // Each plugin's own commands, in its app, while the user can open that app (feature on, seat held).
    const openable = new Set(this.appSwitcher.availableApps().map((app) => app.id));
    const pluginCommands: AnyCommand[] = this.pluginService.workspacePlugins()
      .filter((plugin) => !plugin.app || openable.has(plugin.app.id))
      .flatMap((plugin) => (plugin.commands ?? []).map((command) => ({
        ...command,
        app: command.app !== undefined ? command.app : plugin.app?.id ?? null,
      })));
    this.unregisterShellCommands?.();
    this.unregisterShellCommands = this.commandRegistry.register([...navCommands, ...pluginCommands, ...accountCommands]);
  });

  logout(): void {
    this.authService.clearUser();
    this.oidcService.logoff().subscribe();
  }

  getInitials(name: string): string {
    return name
      .split(' ')
      .slice(0, 2)
      .map((n) => n[0])
      .join('')
      .toUpperCase();
  }

  getBadgeValue(badge: number | (() => number) | undefined): number {
    if (typeof badge === 'function') {
      return badge();
    }
    return badge ?? 0;
  }

  private filterByPermissions(item: WorkspaceNavItem): boolean {
    if (item.managerOnly && !this.authService.isManager()) {
      return false;
    }
    if (item.permission && !this.authService.hasPermission(item.permission)) {
      return false;
    }
    if (item.anyPermission && !this.authService.hasAnyPermission(item.anyPermission)) {
      return false;
    }
    if (item.feature && !this.authService.hasFeature(item.feature)) {
      return false;
    }
    return true;
  }
}
