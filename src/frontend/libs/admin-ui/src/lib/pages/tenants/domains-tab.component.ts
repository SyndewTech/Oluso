import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AuthStore } from '@oluso/auth';
import { ModalComponent, IconComponent, ToastService, TAB_CONTEXT } from '@oluso/ui-core';
import type { TabContext } from '@oluso/ui-core';
import { validateForm, extractErrorMessage } from '@oluso/ui-core';
import { ApiService } from '../../services/api.service';
import { TenantService } from '../../services/tenant.service';
import { EmailDomainsSectionComponent } from './email-domains-section.component';

interface DomainVerificationDto {
  id: string;
  tenantId: string;
  domain: string;
  status: string;
  txtRecordName: string;
  txtRecordValue: string;
  /**
   * Hostname tenants must CNAME their custom domain at to land on the
   * SaaS edge. Sourced from `Oluso:CustomDomains:CnameTarget` on the
   * platform; null when the platform admin hasn't configured it yet.
   */
  cnameTarget?: string;
  /**
   * Set when the edge provisioner (e.g. Cloudflare Custom Hostnames)
   * accepted the domain. Null on Verified-with-edge-failure or when the
   * platform has no provisioner registered (local dev).
   */
  externalProvisioningId?: string;
  createdAt: string;
  verifiedAt?: string;
  activatedAt?: string;
  lastCheckedAt?: string;
  checkAttempts: number;
  failureReason?: string;
}

const DOMAIN_RULES = [
  { field: 'domain', label: 'Domain', required: true, minLength: 4 },
];

@Component({
  selector: 'admin-domains-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, ModalComponent, IconComponent, EmailDomainsSectionComponent],
  template: `
    <div class="space-y-6">
      <!-- Automatic Subdomain -->
      <div class="border border-gray-200 rounded-lg p-4">
        <div class="flex items-start justify-between gap-4 mb-1">
          <div>
            <h3 class="text-sm font-medium text-gray-900">Automatic subdomain</h3>
            <p class="text-xs text-gray-500 mt-0.5">
              Every tenant gets a subdomain of the identity server's host —
              <code class="font-mono text-gray-700">&lt;tenant-identifier&gt;.&lt;issuerUri host&gt;</code>.
              This is the URL public visitors hit for sign-in, journey forms, and OIDC discovery.
            </p>
          </div>
        </div>
        @if (automaticSubdomain(); as s) {
          <div class="mt-3 flex items-center gap-2 bg-gray-50 border border-gray-200 rounded-lg px-3 py-2">
            <ui-icon name="globe-alt" class="h-4 w-4 text-gray-400 flex-shrink-0" />
            <code class="text-sm text-gray-800 font-mono flex-1 break-all">{{ s }}</code>
            <button
              type="button"
              (click)="copyToClipboard(automaticSubdomainUrl())"
              class="inline-flex items-center gap-1 text-xs font-medium text-primary-600 hover:text-primary-700 flex-shrink-0"
              [attr.aria-label]="'Copy ' + s"
            >
              <ui-icon name="clipboard" class="h-3.5 w-3.5" />
              Copy
            </button>
            <a
              [href]="automaticSubdomainUrl()"
              target="_blank"
              rel="noopener"
              class="inline-flex items-center gap-1 text-xs font-medium text-primary-600 hover:text-primary-700 flex-shrink-0"
            >
              <ui-icon name="arrow-top-right-on-square" class="h-3.5 w-3.5" />
              Open
            </a>
          </div>
        } @else {
          <div class="mt-3 bg-amber-50 border border-amber-200 rounded-lg px-3 py-2 text-xs text-amber-700">
            Identity server's <code class="font-mono">IssuerUri</code> isn't configured, or the tenant has no identifier yet — automatic subdomain can't be derived.
          </div>
        }
      </div>

      <!-- Custom Domains -->
      <div class="flex items-center justify-between">
        <h3 class="text-sm font-medium text-gray-700">Custom Domains</h3>
        <button (click)="openAddModal()" class="inline-flex items-center px-3 py-1.5 text-sm font-medium rounded-md text-white bg-primary-600 hover:bg-primary-700">
          <ui-icon name="plus" class="h-4 w-4 mr-1.5" />
          Add Domain
        </button>
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center h-24">
          <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary-600"></div>
        </div>
      } @else if (domains().length === 0) {
        <div class="text-center py-8 text-sm text-gray-400">
          <p>No custom domains configured</p>
          <p class="text-xs mt-1">Add a custom domain to serve your login page from your own URL</p>
        </div>
      } @else {
        <div class="space-y-3">
          @for (domain of domains(); track domain.id) {
            <div class="border rounded-lg p-4">
              <div class="flex items-center justify-between mb-2">
                <div>
                  <span class="text-sm font-medium text-gray-900 font-mono">{{ domain.domain }}</span>
                  <span [class]="'ml-2 inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ' + getStatusClass(domain)">
                    {{ statusLabel(domain) }}
                  </span>
                </div>
                <div class="flex items-center gap-2">
                  @if (canRetry(domain)) {
                    <button (click)="verify(domain)" [disabled]="verifying()" class="text-sm text-primary-600 hover:text-primary-800">
                      {{ verifying() ? 'Checking...' : verifyButtonLabel(domain) }}
                    </button>
                  }
                  <button (click)="confirmRemove(domain)" class="text-sm text-red-600 hover:text-red-800">Remove</button>
                </div>
              </div>

              <!-- Persistent failure surfacing — visible regardless of status -->
              @if (domain.failureReason && domain.status !== 'Pending') {
                <div class="bg-red-50 border border-red-200 rounded-md p-3 mt-2">
                  <p class="text-xs font-medium text-red-800 mb-0.5">Last error</p>
                  <p class="text-xs text-red-700 whitespace-pre-line">{{ domain.failureReason }}</p>
                  @if (domain.status === 'Verified') {
                    <p class="text-[11px] text-red-600 mt-1">
                      Ownership is verified, but provisioning the edge cert / hostname failed. Fix the underlying cause (Cloudflare config, plan tier, or token scope) then click <strong>Retry provisioning</strong>.
                    </p>
                  }
                </div>
              }

              @if (domain.status === 'Pending') {
                <div class="space-y-2 mt-2">
                  <!-- Step 1: TXT record (ownership verification) -->
                  <div class="bg-yellow-50 border border-yellow-200 rounded-md p-3">
                    <p class="text-xs font-medium text-yellow-800 mb-2">
                      <span class="inline-flex items-center justify-center w-4 h-4 rounded-full bg-yellow-200 text-yellow-900 mr-1 text-[10px] font-bold">1</span>
                      Verify ownership — add this DNS <strong>TXT</strong> record:
                    </p>
                    <div class="grid grid-cols-2 gap-2 text-xs">
                      <div>
                        <span class="text-yellow-600">Name:</span>
                        <div class="mt-0.5 bg-white border border-yellow-200 rounded px-2 py-1 font-mono flex items-center justify-between">
                          <span class="truncate">{{ domain.txtRecordName }}</span>
                          <button (click)="copyToClipboard(domain.txtRecordName)" class="ml-1 text-yellow-500 hover:text-yellow-700">
                            <ui-icon name="clipboard" class="h-3.5 w-3.5" />
                          </button>
                        </div>
                      </div>
                      <div>
                        <span class="text-yellow-600">Value:</span>
                        <div class="mt-0.5 bg-white border border-yellow-200 rounded px-2 py-1 font-mono flex items-center justify-between">
                          <span class="truncate">{{ domain.txtRecordValue }}</span>
                          <button (click)="copyToClipboard(domain.txtRecordValue)" class="ml-1 text-yellow-500 hover:text-yellow-700">
                            <ui-icon name="clipboard" class="h-3.5 w-3.5" />
                          </button>
                        </div>
                      </div>
                    </div>
                  </div>

                  <!-- Step 2: CNAME — moves traffic to the SaaS edge -->
                  @if (domain.cnameTarget) {
                    <div class="bg-blue-50 border border-blue-200 rounded-md p-3">
                      <p class="text-xs font-medium text-blue-800 mb-2">
                        <span class="inline-flex items-center justify-center w-4 h-4 rounded-full bg-blue-200 text-blue-900 mr-1 text-[10px] font-bold">2</span>
                        Route traffic — add a <strong>CNAME</strong> record on <span class="font-mono">{{ domain.domain }}</span> pointing at:
                      </p>
                      <div class="bg-white border border-blue-200 rounded px-2 py-1 font-mono text-xs flex items-center justify-between">
                        <span class="truncate">{{ domain.cnameTarget }}</span>
                        <button (click)="copyToClipboard(domain.cnameTarget!)" class="ml-1 text-blue-500 hover:text-blue-700">
                          <ui-icon name="clipboard" class="h-3.5 w-3.5" />
                        </button>
                      </div>
                      <p class="text-[11px] text-blue-700 mt-1.5">
                        Without the CNAME the domain still resolves to its previous host. The cert won't finish provisioning until DNS lands on our edge.
                      </p>
                    </div>
                  } @else {
                    <div class="bg-amber-50 border border-amber-200 rounded-md p-3">
                      <p class="text-xs text-amber-800">
                        <strong>CNAME target couldn't be derived.</strong> The platform's <code class="font-mono">Oluso:IssuerUri</code> appears unset or invalid — tenants need a DNS target so requests land on the SaaS edge. Fix the IssuerUri (or override with <code class="font-mono">Oluso:CustomDomains:CnameTarget</code>).
                      </p>
                    </div>
                  }

                  @if (domain.checkAttempts > 0) {
                    <p class="text-xs text-yellow-600">
                      Checked {{ domain.checkAttempts }} times. Last: {{ domain.lastCheckedAt }}
                      @if (domain.failureReason) { — {{ domain.failureReason }} }
                    </p>
                  }
                </div>
              }

              @if (domain.status === 'Verified' && domain.cnameTarget) {
                <div class="bg-blue-50 border border-blue-200 rounded-md p-3 mt-2">
                  <p class="text-xs text-blue-800">
                    Ownership verified. Make sure <span class="font-mono">{{ domain.domain }}</span> CNAMEs to
                    <span class="font-mono font-medium">{{ domain.cnameTarget }}</span>
                    so traffic reaches the SaaS edge and the cert can finish provisioning.
                  </p>
                </div>
              }

              @if (domain.status === 'Failed') {
                <div class="bg-red-50 border border-red-200 rounded-md p-2 mt-2 text-xs text-red-600">
                  {{ domain.failureReason || 'Verification timed out' }}
                </div>
              }

              @if (domain.status === 'Active' && domain.activatedAt) {
                <p class="text-xs text-gray-400 mt-1">Active since {{ domain.activatedAt }}</p>
                <div class="text-[11px] text-gray-500 mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-0.5">
                  @if (domain.externalProvisioningId) {
                    <span class="inline-flex items-center gap-1 text-emerald-700">
                      <ui-icon name="check-circle" class="h-3 w-3" />
                      Edge cert provisioned
                    </span>
                  } @else if (domain.cnameTarget) {
                    <span class="inline-flex items-center gap-1 text-amber-700">
                      <ui-icon name="exclamation-triangle" class="h-3 w-3" />
                      Edge cert not provisioned — Retry to attempt again
                    </span>
                  }
                  @if (domain.cnameTarget) {
                    <span>
                      DNS: <span class="font-mono">{{ domain.domain }}</span> should remain CNAMEd to
                      <span class="font-mono">{{ domain.cnameTarget }}</span>.
                    </span>
                  }
                </div>
              }
            </div>
          }
        </div>
      }

      @if (tenantId) {
        <div class="border-t border-gray-200 pt-6">
          <admin-email-domains-section [tenantId]="tenantId" />
        </div>
      }

      <!-- Add Domain Modal -->
      <ui-modal [open]="showAddModal()" title="Add Custom Domain" size="md" [closeOnBackdrop]="false" (closed)="closeAddModal()">
        @if (addError()) {
          <div class="bg-red-50 border border-red-200 rounded-md p-3 mb-4">
            <p class="text-sm text-red-600 whitespace-pre-line">{{ addError() }}</p>
          </div>
        }
        <div class="space-y-4">
          <div>
            <label class="block text-sm font-medium text-gray-700">Domain</label>
            <input type="text" [(ngModel)]="newDomain" name="domain" required placeholder="login.customer.com"
              class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:ring-primary-500 focus:border-primary-500 sm:text-sm" />
            <p class="mt-1 text-xs text-gray-400">Enter the domain you want to use for your login page. You'll need to create a DNS record to verify ownership.</p>
          </div>
        </div>
        <div modal-footer class="flex justify-end gap-3 px-6 py-4 border-t border-gray-200 bg-gray-50">
          <button type="button" (click)="closeAddModal()" class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
          <button type="button" (click)="addDomain()" [disabled]="adding()"
            class="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50">
            {{ adding() ? 'Adding...' : 'Add Domain' }}
          </button>
        </div>
      </ui-modal>

      <!-- Remove Confirmation -->
      <ui-modal [open]="!!removingDomain()" title="Remove Custom Domain" size="md" (closed)="removingDomain.set(null)">
        <p class="text-sm text-gray-600">Remove <strong class="font-mono">{{ removingDomain()?.domain }}</strong>?</p>
        <p class="text-xs text-gray-400 mt-1">This will stop serving your login page from this domain. OIDC discovery will no longer include it as an issuer.</p>
        <div modal-footer class="flex justify-end gap-3 px-6 py-4 border-t border-gray-200 bg-gray-50">
          <button type="button" (click)="removingDomain.set(null)" class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
          <button type="button" (click)="removeDomain()" [disabled]="removing()"
            class="px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 disabled:opacity-50">
            {{ removing() ? 'Removing...' : 'Remove' }}
          </button>
        </div>
      </ui-modal>
    </div>
  `,
})
export class DomainsTabComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly tenantService = inject(TenantService);
  private readonly tabContext = inject<TabContext>(TAB_CONTEXT);
  private readonly destroy$ = new Subject<void>();

  private readonly _tenantId = signal<string>('');
  private readonly _tenantIdentifier = signal<string | undefined>(undefined);

  get tenantId(): string { return this._tenantId(); }
  get tenantIdentifier(): string | undefined { return this._tenantIdentifier(); }

  loading = signal(true);
  domains = signal<DomainVerificationDto[]>([]);
  automaticSubdomain = signal<string>('');
  automaticSubdomainUrl = signal<string>('');

  showAddModal = signal(false);
  newDomain = '';
  adding = signal(false);
  addError = signal<string | null>(null);

  verifying = signal(false);
  removingDomain = signal<DomainVerificationDto | null>(null);
  removing = signal(false);

  ngOnInit(): void {
    if (this.tabContext.entityType === 'settings') {
      const tid = this.authStore.currentTenantId();
      if (!tid) { this.loading.set(false); return; }
      this._tenantId.set(tid);
      this.tenantService.getById(tid)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (t) => {
            this._tenantIdentifier.set(t.identifier);
            this.loadDomains();
            this.loadSubdomain();
          },
          error: () => { this.loading.set(false); },
        });
      return;
    }

    this._tenantId.set(this.tabContext.entityId);
    const data = this.tabContext.data as { tenant?: { identifier?: string } } | undefined;
    this._tenantIdentifier.set(data?.tenant?.identifier);
    this.loadDomains();
    this.loadSubdomain();
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  loadDomains(): void {
    this.loading.set(true);
    this.api.get<DomainVerificationDto[]>(`/tenants/${this.tenantId}/domains`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (d) => { this.domains.set(d); this.loading.set(false); },
        error: () => { this.loading.set(false); },
      });
  }

  loadSubdomain(): void {
    // server-info now exposes autoSubdomain explicitly so the panel always
    // shows {identifier}.{base-host} regardless of CustomDomain. Pass the
    // viewed tenant's identifier so a platform admin viewing tenant "acme"
    // sees acme's subdomain rather than the platform's. Earlier this used
    // GetIssuerAsync's resolved URL and re-prepended the identifier, which
    // produced "default.default.localhost:5050" because the resolved URL
    // already had the tenant prefix baked in.
    const id = this.tenantIdentifier;
    const path = id
      ? `/identity-providers/server-info?forTenantId=${encodeURIComponent(id)}`
      : '/identity-providers/server-info';

    this.api.get<{ issuerUri: string; autoSubdomain?: string | null }>(path)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (r) => {
          if (!r?.autoSubdomain) return;
          try {
            const url = new URL(r.autoSubdomain);
            this.automaticSubdomain.set(url.host);
            this.automaticSubdomainUrl.set(r.autoSubdomain.replace(/\/$/, ''));
          } catch {
            // ignore malformed URL
          }
        },
        // Informational panel: a failed lookup just leaves it blank.
        error: () => {
          this.automaticSubdomain.set('');
          this.automaticSubdomainUrl.set('');
        },
      });
  }

  openAddModal(): void { this.newDomain = ''; this.addError.set(null); this.showAddModal.set(true); }
  closeAddModal(): void { this.showAddModal.set(false); this.addError.set(null); }

  addDomain(): void {
    const v = validateForm({ domain: this.newDomain }, DOMAIN_RULES);
    if (!v.valid) { this.addError.set(v.errorSummary); return; }

    this.adding.set(true); this.addError.set(null);
    this.api.post<DomainVerificationDto>(`/tenants/${this.tenantId}/domains`, { domain: this.newDomain })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.toast.success('Domain added. Create the DNS TXT record to verify ownership.');
          this.closeAddModal();
          this.adding.set(false);
          this.loadDomains();
        },
        error: (err: unknown) => {
          this.addError.set(extractErrorMessage(err, 'Failed to add domain'));
          this.adding.set(false);
        },
      });
  }

  verify(domain: DomainVerificationDto): void {
    this.verifying.set(true);
    this.api.post<DomainVerificationDto>(`/tenants/${this.tenantId}/domains/${domain.id}/verify`, {})
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          if (result.status === 'Active') {
            this.toast.success(`Domain ${domain.domain} verified and activated`);
          } else if (result.status === 'Verified') {
            this.toast.success(`Domain ${domain.domain} verified`);
          } else {
            this.toast.warning(result.failureReason || 'Verification not yet successful — check DNS records');
          }
          this.verifying.set(false);
          this.loadDomains();
        },
        error: (err: unknown) => {
          this.toast.error(extractErrorMessage(err, 'Verification failed'));
          this.verifying.set(false);
        },
      });
  }

  confirmRemove(domain: DomainVerificationDto): void { this.removingDomain.set(domain); }

  removeDomain(): void {
    const domain = this.removingDomain();
    if (!domain) return;
    this.removing.set(true);
    this.api.delete(`/tenants/${this.tenantId}/domains/${domain.id}`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.toast.success('Domain removed');
          this.removingDomain.set(null);
          this.removing.set(false);
          this.loadDomains();
        },
        error: (err: unknown) => {
          this.toast.error(extractErrorMessage(err, 'Failed to remove'));
          this.removing.set(false);
        },
      });
  }

  copyToClipboard(text: string): void {
    navigator.clipboard.writeText(text).then(() => this.toast.success('Copied'));
  }

  getStatusClass(domain: DomainVerificationDto): string {
    // Verified-with-failure means TXT is good but the edge rejected the
    // hostname — surface that as a red badge so admins don't read
    // "Verified" as "everything's fine".
    if (domain.status === 'Verified' && domain.failureReason) {
      return 'bg-red-100 text-red-800';
    }
    // Active without an externalProvisioningId means the edge attempt
    // failed silently or no provisioner was registered. Amber so it's
    // visually distinct from "fully provisioned" green.
    if (domain.status === 'Active' && domain.cnameTarget && !domain.externalProvisioningId) {
      return 'bg-amber-100 text-amber-800';
    }
    switch (domain.status) {
      case 'Active': return 'bg-green-100 text-green-800';
      case 'Verified': return 'bg-blue-100 text-blue-800';
      case 'Pending': return 'bg-yellow-100 text-yellow-800';
      case 'Failed': return 'bg-red-100 text-red-800';
      default: return 'bg-gray-100 text-gray-500';
    }
  }

  statusLabel(domain: DomainVerificationDto): string {
    if (domain.status === 'Verified' && domain.failureReason) {
      return 'Verified · Edge error';
    }
    if (domain.status === 'Active' && domain.cnameTarget && !domain.externalProvisioningId) {
      return 'Active · No edge cert';
    }
    return domain.status;
  }

  /** Verify/retry button is offered for Pending, Verified, and Active-without-cert. */
  canRetry(domain: DomainVerificationDto): boolean {
    if (domain.status === 'Pending' || domain.status === 'Verified') return true;
    if (domain.status === 'Active' && domain.cnameTarget && !domain.externalProvisioningId) return true;
    return false;
  }

  verifyButtonLabel(domain: DomainVerificationDto): string {
    if (domain.status === 'Verified' && domain.failureReason) return 'Retry provisioning';
    if (domain.status === 'Active' && !domain.externalProvisioningId) return 'Retry provisioning';
    return 'Verify Now';
  }
}
