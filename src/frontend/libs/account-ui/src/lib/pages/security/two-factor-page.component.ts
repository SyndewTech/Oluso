import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CardComponent, ButtonComponent, InputComponent, ModalComponent, BadgeComponent, IconComponent } from '@oluso/ui-core';
import { AccountService } from '../../services/account.service';
import { MfaStatus, MfaSetupResponse } from '../../models';

type MfaMethod = 'totp' | 'sms' | 'email';

@Component({
  selector: 'account-two-factor-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, CardComponent, ButtonComponent, InputComponent, ModalComponent, BadgeComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <div class="flex items-center gap-4">
        <a routerLink="/security" class="text-gray-500 hover:text-gray-700">
          <ui-icon name="chevron-left" class="h-5 w-5" />
        </a>
        <div>
          <h1 class="text-2xl font-semibold text-gray-900">Two-Factor Authentication</h1>
          <p class="text-gray-500 mt-1">Manage your two-factor authentication methods</p>
        </div>
      </div>

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
      } @else {
        <!-- Authenticator App (TOTP) -->
        <ui-card title="Authenticator App">
          <div class="flex items-center justify-between">
            <div class="flex-1">
              <div class="flex items-center gap-2 mb-1">
                <ui-icon name="device-phone-mobile" class="h-5 w-5 text-gray-400" />
                <span class="font-medium">Authenticator App</span>
                @if (mfaStatus()?.hasTotp) {
                  <ui-badge variant="success">Enabled</ui-badge>
                }
              </div>
              <p class="text-sm text-gray-600">
                Use an authenticator app like Google Authenticator or Authy
              </p>
            </div>
            @if (mfaStatus()?.hasTotp) {
              <ui-button variant="danger" size="sm" (clicked)="disableMethod('totp')">
                Disable
              </ui-button>
            } @else {
              <ui-button variant="secondary" (clicked)="startSetup('totp')">
                Set Up
              </ui-button>
            }
          </div>
        </ui-card>

        <!-- SMS -->
        @if (mfaStatus()?.availableMethods?.includes('sms')) {
          <ui-card title="SMS Authentication">
            <div class="flex items-center justify-between">
              <div class="flex-1">
                <div class="flex items-center gap-2 mb-1">
                  <ui-icon name="chat-bubble-left" class="h-5 w-5 text-gray-400" />
                  <span class="font-medium">SMS</span>
                  @if (mfaStatus()?.hasSms) {
                    <ui-badge variant="success">Enabled</ui-badge>
                  }
                </div>
                <p class="text-sm text-gray-600">
                  Receive verification codes via SMS
                </p>
              </div>
              @if (mfaStatus()?.hasSms) {
                <ui-button variant="danger" size="sm" (clicked)="disableMethod('sms')">
                  Disable
                </ui-button>
              } @else {
                <ui-button variant="secondary" (clicked)="startSetup('sms')">
                  Set Up
                </ui-button>
              }
            </div>
          </ui-card>
        }

        <!-- Email -->
        @if (mfaStatus()?.availableMethods?.includes('email')) {
          <ui-card title="Email Authentication">
            <div class="flex items-center justify-between">
              <div class="flex-1">
                <div class="flex items-center gap-2 mb-1">
                  <ui-icon name="envelope" class="h-5 w-5 text-gray-400" />
                  <span class="font-medium">Email</span>
                  @if (mfaStatus()?.hasEmail) {
                    <ui-badge variant="success">Enabled</ui-badge>
                  }
                </div>
                <p class="text-sm text-gray-600">
                  Receive verification codes via email
                </p>
              </div>
              @if (mfaStatus()?.hasEmail) {
                <ui-button variant="danger" size="sm" (clicked)="disableMethod('email')">
                  Disable
                </ui-button>
              } @else {
                <ui-button variant="secondary" (clicked)="startSetup('email')">
                  Set Up
                </ui-button>
              }
            </div>
          </ui-card>
        }
      }
    </div>

    <!-- Setup Modal -->
    <ui-modal
      [open]="showSetupModal()"
      [title]="'Set Up ' + getMethodName(setupMethod())"
      (closed)="closeSetupModal()"
    >
      <div class="space-y-4">
        @if (setupStep() === 'loading') {
          <div class="flex justify-center py-8">
            <svg class="animate-spin h-8 w-8 text-primary-600" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
            </svg>
          </div>
        } @else if (setupStep() === 'setup') {
          @if (setupMethod() === 'totp' && setupResponse()?.qrCodeImage) {
            <div class="text-center">
              <p class="text-sm text-gray-600 mb-4">
                Scan this QR code with your authenticator app
              </p>
              <div class="flex justify-center mb-4">
                <div class="p-4 bg-white border rounded-lg">
                  <img [src]="setupResponse()?.qrCodeImage" alt="QR Code" class="w-48 h-48" />
                </div>
              </div>
              <div class="bg-gray-50 rounded-lg p-4">
                <p class="text-xs text-gray-500 mb-1">Or enter this key manually:</p>
                <code class="text-sm font-mono text-gray-700">{{ setupResponse()?.sharedKey }}</code>
              </div>
            </div>
          } @else {
            <p class="text-sm text-gray-600">
              A verification code has been sent to your {{ setupMethod() === 'sms' ? 'phone' : 'email' }}.
            </p>
          }

          <ui-input
            label="Verification Code"
            [(ngModel)]="verificationCode"
            name="verificationCode"
            placeholder="Enter 6-digit code"
            [required]="true"
          ></ui-input>

          @if (setupError()) {
            <div class="bg-red-50 text-red-700 p-3 rounded-md text-sm">
              {{ setupError() }}
            </div>
          }
        }
      </div>

      <div modal-footer class="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="closeSetupModal()">Cancel</ui-button>
        @if (setupStep() === 'setup') {
          <ui-button [loading]="verifying()" (clicked)="verifySetup()">
            Verify & Enable
          </ui-button>
        }
      </div>
    </ui-modal>
  `,
})
export class TwoFactorPageComponent implements OnInit {
  private readonly accountService = inject(AccountService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly mfaStatus = signal<MfaStatus | null>(null);

  readonly showSetupModal = signal(false);
  readonly setupMethod = signal<MfaMethod>('totp');
  readonly setupStep = signal<'loading' | 'setup'>('loading');
  readonly setupResponse = signal<MfaSetupResponse | null>(null);
  readonly setupError = signal<string | null>(null);
  readonly verifying = signal(false);

  verificationCode = '';

  ngOnInit(): void {
    this.loadMfaStatus();
  }

  loadMfaStatus(): void {
    this.loading.set(true);
    this.accountService.getMfaStatus().subscribe({
      next: (status) => {
        this.mfaStatus.set(status);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load MFA status:', err);
        this.loading.set(false);
      },
    });
  }

  startSetup(method: MfaMethod): void {
    this.setupMethod.set(method);
    this.setupStep.set('loading');
    this.setupError.set(null);
    this.verificationCode = '';
    this.showSetupModal.set(true);

    this.accountService.setupMfaMethod(method).subscribe({
      next: (response) => {
        this.setupResponse.set(response);
        this.setupStep.set('setup');
      },
      error: (err) => {
        console.error('Failed to start MFA setup:', err);
        this.setupError.set(err.error?.message || 'Failed to start setup');
        this.setupStep.set('setup');
      },
    });
  }

  verifySetup(): void {
    if (!this.verificationCode) {
      this.setupError.set('Please enter the verification code');
      return;
    }

    this.verifying.set(true);
    this.setupError.set(null);

    this.accountService.verifyMfaMethod(this.setupMethod(), this.verificationCode).subscribe({
      next: () => {
        this.verifying.set(false);
        this.closeSetupModal();
        this.loadMfaStatus();
      },
      error: (err) => {
        console.error('Failed to verify MFA:', err);
        this.setupError.set(err.error?.message || 'Invalid verification code');
        this.verifying.set(false);
      },
    });
  }

  disableMethod(method: MfaMethod): void {
    if (!confirm(`Are you sure you want to disable ${this.getMethodName(method)}?`)) {
      return;
    }

    this.accountService.disableMfaMethod(method).subscribe({
      next: () => this.loadMfaStatus(),
      error: (err) => {
        console.error('Failed to disable MFA method:', err);
        alert('Failed to disable MFA method');
      },
    });
  }

  closeSetupModal(): void {
    this.showSetupModal.set(false);
    this.setupResponse.set(null);
    this.setupError.set(null);
    this.verificationCode = '';
  }

  getMethodName(method: MfaMethod | null): string {
    switch (method) {
      case 'totp':
        return 'Authenticator App';
      case 'sms':
        return 'SMS Authentication';
      case 'email':
        return 'Email Authentication';
      default:
        return '';
    }
  }

}
