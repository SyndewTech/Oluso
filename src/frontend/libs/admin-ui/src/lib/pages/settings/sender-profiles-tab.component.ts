import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, CardComponent, IconComponent, LocaleDatePipe, TAB_CONTEXT, TabContext } from '@oluso/ui-core';
import { Observable } from 'rxjs';
import { SenderProfilesService, TenantReplyTo } from '../../services/sender-profiles.service';
import {
  DEFAULT_SENDER_PROFILE,
  EmailProvider,
  SenderProfile,
} from '../../models/sender-profile.model';

type ViewMode = 'list' | 'detail';

interface ProviderOption {
  value: EmailProvider;
  label: string;
  description: string;
}

const PROVIDER_OPTIONS: ProviderOption[] = [
  { value: 'SendGrid', label: 'SendGrid', description: 'Cloud email API' },
  { value: 'Brevo', label: 'Brevo', description: 'Transactional email API' },
  { value: 'Cloudflare', label: 'Cloudflare', description: 'Email Sending (beta)' },
  { value: 'Smtp', label: 'SMTP', description: 'Custom mail server' },
];

@Component({
  selector: 'admin-sender-profiles-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, IconComponent, LocaleDatePipe],
  template: `
    @if (loading()) {
      <div class="flex items-center justify-center py-12">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
      </div>
    } @else if (mode() === 'list') {
      <div class="space-y-6">
        <div class="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <div class="flex items-start gap-3">
            <ui-icon name="information-circle" class="h-5 w-5 text-blue-500 mt-0.5 flex-shrink-0" />
            <div class="text-sm text-blue-800">
              <p class="font-medium">Sender Profiles</p>
              <p class="mt-1">
                Each profile holds the credentials and from-address for one outbound sender.
                Mark one as default — that's the one used by system mail (auth, password reset, workflow notifications).
                Campaign sends pick a specific profile.
              </p>
            </div>
          </div>
        </div>

        <ui-card>
          <div class="space-y-3">
            <div>
              <h3 class="text-sm font-semibold text-gray-900">Replies</h3>
              <p class="mt-0.5 text-sm text-gray-500">
                Inbox that receives replies to this tenant's mail when a sender profile has no reply-to of its own.
                A new address is confirmed by a link mailed to it before it is used. Tenants sending from the platform
                address without one fall back to the platform's reply-to.
              </p>
            </div>

            @if (replyTo()?.replyToAddress; as confirmed) {
              <div class="flex flex-wrap items-center gap-2 text-sm">
                <ui-icon name="check-circle" class="h-4 w-4 text-green-600" />
                <span>Replies go to <strong>{{ confirmed }}</strong></span>
                @if (replyTo()?.verifiedAt; as verifiedAt) {
                  <span class="text-gray-400">· confirmed {{ verifiedAt | localeDate:'mediumDate' }}</span>
                }
                <button type="button" (click)="removeReplyTo()" [disabled]="savingReplyTo()" class="ml-auto text-xs text-red-600 hover:text-red-700">Remove</button>
              </div>
            } @else {
              <p class="text-sm text-gray-500">No reply-to confirmed yet.</p>
            }

            @if (replyTo()?.pendingAddress; as pending) {
              <div class="flex flex-wrap items-center gap-2 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
                <ui-icon name="mail" class="h-4 w-4" />
                <span>
                  Confirmation sent to <strong>{{ pending }}</strong>
                  @if (replyTo()?.pendingSentAt; as sentAt) { on {{ sentAt | localeDate:'short' }} }.
                  It takes effect once confirmed.
                </span>
                <span class="ml-auto flex gap-3">
                  <button type="button" (click)="resendReplyTo()" [disabled]="savingReplyTo()" class="text-xs font-medium hover:underline">Resend</button>
                  <button type="button" (click)="cancelPendingReplyTo()" [disabled]="savingReplyTo()" class="text-xs font-medium hover:underline">Cancel</button>
                </span>
              </div>
            }

            <div class="flex flex-wrap items-end gap-3">
              <div class="flex-1 min-w-[16rem] max-w-md">
                <label for="tenant-reply-to" class="block text-xs font-medium text-gray-600 mb-1">
                  {{ replyTo()?.replyToAddress ? 'Change reply-to address' : 'Reply-to address' }}
                </label>
                <input id="tenant-reply-to" type="email" [(ngModel)]="replyToDraft" placeholder="replies@yourcompany.com"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm" />
              </div>
              <ui-button size="sm" (clicked)="requestReplyTo()" [disabled]="savingReplyTo() || !replyToDraft.trim()">
                {{ savingReplyTo() ? 'Sending...' : 'Send confirmation' }}
              </ui-button>
            </div>
          </div>
        </ui-card>

        @if (error()) {
          <div class="bg-red-50 border border-red-200 rounded-md p-4">
            <p class="text-sm text-red-600">{{ error() }}</p>
          </div>
        }

        @if (successMessage()) {
          <div class="bg-green-50 border border-green-200 rounded-md p-4">
            <div class="flex items-center gap-2">
              <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
              <p class="text-sm text-green-700">{{ successMessage() }}</p>
            </div>
          </div>
        }

        <div class="flex justify-end">
          <ui-button (click)="newProfile()">
            <span class="flex items-center gap-1">
              <ui-icon name="plus" class="h-4 w-4" />
              New Profile
            </span>
          </ui-button>
        </div>

        @if (profiles().length === 0) {
          <ui-card>
            <div class="p-12 text-center text-sm text-gray-500">
              No sender profiles yet. Create one to start sending outbound mail through your own provider.
            </div>
          </ui-card>
        } @else {
          <ui-card>
            <div class="divide-y divide-gray-200">
              @for (profile of profiles(); track profile.id) {
                <div class="p-4 flex items-center gap-4">
                  <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2">
                      <p class="text-sm font-medium text-gray-900 truncate">{{ profile.name }}</p>
                      @if (profile.isDefault) {
                        <span class="inline-flex items-center gap-1 rounded-full bg-primary-50 px-2 py-0.5 text-xs font-medium text-primary-700">
                          <ui-icon name="check-circle" class="h-3 w-3" />
                          Default
                        </span>
                      }
                      @if (!profile.isActive) {
                        <span class="inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600">
                          Inactive
                        </span>
                      }
                      @if (profile.lastTestError) {
                        <span class="inline-flex items-center gap-1 rounded-full bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700"
                              [title]="profile.lastTestError">
                          <ui-icon name="exclamation-triangle" class="h-3 w-3" />
                          Test failed
                        </span>
                      }
                    </div>
                    <p class="text-xs text-gray-500 truncate mt-0.5">
                      {{ profile.provider }} · {{ profile.fromName }} &lt;{{ profile.fromAddress }}&gt;
                    </p>
                    @if (profile.lastTestSendAt && !profile.lastTestError) {
                      <p class="text-xs text-green-600 mt-0.5">
                        Last tested {{ profile.lastTestSendAt | localeDate:'short' }}
                      </p>
                    }
                  </div>
                  <div class="flex items-center gap-2 flex-shrink-0">
                    @if (!profile.isDefault) {
                      <button type="button" (click)="markDefault(profile)"
                              class="text-xs text-primary-600 hover:text-primary-700 underline">
                        Set default
                      </button>
                    }
                    <button type="button" (click)="edit(profile)"
                            class="rounded-md p-1.5 text-gray-500 hover:bg-gray-100" title="Edit">
                      <ui-icon name="pencil" class="h-4 w-4" />
                    </button>
                    <button type="button" (click)="remove(profile)"
                            class="rounded-md p-1.5 text-red-500 hover:bg-red-50"
                            [disabled]="profile.isDefault"
                            [class.opacity-30]="profile.isDefault"
                            [class.cursor-not-allowed]="profile.isDefault"
                            [title]="profile.isDefault ? 'Cannot delete the default profile' : 'Delete'">
                      <ui-icon name="trash" class="h-4 w-4" />
                    </button>
                  </div>
                </div>
              }
            </div>
          </ui-card>
        }
      </div>
    } @else {
      <!-- DETAIL MODE -->
      <div class="space-y-6">
        @if (error()) {
          <div class="bg-red-50 border border-red-200 rounded-md p-4">
            <p class="text-sm text-red-600">{{ error() }}</p>
          </div>
        }

        @if (successMessage()) {
          <div class="bg-green-50 border border-green-200 rounded-md p-4">
            <div class="flex items-center gap-2">
              <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
              <p class="text-sm text-green-700">{{ successMessage() }}</p>
            </div>
          </div>
        }

        <ui-card>
          <div class="p-6 space-y-4">
            <h3 class="text-lg font-medium text-gray-900">
              {{ isNew() ? 'New Sender Profile' : 'Edit Sender Profile' }}
            </h3>
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Profile name</label>
                <input type="text" [(ngModel)]="form.name" placeholder="Newsletter"
                       class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
              </div>
              <div class="flex items-center gap-4 mt-6">
                <label class="flex items-center gap-2 text-sm text-gray-700">
                  <input type="checkbox" [(ngModel)]="form.isActive"
                         class="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500" />
                  Active
                </label>
                <label class="flex items-center gap-2 text-sm text-gray-700">
                  <input type="checkbox" [(ngModel)]="form.isDefault"
                         class="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500" />
                  Default (used by system mail)
                </label>
              </div>
            </div>
          </div>
        </ui-card>

        <ui-card>
          <div class="p-6 space-y-4">
            <h3 class="text-lg font-medium text-gray-900">Provider</h3>
            <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
              @for (opt of providerOptions; track opt.value) {
                <button type="button" (click)="form.provider = opt.value"
                        class="relative flex items-center gap-3 rounded-lg border-2 p-4 transition-colors"
                        [class]="form.provider === opt.value ? 'border-primary-500 bg-primary-50' : 'border-gray-200 hover:border-gray-300'">
                  <div class="flex-1 text-left">
                    <p class="text-sm font-medium text-gray-900">{{ opt.label }}</p>
                    <p class="text-xs text-gray-500">{{ opt.description }}</p>
                  </div>
                  @if (form.provider === opt.value) {
                    <ui-icon name="check-circle" class="h-5 w-5 text-primary-500" />
                  }
                </button>
              }
            </div>
          </div>
        </ui-card>

        <ui-card>
          <div class="p-6 space-y-4">
            <h3 class="text-lg font-medium text-gray-900">Sender details</h3>
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">From email address</label>
                <input type="email" [(ngModel)]="form.fromAddress" placeholder="noreply@yourcompany.com"
                       class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">From display name</label>
                <input type="text" [(ngModel)]="form.fromName" placeholder="Your Company"
                       class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
              </div>
              <div class="sm:col-span-2">
                <label class="block text-sm font-medium text-gray-700 mb-1">Reply-to address (optional)</label>
                <input type="email" [(ngModel)]="form.replyToAddress" placeholder="Defaults to From address"
                       class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
              </div>
            </div>
          </div>
        </ui-card>

        @if (form.provider === 'SendGrid') {
          <ui-card>
            <div class="p-6 space-y-4">
              <h3 class="text-lg font-medium text-gray-900">SendGrid credentials</h3>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">API key</label>
                <input type="password" [(ngModel)]="form.apiKey" placeholder="SG.xxxxxxxx"
                       class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm font-mono focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
                <p class="mt-1 text-xs text-gray-500">Stored encrypted. Leave the masked value to keep the existing key.</p>
              </div>
            </div>
          </ui-card>
        }

        @if (form.provider === 'Brevo') {
          <ui-card>
            <div class="p-6 space-y-4">
              <h3 class="text-lg font-medium text-gray-900">Brevo credentials</h3>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">API key</label>
                <input type="password" [(ngModel)]="form.apiKey" placeholder="xkeysib-xxxxxxxx"
                       class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm font-mono focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
                <p class="mt-1 text-xs text-gray-500">Stored encrypted. Leave the masked value to keep the existing key.</p>
              </div>
            </div>
          </ui-card>
        }

        @if (form.provider === 'Cloudflare') {
          <ui-card>
            <div class="p-6 space-y-4">
              <h3 class="text-lg font-medium text-gray-900">Cloudflare Email Sending</h3>
              <p class="text-xs text-gray-500">
                Sender domain must already be onboarded in your Cloudflare dashboard
                (Email → Email Sending → Verified Domains) before sends will succeed.
              </p>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Account ID</label>
                <input type="text" [(ngModel)]="form.cloudflareAccountId" placeholder="32-character account ID"
                       class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm font-mono focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">API token</label>
                <input type="password" [(ngModel)]="form.apiKey" placeholder="Token with Email Sending: Send permission"
                       class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm font-mono focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
                <p class="mt-1 text-xs text-gray-500">Stored encrypted. Leave the masked value to keep the existing token.</p>
              </div>
            </div>
          </ui-card>
        }

        @if (form.provider === 'Smtp') {
          <ui-card>
            <div class="p-6 space-y-4">
              <h3 class="text-lg font-medium text-gray-900">SMTP settings</h3>
              <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Host</label>
                  <input type="text" [(ngModel)]="form.smtpHost" placeholder="smtp.example.com"
                         class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Port</label>
                  <input type="number" [(ngModel)]="form.smtpPort"
                         class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Username</label>
                  <input type="text" [(ngModel)]="form.smtpUsername" placeholder="user@example.com"
                         class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Password</label>
                  <input type="password" [(ngModel)]="form.smtpPassword"
                         class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
                  <p class="mt-1 text-xs text-gray-500">Stored encrypted. Leave the masked value to keep the existing password.</p>
                </div>
              </div>
              <label class="flex items-center gap-2 text-sm text-gray-700">
                <input type="checkbox" [(ngModel)]="form.smtpUseSsl"
                       class="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500" />
                Use SSL/TLS
              </label>
            </div>
          </ui-card>
        }

        @if (!isNew()) {
          <ui-card>
            <div class="p-6 space-y-4">
              <h3 class="text-lg font-medium text-gray-900">Send test email</h3>
              <p class="text-sm text-gray-500">Verifies this profile's credentials by sending a real message through the provider.</p>
              <div class="flex items-center gap-3">
                <input type="email" [(ngModel)]="testEmailAddress" placeholder="test@example.com"
                       class="flex-1 max-w-xs rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500" />
                <ui-button variant="secondary" (click)="sendTest()" [disabled]="sendingTest() || !testEmailAddress">
                  @if (sendingTest()) {
                    <span class="flex items-center gap-2">
                      <div class="animate-spin h-4 w-4 border-2 border-primary-600 border-t-transparent rounded-full"></div>
                      Sending...
                    </span>
                  } @else {
                    <span class="flex items-center gap-1">
                      <ui-icon name="envelope" class="h-4 w-4" />
                      Send test
                    </span>
                  }
                </ui-button>
              </div>
            </div>
          </ui-card>
        }

        <div class="flex items-center gap-3">
          <ui-button (click)="save()" [disabled]="saving()">
            @if (saving()) {
              <span class="flex items-center gap-2">
                <div class="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full"></div>
                Saving...
              </span>
            } @else {
              {{ isNew() ? 'Create Profile' : 'Save Changes' }}
            }
          </ui-button>
          <button type="button" (click)="cancel()"
                  class="text-sm text-gray-600 hover:text-gray-700 underline">
            Cancel
          </button>
        </div>
      </div>
    }
  `,
})
export class SenderProfilesTabComponent implements OnInit {
  // On a tenant's settings page the tab edits that tenant's profiles, not the
  // switcher's; on the general settings page it edits the current tenant.
  private readonly tabContext = inject<TabContext>(TAB_CONTEXT);
  private readonly senderProfiles = inject(SenderProfilesService)
    .forTenant(this.tabContext.entityType === 'tenant' ? this.tabContext.entityId : undefined);

  readonly providerOptions = PROVIDER_OPTIONS;

  loading = signal(true);
  saving = signal(false);
  sendingTest = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  profiles = signal<SenderProfile[]>([]);
  mode = signal<ViewMode>('list');
  selectedId = signal<string | null>(null);

  isNew = computed(() => this.selectedId() === null);

  form: SenderProfile = { ...DEFAULT_SENDER_PROFILE };
  testEmailAddress = '';

  readonly replyTo = signal<TenantReplyTo | null>(null);
  readonly savingReplyTo = signal(false);
  replyToDraft = '';

  ngOnInit(): void {
    this.loadList();
    this.loadReplyTo();
  }

  private loadReplyTo(): void {
    this.senderProfiles.getReplyTo().subscribe({
      next: (status) => this.replyTo.set(status),
      error: (err) => this.error.set(err?.error?.error || 'Failed to load the reply-to address'),
    });
  }

  requestReplyTo(): void {
    const address = this.replyToDraft.trim();
    if (!address) return;
    this.runReplyTo(this.senderProfiles.requestReplyTo(address), (status) => {
      this.replyToDraft = '';
      return status.pendingAddress
        ? `A confirmation link was sent to ${status.pendingAddress}.`
        : `${address} is already the confirmed reply-to.`;
    });
  }

  resendReplyTo(): void {
    this.runReplyTo(this.senderProfiles.resendReplyTo(), (status) => `Confirmation resent to ${status.pendingAddress}.`);
  }

  cancelPendingReplyTo(): void {
    this.runReplyTo(this.senderProfiles.cancelPendingReplyTo(), () => 'Pending confirmation cancelled.');
  }

  removeReplyTo(): void {
    this.runReplyTo(this.senderProfiles.requestReplyTo(null), () => 'Reply-to address removed.');
  }

  private runReplyTo(call: Observable<TenantReplyTo>, success: (status: TenantReplyTo) => string): void {
    this.savingReplyTo.set(true);
    this.error.set(null);
    this.successMessage.set(null);
    call.subscribe({
      next: (status) => {
        this.replyTo.set(status);
        this.savingReplyTo.set(false);
        this.successMessage.set(success(status));
      },
      error: (err) => {
        this.savingReplyTo.set(false);
        this.error.set(err?.error?.error || 'The reply-to change could not be made');
      },
    });
  }

  private loadList(): void {
    this.loading.set(true);
    this.error.set(null);

    this.senderProfiles.list().subscribe({
      next: (profiles) => {
        this.profiles.set(profiles);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load sender profiles:', err);
        this.error.set(err?.error?.error || 'Failed to load sender profiles');
        this.loading.set(false);
      },
    });
  }

  newProfile(): void {
    this.form = { ...DEFAULT_SENDER_PROFILE };
    this.selectedId.set(null);
    this.testEmailAddress = '';
    this.error.set(null);
    this.successMessage.set(null);
    this.mode.set('detail');
  }

  edit(profile: SenderProfile): void {
    this.form = { ...DEFAULT_SENDER_PROFILE, ...profile };
    this.selectedId.set(profile.id);
    this.testEmailAddress = '';
    this.error.set(null);
    this.successMessage.set(null);
    this.mode.set('detail');
  }

  cancel(): void {
    this.mode.set('list');
    this.error.set(null);
    this.successMessage.set(null);
  }

  save(): void {
    const selectedId = this.selectedId();
    if (!this.isNew() && !selectedId) return;
    this.saving.set(true);
    this.error.set(null);
    this.successMessage.set(null);

    const request$ = this.isNew() || !selectedId
      ? this.senderProfiles.create(this.form)
      : this.senderProfiles.update(selectedId, this.form);

    request$.subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.successMessage.set('Profile saved.');
        this.selectedId.set(saved.id);
        this.form = { ...DEFAULT_SENDER_PROFILE, ...saved };
        this.loadList();
      },
      error: (err) => {
        console.error('Failed to save sender profile:', err);
        this.saving.set(false);
        this.error.set(err?.error?.error || 'Failed to save sender profile');
      },
    });
  }

  remove(profile: SenderProfile): void {
    if (profile.isDefault) return;
    if (!confirm(`Delete sender profile "${profile.name}"?`)) return;

    this.senderProfiles.delete(profile.id).subscribe({
      next: () => {
        this.successMessage.set(`Profile "${profile.name}" deleted.`);
        this.loadList();
      },
      error: (err) => {
        console.error('Failed to delete sender profile:', err);
        this.error.set(err?.error?.error || 'Failed to delete profile');
      },
    });
  }

  markDefault(profile: SenderProfile): void {
    this.senderProfiles.setDefault(profile.id).subscribe({
      next: () => {
        this.successMessage.set(`"${profile.name}" is now the default sender.`);
        this.loadList();
      },
      error: (err) => {
        console.error('Failed to set default sender profile:', err);
        this.error.set(err?.error?.error || 'Failed to set default');
      },
    });
  }

  sendTest(): void {
    const selectedId = this.selectedId();
    if (!this.testEmailAddress || this.isNew() || !selectedId) return;
    this.sendingTest.set(true);
    this.error.set(null);
    this.successMessage.set(null);

    this.senderProfiles.sendTest(selectedId, this.testEmailAddress).subscribe({
      next: (res) => {
        this.sendingTest.set(false);
        this.successMessage.set(res.message);
      },
      error: (err) => {
        console.error('Failed to send test email:', err);
        this.sendingTest.set(false);
        this.error.set(err?.error?.error || 'Failed to send test email');
      },
    });
  }
}
