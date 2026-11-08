import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CardComponent, ButtonComponent, IconComponent, InputComponent, ModalComponent } from '@oluso/ui-core';
import {
  PasskeysAccountService,
  AccountPasskey,
  AccountPasskeyList,
  convertOptionsForWebAuthn,
  arrayBufferToBase64Url,
  getDeviceName,
} from '../../services/passkeys-account.service';

@Component({
  selector: 'fido2-passkeys-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, CardComponent, ButtonComponent, IconComponent, InputComponent, ModalComponent],
  template: `
    <div class="space-y-6">
      <div class="flex items-center gap-4">
        <a routerLink="/security" class="text-gray-500 hover:text-gray-700">
          <ui-icon name="chevron-left" class="h-5 w-5" />
        </a>
        <div class="flex-1">
          <h1 class="text-2xl font-semibold text-gray-900">Passkeys</h1>
          <p class="text-gray-500 mt-1">Sign in without a password using biometrics or security keys</p>
        </div>
        <ui-button (clicked)="startRegistration()" [loading]="registering()" [disabled]="!webAuthnSupported">
          <ui-icon name="plus" class="h-5 w-5 mr-2" />
          Add Passkey
        </ui-button>
      </div>

      @if (!webAuthnSupported) {
        <div class="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
          <p class="text-sm text-yellow-800">
            Your browser does not support passkeys. Please use a modern browser like Chrome, Safari, Firefox, or Edge.
          </p>
        </div>
      }

      @if (loading()) {
        <div class="flex justify-center py-12">
          <svg
            class="animate-spin h-8 w-8 text-primary-600"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
          >
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
        </div>
      } @else if (!passkeyList()?.isEnabled) {
        <div class="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
          <p class="text-sm text-yellow-800">
            {{ passkeyList()?.message || 'Passkeys are not enabled for this server.' }}
          </p>
        </div>
      } @else {
        @if (passkeyList()?.passkeys?.length === 0) {
          <ui-card>
            <div class="text-center py-8">
              <ui-icon name="fingerprint" class="mx-auto h-12 w-12 text-gray-400" />
              <h3 class="mt-4 text-sm font-semibold text-gray-900">No passkeys</h3>
              <p class="mt-2 text-sm text-gray-500">
                Get started by registering a passkey for faster, more secure sign-in.
              </p>
            </div>
          </ui-card>
        } @else {
          <div class="bg-white shadow rounded-lg divide-y divide-gray-200">
            @for (passkey of passkeyList()?.passkeys; track passkey.id) {
              <div class="px-6 py-4">
                <div class="flex items-center justify-between">
                  <div class="flex items-center gap-4">
                    @if (passkey.authenticatorType === 'platform') {
                      <ui-icon name="fingerprint" class="h-8 w-8 text-primary-600" />
                    } @else {
                      <ui-icon name="key" class="h-8 w-8 text-amber-600" />
                    }
                    <div>
                      <p class="text-sm font-medium text-gray-900">{{ passkey.name }}</p>
                      <div class="mt-1 flex items-center gap-3 text-xs text-gray-500">
                        <span>Added {{ formatDate(passkey.createdAt) }}</span>
                        @if (passkey.lastUsedAt) {
                          <span class="text-gray-300">|</span>
                          <span>Last used {{ formatDate(passkey.lastUsedAt) }}</span>
                        }
                      </div>
                    </div>
                  </div>
                  <div class="flex items-center gap-2">
                    <button
                      type="button"
                      (click)="startEdit(passkey)"
                      class="p-2 text-gray-400 hover:text-primary-600 rounded hover:bg-gray-100"
                      title="Rename passkey"
                    >
                      <ui-icon name="pencil" class="h-4 w-4" />
                    </button>
                    <button
                      type="button"
                      (click)="deletePasskey(passkey)"
                      [disabled]="deletingId() === passkey.id"
                      class="p-2 text-gray-400 hover:text-red-500 disabled:opacity-50 rounded hover:bg-gray-100"
                      title="Delete passkey"
                    >
                      @if (deletingId() === passkey.id) {
                        <svg class="animate-spin h-4 w-4 text-red-500" fill="none" viewBox="0 0 24 24">
                          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                        </svg>
                      } @else {
                        <ui-icon name="trash" class="h-4 w-4" />
                      }
                    </button>
                  </div>
                </div>
              </div>
            }
          </div>
        }

        <!-- Info section -->
        <div class="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <h3 class="text-sm font-semibold text-blue-900 mb-2">About Passkeys</h3>
          <ul class="text-sm text-blue-800 space-y-1">
            <li>Passkeys are a more secure alternative to passwords</li>
            <li>They use biometrics (fingerprint, face) or a security key</li>
            <li>Your biometric data never leaves your device</li>
            <li>Passkeys are resistant to phishing attacks</li>
          </ul>
        </div>
      }
    </div>

    <!-- Name Prompt Modal (for new passkey) -->
    <ui-modal
      [open]="showNamePrompt()"
      title="Name your passkey"
      (closed)="cancelRegistration()"
    >
      <div class="space-y-4">
        <p class="text-sm text-gray-500">
          Give your passkey a name to help you identify it later.
        </p>
        <ui-input
          label="Passkey Name"
          [(ngModel)]="newPasskeyName"
          name="passkeyName"
          placeholder="e.g., MacBook Pro, iPhone"
        ></ui-input>
        @if (registrationError()) {
          <div class="bg-red-50 text-red-700 p-3 rounded-md text-sm">
            {{ registrationError() }}
          </div>
        }
      </div>
      <div modal-footer class="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="cancelRegistration()">Cancel</ui-button>
        <ui-button [loading]="completing()" (clicked)="completeRegistration()">
          Save Passkey
        </ui-button>
      </div>
    </ui-modal>

    <!-- Edit Modal -->
    <ui-modal
      [open]="showEditModal()"
      title="Rename passkey"
      (closed)="closeEditModal()"
    >
      <div class="space-y-4">
        <ui-input
          label="Passkey Name"
          [(ngModel)]="editName"
          name="editName"
          placeholder="Passkey name"
        ></ui-input>
        @if (editError()) {
          <div class="bg-red-50 text-red-700 p-3 rounded-md text-sm">
            {{ editError() }}
          </div>
        }
      </div>
      <div modal-footer class="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="closeEditModal()">Cancel</ui-button>
        <ui-button [loading]="saving()" (clicked)="saveEdit()">
          Save
        </ui-button>
      </div>
    </ui-modal>
  `,
})
export class PasskeysPageComponent implements OnInit {
  private readonly passkeysService = inject(PasskeysAccountService);

  readonly loading = signal(true);
  readonly passkeyList = signal<AccountPasskeyList | null>(null);
  readonly registering = signal(false);
  readonly completing = signal(false);
  readonly registrationError = signal<string | null>(null);
  readonly showNamePrompt = signal(false);
  readonly deletingId = signal<string | null>(null);

  readonly showEditModal = signal(false);
  readonly editingPasskey = signal<AccountPasskey | null>(null);
  readonly saving = signal(false);
  readonly editError = signal<string | null>(null);

  newPasskeyName = '';
  editName = '';

  private pendingRegistration: {
    registrationId: string;
    attestationResponse: string;
  } | null = null;

  readonly webAuthnSupported = typeof window !== 'undefined' && !!window.PublicKeyCredential;

  ngOnInit(): void {
    this.loadPasskeys();
  }

  loadPasskeys(): void {
    this.loading.set(true);
    this.passkeysService.getPasskeys().subscribe({
      next: (list) => {
        this.passkeyList.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load passkeys:', err);
        // Set as not enabled if we get an error (likely FIDO2 not configured)
        this.passkeyList.set({
          passkeys: [],
          isEnabled: false,
          message: 'Passkeys are not available. The FIDO2 module may not be configured.',
        });
        this.loading.set(false);
      },
    });
  }

  async startRegistration(): Promise<void> {
    if (!this.webAuthnSupported) {
      this.registrationError.set('WebAuthn is not supported in this browser');
      return;
    }

    this.registering.set(true);
    this.registrationError.set(null);

    try {
      // Step 1: Get registration options from server
      const response = await this.passkeysService
        .startRegistration({ requireDiscoverableCredential: true })
        .toPromise();

      if (!response) {
        throw new Error('Failed to get registration options');
      }

      // Step 2: Convert options for WebAuthn API
      const publicKeyOptions = convertOptionsForWebAuthn(response.options);

      // Step 3: Create credential using browser API
      const credential = (await navigator.credentials.create({
        publicKey: publicKeyOptions,
      })) as PublicKeyCredential;

      if (!credential) {
        throw new Error('Failed to create credential');
      }

      const attestationResponse = credential.response as AuthenticatorAttestationResponse;

      // Step 4: Build attestation response
      const attestationResponseJson = JSON.stringify({
        id: credential.id,
        rawId: arrayBufferToBase64Url(credential.rawId),
        type: credential.type,
        response: {
          clientDataJSON: arrayBufferToBase64Url(attestationResponse.clientDataJSON),
          attestationObject: arrayBufferToBase64Url(attestationResponse.attestationObject),
          transports: attestationResponse.getTransports?.() || [],
        },
      });

      // Step 5: Store pending registration and show name prompt
      this.pendingRegistration = {
        registrationId: response.registrationId,
        attestationResponse: attestationResponseJson,
      };
      this.newPasskeyName = getDeviceName();
      this.showNamePrompt.set(true);
      this.registering.set(false);
    } catch (error: any) {
      console.error('Passkey registration failed:', error);
      if (error.name === 'NotAllowedError') {
        this.registrationError.set('Registration was cancelled or not allowed');
      } else if (error.name === 'SecurityError') {
        this.registrationError.set('Security error - please ensure you are using HTTPS');
      } else {
        this.registrationError.set(error.message || 'Failed to register passkey');
      }
      this.registering.set(false);
    }
  }

  completeRegistration(): void {
    if (!this.pendingRegistration) {
      return;
    }

    this.completing.set(true);
    this.registrationError.set(null);

    this.passkeysService
      .completeRegistration(
        this.pendingRegistration.registrationId,
        this.pendingRegistration.attestationResponse,
        this.newPasskeyName || getDeviceName()
      )
      .subscribe({
        next: () => {
          this.completing.set(false);
          this.showNamePrompt.set(false);
          this.pendingRegistration = null;
          this.newPasskeyName = '';
          this.loadPasskeys();
        },
        error: (err) => {
          console.error('Failed to complete registration:', err);
          this.registrationError.set(err.error?.error || err.message || 'Failed to register passkey');
          this.completing.set(false);
        },
      });
  }

  cancelRegistration(): void {
    this.showNamePrompt.set(false);
    this.pendingRegistration = null;
    this.newPasskeyName = '';
    this.registrationError.set(null);
    this.registering.set(false);
  }

  startEdit(passkey: AccountPasskey): void {
    this.editingPasskey.set(passkey);
    this.editName = passkey.name;
    this.editError.set(null);
    this.showEditModal.set(true);
  }

  saveEdit(): void {
    const passkey = this.editingPasskey();
    if (!passkey || !this.editName.trim()) {
      return;
    }

    this.saving.set(true);
    this.editError.set(null);

    this.passkeysService.updatePasskey(passkey.id, this.editName.trim()).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeEditModal();
        this.loadPasskeys();
      },
      error: (err) => {
        console.error('Failed to rename passkey:', err);
        this.editError.set(err.error?.error || 'Failed to rename passkey');
        this.saving.set(false);
      },
    });
  }

  closeEditModal(): void {
    this.showEditModal.set(false);
    this.editingPasskey.set(null);
    this.editName = '';
    this.editError.set(null);
  }

  deletePasskey(passkey: AccountPasskey): void {
    const list = this.passkeyList();
    if (list?.passkeys.length === 1) {
      if (!confirm('This is your last passkey. Are you sure you want to delete it?')) {
        return;
      }
    } else {
      if (!confirm(`Delete passkey "${passkey.name}"?`)) {
        return;
      }
    }

    this.deletingId.set(passkey.id);
    this.passkeysService.deletePasskey(passkey.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.loadPasskeys();
      },
      error: (err) => {
        console.error('Failed to delete passkey:', err);
        alert(err.error?.error || 'Failed to delete passkey');
        this.deletingId.set(null);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
