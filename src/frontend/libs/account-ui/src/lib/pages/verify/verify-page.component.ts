import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { IconComponent } from '@oluso/ui-core';
import { AccountService } from '../../services/account.service';

type VerificationType = 'email' | 'phone';
type Step = 'send' | 'verify' | 'success';

@Component({
  selector: 'account-verify-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="max-w-md mx-auto">
      <!-- Back link -->
      @if (step() !== 'success') {
        <button
          (click)="goBack()"
          class="flex items-center text-sm text-gray-500 hover:text-gray-700 mb-6">
          <ui-icon name="arrow-left" class="h-4 w-4 mr-1" />
          Back to Security
        </button>
      }

      <!-- Success State -->
      @if (step() === 'success') {
        <div class="bg-white shadow rounded-lg p-8 text-center">
          <ui-icon name="check-circle" class="h-16 w-16 text-green-500 mx-auto mb-4" strokeWidth="1.5" />
          <h2 class="text-2xl font-bold text-gray-900 mb-2">
            {{ type === 'email' ? 'Email' : 'Phone Number' }} Verified!
          </h2>
          <p class="text-gray-600 mb-6">
            Your {{ type === 'email' ? 'email address' : 'phone number' }} has been successfully verified.
          </p>
          <button
            (click)="goBack()"
            class="w-full rounded-md bg-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-primary-500">
            Back to Security Settings
          </button>
        </div>
      } @else {
        <!-- Main Card -->
        <div class="bg-white shadow rounded-lg">
          <div class="px-6 py-5 border-b border-gray-200">
            <div class="flex items-center gap-x-3">
              @if (type === 'email') {
                <ui-icon name="envelope" class="h-6 w-6 text-gray-400" strokeWidth="1.5" />
              } @else {
                <ui-icon name="phone" class="h-6 w-6 text-gray-400" strokeWidth="1.5" />
              }
              <div>
                <h2 class="text-lg font-semibold text-gray-900">
                  Verify {{ type === 'email' ? 'Email Address' : 'Phone Number' }}
                </h2>
                <p class="text-sm text-gray-500">
                  @if (step() === 'send') {
                    We'll send a verification code to your {{ type }}
                  } @else {
                    Enter the 6-digit code sent to {{ maskedValue() || 'your ' + type }}
                  }
                </p>
              </div>
            </div>
          </div>

          <div class="px-6 py-5">
            <!-- Send Step -->
            @if (step() === 'send' && !showUpdateForm()) {
              <div class="space-y-4">
                <button
                  (click)="sendCode()"
                  [disabled]="sending()"
                  class="w-full rounded-md bg-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-primary-500 disabled:opacity-50">
                  {{ sending() ? 'Sending...' : 'Send Verification Code' }}
                </button>

                <div class="text-center">
                  <button
                    (click)="showUpdateForm.set(true)"
                    class="text-sm text-primary-600 hover:text-primary-500">
                    {{ type === 'email' ? 'Use a different email' : 'Use a different phone number' }}
                  </button>
                </div>
              </div>
            }

            <!-- Update Value Form -->
            @if (step() === 'send' && showUpdateForm()) {
              <form (ngSubmit)="updateValue()" class="space-y-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">
                    {{ type === 'email' ? 'New Email Address' : 'New Phone Number' }}
                  </label>
                  <input
                    [type]="type === 'email' ? 'email' : 'tel'"
                    [(ngModel)]="newValue"
                    name="newValue"
                    [placeholder]="type === 'email' ? 'you@example.com' : '+1 (555) 123-4567'"
                    class="w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                    required>
                </div>

                <div class="flex gap-3">
                  <button
                    type="button"
                    (click)="showUpdateForm.set(false); newValue = ''"
                    class="flex-1 rounded-md bg-white px-4 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50">
                    Cancel
                  </button>
                  <button
                    type="submit"
                    [disabled]="updating() || !newValue"
                    class="flex-1 rounded-md bg-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-primary-500 disabled:opacity-50">
                    {{ updating() ? 'Updating...' : 'Update & Verify' }}
                  </button>
                </div>
              </form>
            }

            <!-- Verify Step -->
            @if (step() === 'verify') {
              <form (ngSubmit)="verify()" class="space-y-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">
                    Verification Code
                  </label>
                  <input
                    type="text"
                    [(ngModel)]="code"
                    name="code"
                    (ngModelChange)="onCodeInput($event)"
                    placeholder="000000"
                    class="w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 text-center text-2xl tracking-widest font-mono"
                    maxlength="6"
                    autocomplete="one-time-code">
                  <p class="mt-2 text-sm text-gray-500 text-center">
                    Code sent to {{ maskedValue() }}
                  </p>
                </div>

                @if (error()) {
                  <p class="text-sm text-red-600 text-center">{{ error() }}</p>
                }

                <button
                  type="submit"
                  [disabled]="verifying() || code.length !== 6"
                  class="w-full rounded-md bg-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-primary-500 disabled:opacity-50">
                  {{ verifying() ? 'Verifying...' : 'Verify' }}
                </button>

                <div class="text-center">
                  <button
                    type="button"
                    (click)="resendCode()"
                    [disabled]="sending()"
                    class="text-sm text-primary-600 hover:text-primary-500 disabled:opacity-50">
                    Didn't receive a code? Resend
                  </button>
                </div>
              </form>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class VerifyPageComponent implements OnInit {
  private readonly accountService = inject(AccountService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  type: VerificationType = 'email';
  code = '';
  newValue = '';

  step = signal<Step>('send');
  maskedValue = signal<string | null>(null);
  showUpdateForm = signal(false);
  sending = signal(false);
  verifying = signal(false);
  updating = signal(false);
  error = signal<string | null>(null);

  ngOnInit(): void {
    const typeParam = this.route.snapshot.queryParamMap.get('type');
    if (typeParam === 'phone') {
      this.type = 'phone';
    }
  }

  sendCode(): void {
    this.sending.set(true);
    this.error.set(null);

    const request$ = this.type === 'email'
      ? this.accountService.sendEmailVerification()
      : this.accountService.sendPhoneVerification();

    request$.subscribe({
      next: () => {
        this.step.set('verify');
        this.sending.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.error || 'Failed to send verification code');
        this.sending.set(false);
      },
    });
  }

  resendCode(): void {
    this.code = '';
    this.sendCode();
  }

  verify(): void {
    if (this.code.length !== 6) return;

    this.verifying.set(true);
    this.error.set(null);

    const request$ = this.type === 'email'
      ? this.accountService.verifyEmail(this.code)
      : this.accountService.verifyPhone(this.code);

    request$.subscribe({
      next: () => {
        this.step.set('success');
        this.verifying.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.error || 'Invalid verification code');
        this.verifying.set(false);
      },
    });
  }

  updateValue(): void {
    if (!this.newValue) return;

    this.updating.set(true);
    this.error.set(null);

    const request$ = this.type === 'email'
      ? this.accountService.updateEmail(this.newValue)
      : this.accountService.updatePhone(this.newValue);

    request$.subscribe({
      next: () => {
        this.showUpdateForm.set(false);
        this.newValue = '';
        this.step.set('verify');
        this.updating.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.error || 'Failed to update');
        this.updating.set(false);
      },
    });
  }

  onCodeInput(value: string): void {
    // Only allow digits, max 6
    this.code = value.replace(/\D/g, '').slice(0, 6);
  }

  goBack(): void {
    this.router.navigate(['/security']);
  }
}
