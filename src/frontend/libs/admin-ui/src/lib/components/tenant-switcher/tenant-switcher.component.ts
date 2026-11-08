import { Component, inject, signal, computed, OnInit, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, of, catchError } from 'rxjs';
import { AuthProvider, AuthStore } from '@oluso/auth';
import { IconComponent, ToastService, extractErrorMessage } from '@oluso/ui-core';
import { ContextService } from '../../services/context.service';
import {
  TenantSwitch,
  OrganizationSwitch,
  getEnvironmentColor,
} from '../../models/context.model';

@Component({
  selector: 'admin-tenant-switcher',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    @if (showSwitcher()) {
    <!-- Trigger Button -->
    <button
      (click)="toggleDropdown()"
      [disabled]="switching()"
      [attr.aria-busy]="switching()"
      class="flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
      [class.text-gray-900]="currentTenant()"
      [class.text-gray-500]="!currentTenant()"
    >
      @if (switchingTo(); as target) {
        <span class="inline-block h-4 w-4 shrink-0 rounded-full border-2 border-primary-600 border-t-transparent animate-spin" aria-hidden="true"></span>
        <span class="max-w-[180px] truncate">Switching to {{ target }}…</span>
      } @else {
      <!-- Building icon -->
      <ui-icon name="building" class="h-5 w-5 text-gray-400" />
      <span class="max-w-[180px] truncate">
        {{ currentTenant() ? (currentTenant()!.displayName || currentTenant()!.name) : 'Select Tenant' }}
      </span>
      }
      @if (currentTenant() && !switching()) {
        <span
          class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium"
          [class]="getEnvBadgeClasses(currentTenant()!.environment)"
        >
          {{ currentTenant()!.environment }}
        </span>
      }
      <!-- Chevron -->
      <ui-icon name="chevron-down" class="h-4 w-4 text-gray-400" />
    </button>

    <!-- Dropdown Menu -->
    @if (isOpen()) {
      <div
        class="absolute right-0 z-50 mt-2 w-80 origin-top-right rounded-lg bg-white shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none"
      >
        <div class="p-3">
          <!-- Search input -->
          <div class="relative mb-3">
            <ui-icon name="search" class="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
            <input
              type="text"
              placeholder="Search tenants..."
              [(ngModel)]="searchQuery"
              class="w-full rounded-md border border-gray-300 py-2 pl-9 pr-3 text-sm placeholder-gray-400 focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500"
            />
          </div>

          <!-- Organization filter (for super admins with multiple orgs) -->
          @if (isSuperAdmin() && organizations().length > 1) {
            <div class="mb-3">
              <select
                [(ngModel)]="selectedOrgId"
                (ngModelChange)="onOrgChange($event)"
                class="w-full rounded-md border border-gray-300 py-2 px-3 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
                <option value="">All Organizations</option>
                @for (org of organizations(); track org.id) {
                  <option [value]="org.id">{{ org.name }} ({{ org.tenantCount }} tenants)</option>
                }
              </select>
            </div>
          }

          <!-- Clear tenant option for super admins -->
          @if (isSuperAdmin() && authStore.tenantSwitched() && homeTenant() && homeTenant()!.id !== authStore.currentTenantId()) {
            <button
              (click)="clearTenant()"
              class="flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm hover:bg-gray-100"
            >
              <ui-icon name="arrow-path" class="h-4 w-4 text-gray-400" />
              <span class="text-gray-600">Clear tenant context</span>
            </button>
            <div class="my-2 border-t border-gray-100"></div>
          }

          <!-- Loading state -->
          @if (loading()) {
            <div class="py-4 text-center text-sm text-gray-500">Loading tenants...</div>
          }

          <!-- Empty state -->
          @if (!loading() && filteredTenants().length === 0) {
            <div class="py-4 text-center text-sm text-gray-500">
              {{ searchQuery ? 'No tenants match your search' : 'No tenants available' }}
            </div>
          }

          <!-- Tenant list -->
          @if (!loading() && filteredTenants().length > 0) {
            <div class="max-h-64 overflow-y-auto">
              @for (group of groupedTenantsEntries(); track group.name) {
                @if (group.name !== 'ungrouped') {
                  <div class="px-3 py-1.5 text-xs font-medium uppercase tracking-wider text-gray-500">
                    {{ group.name }}
                  </div>
                }
                @for (tenant of group.tenants; track tenant.id) {
                  <button
                    (click)="selectTenant(tenant)"
                    [disabled]="!tenant.enabled || switching()"
                    class="flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm hover:bg-gray-100"
                    [class.cursor-not-allowed]="!tenant.enabled || switching()"
                    [class.opacity-50]="!tenant.enabled || (switching() && switchingId() !== tenant.id)"
                    [class.bg-primary-50]="tenant.id === authStore.currentTenantId()"
                  >
                    <div class="flex-1 text-left">
                      <div class="flex items-center gap-2">
                        <span class="font-medium text-gray-900">
                          {{ tenant.displayName || tenant.name }}
                        </span>
                        @if (switchingId() === tenant.id) {
                          <span class="inline-block h-4 w-4 shrink-0 rounded-full border-2 border-primary-600 border-t-transparent animate-spin" aria-hidden="true"></span>
                        } @else if (tenant.id === authStore.currentTenantId()) {
                          <ui-icon name="check" class="h-4 w-4 text-primary-600" />
                        }
                      </div>
                      <div class="flex items-center gap-2 text-xs text-gray-500">
                        <span>{{ tenant.identifier }}</span>
                        <span
                          class="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium"
                          [class]="getEnvBadgeClasses(tenant.environment)"
                        >
                          {{ tenant.environment }}
                        </span>
                        @if (tenant.userRole && tenant.userRole !== 'superadmin') {
                          <span class="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-800">
                            {{ tenant.userRole }}
                          </span>
                        }
                      </div>
                    </div>
                  </button>
                }
              }
            </div>
          }
        </div>
      </div>
    }
    }
  `,
  host: {
    class: 'relative inline-block',
  },
})
export class TenantSwitcherComponent implements OnInit {
  private readonly toast = inject(ToastService);
  readonly authStore = inject(AuthStore);
  private readonly contextService = inject(ContextService);
  private readonly authProvider = inject(AuthProvider);
  private readonly elementRef = inject(ElementRef);

  isOpen = signal(false);
  loading = signal(false);
  /** The tenant being switched to (id and name); set until the page reloads into it. */
  private readonly switchTarget = signal<{ id: string; name: string } | null>(null);
  readonly switching = computed(() => this.switchTarget() !== null);
  readonly switchingId = computed(() => this.switchTarget()?.id ?? null);
  readonly switchingTo = computed(() => this.switchTarget()?.name ?? null);
  tenants = signal<TenantSwitch[]>([]);
  organizations = signal<OrganizationSwitch[]>([]);
  searchQuery = '';
  selectedOrgId = '';

  isSuperAdmin = computed(() => {
    const user = this.authStore.user();
    return user?.roles?.includes('SuperAdmin') || false;
  });

  /** Only show the switcher if the user has access to more than one tenant */
  showSwitcher = computed(() => this.tenants().length > 1 || this.isSuperAdmin());

  homeTenant = computed(() => this.tenants().find((t) => t.isHome) ?? null);

  currentTenant = computed(() => {
    const tenantId = this.authStore.currentTenantId();
    if (!tenantId) return null;
    return this.tenants().find((t) => t.id === tenantId) || null;
  });

  filteredTenants = computed(() => {
    let result = this.tenants();

    // Filter by selected org
    if (this.selectedOrgId) {
      result = result.filter((t) => t.organizationId === this.selectedOrgId);
    }

    // Filter by search query
    if (this.searchQuery) {
      const query = this.searchQuery.toLowerCase();
      result = result.filter(
        (t) =>
          t.name.toLowerCase().includes(query) ||
          t.identifier.toLowerCase().includes(query) ||
          (t.displayName && t.displayName.toLowerCase().includes(query))
      );
    }

    return result;
  });

  groupedTenantsEntries = computed(() => {
    const filtered = this.filteredTenants();

    if (this.selectedOrgId || !this.isSuperAdmin()) {
      return [{ name: 'ungrouped', tenants: filtered }];
    }

    const groups: Record<string, TenantSwitch[]> = {};
    const orgMap = new Map(this.organizations().map((o) => [o.id, o]));

    for (const tenant of filtered) {
      const orgId = tenant.organizationId || 'none';
      const org = orgMap.get(orgId);
      const groupKey = org?.name || (tenant.organizationId ? 'Unknown Organization' : 'No Organization');

      if (!groups[groupKey]) {
        groups[groupKey] = [];
      }
      groups[groupKey].push(tenant);
    }

    return Object.entries(groups).map(([name, tenants]) => ({ name, tenants }));
  });

  ngOnInit(): void {
    this.loadData();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.isOpen.set(false);
    }
  }

  toggleDropdown(): void {
    this.isOpen.update((v) => !v);
    if (this.isOpen() && this.tenants().length === 0) {
      this.loadData();
    }
  }

  loadData(): void {
    this.loading.set(true);

    // Use catchError on each observable individually so one failure
    // doesn't prevent the other from completing (forkJoin fails if ANY source errors)
    forkJoin({
      organizations: this.contextService.getAvailableOrganizations().pipe(
        catchError((err) => {
          console.error('Failed to load organizations:', err);
          return of([] as OrganizationSwitch[]);
        })
      ),
      tenants: this.contextService.getAvailableTenants().pipe(
        catchError((err) => {
          console.error('Failed to load tenants:', err);
          return of([] as TenantSwitch[]);
        })
      ),
    }).subscribe({
      next: ({ organizations, tenants }) => {
        this.organizations.set(organizations);
        this.tenants.set(tenants);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load context data:', err);
        this.loading.set(false);
      },
    });
  }

  onOrgChange(orgId: string): void {
    this.selectedOrgId = orgId;
  }

  selectTenant(tenant: TenantSwitch): void {
    if (this.switching()) return;

    // Refresh the session into the target first. Roles are per-tenant assignments,
    // so the current token describes the tenant we're leaving — carrying it over
    // would leave every role check answering for the wrong tenant. The auth
    // library holds the new tokens, so every request and later refresh uses them.
    // Busy until the reload replaces the page, so the wait for it shows too.
    const failed = (err: unknown) => {
      this.switchTarget.set(null);
      this.toast.error(extractErrorMessage(err, `Couldn't switch to ${tenant.displayName || tenant.name}. Try again.`));
    };
    this.switchTarget.set({ id: tenant.id, name: tenant.displayName || tenant.name });
    this.authProvider.switchTenant(tenant.id).subscribe({
      next: (result) => {
        this.authStore.switchTenant(tenant.id, result.accessToken!);
        this.searchQuery = '';
        // Invalidate cached context data before reload so fresh data is fetched
        this.contextService.invalidateCache();
        window.location.reload();
      },
      error: (err) => failed(err),
    });
  }

  /**
   * Leaves the switched tenant. The session's token is for that tenant, so emptying the stored tenant alone
   * changes nothing; a switch back to the home tenant mints a token for it, as signing in does.
   */
  clearTenant(): void {
    const home = this.homeTenant();
    if (!home) return;
    this.isOpen.set(false);
    this.selectTenant(home);
  }

  getEnvBadgeClasses(environment: string): string {
    const variant = getEnvironmentColor(environment);
    switch (variant) {
      case 'success':
        return 'bg-green-100 text-green-800';
      case 'warning':
        return 'bg-yellow-100 text-yellow-800';
      case 'info':
        return 'bg-blue-100 text-blue-800';
      case 'error':
        return 'bg-red-100 text-red-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }
}
