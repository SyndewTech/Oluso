import { Component, inject, signal, computed, OnInit, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { AuthProvider, AuthStore } from '@oluso/auth';
import { IconComponent, ToastService, extractErrorMessage } from '@oluso/ui-core';
import { apiUrl } from '../../auth/oidc.config';

interface UserTenant {
  id: string;
  identifier: string;
  name: string;
  displayName?: string;
  logoUrl?: string;
  isCurrent: boolean;
}

interface TenantListResponse {
  tenants: UserTenant[];
  currentTenantId?: string;
  isMultiTenant: boolean;
}

@Component({
  selector: 'workspace-tenant-switcher',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    @if (showSwitcher()) {
      <div class="relative inline-block">
        <button
          (click)="toggleDropdown()"
          [disabled]="switching()"
          [attr.aria-busy]="switching()"
          class="flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-100 transition-colors"
        >
          @if (switchingTo(); as target) {
            <span class="inline-block h-4 w-4 shrink-0 rounded-full border-2 border-primary-600 border-t-transparent animate-spin" aria-hidden="true"></span>
            <span class="max-w-[160px] truncate">Switching to {{ target }}…</span>
          } @else {
            <ui-icon name="building" class="h-4 w-4 text-gray-400" />
            <span class="max-w-[160px] truncate">
              {{ currentTenantName() }}
            </span>
          }
          <ui-icon name="chevron-down" class="h-3 w-3 text-gray-400" />
        </button>

        @if (isOpen()) {
          <div class="absolute left-0 z-50 mt-1 w-64 origin-top-left rounded-lg bg-white shadow-lg ring-1 ring-black ring-opacity-5">
            <div class="py-1 max-h-64 overflow-y-auto">
              @for (tenant of tenants(); track tenant.id) {
                <button
                  (click)="selectTenant(tenant)"
                  [disabled]="switching()"
                  class="flex w-full items-center gap-3 px-4 py-2 text-sm hover:bg-gray-50"
                  [class.bg-primary-50]="tenant.id === authStore.currentTenantId()"
                  [class.cursor-not-allowed]="switching()"
                  [class.opacity-50]="switching() && switchingId() !== tenant.id"
                >
                  <div class="flex-1 text-left min-w-0">
                    <div class="flex items-center gap-2">
                      <span class="font-medium text-gray-900 truncate">
                        {{ tenant.displayName || tenant.name }}
                      </span>
                      @if (switchingId() === tenant.id) {
                        <span class="inline-block h-4 w-4 shrink-0 rounded-full border-2 border-primary-600 border-t-transparent animate-spin" aria-hidden="true"></span>
                      } @else if (tenant.id === authStore.currentTenantId()) {
                        <ui-icon name="check" class="h-4 w-4 text-primary-600 shrink-0" />
                      }
                    </div>
                    <div class="text-xs text-gray-500 truncate">{{ tenant.identifier }}</div>
                  </div>
                </button>
              }
            </div>
          </div>
        }
      </div>
    }
  `,
})
export class WorkspaceTenantSwitcherComponent implements OnInit {
  readonly authStore = inject(AuthStore);
  private readonly http = inject(HttpClient);
  private readonly authProvider = inject(AuthProvider);
  private readonly elementRef = inject(ElementRef);
  private readonly toast = inject(ToastService);

  readonly isOpen = signal(false);
  readonly tenants = signal<UserTenant[]>([]);
  /** The tenant being switched to (id and name); set until the page reloads into it. */
  private readonly switchTarget = signal<{ id: string; name: string } | null>(null);
  readonly switching = computed(() => this.switchTarget() !== null);
  readonly switchingId = computed(() => this.switchTarget()?.id ?? null);
  readonly switchingTo = computed(() => this.switchTarget()?.name ?? null);

  readonly showSwitcher = computed(() => this.tenants().length > 1);

  readonly currentTenantName = computed(() => {
    const tenantId = this.authStore.currentTenantId();
    const tenant = this.tenants().find(t => t.id === tenantId);
    return tenant?.displayName || tenant?.name || 'Select Tenant';
  });

  ngOnInit(): void {
    this.loadTenants();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.isOpen.set(false);
    }
  }

  toggleDropdown(): void {
    this.isOpen.update(v => !v);
  }

  private loadTenants(): void {
    this.http.get<TenantListResponse>(`${apiUrl}/api/account/tenants`).subscribe({
      next: (response) => this.tenants.set(response.tenants),
      error: () => this.tenants.set([]),
    });
  }

  selectTenant(tenant: UserTenant): void {
    if (tenant.id === this.authStore.currentTenantId() || this.switching()) {
      this.isOpen.set(false);
      return;
    }

    // Refresh the session into the target before adopting it. Roles and
    // permissions are per-tenant, so the old token describes the old tenant —
    // setting the header alone would leave every role check answering for where
    // we came from. The auth library holds the new tokens, so every request and
    // later refresh uses them; the store moves only once they exist, so a
    // refused switch changes nothing.
    // Busy until the reload replaces the page, so the wait for it shows too.
    const failed = (err: unknown) => {
      this.switchTarget.set(null);
      this.isOpen.set(false);
      this.toast.error(extractErrorMessage(err, `Couldn't switch to ${tenant.displayName || tenant.name}. Try again.`));
    };
    this.switchTarget.set({ id: tenant.id, name: tenant.displayName || tenant.name });
    this.authProvider.switchTenant(tenant.id).subscribe({
      next: (result) => {
        this.authStore.switchTenant(tenant.id, result.accessToken!);
        window.location.reload();
      },
      error: (err) => failed(err),
    });
  }
}
