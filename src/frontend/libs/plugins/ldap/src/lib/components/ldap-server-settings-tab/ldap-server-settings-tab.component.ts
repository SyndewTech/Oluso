import { Component, inject, signal, OnInit, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '@oluso/ui-core';
import { LdapService } from '../../services/ldap.service';
import { LdapServerSettings, LdapServerInfo, LdapCertificateInfo } from '../../models/ldap.model';

@Component({
  selector: 'ldap-server-settings-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="space-y-6">
      <!-- Error Alert -->
      @if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4 flex items-center space-x-2">
          <ui-icon name="exclamation-circle" class="h-5 w-5 text-red-500" />
          <p class="text-red-700">{{ error() }}</p>
        </div>
      }

      <!-- Success Alert -->
      @if (success()) {
        <div class="bg-green-50 border border-green-200 rounded-lg p-4 flex items-center space-x-2">
          <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
          <p class="text-green-700">{{ success() }}</p>
        </div>
      }

      <!-- Loading State -->
      @if (loading()) {
        <div class="space-y-6">
          <div class="h-8 w-64 bg-gray-200 rounded animate-pulse"></div>
          <div class="h-48 bg-gray-200 rounded-lg animate-pulse"></div>
        </div>
      }

      @if (!loading()) {
        <!-- LDAP Server Settings -->
        <div class="space-y-6">
          <div>
            <h3 class="text-lg font-medium text-gray-900">LDAP Server Configuration</h3>
            <p class="mt-1 text-sm text-gray-500">
              Enable and configure LDAP Server functionality for this tenant
            </p>
          </div>

          <!-- Enable Toggle -->
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-700">Enable LDAP Server</p>
              <p class="text-sm text-gray-500">
                Allow external applications to authenticate users via LDAP
              </p>
            </div>
            <button
              type="button"
              (click)="handleChange('enabled', !settings.enabled)"
              class="relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
              [class.bg-primary-600]="settings.enabled"
              [class.bg-gray-200]="!settings.enabled"
              role="switch"
              [attr.aria-checked]="settings.enabled">
              <span
                class="pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out"
                [class.translate-x-5]="settings.enabled"
                [class.translate-x-0]="!settings.enabled">
              </span>
            </button>
          </div>

          <!-- Base DN -->
          <div>
            <label for="baseDn" class="block text-sm font-medium text-gray-700">Base DN (Optional)</label>
            <input
              type="text"
              id="baseDn"
              [(ngModel)]="settings.baseDn"
              (ngModelChange)="handleChange('baseDn', $event)"
              [placeholder]="serverInfo()?.baseDn || 'dc=oluso,dc=local'"
              [disabled]="!settings.enabled"
              class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500" />
            <p class="mt-2 text-sm text-gray-500">
              Custom Base DN for this tenant. Leave empty to use the global setting with tenant isolation.
            </p>
          </div>

          <!-- Organization -->
          <div>
            <label for="organization" class="block text-sm font-medium text-gray-700">Organization Name (Optional)</label>
            <input
              type="text"
              id="organization"
              [(ngModel)]="settings.organization"
              (ngModelChange)="handleChange('organization', $event)"
              [placeholder]="serverInfo()?.organization || 'Oluso'"
              [disabled]="!settings.enabled"
              class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500" />
          </div>

          <!-- Allow Anonymous Bind -->
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-700">Allow Anonymous Bind</p>
              <p class="text-sm text-gray-500">
                Allow read-only access without authentication (not recommended)
              </p>
            </div>
            <button
              type="button"
              (click)="handleChange('allowAnonymousBind', !settings.allowAnonymousBind)"
              [disabled]="!settings.enabled"
              class="relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed"
              [class.bg-primary-600]="settings.allowAnonymousBind"
              [class.bg-gray-200]="!settings.allowAnonymousBind"
              role="switch"
              [attr.aria-checked]="settings.allowAnonymousBind">
              <span
                class="pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out"
                [class.translate-x-5]="settings.allowAnonymousBind"
                [class.translate-x-0]="!settings.allowAnonymousBind">
              </span>
            </button>
          </div>

          <!-- Max Search Results -->
          <div>
            <label for="maxSearchResults" class="block text-sm font-medium text-gray-700">Max Search Results (Optional)</label>
            <input
              type="number"
              id="maxSearchResults"
              [(ngModel)]="settings.maxSearchResults"
              (ngModelChange)="handleChange('maxSearchResults', $event)"
              [placeholder]="'' + (serverInfo()?.maxSearchResults || 1000)"
              min="1"
              max="10000"
              [disabled]="!settings.enabled"
              class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500" />
          </div>

          <!-- Admin DN -->
          <div>
            <label for="adminDn" class="block text-sm font-medium text-gray-700">Admin DN (Optional)</label>
            <input
              type="text"
              id="adminDn"
              [(ngModel)]="settings.adminDn"
              (ngModelChange)="handleChange('adminDn', $event)"
              [placeholder]="serverInfo()?.adminDn || 'cn=admin,...'"
              [disabled]="!settings.enabled"
              class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500" />
            <p class="mt-2 text-sm text-gray-500">
              Admin DN for this tenant (e.g., cn=admin,o=tenantId,dc=oluso,dc=local). Required for admin operations.
            </p>
          </div>

          <!-- Admin Password -->
          <div>
            <label for="adminPassword" class="block text-sm font-medium text-gray-700">Admin Password</label>
            <div class="mt-1 relative">
              <input
                [type]="showAdminPassword() ? 'text' : 'password'"
                id="adminPassword"
                [(ngModel)]="settings.adminPassword"
                (ngModelChange)="handleChange('adminPassword', $event)"
                placeholder="Enter new password to change"
                [disabled]="!settings.enabled || !settings.adminDn"
                class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500 pr-10" />
              <button
                type="button"
                (click)="showAdminPassword.set(!showAdminPassword())"
                class="absolute inset-y-0 right-0 pr-3 flex items-center text-gray-400 hover:text-gray-600">
                @if (showAdminPassword()) {
                  <ui-icon name="eye-slash" class="h-5 w-5" />
                } @else {
                  <ui-icon name="eye" class="h-5 w-5" />
                }
              </button>
            </div>
            <p class="mt-2 text-sm text-gray-500">
              Password for the tenant admin. Only enter if you want to set/change it.
            </p>
          </div>

          <!-- Info Box -->
          <div class="bg-blue-50 border border-blue-200 rounded-lg p-4">
            <div class="flex">
              <ui-icon name="information-circle" class="h-5 w-5 text-blue-500 flex-shrink-0" />
              <div class="ml-3">
                <h4 class="text-sm font-medium text-blue-800">How LDAP Server Works</h4>
                <p class="mt-1 text-sm text-blue-700">
                  The LDAP Server exposes your tenant's users and groups via the LDAP protocol,
                  allowing legacy applications to authenticate against Oluso. Users are organized
                  under the configured Base DN with tenant isolation enabled.
                </p>
              </div>
            </div>
          </div>
        </div>

        <!-- Server Info - Only show when enabled -->
        @if (settings.enabled && serverInfo()) {
          <div class="space-y-4">
            <div>
              <h3 class="text-lg font-medium text-gray-900">Connection Details</h3>
              <p class="mt-1 text-sm text-gray-500">
                Use these settings to configure LDAP clients
              </p>
            </div>
            <div class="bg-gray-50 rounded-lg p-4 space-y-3">
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">LDAP Port:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">{{ serverInfo()?.port }}</code>
                </div>
                <button (click)="copyToClipboard('' + serverInfo()?.port)" class="ml-4 text-sm text-primary-600 hover:text-primary-700">Copy</button>
              </div>
              @if (serverInfo()?.enableSsl) {
                <div class="flex items-center justify-between">
                  <div class="flex-1 min-w-0">
                    <span class="text-sm font-medium text-gray-500">LDAPS Port:</span>
                    <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">{{ serverInfo()?.sslPort }}</code>
                  </div>
                  <button (click)="copyToClipboard('' + serverInfo()?.sslPort)" class="ml-4 text-sm text-primary-600 hover:text-primary-700">Copy</button>
                </div>
              }
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">Base DN:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">{{ serverInfo()?.baseDn }}</code>
                </div>
                <button (click)="copyToClipboard(serverInfo()?.baseDn || '')" class="ml-4 text-sm text-primary-600 hover:text-primary-700">Copy</button>
              </div>
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">User OU:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">ou={{ serverInfo()?.userOu }},{{ serverInfo()?.baseDn }}</code>
                </div>
                <button (click)="copyToClipboard('ou=' + serverInfo()?.userOu + ',' + serverInfo()?.baseDn)" class="ml-4 text-sm text-primary-600 hover:text-primary-700">Copy</button>
              </div>
            </div>

            <!-- Test Connection -->
            <div class="flex items-center gap-4">
              <button
                type="button"
                (click)="handleTestConnection()"
                [disabled]="testing()"
                class="inline-flex items-center px-4 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500 disabled:opacity-50">
                @if (testing()) {
                  <svg class="h-4 w-4 mr-2 animate-spin" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
                  </svg>
                  Testing...
                } @else {
                  <ui-icon name="play-circle" class="h-4 w-4 mr-2" />
                  Test Connection
                }
              </button>
            </div>
          </div>
        }

        <!-- TLS Certificate - Show when SSL/TLS is enabled -->
        @if (settings.enabled && (serverInfo()?.enableSsl || serverInfo()?.enableStartTls)) {
          <div class="space-y-4">
            <div>
              <h3 class="text-lg font-medium text-gray-900">TLS Certificate</h3>
              <p class="mt-1 text-sm text-gray-500">
                Configure the SSL/TLS certificate for secure LDAP connections
              </p>
            </div>

            <div class="bg-gray-50 rounded-lg p-4 space-y-3">
              <div class="flex items-start justify-between">
                <div>
                  <div class="flex items-center gap-2">
                    <ui-icon name="shield-check" class="h-5 w-5 text-gray-400" />
                    <h4 class="text-sm font-medium text-gray-900">TLS Certificate</h4>
                    @if (tlsCert()) {
                      <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium"
                            [class]="getCertSourceClass(tlsCert()?.source)">
                        {{ tlsCert()?.source }}
                      </span>
                    }
                  </div>
                  <p class="text-sm text-gray-500 mt-1">
                    Used to encrypt LDAP connections (LDAPS and STARTTLS)
                  </p>
                </div>
              </div>

              @if (tlsCert()?.hasCertificate) {
                <div class="bg-white rounded border border-gray-200 p-3 space-y-2">
                  <div class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                    <div>
                      <span class="text-gray-500">Subject:</span>
                      <span class="ml-2 text-gray-900 font-mono text-xs">{{ tlsCert()?.subject || 'N/A' }}</span>
                    </div>
                    <div>
                      <span class="text-gray-500">Thumbprint:</span>
                      <span class="ml-2 text-gray-900 font-mono text-xs">{{ (tlsCert()?.thumbprint || '').substring(0, 16) }}...</span>
                    </div>
                    <div>
                      <span class="text-gray-500">Valid From:</span>
                      <span class="ml-2 text-gray-900">{{ formatDate(tlsCert()?.notBefore) }}</span>
                    </div>
                    <div class="flex items-center gap-2">
                      <span class="text-gray-500">Valid Until:</span>
                      <span class="ml-2"
                            [class.text-red-600]="tlsCert()?.isExpired"
                            [class.text-yellow-600]="tlsCert()?.isExpiringSoon && !tlsCert()?.isExpired"
                            [class.text-gray-900]="!tlsCert()?.isExpired && !tlsCert()?.isExpiringSoon">
                        {{ formatDate(tlsCert()?.notAfter) }}
                      </span>
                      @if (tlsCert()?.isExpired) {
                        <ui-icon name="warning" class="h-4 w-4 text-red-500" />
                      }
                    </div>
                  </div>
                </div>
              } @else {
                <div class="text-sm text-gray-500 italic">
                  No certificate configured. Using global certificate if available.
                </div>
              }

              <!-- Action buttons -->
              <div class="flex items-center gap-2 pt-2">
                <button
                  type="button"
                  (click)="handleGenerateCertificate()"
                  [disabled]="certLoading()"
                  class="inline-flex items-center px-3 py-1.5 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50">
                  <svg class="h-4 w-4 mr-1.5" [class.animate-spin]="certLoading()" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                  </svg>
                  Generate New
                </button>

                @if (tlsCert()?.source !== 'Global' && tlsCert()?.hasCertificate) {
                  <button
                    type="button"
                    (click)="handleResetCertificate()"
                    [disabled]="certLoading()"
                    class="inline-flex items-center px-3 py-1.5 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50">
                    Reset to Global
                  </button>
                }
              </div>
            </div>
          </div>
        }

        <!-- Save Button -->
        <div class="flex justify-end items-center gap-4 pt-4 border-t border-gray-200">
          @if (hasChanges()) {
            <span class="text-sm text-gray-500">You have unsaved changes</span>
          }
          <button
            (click)="handleSave()"
            [disabled]="!hasChanges() || saving()"
            class="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-primary-500 disabled:opacity-50 disabled:cursor-not-allowed">
            {{ saving() ? 'Saving...' : 'Save Settings' }}
          </button>
        </div>
      }
    </div>
  `,
})
export class LdapServerSettingsTabComponent implements OnInit {
  @Input() isActive = false;
  @Output() saveComplete = new EventEmitter<void>();
  @Output() hasChangesChange = new EventEmitter<boolean>();

  private readonly ldapService = inject(LdapService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly testing = signal(false);
  readonly certLoading = signal(false);
  readonly error = signal<string | null>(null);
  readonly success = signal<string | null>(null);
  readonly hasChanges = signal(false);
  readonly showAdminPassword = signal(false);

  readonly serverInfo = signal<LdapServerInfo | null>(null);
  readonly tlsCert = signal<LdapCertificateInfo | null>(null);

  settings: LdapServerSettings = {
    enabled: false,
    allowAnonymousBind: false,
  };

  ngOnInit(): void {
    this.loadData();
  }

  private loadData(): void {
    this.loading.set(true);

    this.ldapService.getSettings().subscribe({
      next: (settingsData) => {
        this.settings = settingsData;

        this.ldapService.getServerInfo().subscribe({
          next: (info) => {
            this.serverInfo.set(info);
            this.loadCertificate();
            this.loading.set(false);
          },
          error: () => {
            this.loading.set(false);
          },
        });
      },
      error: (err) => {
        console.error('Error loading LDAP config:', err);
        this.error.set('Failed to load LDAP Server configuration');
        this.loading.set(false);
      },
    });
  }

  private loadCertificate(): void {
    this.ldapService.getTlsCertificate().subscribe({
      next: (cert) => this.tlsCert.set(cert),
      error: () => {}, // Ignore certificate errors
    });
  }

  handleChange<K extends keyof LdapServerSettings>(key: K, value: LdapServerSettings[K]): void {
    this.settings = { ...this.settings, [key]: value };
    this.hasChanges.set(true);
    this.success.set(null);
    this.hasChangesChange.emit(true);
  }

  handleSave(): void {
    this.saving.set(true);
    this.error.set(null);

    this.ldapService.updateSettings(this.settings).subscribe({
      next: () => {
        this.ldapService.getServerInfo().subscribe({
          next: (info) => {
            this.serverInfo.set(info);
            this.success.set('LDAP Server settings saved successfully');
            this.hasChanges.set(false);
            this.hasChangesChange.emit(false);
            this.saveComplete.emit();
            this.saving.set(false);
          },
          error: () => {
            this.saving.set(false);
          },
        });
      },
      error: (err) => {
        console.error('Error saving LDAP config:', err);
        this.error.set('Failed to save LDAP Server configuration');
        this.saving.set(false);
      },
    });
  }

  handleTestConnection(): void {
    this.testing.set(true);
    this.error.set(null);

    this.ldapService.testConnection().subscribe({
      next: (result) => {
        if (result.success) {
          this.success.set(result.message);
        } else {
          this.error.set(result.message);
        }
        this.testing.set(false);
      },
      error: (err) => {
        console.error('Error testing connection:', err);
        this.error.set('Failed to test LDAP connection');
        this.testing.set(false);
      },
    });
  }

  handleGenerateCertificate(): void {
    this.certLoading.set(true);

    this.ldapService.generateTlsCertificate().subscribe({
      next: (cert) => {
        this.tlsCert.set(cert);
        this.success.set('TLS certificate generated successfully');
        this.certLoading.set(false);
      },
      error: (err) => {
        console.error('Error generating certificate:', err);
        this.error.set('Failed to generate TLS certificate');
        this.certLoading.set(false);
      },
    });
  }

  handleResetCertificate(): void {
    this.certLoading.set(true);

    this.ldapService.resetTlsCertificate().subscribe({
      next: () => {
        this.loadCertificate();
        this.success.set('TLS certificate reset to global');
        this.certLoading.set(false);
      },
      error: (err) => {
        console.error('Error resetting certificate:', err);
        this.error.set('Failed to reset TLS certificate');
        this.certLoading.set(false);
      },
    });
  }

  copyToClipboard(text: string): void {
    navigator.clipboard.writeText(text);
  }

  formatDate(dateStr?: string): string {
    if (!dateStr) return 'N/A';
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  getCertSourceClass(source?: string): string {
    switch (source) {
      case 'Global':
        return 'bg-gray-100 text-gray-800';
      case 'Auto':
        return 'bg-blue-100 text-blue-800';
      case 'Uploaded':
        return 'bg-green-100 text-green-800';
      case 'NotConfigured':
        return 'bg-yellow-100 text-yellow-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }
}
