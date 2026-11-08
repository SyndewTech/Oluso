import { Component, inject, input, signal, effect, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { ModalComponent, IconComponent, ToastService, validateForm, extractErrorMessage } from '@oluso/ui-core';
import { ApiService } from '../../services/api.service';

interface EmailDomainDto {
  id: string;
  tenantId: string;
  domain: string;
  status: 'Pending' | 'Verified' | 'Failed' | string;
  txtRecordName: string;
  txtRecordValue: string;
  createdAt: string;
  verifiedAt?: string;
  lastCheckedAt?: string;
  lastRevalidatedAt?: string;
  checkAttempts: number;
  failureReason?: string;
}

const EMAIL_DOMAIN_RULES = [
  { field: 'domain', label: 'Domain', required: true, minLength: 3 },
];

/**
 * The email domains a tenant has proven it controls. Addresses there (and at their subdomains) are trusted as the
 * organization's own: provisioning and the organization's identity providers can mark them verified, and an
 * external sign-in at that address can join the existing account.
 */
@Component({
  selector: 'admin-email-domains-section',
  standalone: true,
  imports: [CommonModule, FormsModule, ModalComponent, IconComponent],
  template: `
    <div class="space-y-3">
      <div class="flex items-start justify-between gap-4">
        <div>
          <h3 class="text-sm font-medium text-gray-700">Email domains</h3>
          <p class="text-xs text-gray-500 mt-0.5">
            Prove your organization owns the domain people's email addresses are at, such as
            <code class="font-mono text-gray-700">example.com</code>. Subdomains are included.
            Addresses at a verified domain can be marked verified by provisioning and by your own identity
            providers; anywhere else, people confirm their address with a code.
          </p>
        </div>
        <button (click)="openAddModal()" class="inline-flex items-center px-3 py-1.5 text-sm font-medium rounded-md text-white bg-primary-600 hover:bg-primary-700 flex-shrink-0">
          <ui-icon name="plus" class="h-4 w-4 mr-1.5" />
          Add email domain
        </button>
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center h-16">
          <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary-600"></div>
        </div>
      } @else if (domains().length === 0) {
        <div class="text-center py-6 text-sm text-gray-400">
          <p>No email domains yet</p>
        </div>
      } @else {
        @for (domain of domains(); track domain.id) {
          <div class="border rounded-lg p-4">
            <div class="flex items-center justify-between">
              <div>
                <span class="text-sm font-medium text-gray-900 font-mono">{{ domain.domain }}</span>
                <span [class]="'ml-2 inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ' + statusClass(domain)">
                  {{ domain.status }}
                </span>
              </div>
              <div class="flex items-center gap-3">
                @if (domain.status !== 'Verified') {
                  <button (click)="verify(domain)" [disabled]="verifyingId() === domain.id" class="text-sm text-primary-600 hover:text-primary-800 disabled:opacity-50">
                    {{ verifyingId() === domain.id ? 'Checking...' : 'Verify now' }}
                  </button>
                }
                <button (click)="removingDomain.set(domain)" class="text-sm text-red-600 hover:text-red-800">Remove</button>
              </div>
            </div>

            @if (domain.status !== 'Verified') {
              <div class="bg-yellow-50 border border-yellow-200 rounded-md p-3 mt-3">
                <p class="text-xs font-medium text-yellow-800 mb-2">Add this DNS <strong>TXT</strong> record at your DNS provider:</p>
                <div class="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
                  <div>
                    <span class="text-yellow-700">Name:</span>
                    <div class="mt-0.5 bg-white border border-yellow-200 rounded px-2 py-1 font-mono flex items-center justify-between">
                      <span class="truncate">{{ domain.txtRecordName }}</span>
                      <button (click)="copy(domain.txtRecordName)" class="ml-1 text-yellow-600 hover:text-yellow-800" aria-label="Copy record name">
                        <ui-icon name="clipboard" class="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                  <div>
                    <span class="text-yellow-700">Value:</span>
                    <div class="mt-0.5 bg-white border border-yellow-200 rounded px-2 py-1 font-mono flex items-center justify-between">
                      <span class="truncate">{{ domain.txtRecordValue }}</span>
                      <button (click)="copy(domain.txtRecordValue)" class="ml-1 text-yellow-600 hover:text-yellow-800" aria-label="Copy record value">
                        <ui-icon name="clipboard" class="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                </div>
                <p class="text-[11px] text-yellow-700 mt-2">
                  We check every few minutes for 24 hours. DNS changes can take a while to appear.
                  @if (domain.checkAttempts > 0) {
                    Checked {{ domain.checkAttempts }} time(s), last at {{ domain.lastCheckedAt | date: 'medium' }}.
                  }
                </p>
              </div>
            }

            @if (domain.failureReason) {
              <div class="bg-red-50 border border-red-200 rounded-md p-2 mt-2 text-xs text-red-700">{{ domain.failureReason }}</div>
            }

            @if (domain.status === 'Verified') {
              <p class="text-xs text-gray-500 mt-2">
                Verified {{ domain.verifiedAt | date: 'mediumDate' }}. Keep the TXT record in place: it's checked again every 30 days,
                and the domain stops being trusted 7 days after the record goes missing.
              </p>
            }
          </div>
        }
      }

      <ui-modal [open]="showAddModal()" title="Add email domain" size="md" [closeOnBackdrop]="false" (closed)="closeAddModal()">
        @if (addError()) {
          <div class="bg-red-50 border border-red-200 rounded-md p-3 mb-4">
            <p class="text-sm text-red-600 whitespace-pre-line">{{ addError() }}</p>
          </div>
        }
        <div>
          <label class="block text-sm font-medium text-gray-700" for="email-domain-input">Domain</label>
          <input id="email-domain-input" type="text" [(ngModel)]="newDomain" name="emailDomain" required placeholder="example.com"
            class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:ring-primary-500 focus:border-primary-500 sm:text-sm" />
          <p class="mt-1 text-xs text-gray-400">The part after the &#64; in your people's addresses. You'll add a TXT record to prove you own it.</p>
        </div>
        <div modal-footer class="flex justify-end gap-3 px-6 py-4 border-t border-gray-200 bg-gray-50">
          <button type="button" (click)="closeAddModal()" class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
          <button type="button" (click)="add()" [disabled]="adding()"
            class="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50">
            {{ adding() ? 'Adding...' : 'Add domain' }}
          </button>
        </div>
      </ui-modal>

      <ui-modal [open]="!!removingDomain()" title="Remove email domain" size="md" (closed)="removingDomain.set(null)">
        <p class="text-sm text-gray-600">Remove <strong class="font-mono">{{ removingDomain()?.domain }}</strong>?</p>
        <p class="text-xs text-gray-400 mt-1">
          Addresses already verified stay verified. New addresses there will need a code to be confirmed, and external
          sign-ins at this domain will no longer join existing accounts without one.
        </p>
        <div modal-footer class="flex justify-end gap-3 px-6 py-4 border-t border-gray-200 bg-gray-50">
          <button type="button" (click)="removingDomain.set(null)" class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
          <button type="button" (click)="remove()" [disabled]="removing()"
            class="px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 disabled:opacity-50">
            {{ removing() ? 'Removing...' : 'Remove' }}
          </button>
        </div>
      </ui-modal>
    </div>
  `,
})
export class EmailDomainsSectionComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly destroy$ = new Subject<void>();

  readonly tenantId = input.required<string>();

  loading = signal(true);
  domains = signal<EmailDomainDto[]>([]);
  showAddModal = signal(false);
  newDomain = '';
  adding = signal(false);
  addError = signal<string | null>(null);
  verifyingId = signal<string | null>(null);
  removingDomain = signal<EmailDomainDto | null>(null);
  removing = signal(false);

  constructor() {
    effect(() => {
      if (this.tenantId()) this.load();
    });
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  private get base(): string { return `/tenants/${this.tenantId()}/email-domains`; }

  load(): void {
    this.loading.set(true);
    this.api.get<EmailDomainDto[]>(this.base)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (d) => { this.domains.set(d); this.loading.set(false); },
        error: (err: unknown) => {
          this.loading.set(false);
          this.toast.error(extractErrorMessage(err, 'Failed to load email domains'));
        },
      });
  }

  openAddModal(): void { this.newDomain = ''; this.addError.set(null); this.showAddModal.set(true); }
  closeAddModal(): void { this.showAddModal.set(false); this.addError.set(null); }

  add(): void {
    const v = validateForm({ domain: this.newDomain }, EMAIL_DOMAIN_RULES);
    if (!v.valid) { this.addError.set(v.errorSummary); return; }

    this.adding.set(true);
    this.addError.set(null);
    this.api.post<EmailDomainDto>(this.base, { domain: this.newDomain.trim() })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.toast.success('Email domain added. Create the TXT record to verify it.');
          this.adding.set(false);
          this.closeAddModal();
          this.load();
        },
        error: (err: unknown) => {
          this.addError.set(extractErrorMessage(err, 'Failed to add the email domain'));
          this.adding.set(false);
        },
      });
  }

  verify(domain: EmailDomainDto): void {
    this.verifyingId.set(domain.id);
    this.api.post<EmailDomainDto>(`${this.base}/${domain.id}/verify`, {})
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          if (result.status === 'Verified') {
            this.toast.success(`${domain.domain} verified`);
          } else {
            this.toast.warning(result.failureReason || 'TXT record not found yet. Check the record and try again.');
          }
          this.verifyingId.set(null);
          this.load();
        },
        error: (err: unknown) => {
          this.toast.error(extractErrorMessage(err, 'Verification failed'));
          this.verifyingId.set(null);
        },
      });
  }

  remove(): void {
    const domain = this.removingDomain();
    if (!domain) return;
    this.removing.set(true);
    this.api.delete(`${this.base}/${domain.id}`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.toast.success('Email domain removed');
          this.removingDomain.set(null);
          this.removing.set(false);
          this.load();
        },
        error: (err: unknown) => {
          this.toast.error(extractErrorMessage(err, 'Failed to remove the email domain'));
          this.removing.set(false);
        },
      });
  }

  copy(text: string): void {
    navigator.clipboard.writeText(text).then(() => this.toast.success('Copied'));
  }

  statusClass(domain: EmailDomainDto): string {
    switch (domain.status) {
      case 'Verified': return 'bg-green-100 text-green-800';
      case 'Pending': return 'bg-yellow-100 text-yellow-800';
      case 'Failed': return 'bg-red-100 text-red-800';
      default: return 'bg-gray-100 text-gray-500';
    }
  }
}
