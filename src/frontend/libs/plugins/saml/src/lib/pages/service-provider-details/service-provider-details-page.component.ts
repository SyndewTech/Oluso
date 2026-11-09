import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonComponent, IconComponent } from '@syndew/oluso-ui-core';
import { SamlService } from '../../services/saml.service';
import {
  CreateSamlServiceProviderRequest,
  SamlServiceProviderEdit,
  SamlTestResult,
  NAME_ID_FORMATS,
  SSO_BINDINGS,
} from '../../models/saml.model';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';

interface FormData {
  entityId: string;
  displayName: string;
  description: string;
  enabled: boolean;
  metadataUrl: string;
  assertionConsumerServiceUrl: string;
  singleLogoutServiceUrl: string;
  signingCertificate: string;
  encryptionCertificate: string;
  encryptAssertions: boolean;
  nameIdFormat: string;
  requireVerifiedEmail: boolean;
  allowedClaims: string;
  claimMappings: string;
  ssoBinding: string;
  signResponses: boolean;
  signAssertions: boolean;
  requireSignedAuthnRequests: boolean;
  defaultRelayState: string;
}

const defaultFormData: FormData = {
  entityId: '',
  displayName: '',
  description: '',
  enabled: true,
  metadataUrl: '',
  assertionConsumerServiceUrl: '',
  singleLogoutServiceUrl: '',
  signingCertificate: '',
  encryptionCertificate: '',
  encryptAssertions: false,
  nameIdFormat: '',
  requireVerifiedEmail: false,
  allowedClaims: '',
  claimMappings: '',
  ssoBinding: 'POST',
  signResponses: true,
  signAssertions: true,
  requireSignedAuthnRequests: false,
  defaultRelayState: '',
};

@Component({
  selector: 'saml-service-provider-details-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ButtonComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <div class="flex items-center space-x-4">
        <a routerLink="/saml/service-providers" class="p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded">
          <ui-icon name="arrow-left" class="h-5 w-5" />
        </a>
        <h1 class="text-2xl font-bold text-gray-900">{{ isNew ? 'Add Service Provider' : 'Edit Service Provider' }}</h1>
      </div>

      @if (loading()) {
        <div class="space-y-6">
          <div class="h-8 w-48 bg-gray-200 rounded animate-pulse"></div>
          <div class="bg-white shadow rounded-lg p-6 space-y-4">
            @for (i of [1, 2, 3, 4]; track i) {
              <div class="h-12 bg-gray-200 rounded animate-pulse"></div>
            }
          </div>
        </div>
      } @else {
        @if (error()) {
          <div class="bg-red-50 border border-red-200 rounded-lg p-4">
            <div class="flex items-center space-x-2">
              <ui-icon name="x-circle" class="h-5 w-5 text-red-600" />
              <span class="text-red-700">{{ error() }}</span>
            </div>
          </div>
        }

        @if (nonEditable()) {
          <div class="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
            <div class="flex items-center space-x-2">
              <ui-icon name="exclamation-triangle" class="h-5 w-5 text-yellow-600" />
              <span class="text-yellow-700">This service provider is system-managed and cannot be modified.</span>
            </div>
          </div>
        }

        @if (testResult()) {
          <div [class]="testResult()?.success ? 'bg-green-50 border-green-200' : 'bg-red-50 border-red-200'" class="rounded-lg p-4 border">
            <div class="flex items-center justify-between">
              <div class="flex items-center space-x-2">
                @if (testResult()?.success) {
                  <ui-icon name="check-circle" class="h-5 w-5 text-green-600" />
                } @else {
                  <ui-icon name="x-circle" class="h-5 w-5 text-red-600" />
                }
                <span [class]="testResult()?.success ? 'text-green-700' : 'text-red-700'">{{ testResult()?.message }}</span>
              </div>
              <button (click)="testResult.set(null)" class="text-gray-400 hover:text-gray-600">&times;</button>
            </div>
          </div>
        }

        <form (ngSubmit)="handleSubmit()" class="space-y-6">
          <!-- Basic Information -->
          <div class="bg-white shadow rounded-lg">
            <div class="px-6 py-4 border-b border-gray-200">
              <h2 class="text-lg font-medium text-gray-900">Basic Information</h2>
            </div>
            <div class="px-6 py-4 space-y-4">
              <div>
                <label for="entityId" class="block text-sm font-medium text-gray-700">
                  Entity ID <span class="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  id="entityId"
                  [(ngModel)]="formData.entityId"
                  name="entityId"
                  (ngModelChange)="onEntityIdChange($event)"
                  [disabled]="!isNew || nonEditable()"
                  required
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  [class.border-red-500]="entityIdError()"
                  [class.bg-gray-100]="!isNew"
                  placeholder="https://sp.example.com/saml"
                />
                @if (checkingEntityId()) {
                  <p class="mt-1 text-sm text-gray-500">Checking availability...</p>
                }
                @if (entityIdError()) {
                  <p class="mt-1 text-sm text-red-600">{{ entityIdError() }}</p>
                }
                <p class="mt-1 text-xs text-gray-500">The unique SAML Entity ID for this Service Provider</p>
              </div>

              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label for="displayName" class="block text-sm font-medium text-gray-700">Display Name</label>
                  <input
                    type="text"
                    id="displayName"
                    [(ngModel)]="formData.displayName"
                    name="displayName"
                    [disabled]="nonEditable()"
                    class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                    placeholder="My Application"
                  />
                </div>

                <div>
                  <label for="enabled" class="block text-sm font-medium text-gray-700">Status</label>
                  <div class="mt-2">
                    <label class="inline-flex items-center">
                      <input
                        type="checkbox"
                        [(ngModel)]="formData.enabled"
                        name="enabled"
                        [disabled]="nonEditable()"
                        class="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                      />
                      <span class="ml-2 text-sm text-gray-700">Enabled</span>
                    </label>
                  </div>
                </div>
              </div>

              <div>
                <label for="description" class="block text-sm font-medium text-gray-700">Description</label>
                <textarea
                  id="description"
                  [(ngModel)]="formData.description"
                  name="description"
                  [disabled]="nonEditable()"
                  rows="2"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  placeholder="Optional description of this service provider"
                ></textarea>
              </div>
            </div>
          </div>

          <!-- Endpoints -->
          <div class="bg-white shadow rounded-lg">
            <div class="px-6 py-4 border-b border-gray-200">
              <h2 class="text-lg font-medium text-gray-900">Endpoints</h2>
              <p class="mt-1 text-sm text-gray-500">You can provide a metadata URL to auto-configure, or manually specify endpoints</p>
            </div>
            <div class="px-6 py-4 space-y-4">
              <div>
                <label for="metadataUrl" class="block text-sm font-medium text-gray-700">Metadata URL</label>
                <input
                  type="url"
                  id="metadataUrl"
                  [(ngModel)]="formData.metadataUrl"
                  name="metadataUrl"
                  [disabled]="nonEditable()"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  placeholder="https://sp.example.com/saml/metadata"
                />
                <p class="mt-1 text-xs text-gray-500">If provided, endpoints and certificates will be fetched from metadata</p>
              </div>

              <div>
                <label for="assertionConsumerServiceUrl" class="block text-sm font-medium text-gray-700">Assertion Consumer Service (ACS) URL</label>
                <input
                  type="url"
                  id="assertionConsumerServiceUrl"
                  [(ngModel)]="formData.assertionConsumerServiceUrl"
                  name="assertionConsumerServiceUrl"
                  [disabled]="nonEditable()"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  placeholder="https://sp.example.com/saml/acs"
                />
                <p class="mt-1 text-xs text-gray-500">Where SAML responses should be sent (required if no metadata URL)</p>
              </div>

              <div>
                <label for="singleLogoutServiceUrl" class="block text-sm font-medium text-gray-700">Single Logout Service URL</label>
                <input
                  type="url"
                  id="singleLogoutServiceUrl"
                  [(ngModel)]="formData.singleLogoutServiceUrl"
                  name="singleLogoutServiceUrl"
                  [disabled]="nonEditable()"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  placeholder="https://sp.example.com/saml/slo"
                />
              </div>

              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label for="ssoBinding" class="block text-sm font-medium text-gray-700">SSO Binding</label>
                  <select
                    id="ssoBinding"
                    [(ngModel)]="formData.ssoBinding"
                    name="ssoBinding"
                    [disabled]="nonEditable()"
                    class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  >
                    @for (binding of ssoBindings; track binding.value) {
                      <option [value]="binding.value">{{ binding.label }}</option>
                    }
                  </select>
                </div>

                <div>
                  <label for="nameIdFormat" class="block text-sm font-medium text-gray-700">Name ID Format</label>
                  <select
                    id="nameIdFormat"
                    [(ngModel)]="formData.nameIdFormat"
                    name="nameIdFormat"
                    [disabled]="nonEditable()"
                    class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  >
                    @for (format of nameIdFormats; track format.value) {
                      <option [value]="format.value">{{ format.label }}</option>
                    }
                  </select>
                </div>
              </div>
            </div>
          </div>

          <!-- Security -->
          <div class="bg-white shadow rounded-lg">
            <div class="px-6 py-4 border-b border-gray-200">
              <h2 class="text-lg font-medium text-gray-900">Security</h2>
            </div>
            <div class="px-6 py-4 space-y-4">
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.signResponses"
                    name="signResponses"
                    [disabled]="nonEditable()"
                    class="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                  />
                  <span class="ml-2 text-sm text-gray-700">Sign SAML Responses</span>
                </label>

                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.signAssertions"
                    name="signAssertions"
                    [disabled]="nonEditable()"
                    class="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                  />
                  <span class="ml-2 text-sm text-gray-700">Sign SAML Assertions</span>
                </label>

                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.requireSignedAuthnRequests"
                    name="requireSignedAuthnRequests"
                    [disabled]="nonEditable()"
                    class="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                  />
                  <span class="ml-2 text-sm text-gray-700">Require Signed AuthnRequests</span>
                </label>

                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.encryptAssertions"
                    name="encryptAssertions"
                    [disabled]="nonEditable()"
                    class="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                  />
                  <span class="ml-2 text-sm text-gray-700">Encrypt Assertions</span>
                </label>

                <label class="flex items-start md:col-span-2">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.requireVerifiedEmail"
                    name="requireVerifiedEmail"
                    [disabled]="nonEditable()"
                    class="mt-0.5 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                  />
                  <span class="ml-2 text-sm text-gray-700">
                    Require a verified email address
                    <span class="block text-xs text-gray-500">
                      Only people whose email is verified get assertions for this service provider. Always applies
                      when the Name ID format is the email address.
                    </span>
                  </span>
                </label>
              </div>

              <div>
                <label for="signingCertificate" class="block text-sm font-medium text-gray-700">SP Signing Certificate (Base64)</label>
                <textarea
                  id="signingCertificate"
                  [(ngModel)]="formData.signingCertificate"
                  name="signingCertificate"
                  [disabled]="nonEditable()"
                  rows="3"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 font-mono text-xs"
                  placeholder="MIIDx..."
                ></textarea>
                <p class="mt-1 text-xs text-gray-500">Public certificate for verifying signed AuthnRequests from this SP</p>
              </div>

              <div>
                <label for="encryptionCertificate" class="block text-sm font-medium text-gray-700">SP Encryption Certificate (Base64)</label>
                <textarea
                  id="encryptionCertificate"
                  [(ngModel)]="formData.encryptionCertificate"
                  name="encryptionCertificate"
                  [disabled]="nonEditable()"
                  rows="3"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 font-mono text-xs"
                  placeholder="MIIDx..."
                ></textarea>
                <p class="mt-1 text-xs text-gray-500">Public certificate for encrypting assertions sent to this SP</p>
              </div>
            </div>
          </div>

          <!-- Claims -->
          <div class="bg-white shadow rounded-lg">
            <div class="px-6 py-4 border-b border-gray-200">
              <h2 class="text-lg font-medium text-gray-900">Claims Configuration</h2>
            </div>
            <div class="px-6 py-4 space-y-4">
              <div>
                <label for="allowedClaims" class="block text-sm font-medium text-gray-700">Allowed Claims</label>
                <input
                  type="text"
                  id="allowedClaims"
                  [(ngModel)]="formData.allowedClaims"
                  name="allowedClaims"
                  [disabled]="nonEditable()"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  placeholder="email, name, given_name, family_name"
                />
                <p class="mt-1 text-xs text-gray-500">Comma-separated list of claim types to include in assertions (empty = all claims)</p>
              </div>

              <div>
                <label for="claimMappings" class="block text-sm font-medium text-gray-700">Claim Mappings</label>
                <textarea
                  id="claimMappings"
                  [(ngModel)]="formData.claimMappings"
                  name="claimMappings"
                  [disabled]="nonEditable()"
                  rows="4"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 font-mono text-sm"
                  placeholder="http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress=email"
                ></textarea>
                <p class="mt-1 text-xs text-gray-500">Map SAML attribute names to claim types (one per line: samlAttribute=claimType)</p>
              </div>

              <div>
                <label for="defaultRelayState" class="block text-sm font-medium text-gray-700">Default RelayState</label>
                <input
                  type="text"
                  id="defaultRelayState"
                  [(ngModel)]="formData.defaultRelayState"
                  name="defaultRelayState"
                  [disabled]="nonEditable()"
                  class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                  placeholder="https://sp.example.com/dashboard"
                />
                <p class="mt-1 text-xs text-gray-500">Default redirect URL if the SP doesn't provide a RelayState</p>
              </div>
            </div>
          </div>

          <!-- Actions -->
          <div class="flex items-center justify-between">
            <div>
              @if (!isNew) {
                <ui-button type="button" variant="secondary" (clicked)="handleTest()">Test Configuration</ui-button>
              }
            </div>
            <div class="flex items-center space-x-4">
              <a routerLink="/saml/service-providers">
                <ui-button type="button" variant="secondary">Cancel</ui-button>
              </a>
              <ui-button type="submit" [disabled]="saving() || nonEditable() || !!entityIdError()">
                {{ saving() ? 'Saving...' : isNew ? 'Create Service Provider' : 'Save Changes' }}
              </ui-button>
            </div>
          </div>
        </form>
      }
    </div>
  `,
})
export class ServiceProviderDetailsPageComponent implements OnInit {
  private readonly samlService = inject(SamlService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly entityIdCheck$ = new Subject<string>();

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly entityIdError = signal<string | null>(null);
  readonly checkingEntityId = signal(false);
  readonly testResult = signal<SamlTestResult | null>(null);
  readonly nonEditable = signal(false);

  formData: FormData = { ...defaultFormData };
  isNew = true;
  private id: number | null = null;

  readonly nameIdFormats = NAME_ID_FORMATS;
  readonly ssoBindings = SSO_BINDINGS;

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    this.isNew = !idParam || idParam === 'new';

    if (!this.isNew && idParam) {
      this.id = parseInt(idParam, 10);
      this.loadServiceProvider();
    }

    // Set up entity ID availability checking with debounce
    this.entityIdCheck$
      .pipe(debounceTime(500), distinctUntilChanged())
      .subscribe((entityId) => this.checkEntityIdAvailability(entityId));
  }

  loadServiceProvider(): void {
    if (!this.id) return;

    this.loading.set(true);
    this.samlService.getForEdit(this.id).subscribe({
      next: (sp) => {
        this.formData = {
          entityId: sp.entityId,
          displayName: sp.displayName || '',
          description: sp.description || '',
          enabled: sp.enabled,
          metadataUrl: sp.metadataUrl || '',
          assertionConsumerServiceUrl: sp.assertionConsumerServiceUrl || '',
          singleLogoutServiceUrl: sp.singleLogoutServiceUrl || '',
          signingCertificate: sp.signingCertificate || '',
          encryptionCertificate: sp.encryptionCertificate || '',
          encryptAssertions: sp.encryptAssertions,
          requireVerifiedEmail: sp.requireVerifiedEmail ?? false,
          nameIdFormat: sp.nameIdFormat || '',
          allowedClaims: sp.allowedClaims?.join(', ') || '',
          claimMappings: sp.claimMappings
            ? Object.entries(sp.claimMappings)
                .map(([k, v]) => `${k}=${v}`)
                .join('\n')
            : '',
          ssoBinding: sp.ssoBinding,
          signResponses: sp.signResponses,
          signAssertions: sp.signAssertions,
          requireSignedAuthnRequests: sp.requireSignedAuthnRequests,
          defaultRelayState: sp.defaultRelayState || '',
        };
        this.nonEditable.set(sp.nonEditable);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading service provider:', err);
        this.error.set('Failed to load service provider');
        this.loading.set(false);
      },
    });
  }

  onEntityIdChange(entityId: string): void {
    if (this.isNew && entityId) {
      this.entityIdCheck$.next(entityId);
    }
  }

  checkEntityIdAvailability(entityId: string): void {
    if (!entityId) return;

    this.checkingEntityId.set(true);
    this.samlService.checkEntityIdAvailability(entityId, this.isNew ? undefined : this.id!).subscribe({
      next: (result) => {
        if (!result.available) {
          this.entityIdError.set(result.message || 'Entity ID is already in use');
        } else {
          this.entityIdError.set(null);
        }
        this.checkingEntityId.set(false);
      },
      error: (err) => {
        console.error('Error checking entity ID:', err);
        this.checkingEntityId.set(false);
      },
    });
  }

  parseAllowedClaims(value: string): string[] | undefined {
    if (!value.trim()) return undefined;
    return value
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s);
  }

  parseClaimMappings(value: string): Record<string, string> | undefined {
    if (!value.trim()) return undefined;
    const mappings: Record<string, string> = {};
    for (const line of value.split('\n')) {
      const [key, val] = line.split('=').map((s) => s.trim());
      if (key && val) {
        mappings[key] = val;
      }
    }
    return Object.keys(mappings).length > 0 ? mappings : undefined;
  }

  handleSubmit(): void {
    if (this.entityIdError()) return;

    this.saving.set(true);
    this.error.set(null);

    if (this.isNew) {
      const request: CreateSamlServiceProviderRequest = {
        entityId: this.formData.entityId,
        displayName: this.formData.displayName || undefined,
        description: this.formData.description || undefined,
        enabled: this.formData.enabled,
        metadataUrl: this.formData.metadataUrl || undefined,
        assertionConsumerServiceUrl: this.formData.assertionConsumerServiceUrl || undefined,
        singleLogoutServiceUrl: this.formData.singleLogoutServiceUrl || undefined,
        signingCertificate: this.formData.signingCertificate || undefined,
        encryptionCertificate: this.formData.encryptionCertificate || undefined,
        encryptAssertions: this.formData.encryptAssertions,
        requireVerifiedEmail: this.formData.requireVerifiedEmail,
        nameIdFormat: this.formData.nameIdFormat || undefined,
        allowedClaims: this.parseAllowedClaims(this.formData.allowedClaims),
        claimMappings: this.parseClaimMappings(this.formData.claimMappings),
        ssoBinding: this.formData.ssoBinding,
        signResponses: this.formData.signResponses,
        signAssertions: this.formData.signAssertions,
        requireSignedAuthnRequests: this.formData.requireSignedAuthnRequests,
        defaultRelayState: this.formData.defaultRelayState || undefined,
      };

      this.samlService.create(request).subscribe({
        next: (created) => {
          this.router.navigate(['/saml/service-providers', created.id]);
        },
        error: (err) => {
          console.error('Error creating service provider:', err);
          this.error.set(err.error?.error || 'Failed to create service provider');
          this.saving.set(false);
        },
      });
    } else {
      const request: SamlServiceProviderEdit = {
        id: this.id!,
        entityId: this.formData.entityId,
        displayName: this.formData.displayName || undefined,
        description: this.formData.description || undefined,
        enabled: this.formData.enabled,
        metadataUrl: this.formData.metadataUrl || undefined,
        assertionConsumerServiceUrl: this.formData.assertionConsumerServiceUrl || undefined,
        singleLogoutServiceUrl: this.formData.singleLogoutServiceUrl || undefined,
        signingCertificate: this.formData.signingCertificate || undefined,
        encryptionCertificate: this.formData.encryptionCertificate || undefined,
        encryptAssertions: this.formData.encryptAssertions,
        requireVerifiedEmail: this.formData.requireVerifiedEmail,
        nameIdFormat: this.formData.nameIdFormat || undefined,
        allowedClaims: this.parseAllowedClaims(this.formData.allowedClaims),
        claimMappings: this.parseClaimMappings(this.formData.claimMappings),
        ssoBinding: this.formData.ssoBinding,
        signResponses: this.formData.signResponses,
        signAssertions: this.formData.signAssertions,
        requireSignedAuthnRequests: this.formData.requireSignedAuthnRequests,
        defaultRelayState: this.formData.defaultRelayState || undefined,
        nonEditable: this.nonEditable(),
      };

      this.samlService.update(this.id!, request).subscribe({
        next: () => {
          this.router.navigate(['/saml/service-providers']);
        },
        error: (err) => {
          console.error('Error updating service provider:', err);
          this.error.set(err.error?.error || 'Failed to save service provider');
          this.saving.set(false);
        },
      });
    }
  }

  handleTest(): void {
    if (this.isNew || !this.id) return;

    this.testResult.set(null);
    this.samlService.test(this.id).subscribe({
      next: (result) => this.testResult.set(result),
      error: () => this.testResult.set({ success: false, message: 'Failed to run test' }),
    });
  }
}
