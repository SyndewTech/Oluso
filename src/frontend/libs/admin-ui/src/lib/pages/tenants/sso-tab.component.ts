import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { IconComponent, ToastService, TAB_CONTEXT } from '@syndew/oluso-ui-core';
import type { TabContext } from '@syndew/oluso-ui-core';
import { AuthStore } from '@syndew/oluso-auth';
import { ApiService } from '../../services/api.service';
import { TenantService } from '../../services/tenant.service';
import { Tenant } from '../../models/tenant.model';
import { IdentityProvider as IdentityProviderDto } from '../../models/identity-provider.model';


interface JourneyPolicyListItem {
  id: string;
  /** What the tenant's default sign-in setting stores: it is handed to clients as ?policy=. */
  slug: string;
  name: string;
  description?: string;
  type: string;
  enabled: boolean;
  isGlobal: boolean;
}

@Component({
  selector: 'admin-sso-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="space-y-6 max-w-3xl">
      <div class="border border-gray-200 rounded-lg p-4">
        <h3 class="text-sm font-medium text-gray-900">Default identity provider for "Login with SSO"</h3>
        <p class="mt-1 text-xs text-gray-500">
          When set, host portals (admin, workspace, account) pass this provider's scheme as <code class="font-mono">domain_hint</code> on the OIDC authorize call,
          so the picker is skipped and users are sent straight to your IdP. Leave unset to show the picker.
        </p>
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center h-32 text-sm text-gray-500">Loading…</div>
      } @else if (providers().length === 0) {
        <div class="bg-amber-50 border border-amber-200 rounded-lg px-4 py-3 text-sm text-amber-800">
          No identity providers configured for this tenant yet. Add one under Federation → Identity Providers, then come back here to set it as the default.
        </div>
      } @else {
        <div class="space-y-2">
          <label class="flex items-start gap-3 p-3 border border-gray-200 rounded-md hover:border-gray-300 cursor-pointer">
            <input
              type="radio"
              name="defaultIdp"
              [value]="''"
              [(ngModel)]="selectedScheme"
              class="mt-1 h-4 w-4 text-primary-600 border-gray-300 focus:ring-primary-500"
            />
            <div class="flex-1">
              <div class="text-sm font-medium text-gray-900">No default — show picker</div>
              <div class="text-xs text-gray-500">Users see all enabled providers and pick one.</div>
            </div>
          </label>

          @for (p of providers(); track p.scheme) {
            <label
              class="flex items-start gap-3 p-3 border rounded-md cursor-pointer transition-colors"
              [class.border-primary-500]="selectedScheme === p.scheme"
              [class.bg-primary-50]="selectedScheme === p.scheme"
              [class.border-gray-200]="selectedScheme !== p.scheme"
              [class.hover:border-gray-300]="selectedScheme !== p.scheme"
              [class.opacity-60]="!p.enabled"
            >
              <input
                type="radio"
                name="defaultIdp"
                [value]="p.scheme"
                [(ngModel)]="selectedScheme"
                [disabled]="!p.enabled"
                class="mt-1 h-4 w-4 text-primary-600 border-gray-300 focus:ring-primary-500"
              />
              <div class="flex-1">
                <div class="flex items-center gap-2">
                  @if (p.iconUrl) {
                    <img [src]="p.iconUrl" alt="" class="h-4 w-4" />
                  } @else {
                    <ui-icon name="key" class="h-4 w-4 text-gray-400" />
                  }
                  <span class="text-sm font-medium text-gray-900">{{ p.displayName || p.scheme }}</span>
                  @if (!p.enabled) {
                    <span class="text-xs text-gray-400">(disabled)</span>
                  }
                </div>
                <div class="mt-0.5 text-xs text-gray-500 font-mono">{{ p.scheme }}{{ p.providerType ? ' · ' + p.providerType : '' }}</div>
              </div>
            </label>
          }
        </div>

      }

      <div class="border border-gray-200 rounded-lg p-4 space-y-3">
        <div>
          <h3 class="text-sm font-medium text-gray-900">Sign-in journey for SSO</h3>
          <p class="mt-1 text-xs text-gray-500">
            Which journey policy to run when a user signs in via the "Login with SSO" landing. Leave on
            <span class="font-medium">Use platform default</span> to fall through to the standard SignIn policy.
          </p>
        </div>
        <label class="block text-xs font-medium text-gray-700">Policy</label>
        <select
          [(ngModel)]="selectedPolicyId"
          class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
        >
          <option value="">Use platform default</option>
          @for (p of policies(); track p.id) {
            <option [value]="p.slug" [disabled]="!p.enabled">
              {{ p.name }}{{ p.isGlobal ? ' (global)' : '' }}{{ !p.enabled ? ' — disabled' : '' }}
            </option>
          }
        </select>
      </div>

      <div class="border border-gray-200 rounded-lg p-4 space-y-3">
        <div>
          <h3 class="text-sm font-medium text-gray-900">UI mode for SSO sign-ins</h3>
          <p class="mt-1 text-xs text-gray-500">
            Forwarded as <code class="font-mono">ui_mode</code> on the OIDC authorize call. Affects only the
            SSO landing flow — other journeys (signup, password reset) still follow the tenant's
            <span class="font-medium">UseJourneyFlow</span> setting.
          </p>
        </div>
        <label class="flex items-start gap-3 p-2 cursor-pointer">
          <input type="radio" name="ssoUiMode" [value]="''" [(ngModel)]="selectedSsoUiMode"
                 class="mt-1 h-4 w-4 text-primary-600 border-gray-300 focus:ring-primary-500" />
          <div>
            <div class="text-sm text-gray-900">Use tenant/client default</div>
            <div class="text-xs text-gray-500">Inherit from <span class="font-mono">UseJourneyFlow</span>.</div>
          </div>
        </label>
        <label class="flex items-start gap-3 p-2 cursor-pointer">
          <input type="radio" name="ssoUiMode" value="Journey" [(ngModel)]="selectedSsoUiMode"
                 class="mt-1 h-4 w-4 text-primary-600 border-gray-300 focus:ring-primary-500" />
          <div>
            <div class="text-sm text-gray-900">Journey</div>
            <div class="text-xs text-gray-500">Run the configured policy steps (MFA, claim gates, etc.).</div>
          </div>
        </label>
        <label class="flex items-start gap-3 p-2 cursor-pointer">
          <input type="radio" name="ssoUiMode" value="Standalone" [(ngModel)]="selectedSsoUiMode"
                 class="mt-1 h-4 w-4 text-primary-600 border-gray-300 focus:ring-primary-500" />
          <div>
            <div class="text-sm text-gray-900">Standalone</div>
            <div class="text-xs text-gray-500">
              Skip the journey for SSO. The standalone login page honours <code class="font-mono">domain_hint</code> and
              auto-redirects to the matching IdP.
            </div>
          </div>
        </label>
      </div>

      <div class="border border-gray-200 rounded-lg p-4">
        <label class="flex items-start gap-3 cursor-pointer">
          <input type="checkbox" [(ngModel)]="allowPlatformOrgSignIn"
                 class="mt-1 h-4 w-4 text-primary-600 border-gray-300 rounded focus:ring-primary-500" />
          <div>
            <div class="text-sm font-medium text-gray-900">Allow sign-in from the platform login page</div>
            <div class="mt-1 text-xs text-gray-500">
              Users can choose <span class="font-medium">Sign in with your organisation</span> on the platform login page and
              enter this tenant's identifier. They are sent to this tenant's own sign-in (its identity providers, journey and MFA)
              and returned signed in. Apps registered only against the platform issuer can then reach this tenant's identity providers.
            </div>
          </div>
        </label>
      </div>

      <div class="flex items-center justify-end gap-3 pt-2">
        @if (saved()) {
          <span class="text-xs text-green-700">Saved.</span>
        }
        <button
          type="button"
          (click)="save()"
          [disabled]="saving() || !hasChanges()"
          class="inline-flex items-center px-4 py-2 text-sm font-medium rounded-md text-white bg-primary-600 hover:bg-primary-700 disabled:opacity-50"
        >
          {{ saving() ? 'Saving…' : 'Save' }}
        </button>
      </div>
    </div>
  `,
})
export class SsoTabComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly tenantService = inject(TenantService);
  private readonly toast = inject(ToastService);
  private readonly tabContext = inject<TabContext>(TAB_CONTEXT);
  private readonly authStore = inject(AuthStore);
  private readonly destroy$ = new Subject<void>();

  /**
   * Resolve the tenant the SSO default applies to. Two host contexts:
   *   - tenant-settings page: tabContext.entityType === 'tenant', entityId is the target
   *     tenant id (supports SuperAdmin cross-tenant edits).
   *   - general settings page: tabContext.entityType === 'settings', no target tenant in
   *     context — fall back to the currently-authenticated tenant.
   */
  private readonly resolvedTenantId = signal<string | null>(null);

  readonly loading = signal(true);
  readonly providers = signal<IdentityProviderDto[]>([]);
  readonly policies = signal<JourneyPolicyListItem[]>([]);
  readonly saving = signal(false);
  readonly saved = signal(false);
  readonly initialScheme = signal<string>('');
  readonly initialPolicyId = signal<string>('');
  readonly initialSsoUiMode = signal<string>('');
  readonly initialAllowPlatformOrgSignIn = signal<boolean>(true);

  selectedScheme = '';
  selectedPolicyId = '';
  selectedSsoUiMode: '' | 'Journey' | 'Standalone' = '';
  allowPlatformOrgSignIn = true;

  // Method, not a computed: ngModel-bound fields are plain properties, not
  // signals, so computed() can't track them. Evaluated each change detection
  // cycle, which is the behavior we want.
  hasChanges(): boolean {
    return this.selectedScheme !== this.initialScheme()
      || this.selectedPolicyId !== this.initialPolicyId()
      || this.selectedSsoUiMode !== this.initialSsoUiMode()
      || this.allowPlatformOrgSignIn !== this.initialAllowPlatformOrgSignIn();
  }

  ngOnInit(): void {
    // 1. Figure out which tenant we're editing.
    const tenantInContext = (this.tabContext.data as { tenant?: Tenant } | undefined)?.tenant;
    const targetTenantId = this.tabContext.entityType === 'tenant'
      ? this.tabContext.entityId
      : this.authStore.currentTenantId();

    if (!targetTenantId) {
      this.toast.error('Unable to resolve tenant for SSO settings');
      this.loading.set(false);
      return;
    }
    this.resolvedTenantId.set(targetTenantId);

    // 2. Seed initial values. The tenant-page passes them through context;
    //    the settings-page doesn't, so fetch the tenant directly in that case.
    const seedFromContext = tenantInContext && tenantInContext.id === targetTenantId
      ? tenantInContext
      : null;
    if (seedFromContext) {
      this.seedInitial(seedFromContext);
    } else {
      this.tenantService.getById(targetTenantId)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (tenant) => this.seedInitial(tenant),
          error: () => this.toast.error('Failed to load tenant'),
        });
    }

    // 3. Load the available identity providers. On a tenant's settings page they
    //    are that tenant's, fetched by tenant path; otherwise the context tenant's.
    const forNamedTenant = this.tabContext.entityType === 'tenant';
    const providers$ = forNamedTenant
      ? this.tenantService.getIdentityProviders(targetTenantId)
      : this.api.get<IdentityProviderDto[]>('/identity-providers');
    providers$
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (list) => {
          this.providers.set(list);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.toast.error('Failed to load identity providers');
        },
      });

    // 4. Load journey policies for the policy dropdown.
    const policies$ = forNamedTenant
      ? this.tenantService.getJourneyPolicies(targetTenantId)
      : this.api.get<JourneyPolicyListItem[]>('/journeys');
    policies$
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (list) => this.policies.set(
          list.filter(p => p.type === 'SignIn')
        ),
        error: () => this.toast.error('Failed to load journey policies'),
      });
  }

  private seedInitial(tenant: Tenant): void {
    const scheme = tenant.defaultIdpScheme ?? '';
    const policyId = tenant.defaultSignInPolicyId ?? '';
    const uiMode = tenant.defaultSsoUiMode ?? '';
    this.initialScheme.set(scheme);
    this.initialPolicyId.set(policyId);
    this.initialSsoUiMode.set(uiMode);
    this.initialAllowPlatformOrgSignIn.set(tenant.allowPlatformOrgSignIn);
    this.allowPlatformOrgSignIn = tenant.allowPlatformOrgSignIn;
    this.selectedScheme = scheme;
    this.selectedPolicyId = policyId;
    this.selectedSsoUiMode = uiMode as '' | 'Journey' | 'Standalone';
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  save(): void {
    if (!this.hasChanges()) return;
    const tenantId = this.resolvedTenantId();
    if (!tenantId) {
      this.toast.error('Unable to resolve tenant for SSO settings');
      return;
    }
    this.saving.set(true);
    this.saved.set(false);
    this.tenantService.update(tenantId, {
      defaultIdpScheme: this.selectedScheme,
      defaultSignInPolicyId: this.selectedPolicyId,
      defaultSsoUiMode: this.selectedSsoUiMode,
      allowPlatformOrgSignIn: this.allowPlatformOrgSignIn,
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.initialScheme.set(this.selectedScheme);
          this.initialPolicyId.set(this.selectedPolicyId);
          this.initialSsoUiMode.set(this.selectedSsoUiMode);
          this.initialAllowPlatformOrgSignIn.set(this.allowPlatformOrgSignIn);
          this.saving.set(false);
          this.saved.set(true);
          this.toast.success('SSO defaults updated');
          setTimeout(() => this.saved.set(false), 2500);
        },
        error: () => {
          this.saving.set(false);
          this.toast.error('Failed to save SSO defaults');
        },
      });
  }
}
