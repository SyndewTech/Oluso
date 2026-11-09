import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, IconComponent, TAB_CONTEXT } from '@syndew/oluso-ui-core';
import { SamlService } from '../../services/saml.service';
import { SamlIdpConfiguration, SamlIdpInfo, SamlCertificateInfo } from '../../models/saml.model';
import { forkJoin, catchError, of } from 'rxjs';

@Component({
  selector: 'saml-idp-settings-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, IconComponent],
  template: `
    <div class="space-y-6">
      @if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4 flex items-center space-x-2">
          <ui-icon name="exclamation-circle" class="h-5 w-5 text-red-500" />
          <p class="text-red-700">{{ error() }}</p>
        </div>
      }

      @if (success()) {
        <div class="bg-green-50 border border-green-200 rounded-lg p-4 flex items-center space-x-2">
          <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
          <p class="text-green-700">{{ success() }}</p>
        </div>
      }

      @if (loading()) {
        <div class="space-y-6">
          <div class="h-8 w-64 bg-gray-200 rounded animate-pulse"></div>
          <div class="h-48 bg-gray-200 rounded-lg animate-pulse"></div>
        </div>
      } @else {
        <!-- SAML IdP Settings -->
        <div class="space-y-6">
          <div>
            <h3 class="text-lg font-medium text-gray-900">SAML IdP Configuration</h3>
            <p class="mt-1 text-sm text-gray-500">Enable and configure SAML Identity Provider functionality for this tenant</p>
          </div>

          <!-- Enable Toggle -->
          <div class="flex items-center justify-between">
            <div>
              <p class="text-sm font-medium text-gray-700">Enable SAML IdP</p>
              <p class="text-sm text-gray-500">Allow external Service Providers to authenticate users via SAML</p>
            </div>
            <button
              type="button"
              (click)="toggleEnabled()"
              [class]="config.enabled ? 'bg-primary-600' : 'bg-gray-200'"
              class="relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
              role="switch"
              [attr.aria-checked]="config.enabled"
            >
              <span
                [class]="config.enabled ? 'translate-x-5' : 'translate-x-0'"
                class="pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out"
              ></span>
            </button>
          </div>

          <!-- Login Journey Name -->
          <div>
            <label for="loginJourneyName" class="block text-sm font-medium text-gray-700">Login Journey Name</label>
            <input
              type="text"
              id="loginJourneyName"
              [(ngModel)]="config.loginJourneyName"
              (ngModelChange)="onConfigChange()"
              placeholder="default-login"
              [disabled]="!config.enabled"
              class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm disabled:bg-gray-100 disabled:text-gray-500"
            />
            <p class="mt-2 text-sm text-gray-500">
              Journey to use for SAML SSO authentication. Leave empty to use the standalone login page at
              <code class="text-xs bg-gray-100 px-1 py-0.5 rounded">/account/login</code>.
            </p>
          </div>

          <!-- Info Box -->
          <div class="bg-blue-50 border border-blue-200 rounded-lg p-4">
            <div class="flex">
              <ui-icon name="information-circle" class="h-5 w-5 text-blue-500 flex-shrink-0" />
              <div class="ml-3">
                <h4 class="text-sm font-medium text-blue-800">How SAML IdP Authentication Works</h4>
                <p class="mt-1 text-sm text-blue-700">
                  When a Service Provider sends an authentication request, users will be redirected to
                  the configured login journey (or the standalone login page if not specified). After
                  authentication, users are returned to the Service Provider with a SAML assertion.
                </p>
              </div>
            </div>
          </div>
        </div>

        <!-- IdP Endpoints Info - Only show when enabled and info available -->
        @if (config.enabled && idpInfo()) {
          <div class="space-y-4">
            <div>
              <h3 class="text-lg font-medium text-gray-900">IdP Endpoints</h3>
              <p class="mt-1 text-sm text-gray-500">Share these URLs with Service Providers to configure SSO</p>
            </div>
            <div class="space-y-3">
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">Metadata URL:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">{{ idpInfo()?.metadataUrl }}</code>
                </div>
                <button (click)="copyToClipboard(idpInfo()?.metadataUrl || '', 'metadata')" class="ml-4 text-sm text-primary-600 hover:text-primary-700">
                  {{ copied() === 'metadata' ? 'Copied!' : 'Copy' }}
                </button>
              </div>
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">SSO Endpoint:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">{{ idpInfo()?.singleSignOnServiceUrl }}</code>
                </div>
                <button (click)="copyToClipboard(idpInfo()?.singleSignOnServiceUrl || '', 'sso')" class="ml-4 text-sm text-primary-600 hover:text-primary-700">
                  {{ copied() === 'sso' ? 'Copied!' : 'Copy' }}
                </button>
              </div>
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">SLO Endpoint:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">{{ idpInfo()?.singleLogoutServiceUrl }}</code>
                </div>
                <button (click)="copyToClipboard(idpInfo()?.singleLogoutServiceUrl || '', 'slo')" class="ml-4 text-sm text-primary-600 hover:text-primary-700">
                  {{ copied() === 'slo' ? 'Copied!' : 'Copy' }}
                </button>
              </div>
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">Entity ID:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">{{ idpInfo()?.entityId }}</code>
                </div>
                <button (click)="copyToClipboard(idpInfo()?.entityId || '', 'entity')" class="ml-4 text-sm text-primary-600 hover:text-primary-700">
                  {{ copied() === 'entity' ? 'Copied!' : 'Copy' }}
                </button>
              </div>
            </div>
          </div>
        }

        <!-- Endpoints Preview - Show when enabled but IdP info not available -->
        @if (config.enabled && !idpInfo()) {
          <div class="space-y-4">
            <div>
              <h3 class="text-lg font-medium text-gray-900">IdP Endpoints</h3>
              <p class="mt-1 text-sm text-gray-500">These endpoints will be available once SAML IdP is enabled</p>
            </div>
            <div class="space-y-3">
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">Metadata:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">/saml/idp/metadata</code>
                </div>
              </div>
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">SSO Endpoint:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">/saml/idp/sso</code>
                </div>
              </div>
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">SLO Endpoint:</span>
                  <code class="ml-2 text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded">/saml/idp/slo</code>
                </div>
              </div>
            </div>
          </div>
        }

        <!-- IdP Certificates - Show when enabled -->
        @if (config.enabled) {
          <div class="space-y-4">
            <div>
              <h3 class="text-lg font-medium text-gray-900">IdP Certificates</h3>
              <p class="mt-1 text-sm text-gray-500">Configure certificates for signing SAML assertions and decrypting requests</p>
            </div>

            <!-- Signing Certificate -->
            <div class="bg-gray-50 rounded-lg p-4 space-y-3">
              <div class="flex items-start justify-between">
                <div>
                  <div class="flex items-center gap-2">
                    <ui-icon name="shield-check" class="h-5 w-5 text-gray-400" />
                    <h4 class="text-sm font-medium text-gray-900">Signing Certificate</h4>
                    @if (signingCert()) {
                      <span [class]="getSourceBadgeClass(signingCert()?.source || '')" class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium">
                        {{ getSourceBadgeText(signingCert()?.source || '') }}
                      </span>
                    }
                  </div>
                  <p class="text-sm text-gray-500 mt-1">Used to sign SAML assertions sent to Service Providers</p>
                </div>
              </div>

              @if (signingCert()?.hasCertificate) {
                <div [class]="signingCert()?.isRevoked ? 'bg-red-50 rounded border border-red-300 p-3 space-y-2' : 'bg-white rounded border border-gray-200 p-3 space-y-2'">
                  @if (signingCert()?.isRevoked) {
                    <div class="flex items-center gap-2 text-red-700 bg-red-100 rounded px-2 py-1 mb-2">
                      <ui-icon name="warning" class="h-4 w-4" />
                      <span class="text-sm font-medium">Certificate Revoked - Please generate a new certificate</span>
                    </div>
                  }
                  <div class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                    <div>
                      <span class="text-gray-500">Subject:</span>
                      <span class="ml-2 text-gray-900 font-mono text-xs">{{ signingCert()?.subject || 'N/A' }}</span>
                    </div>
                    <div>
                      <span class="text-gray-500">Thumbprint:</span>
                      <span class="ml-2 text-gray-900 font-mono text-xs">{{ truncateThumbprint(signingCert()?.thumbprint) }}</span>
                    </div>
                    <div>
                      <span class="text-gray-500">Valid From:</span>
                      <span class="ml-2 text-gray-900">{{ formatDate(signingCert()?.notBefore) }}</span>
                    </div>
                    <div class="flex items-center gap-2">
                      <span class="text-gray-500">Valid Until:</span>
                      <span [class]="signingCert()?.isRevoked ? 'text-red-600' : signingCert()?.isExpired ? 'text-red-600' : signingCert()?.isExpiringSoon ? 'text-yellow-600' : 'text-gray-900'" class="ml-2">
                        {{ formatDate(signingCert()?.notAfter) }}
                      </span>
                      @if (signingCert()?.isRevoked) {
                        <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-red-100 text-red-800">Revoked</span>
                      }
                      @if (!signingCert()?.isRevoked && signingCert()?.isExpired) {
                        <ui-icon name="warning" class="h-4 w-4 text-red-500" />
                      }
                      @if (!signingCert()?.isRevoked && signingCert()?.isExpiringSoon && !signingCert()?.isExpired) {
                        <ui-icon name="warning" class="h-4 w-4 text-yellow-500" />
                      }
                    </div>
                  </div>
                </div>
              } @else {
                <div class="text-sm text-gray-500 italic">No certificate configured. Using global certificate.</div>
              }

              <!-- Action buttons -->
              <div class="flex items-center gap-2 pt-2">
                <ui-button variant="secondary" size="sm" [disabled]="certLoading() === 'signing'" (clicked)="generateSigningCert()">
                  @if (certLoading() === 'signing') {
                    <svg class="h-4 w-4 mr-1.5 animate-spin" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                    </svg>
                  } @else {
                    <ui-icon name="refresh" class="h-4 w-4 mr-1.5" />
                  }
                  Generate New
                </ui-button>
                <ui-button variant="secondary" size="sm" [disabled]="certLoading() === 'signing'" (clicked)="toggleSigningUpload()">
                  <ui-icon name="arrow-up-tray" class="h-4 w-4 mr-1.5" />
                  Upload PFX
                </ui-button>
                @if (signingCert()?.source !== 'Global') {
                  <ui-button variant="secondary" size="sm" [disabled]="certLoading() === 'signing'" (clicked)="resetSigningCert()">
                    Reset to Global
                  </ui-button>
                }
              </div>

              <!-- Upload form for signing cert -->
              @if (showSigningUpload()) {
                <div class="bg-white rounded border border-gray-200 p-3 space-y-3">
                  <div>
                    <label class="block text-sm font-medium text-gray-700">PFX Password (optional)</label>
                    <input
                      type="password"
                      [(ngModel)]="signingPassword"
                      placeholder="Enter password if PFX is protected"
                      class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                    />
                  </div>
                  <div>
                    <label class="block text-sm font-medium text-gray-700">Select PFX File</label>
                    <input
                      type="file"
                      accept=".pfx,.p12"
                      (change)="onSigningFileSelect($event)"
                      [disabled]="certLoading() === 'signing'"
                      class="mt-1 block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-medium file:bg-primary-50 file:text-primary-700 hover:file:bg-primary-100"
                    />
                  </div>
                  <button
                    type="button"
                    (click)="toggleSigningUpload()"
                    class="text-sm text-gray-500 hover:text-gray-700"
                  >
                    Cancel
                  </button>
                </div>
              }
            </div>

            <!-- Encryption Certificate -->
            <div class="bg-gray-50 rounded-lg p-4 space-y-3">
              <div class="flex items-start justify-between">
                <div>
                  <div class="flex items-center gap-2">
                    <ui-icon name="shield-check" class="h-5 w-5 text-gray-400" />
                    <h4 class="text-sm font-medium text-gray-900">Encryption Certificate</h4>
                    @if (encryptionCert()) {
                      <span [class]="getSourceBadgeClass(encryptionCert()?.source || '')" class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium">
                        {{ getSourceBadgeText(encryptionCert()?.source || '') }}
                      </span>
                    }
                  </div>
                  <p class="text-sm text-gray-500 mt-1">Used to decrypt incoming encrypted SAML requests (optional)</p>
                </div>
              </div>

              @if (encryptionCert()?.hasCertificate) {
                <div [class]="encryptionCert()?.isRevoked ? 'bg-red-50 rounded border border-red-300 p-3 space-y-2' : 'bg-white rounded border border-gray-200 p-3 space-y-2'">
                  @if (encryptionCert()?.isRevoked) {
                    <div class="flex items-center gap-2 text-red-700 bg-red-100 rounded px-2 py-1 mb-2">
                      <ui-icon name="warning" class="h-4 w-4" />
                      <span class="text-sm font-medium">Certificate Revoked - Please generate a new certificate</span>
                    </div>
                  }
                  <div class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                    <div>
                      <span class="text-gray-500">Subject:</span>
                      <span class="ml-2 text-gray-900 font-mono text-xs">{{ encryptionCert()?.subject || 'N/A' }}</span>
                    </div>
                    <div>
                      <span class="text-gray-500">Thumbprint:</span>
                      <span class="ml-2 text-gray-900 font-mono text-xs">{{ truncateThumbprint(encryptionCert()?.thumbprint) }}</span>
                    </div>
                    <div>
                      <span class="text-gray-500">Valid From:</span>
                      <span class="ml-2 text-gray-900">{{ formatDate(encryptionCert()?.notBefore) }}</span>
                    </div>
                    <div class="flex items-center gap-2">
                      <span class="text-gray-500">Valid Until:</span>
                      <span [class]="encryptionCert()?.isRevoked ? 'text-red-600' : encryptionCert()?.isExpired ? 'text-red-600' : encryptionCert()?.isExpiringSoon ? 'text-yellow-600' : 'text-gray-900'" class="ml-2">
                        {{ formatDate(encryptionCert()?.notAfter) }}
                      </span>
                      @if (encryptionCert()?.isRevoked) {
                        <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-red-100 text-red-800">Revoked</span>
                      }
                      @if (!encryptionCert()?.isRevoked && encryptionCert()?.isExpired) {
                        <ui-icon name="warning" class="h-4 w-4 text-red-500" />
                      }
                      @if (!encryptionCert()?.isRevoked && encryptionCert()?.isExpiringSoon && !encryptionCert()?.isExpired) {
                        <ui-icon name="warning" class="h-4 w-4 text-yellow-500" />
                      }
                    </div>
                  </div>
                </div>
              } @else {
                <div class="text-sm text-gray-500 italic">No certificate configured. Using global certificate.</div>
              }

              <!-- Action buttons -->
              <div class="flex items-center gap-2 pt-2">
                <ui-button variant="secondary" size="sm" [disabled]="certLoading() === 'encryption'" (clicked)="generateEncryptionCert()">
                  @if (certLoading() === 'encryption') {
                    <svg class="h-4 w-4 mr-1.5 animate-spin" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                    </svg>
                  } @else {
                    <ui-icon name="refresh" class="h-4 w-4 mr-1.5" />
                  }
                  Generate New
                </ui-button>
                <ui-button variant="secondary" size="sm" [disabled]="certLoading() === 'encryption'" (clicked)="toggleEncryptionUpload()">
                  <ui-icon name="arrow-up-tray" class="h-4 w-4 mr-1.5" />
                  Upload PFX
                </ui-button>
                @if (encryptionCert()?.source !== 'Global') {
                  <ui-button variant="secondary" size="sm" [disabled]="certLoading() === 'encryption'" (clicked)="resetEncryptionCert()">
                    Reset to Global
                  </ui-button>
                }
              </div>

              <!-- Upload form for encryption cert -->
              @if (showEncryptionUpload()) {
                <div class="bg-white rounded border border-gray-200 p-3 space-y-3">
                  <div>
                    <label class="block text-sm font-medium text-gray-700">PFX Password (optional)</label>
                    <input
                      type="password"
                      [(ngModel)]="encryptionPassword"
                      placeholder="Enter password if PFX is protected"
                      class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                    />
                  </div>
                  <div>
                    <label class="block text-sm font-medium text-gray-700">Select PFX File</label>
                    <input
                      type="file"
                      accept=".pfx,.p12"
                      (change)="onEncryptionFileSelect($event)"
                      [disabled]="certLoading() === 'encryption'"
                      class="mt-1 block w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-medium file:bg-primary-50 file:text-primary-700 hover:file:bg-primary-100"
                    />
                  </div>
                  <button
                    type="button"
                    (click)="toggleEncryptionUpload()"
                    class="text-sm text-gray-500 hover:text-gray-700"
                  >
                    Cancel
                  </button>
                </div>
              }
            </div>
          </div>
        }

        <!-- Save Button -->
        <div class="flex justify-end items-center gap-4 pt-4 border-t border-gray-200">
          @if (hasChanges()) {
            <span class="text-sm text-gray-500">You have unsaved changes</span>
          }
          <ui-button [disabled]="!hasChanges() || saving()" (clicked)="handleSave()">
            {{ saving() ? 'Saving...' : 'Save Settings' }}
          </ui-button>
        </div>
      }
    </div>
  `,
})
export class SamlIdpSettingsTabComponent implements OnInit {
  private readonly samlService = inject(SamlService);
  private readonly tabContext = inject(TAB_CONTEXT, { optional: true });

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly success = signal<string | null>(null);
  readonly hasChanges = signal(false);
  readonly idpInfo = signal<SamlIdpInfo | null>(null);
  readonly signingCert = signal<SamlCertificateInfo | null>(null);
  readonly encryptionCert = signal<SamlCertificateInfo | null>(null);
  readonly certLoading = signal<'signing' | 'encryption' | null>(null);
  readonly copied = signal<string | null>(null);
  readonly showSigningUpload = signal(false);
  readonly showEncryptionUpload = signal(false);

  config: SamlIdpConfiguration = {
    enabled: false,
    loginJourneyName: undefined,
  };

  signingPassword = '';
  encryptionPassword = '';

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.loading.set(true);
    forkJoin({
      config: this.samlService.getIdpConfiguration(),
      idpInfo: this.samlService.getIdpInfo().pipe(catchError(() => of(null))),
      signingCert: this.samlService.getSigningCertificate().pipe(catchError(() => of(null))),
      encryptionCert: this.samlService.getEncryptionCertificate().pipe(catchError(() => of(null))),
    }).subscribe({
      next: (data) => {
        this.config = data.config;
        this.idpInfo.set(data.idpInfo);
        this.signingCert.set(data.signingCert);
        this.encryptionCert.set(data.encryptionCert);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading SAML IdP config:', err);
        this.error.set('Failed to load SAML IdP configuration');
        this.loading.set(false);
      },
    });
  }

  toggleEnabled(): void {
    this.config.enabled = !this.config.enabled;
    this.onConfigChange();
  }

  onConfigChange(): void {
    this.hasChanges.set(true);
    this.success.set(null);
  }

  handleSave(): void {
    this.saving.set(true);
    this.error.set(null);
    this.samlService.updateIdpConfiguration(this.config).subscribe({
      next: () => {
        this.success.set('SAML IdP settings saved successfully');
        this.hasChanges.set(false);
        this.saving.set(false);
        // Reload IdP info in case endpoints changed
        this.samlService.getIdpInfo().subscribe({
          next: (info) => this.idpInfo.set(info),
          error: () => {},
        });
      },
      error: (err) => {
        console.error('Error saving SAML IdP config:', err);
        this.error.set('Failed to save SAML IdP configuration');
        this.saving.set(false);
      },
    });
  }

  // Signing Certificate Methods
  generateSigningCert(): void {
    this.certLoading.set('signing');
    this.samlService.generateSigningCertificate().subscribe({
      next: (cert) => {
        this.signingCert.set(cert);
        this.success.set('Signing certificate generated successfully');
        this.certLoading.set(null);
      },
      error: (err) => {
        console.error('Error generating signing certificate:', err);
        this.error.set('Failed to generate signing certificate');
        this.certLoading.set(null);
      },
    });
  }

  resetSigningCert(): void {
    this.certLoading.set('signing');
    this.samlService.resetSigningCertificate().subscribe({
      next: () => {
        this.loadCertificates();
        this.success.set('Signing certificate reset to global');
        this.certLoading.set(null);
      },
      error: (err) => {
        console.error('Error resetting signing certificate:', err);
        this.error.set('Failed to reset signing certificate');
        this.certLoading.set(null);
      },
    });
  }

  toggleSigningUpload(): void {
    this.showSigningUpload.update((v) => !v);
    if (!this.showSigningUpload()) {
      this.signingPassword = '';
    }
  }

  onSigningFileSelect(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.certLoading.set('signing');
    const reader = new FileReader();
    reader.onload = () => {
      const arrayBuffer = reader.result as ArrayBuffer;
      const bytes = new Uint8Array(arrayBuffer);
      const base64 = btoa(String.fromCharCode(...bytes));

      this.samlService.uploadSigningCertificate({
        base64Pfx: base64,
        password: this.signingPassword || undefined,
      }).subscribe({
        next: (cert) => {
          this.signingCert.set(cert);
          this.success.set('Signing certificate uploaded successfully');
          this.certLoading.set(null);
          this.showSigningUpload.set(false);
          this.signingPassword = '';
        },
        error: (err) => {
          console.error('Error uploading signing certificate:', err);
          this.error.set('Failed to upload signing certificate');
          this.certLoading.set(null);
        },
      });
    };
    reader.readAsArrayBuffer(file);
    input.value = '';
  }

  // Encryption Certificate Methods
  generateEncryptionCert(): void {
    this.certLoading.set('encryption');
    this.samlService.generateEncryptionCertificate().subscribe({
      next: (cert) => {
        this.encryptionCert.set(cert);
        this.success.set('Encryption certificate generated successfully');
        this.certLoading.set(null);
      },
      error: (err) => {
        console.error('Error generating encryption certificate:', err);
        this.error.set('Failed to generate encryption certificate');
        this.certLoading.set(null);
      },
    });
  }

  resetEncryptionCert(): void {
    this.certLoading.set('encryption');
    this.samlService.resetEncryptionCertificate().subscribe({
      next: () => {
        this.loadCertificates();
        this.success.set('Encryption certificate reset to global');
        this.certLoading.set(null);
      },
      error: (err) => {
        console.error('Error resetting encryption certificate:', err);
        this.error.set('Failed to reset encryption certificate');
        this.certLoading.set(null);
      },
    });
  }

  toggleEncryptionUpload(): void {
    this.showEncryptionUpload.update((v) => !v);
    if (!this.showEncryptionUpload()) {
      this.encryptionPassword = '';
    }
  }

  onEncryptionFileSelect(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.certLoading.set('encryption');
    const reader = new FileReader();
    reader.onload = () => {
      const arrayBuffer = reader.result as ArrayBuffer;
      const bytes = new Uint8Array(arrayBuffer);
      const base64 = btoa(String.fromCharCode(...bytes));

      this.samlService.uploadEncryptionCertificate({
        base64Pfx: base64,
        password: this.encryptionPassword || undefined,
      }).subscribe({
        next: (cert) => {
          this.encryptionCert.set(cert);
          this.success.set('Encryption certificate uploaded successfully');
          this.certLoading.set(null);
          this.showEncryptionUpload.set(false);
          this.encryptionPassword = '';
        },
        error: (err) => {
          console.error('Error uploading encryption certificate:', err);
          this.error.set('Failed to upload encryption certificate');
          this.certLoading.set(null);
        },
      });
    };
    reader.readAsArrayBuffer(file);
    input.value = '';
  }

  private loadCertificates(): void {
    forkJoin({
      signing: this.samlService.getSigningCertificate().pipe(catchError(() => of(null))),
      encryption: this.samlService.getEncryptionCertificate().pipe(catchError(() => of(null))),
    }).subscribe({
      next: (data) => {
        this.signingCert.set(data.signing);
        this.encryptionCert.set(data.encryption);
      },
      error: (err) => {
        console.error('Error loading certificates:', err);
      },
    });
  }

  copyToClipboard(value: string, key = 'metadata'): void {
    navigator.clipboard.writeText(value).then(() => {
      this.copied.set(key);
      setTimeout(() => this.copied.set(null), 2000);
    });
  }

  formatDate(dateStr?: string): string {
    if (!dateStr) return 'N/A';
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  }

  truncateThumbprint(thumbprint?: string): string {
    if (!thumbprint) return 'N/A';
    return thumbprint.substring(0, 16) + '...';
  }

  getSourceBadgeClass(source: string): string {
    switch (source) {
      case 'Global':
        return 'bg-gray-100 text-gray-800';
      case 'Auto':
        return 'bg-blue-100 text-blue-800';
      case 'Uploaded':
        return 'bg-green-100 text-green-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }

  getSourceBadgeText(source: string): string {
    switch (source) {
      case 'Auto':
        return 'Auto-generated';
      default:
        return source;
    }
  }
}
