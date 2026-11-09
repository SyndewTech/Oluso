import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CardComponent, ButtonComponent, InputComponent, ModalComponent, BadgeComponent, IconComponent } from '@syndew/oluso-ui-core';
import { AccountService } from '../../services/account.service';
import { SecurityOverview, MfaStatus, ChangePasswordRequest, ExternalLogin } from '../../models';

@Component({
  selector: 'account-security-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, CardComponent, ButtonComponent, InputComponent, ModalComponent, BadgeComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <div>
        <h1 class="text-2xl font-semibold text-gray-900">Security</h1>
        <p class="text-gray-500 mt-1">Manage your account security settings</p>
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
        <!-- Password Section (an external provider manages the password of accounts signed in through it) -->
        @if (!securityOverview()?.isExternalAccount) {
          <ui-card title="Password">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm text-gray-600">
                  @if (securityOverview()?.hasPassword) {
                    @if (securityOverview()?.lastPasswordChange) {
                      Last changed {{ formatDate(securityOverview()!.lastPasswordChange!) }}
                    } @else {
                      Password is set
                    }
                  } @else {
                    No password set
                  }
                </p>
              </div>
              <ui-button variant="secondary" (clicked)="showPasswordModal.set(true)">
                {{ securityOverview()?.hasPassword ? 'Change Password' : 'Set Password' }}
              </ui-button>
            </div>
          </ui-card>
        }

        <!-- Two-Factor Authentication Section -->
        <ui-card title="Two-Factor Authentication">
          <div class="flex items-center justify-between">
            <div>
              <div class="flex items-center gap-2">
                @if (securityOverview()?.mfaEnabled) {
                  <ui-badge variant="success">Enabled</ui-badge>
                } @else {
                  <ui-badge variant="warning">Disabled</ui-badge>
                }
              </div>
              <p class="text-sm text-gray-600 mt-1">
                @if (mfaStatus()?.enabledMethods?.length) {
                  Active methods: {{ mfaStatus()!.enabledMethods.join(', ') }}
                } @else {
                  Add an extra layer of security to your account
                }
              </p>
            </div>
            <ui-button variant="secondary" routerLink="/security/two-factor">
              Manage
            </ui-button>
          </div>
        </ui-card>

        <!-- Passkeys Section -->
        <ui-card title="Passkeys">
          <div class="flex items-center justify-between">
            <div>
              <div class="flex items-center gap-2 mb-1">
                <ui-icon name="fingerprint" class="h-5 w-5 text-gray-400" />
                <span class="text-sm font-medium text-gray-900">Passwordless Sign-in</span>
              </div>
              <p class="text-sm text-gray-600">
                @if (securityOverview()?.passkeyCount) {
                  {{ securityOverview()!.passkeyCount }} passkey{{ securityOverview()!.passkeyCount > 1 ? 's' : '' }} registered
                } @else {
                  Sign in without a password using biometrics or security keys
                }
              </p>
            </div>
            <ui-button variant="secondary" routerLink="/security/passkeys">
              Manage
            </ui-button>
          </div>
        </ui-card>

        <!-- Email Verification -->
        <ui-card title="Email Verification">
          <div class="flex items-center justify-between">
            <div>
              @if (securityOverview()?.emailVerified) {
                <ui-badge variant="success">Verified</ui-badge>
              } @else {
                <ui-badge variant="warning">Not Verified</ui-badge>
              }
            </div>
            @if (!securityOverview()?.emailVerified) {
              <ui-button variant="secondary" routerLink="/security/verify" [queryParams]="{ type: 'email' }">
                Verify Email
              </ui-button>
            }
          </div>
        </ui-card>

        <!-- Phone Verification -->
        <ui-card title="Phone Verification">
          <div class="flex items-center justify-between">
            <div>
              @if (securityOverview()?.phoneNumberVerified) {
                <ui-badge variant="success">Verified</ui-badge>
              } @else {
                <ui-badge variant="warning">Not Verified</ui-badge>
              }
            </div>
            @if (!securityOverview()?.phoneNumberVerified) {
              <ui-button variant="secondary" routerLink="/security/verify" [queryParams]="{ type: 'phone' }">
                Verify Phone
              </ui-button>
            }
          </div>
        </ui-card>

        <!-- External Logins -->
        <ui-card title="Connected Accounts">
          <p class="text-sm text-gray-600 mb-4">
            External identity providers linked to your account
          </p>
          @if (externalLogins().length === 0) {
            <p class="text-sm text-gray-500 italic">No external accounts connected</p>
          } @else {
            <div class="space-y-3">
              @for (login of externalLogins(); track login.providerKey) {
                <div class="flex items-center justify-between p-3 bg-gray-50 rounded-lg">
                  <div class="flex items-center gap-3">
                    <div class="h-10 w-10 rounded-full bg-gray-200 flex items-center justify-center">
                      <span class="text-sm font-medium text-gray-600">
                        {{ login.provider.charAt(0).toUpperCase() }}
                      </span>
                    </div>
                    <div>
                      <p class="text-sm font-medium text-gray-900">{{ login.displayName || login.provider }}</p>
                      <p class="text-xs text-gray-500">{{ login.provider }}</p>
                    </div>
                  </div>
                  <ui-button
                    variant="danger"
                    size="sm"
                    (clicked)="removeExternalLogin(login)"
                  >
                    Remove
                  </ui-button>
                </div>
              }
            </div>
          }
        </ui-card>
      }
    </div>

    <!-- Change Password Modal -->
    <ui-modal
      [open]="showPasswordModal()"
      title="Change Password"
      (closed)="closePasswordModal()"
    >
      <form (ngSubmit)="changePassword()" class="space-y-4">
        @if (securityOverview()?.hasPassword) {
          <ui-input
            type="password"
            label="Current Password"
            [(ngModel)]="passwordForm.currentPassword"
            name="currentPassword"
            [required]="true"
          ></ui-input>
        }

        <ui-input
          type="password"
          label="New Password"
          [(ngModel)]="passwordForm.newPassword"
          name="newPassword"
          [required]="true"
        ></ui-input>

        <ui-input
          type="password"
          label="Confirm New Password"
          [(ngModel)]="passwordForm.confirmPassword"
          name="confirmPassword"
          [required]="true"
        ></ui-input>

        @if (passwordError()) {
          <div class="bg-red-50 text-red-700 p-3 rounded-md text-sm">
            {{ passwordError() }}
          </div>
        }
      </form>

      <div modal-footer class="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="closePasswordModal()">Cancel</ui-button>
        <ui-button [loading]="changingPassword()" (clicked)="changePassword()">
          Change Password
        </ui-button>
      </div>
    </ui-modal>
  `,
})
export class SecurityPageComponent implements OnInit {
  private readonly accountService = inject(AccountService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly securityOverview = signal<SecurityOverview | null>(null);
  readonly mfaStatus = signal<MfaStatus | null>(null);
  readonly externalLogins = signal<ExternalLogin[]>([]);

  readonly showPasswordModal = signal(false);
  readonly changingPassword = signal(false);
  readonly passwordError = signal<string | null>(null);

  passwordForm: ChangePasswordRequest = {
    currentPassword: '',
    newPassword: '',
    confirmPassword: '',
  };

  ngOnInit(): void {
    this.loadSecurityData();
  }

  loadSecurityData(): void {
    this.loading.set(true);

    // Load security overview
    this.accountService.getSecurityOverview().subscribe({
      next: (overview) => {
        this.securityOverview.set(overview);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load security overview:', err);
        this.loading.set(false);
      },
    });

    // Load MFA status
    this.accountService.getMfaStatus().subscribe({
      next: (status) => this.mfaStatus.set(status),
      error: (err) => console.error('Failed to load MFA status:', err),
    });

    // Load external logins
    this.accountService.getExternalLogins().subscribe({
      next: (logins) => this.externalLogins.set(logins),
      error: (err) => console.error('Failed to load external logins:', err),
    });
  }

  changePassword(): void {
    this.passwordError.set(null);

    if (this.passwordForm.newPassword !== this.passwordForm.confirmPassword) {
      this.passwordError.set('Passwords do not match');
      return;
    }

    if (this.passwordForm.newPassword.length < 8) {
      this.passwordError.set('Password must be at least 8 characters');
      return;
    }

    this.changingPassword.set(true);
    this.accountService.changePassword(this.passwordForm).subscribe({
      next: () => {
        this.changingPassword.set(false);
        this.closePasswordModal();
        this.loadSecurityData();
      },
      error: (err) => {
        console.error('Failed to change password:', err);
        this.passwordError.set(err.error?.message || 'Failed to change password');
        this.changingPassword.set(false);
      },
    });
  }

  closePasswordModal(): void {
    this.showPasswordModal.set(false);
    this.passwordForm = {
      currentPassword: '',
      newPassword: '',
      confirmPassword: '',
    };
    this.passwordError.set(null);
  }

  removeExternalLogin(login: ExternalLogin): void {
    if (!confirm(`Remove ${login.displayName || login.provider} from your account?`)) {
      return;
    }

    this.accountService.removeExternalLogin(login.provider, login.providerKey).subscribe({
      next: () => {
        this.externalLogins.update((logins) =>
          logins.filter((l) => l.providerKey !== login.providerKey)
        );
      },
      error: (err) => {
        console.error('Failed to remove external login:', err);
        alert('Failed to remove external login');
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
