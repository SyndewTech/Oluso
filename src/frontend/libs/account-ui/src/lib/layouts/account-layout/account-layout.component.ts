import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import type { NavItem } from '@oluso/ui-core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { IconComponent, ToastContainerComponent } from '@oluso/ui-core';
import { AccountPluginService } from '../../services/account-plugin.service';
import { AccountAuthService } from '../../services/account-auth.service';
import { workspaceUrl } from '../../auth/oidc.config';

@Component({
  selector: 'account-layout',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, RouterOutlet, IconComponent, ToastContainerComponent],
  template: `
    <oluso-toast-container />
    <div class="min-h-screen bg-gray-50">
      <!-- Mobile menu button -->
      <div class="lg:hidden fixed top-0 left-0 right-0 z-20 bg-white border-b border-gray-200 px-4 py-3">
        <div class="flex items-center justify-between">
          <h1 class="text-lg font-semibold text-gray-900">My Account</h1>
          <button
            type="button"
            class="p-2 text-gray-500 hover:text-gray-700"
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
          class="lg:hidden fixed inset-0 z-30 bg-gray-600 bg-opacity-50"
          (click)="mobileMenuOpen.set(false)"
          (keydown.escape)="mobileMenuOpen.set(false)"
          role="presentation"
        ></div>
      }

      <!-- Mobile sidebar -->
      <div
        class="lg:hidden fixed inset-y-0 left-0 z-40 w-64 bg-white transform transition-transform duration-200 ease-in-out"
        [class.translate-x-0]="mobileMenuOpen()"
        [class.-translate-x-full]="!mobileMenuOpen()"
      >
        <div class="h-full overflow-y-auto flex flex-col">
          <ng-container *ngTemplateOutlet="userTemplate" />
          <nav class="flex-1 p-4 space-y-1">
            <ng-container *ngTemplateOutlet="navTemplate" />
          </nav>
          <div class="p-4 border-t border-gray-200 space-y-1">
            @if (workspaceUrl) {
              <a
                [href]="workspaceUrl"
                class="flex items-center gap-3 w-full px-3 py-2 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-100"
              >
                <ui-icon name="arrow-left" class="h-5 w-5" />
                Back to workspace
              </a>
            }
            <button
              type="button"
              class="flex items-center gap-3 w-full px-3 py-2 rounded-md text-sm font-medium text-red-600 hover:bg-red-50"
              (click)="logout()"
            >
              <ui-icon name="logout" class="h-5 w-5" />
              Sign Out
            </button>
          </div>
        </div>
      </div>

      <div class="flex">
        <!-- Desktop sidebar -->
        <div class="hidden lg:flex lg:flex-col lg:w-64 lg:fixed lg:inset-y-0 bg-white border-r border-gray-200">
          <div class="flex-1 flex flex-col overflow-y-auto">
            <ng-container *ngTemplateOutlet="userTemplate" />
            <nav class="flex-1 p-4 space-y-1">
              <ng-container *ngTemplateOutlet="navTemplate" />
            </nav>
            <div class="p-4 border-t border-gray-200 space-y-1">
              @if (workspaceUrl) {
                <a
                  [href]="workspaceUrl"
                  class="flex items-center gap-3 w-full px-3 py-2 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-100"
                >
                  <ui-icon name="arrow-left" class="h-5 w-5" />
                  Back to workspace
                </a>
              }
              <button
                type="button"
                class="flex items-center gap-3 w-full px-3 py-2 rounded-md text-sm font-medium text-red-600 hover:bg-red-50"
                (click)="logout()"
              >
                <ui-icon name="logout" class="h-5 w-5" />
                Sign Out
              </button>
            </div>
          </div>
        </div>

        <!-- Main content -->
        <div class="flex-1 lg:pl-64">
          <main class="py-6 px-4 sm:px-6 lg:px-8 pt-20 lg:pt-6">
            <div class="max-w-4xl mx-auto">
              <router-outlet />
            </div>
          </main>
        </div>
      </div>
    </div>

    <!-- Signed-in user block, shared by both sidebars -->
    <ng-template #userTemplate>
      @if (me(); as me) {
        <a
          routerLink="/profile"
          class="flex items-center gap-3 px-4 py-3 border-b border-gray-200 hover:bg-gray-50"
          (click)="mobileMenuOpen.set(false)"
        >
          @if (picture(); as picture) {
            <img
              [src]="picture"
              [alt]="me.displayName"
              class="h-10 w-10 shrink-0 rounded-full object-cover"
              (error)="pictureFailed.set(true)"
            />
          } @else {
            <div class="h-10 w-10 shrink-0 rounded-full bg-primary-600 flex items-center justify-center text-sm font-semibold text-white">
              {{ initials() }}
            </div>
          }
          <div class="min-w-0">
            <p class="text-sm font-medium text-gray-900 truncate">{{ me.displayName }}</p>
            @if (me.email) {
              <p class="text-xs text-gray-500 truncate">{{ me.email }}</p>
            }
          </div>
        </a>
      }
    </ng-template>

    <!-- Shared nav template -->
    <ng-template #navTemplate>
      @if (moduleNavItems().length === 0) {
        <!-- No module pages for this person: the account pages are the whole menu -->
        @for (item of coreNavItems; track item.id ?? item.path) {
          <ng-container *ngTemplateOutlet="navLink; context: { $implicit: item, nested: false }" />
        }
      } @else {
        <!-- Module pages lead; the account's own pages fold into one group after them -->
        @for (item of groupedNavItems(); track item.id ?? item.path; let first = $first) {
          @if (item.children && item.children.length > 0) {
            <div [class.pt-1]="!first" [class.mt-3]="item.id === accountGroupId" [class.border-t]="item.id === accountGroupId" [class.border-gray-200]="item.id === accountGroupId" [class.pt-3]="item.id === accountGroupId">
              <button
                type="button"
                class="flex items-center justify-between w-full px-3 py-2 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-100"
                [attr.aria-expanded]="isSectionExpanded(sectionId(item))"
                (click)="toggleSection(sectionId(item))"
              >
                <span class="flex items-center gap-3">
                  @if (item.icon) {
                    <ui-icon [name]="item.icon" class="h-5 w-5" />
                  }
                  {{ item.label }}
                </span>
                <ui-icon
                  [name]="isSectionExpanded(sectionId(item)) ? 'chevron-up' : 'chevron-down'"
                  class="h-4 w-4 text-gray-400"
                />
              </button>
              @if (isSectionExpanded(sectionId(item))) {
                <div class="mt-1 space-y-1">
                  @for (child of item.children; track child.id ?? child.path) {
                    <ng-container *ngTemplateOutlet="navLink; context: { $implicit: child, nested: true }" />
                  }
                </div>
              }
            </div>
          } @else {
            <ng-container *ngTemplateOutlet="navLink; context: { $implicit: item, nested: false }" />
          }
        }
      }
    </ng-template>

    <ng-template #navLink let-item let-nested="nested">
      <a
        [routerLink]="item.path"
        routerLinkActive="bg-primary-50 text-primary-700"
        [routerLinkActiveOptions]="{ exact: item.path === '/profile' }"
        class="flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-100"
        [class.pl-9]="nested"
        (click)="mobileMenuOpen.set(false)"
      >
        @if (item.icon) {
          <ui-icon [name]="item.icon" class="h-5 w-5" />
        }
        {{ item.label }}
      </a>
    </ng-template>
  `,
})
export class AccountLayoutComponent {
  private readonly oidcService = inject(OidcSecurityService);
  private readonly accountPluginService = inject(AccountPluginService);
  private readonly accountAuth = inject(AccountAuthService);

  readonly mobileMenuOpen = signal(false);
  readonly workspaceUrl = workspaceUrl;
  readonly me = this.accountAuth.me;
  readonly pictureFailed = signal(false);
  readonly picture = computed(() => (this.pictureFailed() ? null : this.me()?.profilePictureUrl || null));
  readonly initials = computed(() => {
    const name = this.me()?.displayName || this.me()?.email || '';
    return (
      name
        .split(' ')
        .slice(0, 2)
        .map((n) => n[0])
        .join('')
        .toUpperCase() || '?'
    );
  });
  private readonly router = inject(Router);
  private readonly expandedSections = signal<Set<string>>(new Set());

  readonly coreNavItems = this.accountPluginService.coreNavigationItems;
  readonly moduleNavItems = this.accountPluginService.pluginNavigationItems;
  readonly accountGroupId = 'account';

  /** With module pages: each module's group, then the account's own pages folded into one group. */
  readonly groupedNavItems = computed<NavItem[]>(() => [
    ...this.moduleNavItems(),
    { id: this.accountGroupId, path: '/profile', label: 'Account & security', icon: 'user', children: [...this.coreNavItems] },
  ]);

  /** Module groups already opened by default once, so a group the person closes stays closed. */
  private readonly seenGroups = new Set<string>();

  /** Navigations finished inside this layout; the first is the page the person landed on. */
  private navigations = 0;

  constructor() {
    // The group holding the page being shown opens so the person can see where they are, except the account
    // group for the landing page: with module groups it starts closed even when the landing page is one of its own.
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((e) => {
        this.navigations++;
        this.openGroupFor(e.urlAfterRedirects, this.navigations > 1);
      });
    // Module groups appear once /me loads and start open: they are what the person came for. Closing a group
    // again sticks.
    effect(() => {
      const modules = this.moduleNavItems();
      untracked(() => {
        const fresh = modules.map((item) => this.sectionId(item)).filter((id) => !this.seenGroups.has(id));
        fresh.forEach((id) => this.seenGroups.add(id));
        if (fresh.length > 0) this.expandedSections.update((sections) => new Set([...sections, ...fresh]));
        this.openGroupFor(this.router.url, this.navigations > 1);
      });
    });

    // A freshly uploaded picture gets a new URL; give it a chance to load
    // even if the previous one had failed.
    effect(() => {
      const url = this.me()?.profilePictureUrl;
      if (url) this.pictureFailed.set(false);
    });
  }

  toggleSection(sectionId: string): void {
    this.expandedSections.update((sections) => {
      const next = new Set(sections);
      if (next.has(sectionId)) {
        next.delete(sectionId);
      } else {
        next.add(sectionId);
      }
      return next;
    });
  }

  sectionId(item: NavItem): string {
    return item.id ?? item.path;
  }

  private openGroupFor(url: string, includeAccount: boolean): void {
    const path = url.split(/[?#]/, 1)[0];
    const group = this.groupedNavItems().find((item) =>
      item.children?.some((child) => child.path && (path === child.path || path.startsWith(child.path + '/')))
    );
    if (!group) return;
    const id = this.sectionId(group);
    if (id === this.accountGroupId && !includeAccount) return;
    if (this.expandedSections().has(id)) return;
    this.expandedSections.update((sections) => new Set(sections).add(id));
  }

  isSectionExpanded(sectionId: string): boolean {
    return this.expandedSections().has(sectionId);
  }

  logout(): void {
    this.oidcService.logoff().subscribe();
  }
}
