import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, IconComponent, LocaleDatePipe } from '@syndew/oluso-ui-core';
import { WhatsAppService } from '../../services/whatsapp.service';
import {
  WhatsAppContact,
  WhatsAppSettings,
  WhatsAppTemplate,
  WhatsAppTestConnectionResult,
} from '../../models/whatsapp.model';

type Section = 'setup' | 'templates' | 'contacts' | 'test';

interface SettingsForm {
  enabled: boolean;
  accessToken: string;
  phoneNumberId: string;
  businessAccountId: string;
  appSecret: string;
  webhookVerifyToken: string;
  apiVersion: string;
  defaultCountry: string;
}

@Component({
  selector: 'admin-whatsapp-settings-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, IconComponent, LocaleDatePipe],
  template: `
    @if (loading()) {
      <div class="flex items-center justify-center py-12">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
      </div>
    } @else {
      <div class="space-y-6">
        <div class="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <div class="flex items-start gap-3">
            <ui-icon name="information-circle" class="h-5 w-5 text-blue-500 mt-0.5 flex-shrink-0" />
            <div class="text-sm text-blue-800">
              <p class="font-medium">WhatsApp Business (Meta Cloud API)</p>
              <p class="mt-1">
                One business number per organisation, shared by every module. Credentials are stored encrypted.
                Meta only accepts free-form text within 24 hours of a person's last message; anything you start
                needs an approved template, so sync your templates after connecting.
              </p>
            </div>
          </div>
        </div>

        @if (settings(); as s) {
          <div class="flex flex-wrap items-center gap-3 text-sm">
            <span class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full font-medium"
                  [class.bg-green-100]="s.isConfigured" [class.text-green-800]="s.isConfigured"
                  [class.bg-gray-100]="!s.isConfigured" [class.text-gray-600]="!s.isConfigured">
              <span class="h-2 w-2 rounded-full" [class.bg-green-500]="s.isConfigured" [class.bg-gray-400]="!s.isConfigured"></span>
              {{ s.isConfigured ? 'Configured' : 'Not configured' }}
            </span>
            @if (s.effectiveSource === 'Platform') {
              <span class="inline-flex items-center px-2.5 py-1 rounded-full bg-gray-100 text-gray-700 font-medium">Using the platform's shared number</span>
            }
            @if (s.account?.verifiedName || s.account?.displayPhoneNumber) {
              <span class="text-gray-600">{{ s.account?.verifiedName }} {{ s.account?.displayPhoneNumber }}
                @if (s.account?.qualityRating) { · quality {{ s.account?.qualityRating }} }
              </span>
            }
          </div>
        }

        <div class="border-b border-gray-200">
          <nav class="-mb-px flex space-x-4 sm:space-x-8 overflow-x-auto [scrollbar-width:thin] [&>*]:shrink-0" aria-label="Sections">
            @for (tab of tabs; track tab.id) {
              <button type="button" (click)="section.set(tab.id)"
                      class="whitespace-nowrap border-b-2 py-3 text-sm font-medium"
                      [class.border-primary-600]="section() === tab.id" [class.text-primary-700]="section() === tab.id"
                      [class.border-transparent]="section() !== tab.id" [class.text-gray-500]="section() !== tab.id">
                {{ tab.label }}
              </button>
            }
          </nav>
        </div>

        @if (error()) {
          <div class="bg-red-50 border border-red-200 rounded-md p-4"><p class="text-sm text-red-600">{{ error() }}</p></div>
        }
        @if (notice()) {
          <div class="bg-green-50 border border-green-200 rounded-md p-4 flex items-center gap-2">
            <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
            <p class="text-sm text-green-700">{{ notice() }}</p>
          </div>
        }

        @switch (section()) {
          @case ('setup') {
            <form (ngSubmit)="save()" class="space-y-5">
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700">Phone number ID <span class="text-red-500">*</span></label>
                  <input type="text" [(ngModel)]="form.phoneNumberId" name="phoneNumberId" class="mt-1 w-full" placeholder="e.g. 106540352242922" />
                  <p class="mt-1 text-xs text-gray-500">From Meta Business → WhatsApp → API Setup. Not the phone number itself.</p>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700">WhatsApp Business Account ID</label>
                  <input type="text" [(ngModel)]="form.businessAccountId" name="businessAccountId" class="mt-1 w-full" placeholder="WABA id" />
                  <p class="mt-1 text-xs text-gray-500">Needed to sync templates.</p>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700">Access token <span class="text-red-500">*</span></label>
                  <input type="password" [(ngModel)]="form.accessToken" name="accessToken" class="mt-1 w-full" autocomplete="new-password"
                         [placeholder]="settings()?.hasAccessToken ? 'Stored. Leave blank to keep.' : 'Permanent system-user token'" />
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700">App secret</label>
                  <input type="password" [(ngModel)]="form.appSecret" name="appSecret" class="mt-1 w-full" autocomplete="new-password"
                         [placeholder]="settings()?.hasAppSecret ? 'Stored. Leave blank to keep.' : 'Verifies webhook signatures'" />
                  <p class="mt-1 text-xs text-gray-500">Required for inbound messages and delivery receipts.</p>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700">Default country</label>
                  <input type="text" [(ngModel)]="form.defaultCountry" name="defaultCountry" class="mt-1 w-full uppercase" maxlength="2" placeholder="NG" />
                  <p class="mt-1 text-xs text-gray-500">Completes numbers typed without a country code (ISO code, e.g. NG, GB).</p>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700">API version</label>
                  <input type="text" [(ngModel)]="form.apiVersion" name="apiVersion" class="mt-1 w-full" placeholder="v21.0" />
                </div>
              </div>

              <label class="flex items-center gap-2">
                <input type="checkbox" [(ngModel)]="form.enabled" name="enabled" class="rounded border-gray-300 text-primary-600" />
                <span class="text-sm text-gray-700">Enabled</span>
              </label>

              <div class="rounded-lg border border-gray-200 p-4 space-y-3 bg-gray-50">
                <p class="text-sm font-medium text-gray-800">Webhook (paste into Meta → App → WhatsApp → Configuration)</p>
                @if (settings()?.webhookUrl) {
                  <div>
                    <p class="text-xs text-gray-500">Callback URL</p>
                    <code class="block text-xs break-all bg-white border rounded px-2 py-1">{{ settings()?.webhookUrl }}</code>
                  </div>
                }
                <div>
                  <label class="block text-xs text-gray-500">Verify token</label>
                  <input type="text" [(ngModel)]="form.webhookVerifyToken" name="webhookVerifyToken" class="mt-1 w-full font-mono text-xs" placeholder="Generated on first save if left blank" />
                </div>
                <p class="text-xs text-gray-500">Subscribe to the <strong>messages</strong> field. Inbound messages open the 24-hour window, STOP/START replies update consent, and delivery receipts update the notification log.</p>
              </div>

              <div class="flex items-center gap-3">
                <ui-button type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save' }}</ui-button>
                <ui-button type="button" variant="secondary" (click)="testConnection()" [disabled]="testing() || !settings()?.isConfigured">
                  {{ testing() ? 'Testing…' : 'Test connection' }}
                </ui-button>
                @if (testResult(); as t) {
                  <span class="text-sm" [class.text-green-700]="t.success" [class.text-red-600]="!t.success">{{ t.message }}</span>
                }
              </div>
            </form>
          }

          @case ('templates') {
            <div class="space-y-4">
              <div class="flex items-center justify-between">
                <p class="text-sm text-gray-600">Templates are approved in Meta Business Manager and pulled here. Only APPROVED templates can be sent.</p>
                <ui-button variant="secondary" (click)="syncTemplates()" [disabled]="syncing()">
                  <span class="flex items-center gap-1"><ui-icon name="arrow-path" class="h-4 w-4" /> {{ syncing() ? 'Syncing…' : 'Sync from Meta' }}</span>
                </ui-button>
              </div>
              @if (templates().length === 0) {
                <p class="text-sm text-gray-500 py-8 text-center">No templates synced yet.</p>
              } @else {
                <div class="overflow-x-auto rounded-lg border border-gray-200">
                  <table class="min-w-full divide-y divide-gray-200 text-sm">
                    <thead class="bg-gray-50 text-xs uppercase text-gray-500">
                      <tr><th class="px-4 py-2 text-left">Name</th><th class="px-4 py-2 text-left">Language</th><th class="px-4 py-2 text-left">Category</th><th class="px-4 py-2 text-left">Status</th><th class="px-4 py-2 text-left">Params</th><th class="px-4 py-2 text-left">Body</th></tr>
                    </thead>
                    <tbody class="divide-y divide-gray-100 bg-white">
                      @for (t of templates(); track t.id) {
                        <tr>
                          <td class="px-4 py-2 font-mono text-xs">{{ t.name }}</td>
                          <td class="px-4 py-2">{{ t.language }}</td>
                          <td class="px-4 py-2">{{ t.category }}</td>
                          <td class="px-4 py-2">
                            <span class="px-2 py-0.5 rounded-full text-xs font-medium"
                                  [class.bg-green-100]="t.status === 'APPROVED'" [class.text-green-800]="t.status === 'APPROVED'"
                                  [class.bg-amber-100]="t.status === 'PENDING'" [class.text-amber-800]="t.status === 'PENDING'"
                                  [class.bg-red-100]="t.status === 'REJECTED'" [class.text-red-800]="t.status === 'REJECTED'">{{ t.status }}</span>
                          </td>
                          <td class="px-4 py-2">{{ t.bodyParameterCount }}</td>
                          <td class="px-4 py-2 text-gray-600 max-w-md truncate" [title]="t.bodyText">{{ t.bodyText }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          }

          @case ('contacts') {
            <div class="space-y-4">
              <div class="flex flex-wrap items-center gap-3">
                <input type="text" [(ngModel)]="contactSearch" name="contactSearch" (keyup.enter)="loadContacts()" class="w-64" placeholder="Search number or name" />
                <select [(ngModel)]="contactConsent" name="contactConsent" (ngModelChange)="loadContacts()">
                  <option value="">All consent states</option>
                  <option value="OptedIn">Opted in</option>
                  <option value="OptedOut">Opted out</option>
                  <option value="Unknown">Unknown</option>
                </select>
                <ui-button variant="secondary" (click)="loadContacts()">Search</ui-button>
                <span class="text-xs text-gray-500">{{ contactTotal() }} contact(s)</span>
              </div>
              @if (contacts().length === 0) {
                <p class="text-sm text-gray-500 py-8 text-center">No contacts yet. A contact appears the first time a number is messaged or writes in.</p>
              } @else {
                <div class="overflow-x-auto rounded-lg border border-gray-200">
                  <table class="min-w-full divide-y divide-gray-200 text-sm">
                    <thead class="bg-gray-50 text-xs uppercase text-gray-500">
                      <tr><th class="px-4 py-2 text-left">Number</th><th class="px-4 py-2 text-left">Name</th><th class="px-4 py-2 text-left">Consent</th><th class="px-4 py-2 text-left">Session</th><th class="px-4 py-2 text-left">Last inbound</th><th class="px-4 py-2 text-right">Actions</th></tr>
                    </thead>
                    <tbody class="divide-y divide-gray-100 bg-white">
                      @for (c of contacts(); track c.id) {
                        <tr>
                          <td class="px-4 py-2 font-mono text-xs">{{ c.phoneNumber }}</td>
                          <td class="px-4 py-2">{{ c.displayName || '—' }}</td>
                          <td class="px-4 py-2">
                            <span class="px-2 py-0.5 rounded-full text-xs font-medium"
                                  [class.bg-green-100]="c.consentStatus === 'OptedIn'" [class.text-green-800]="c.consentStatus === 'OptedIn'"
                                  [class.bg-red-100]="c.consentStatus === 'OptedOut'" [class.text-red-800]="c.consentStatus === 'OptedOut'"
                                  [class.bg-gray-100]="c.consentStatus === 'Unknown'" [class.text-gray-700]="c.consentStatus === 'Unknown'">{{ c.consentStatus }}</span>
                            @if (c.consentSource) { <span class="ml-1 text-xs text-gray-400">via {{ c.consentSource }}</span> }
                          </td>
                          <td class="px-4 py-2 text-xs" [class.text-green-700]="c.sessionOpen" [class.text-gray-400]="!c.sessionOpen">{{ c.sessionOpen ? 'Open (free text allowed)' : 'Closed (template only)' }}</td>
                          <td class="px-4 py-2 text-xs text-gray-600">{{ c.lastInboundAt ? (c.lastInboundAt | localeDate:'medium') : '—' }}</td>
                          <td class="px-4 py-2 text-right">
                            @if (c.consentStatus === 'OptedOut') {
                              <button type="button" (click)="setConsent(c, 'OptedIn')" class="text-xs text-primary-600 hover:underline">Opt back in</button>
                            } @else {
                              <button type="button" (click)="setConsent(c, 'OptedOut')" class="text-xs text-red-600 hover:underline">Opt out</button>
                            }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          }

          @case ('test') {
            <form (ngSubmit)="sendTest()" class="space-y-4 max-w-xl">
              <div>
                <label class="block text-sm font-medium text-gray-700">Recipient</label>
                <input type="text" [(ngModel)]="testTo" name="testTo" class="mt-1 w-full" placeholder="+2348031234567" />
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700">Template</label>
                <select [(ngModel)]="testTemplate" name="testTemplate" class="mt-1 w-full">
                  <option value="">Free-form text (only inside an open session)</option>
                  @for (t of approvedTemplates(); track t.id) {
                    <option [value]="t.name + '|' + t.language">{{ t.name }} ({{ t.language }}){{ t.bodyParameterCount ? ' · ' + t.bodyParameterCount + ' param(s)' : '' }}</option>
                  }
                </select>
              </div>
              @if (testTemplate) {
                <div>
                  <label class="block text-sm font-medium text-gray-700">Template parameters (one per line, in order)</label>
                  <textarea [(ngModel)]="testParams" name="testParams" rows="3" class="mt-1 w-full"></textarea>
                </div>
              } @else {
                <div>
                  <label class="block text-sm font-medium text-gray-700">Message</label>
                  <textarea [(ngModel)]="testText" name="testText" rows="3" class="mt-1 w-full" placeholder="Hello from Oluso"></textarea>
                </div>
              }
              <div class="flex items-center gap-3">
                <ui-button type="submit" [disabled]="sendingTest()">
                  <span class="flex items-center gap-1"><ui-icon name="paper-airplane" class="h-4 w-4" /> {{ sendingTest() ? 'Sending…' : 'Send test' }}</span>
                </ui-button>
                @if (testSendMessage()) { <span class="text-sm" [class.text-green-700]="testSendOk()" [class.text-red-600]="!testSendOk()">{{ testSendMessage() }}</span> }
              </div>
            </form>
          }
        }
      </div>
    }
  `,
})
export class WhatsAppSettingsTabComponent implements OnInit {
  private readonly service = inject(WhatsAppService);

  readonly tabs: { id: Section; label: string }[] = [
    { id: 'setup', label: 'Setup' },
    { id: 'templates', label: 'Templates' },
    { id: 'contacts', label: 'Contacts & consent' },
    { id: 'test', label: 'Send a test' },
  ];

  readonly section = signal<Section>('setup');
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly testing = signal(false);
  readonly syncing = signal(false);
  readonly sendingTest = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly settings = signal<WhatsAppSettings | null>(null);
  readonly testResult = signal<WhatsAppTestConnectionResult | null>(null);
  readonly templates = signal<WhatsAppTemplate[]>([]);
  readonly approvedTemplates = signal<WhatsAppTemplate[]>([]);
  readonly contacts = signal<WhatsAppContact[]>([]);
  readonly contactTotal = signal(0);
  readonly testSendMessage = signal<string | null>(null);
  readonly testSendOk = signal(false);

  form: SettingsForm = { enabled: false, accessToken: '', phoneNumberId: '', businessAccountId: '', appSecret: '', webhookVerifyToken: '', apiVersion: '', defaultCountry: '' };
  contactSearch = '';
  contactConsent = '';
  testTo = '';
  testTemplate = '';
  testParams = '';
  testText = '';

  ngOnInit(): void {
    this.service.getSettings().subscribe({
      next: (s) => {
        this.apply(s);
        this.loading.set(false);
        this.loadTemplates();
        this.loadContacts();
      },
      error: (err) => {
        this.error.set(err?.error?.message || 'Failed to load WhatsApp settings');
        this.loading.set(false);
      },
    });
  }

  private apply(s: WhatsAppSettings): void {
    this.settings.set(s);
    this.form = {
      enabled: s.enabled,
      accessToken: '',
      phoneNumberId: s.phoneNumberId ?? '',
      businessAccountId: s.businessAccountId ?? '',
      appSecret: '',
      webhookVerifyToken: s.webhookVerifyToken ?? '',
      apiVersion: s.apiVersion ?? '',
      defaultCountry: s.defaultCountry ?? '',
    };
  }

  save(): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.service.updateSettings({
      enabled: this.form.enabled,
      accessToken: this.form.accessToken || undefined,
      phoneNumberId: this.form.phoneNumberId || undefined,
      businessAccountId: this.form.businessAccountId || undefined,
      appSecret: this.form.appSecret || undefined,
      webhookVerifyToken: this.form.webhookVerifyToken || undefined,
      apiVersion: this.form.apiVersion || undefined,
      defaultCountry: this.form.defaultCountry || undefined,
    }).subscribe({
      next: (s) => {
        this.apply(s);
        this.saving.set(false);
        this.notice.set('WhatsApp settings saved.');
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(err?.error?.message || 'Failed to save');
      },
    });
  }

  testConnection(): void {
    this.testing.set(true);
    this.testResult.set(null);
    this.service.testConnection().subscribe({
      next: (r) => {
        this.testResult.set(r);
        this.testing.set(false);
        if (r.success) this.service.getSettings().subscribe((s) => this.apply(s));
      },
      error: (err) => {
        this.testing.set(false);
        this.testResult.set({ success: false, message: err?.error?.message || 'Test failed' });
      },
    });
  }

  loadTemplates(): void {
    this.service.listTemplates().subscribe({
      next: (rows) => {
        this.templates.set(rows);
        this.approvedTemplates.set(rows.filter((t) => t.status === 'APPROVED'));
      },
      error: () => this.templates.set([]),
    });
  }

  syncTemplates(): void {
    this.syncing.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.service.syncTemplates().subscribe({
      next: (r) => {
        this.syncing.set(false);
        if (r.success) { this.notice.set(r.message ?? 'Templates synced.'); this.loadTemplates(); }
        else this.error.set(r.message ?? 'Template sync failed');
      },
      error: (err) => {
        this.syncing.set(false);
        this.error.set(err?.error?.message || 'Template sync failed');
      },
    });
  }

  loadContacts(): void {
    this.service.listContacts({ search: this.contactSearch || undefined, consent: this.contactConsent || undefined, pageSize: 50 }).subscribe({
      next: (list) => {
        this.contacts.set(list.items);
        this.contactTotal.set(list.totalCount);
      },
      error: () => this.contacts.set([]),
    });
  }

  setConsent(contact: WhatsAppContact, status: 'OptedIn' | 'OptedOut'): void {
    this.service.setConsent(contact.id, status).subscribe({
      next: (updated) => this.contacts.update((rows) => rows.map((c) => (c.id === updated.id ? updated : c))),
      error: (err) => this.error.set(err?.error?.message || 'Failed to update consent'),
    });
  }

  sendTest(): void {
    if (this.sendingTest() || !this.testTo.trim()) return;
    this.sendingTest.set(true);
    this.testSendMessage.set(null);
    const [name, language] = this.testTemplate ? this.testTemplate.split('|') : ['', ''];
    const parameters = this.testParams.split('\n').map((p) => p.trim()).filter((p) => p.length > 0);
    this.service.sendTest({
      to: this.testTo.trim(),
      text: this.testTemplate ? undefined : this.testText,
      templateName: name || undefined,
      templateLanguage: language || undefined,
      templateParameters: name ? parameters : undefined,
    }).subscribe({
      next: (r) => {
        this.sendingTest.set(false);
        this.testSendOk.set(r.success);
        this.testSendMessage.set(r.success ? `Sent (${r.messageId}) to ${r.normalizedPhone}` : r.error ?? 'Send failed');
        if (r.success) this.loadContacts();
      },
      error: (err) => {
        this.sendingTest.set(false);
        this.testSendOk.set(false);
        this.testSendMessage.set(err?.error?.message || 'Send failed');
      },
    });
  }
}
