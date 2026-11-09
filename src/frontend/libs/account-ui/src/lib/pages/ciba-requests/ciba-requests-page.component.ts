import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardComponent, ButtonComponent, IconComponent } from '@syndew/oluso-ui-core';
import { AccountService } from '../../services/account.service';
import { CibaRequest } from '../../models';

// Scope descriptions for common OIDC scopes
const scopeDescriptions: Record<string, string> = {
  openid: 'Verify your identity',
  profile: 'Access your profile information (name, picture)',
  email: 'Access your email address',
  address: 'Access your address',
  phone: 'Access your phone number',
  offline_access: "Maintain access when you're not using the app",
};

@Component({
  selector: 'account-ciba-requests-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <div>
        <h1 class="text-2xl font-semibold text-gray-900">Authentication Requests</h1>
        <p class="text-gray-500 mt-1">Review and respond to pending sign-in requests from applications.</p>
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
            <path
              class="opacity-75"
              fill="currentColor"
              d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
            ></path>
          </svg>
        </div>
      } @else if (error()) {
        <ui-card>
          <div class="text-center py-8">
            <ui-icon name="exclamation-triangle" class="mx-auto h-12 w-12 text-red-400" />
            <p class="mt-4 text-red-600">Failed to load authentication requests.</p>
            <ui-button class="mt-4" variant="secondary" (clicked)="loadRequests()">Retry</ui-button>
          </div>
        </ui-card>
      } @else if (requests().length === 0) {
        <ui-card>
          <div class="text-center py-8 text-gray-500">
            <ui-icon name="shield-check" class="mx-auto h-12 w-12 text-gray-400" />
            <h3 class="mt-4 text-sm font-semibold text-gray-900">No pending requests</h3>
            <p class="mt-1 text-sm">You have no pending authentication requests at this time.</p>
          </div>
        </ui-card>
      } @else {
        <div class="space-y-4">
          @for (request of requests(); track request.authReqId) {
            <ui-card>
              <div class="p-6">
                <div class="flex items-start gap-4">
                  @if (request.clientLogoUri) {
                    <img
                      [src]="request.clientLogoUri"
                      [alt]="request.clientName || request.clientId"
                      class="h-12 w-12 rounded-lg object-contain bg-gray-100"
                    />
                  } @else {
                    <div class="h-12 w-12 rounded-lg bg-primary-100 flex items-center justify-center">
                      <span class="text-primary-600 text-lg font-semibold">
                        {{ (request.clientName || request.clientId).charAt(0).toUpperCase() }}
                      </span>
                    </div>
                  }
                  <div class="flex-1 min-w-0">
                    <h3 class="text-lg font-semibold text-gray-900">
                      {{ request.clientName || request.clientId }}
                    </h3>
                    <p class="text-sm text-gray-500">wants to sign you in</p>
                  </div>
                  <div class="flex items-center text-sm text-gray-500">
                    <ui-icon name="clock" class="h-4 w-4 mr-1" />
                    Expires {{ formatTimeRemaining(request.expiresAt) }}
                  </div>
                </div>

                @if (request.bindingMessage) {
                  <div class="mt-4 p-3 bg-blue-50 border border-blue-200 rounded-md">
                    <p class="text-sm font-medium text-blue-800">
                      Verification code: <span class="font-mono">{{ request.bindingMessage }}</span>
                    </p>
                    <p class="text-xs text-blue-600 mt-1">
                      Make sure this code matches what you see on the requesting device.
                    </p>
                  </div>
                }

                <div class="mt-4">
                  <p class="text-sm font-medium text-gray-700 mb-2">This application is requesting access to:</p>
                  <ul class="space-y-1">
                    @for (scope of request.requestedScopes; track scope) {
                      <li class="flex items-center text-sm text-gray-600">
                        <ui-icon name="shield-check" class="h-4 w-4 mr-2 text-green-500" />
                        {{ getScopeDescription(scope) }}
                      </li>
                    }
                  </ul>
                </div>
              </div>

              <div class="bg-gray-50 px-6 py-4 flex justify-end gap-3 border-t border-gray-200">
                <ui-button
                  variant="secondary"
                  [loading]="processingId() === request.authReqId && processingAction() === 'deny'"
                  [disabled]="processingId() !== null"
                  (clicked)="denyRequest(request)"
                >
                  <ui-icon name="x-circle" class="h-4 w-4 mr-1.5 text-red-500" />
                  Deny
                </ui-button>
                <ui-button
                  variant="primary"
                  [loading]="processingId() === request.authReqId && processingAction() === 'approve'"
                  [disabled]="processingId() !== null"
                  (clicked)="approveRequest(request)"
                >
                  <ui-icon name="shield-check" class="h-4 w-4 mr-1.5" />
                  Approve
                </ui-button>
              </div>
            </ui-card>
          }
        </div>
      }
    </div>
  `,
})
export class CibaRequestsPageComponent implements OnInit, OnDestroy {
  private readonly accountService = inject(AccountService);
  private refreshInterval: ReturnType<typeof setInterval> | null = null;

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly requests = signal<CibaRequest[]>([]);
  readonly processingId = signal<string | null>(null);
  readonly processingAction = signal<'approve' | 'deny' | null>(null);

  ngOnInit(): void {
    this.loadRequests();
    // Refresh every 10 seconds for pending requests
    this.refreshInterval = setInterval(() => this.loadRequests(false), 10000);
  }

  ngOnDestroy(): void {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
  }

  loadRequests(showLoading = true): void {
    if (showLoading) {
      this.loading.set(true);
    }
    this.error.set(false);

    this.accountService.getPendingCibaRequests().subscribe({
      next: (requests) => {
        this.requests.set(requests);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load CIBA requests:', err);
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  approveRequest(request: CibaRequest): void {
    this.processingId.set(request.authReqId);
    this.processingAction.set('approve');

    this.accountService.approveCibaRequest(request.authReqId).subscribe({
      next: () => {
        this.requests.update((requests) => requests.filter((r) => r.authReqId !== request.authReqId));
        this.processingId.set(null);
        this.processingAction.set(null);
      },
      error: (err) => {
        console.error('Failed to approve request:', err);
        this.processingId.set(null);
        this.processingAction.set(null);
        alert(err.error?.error || 'Failed to approve request');
      },
    });
  }

  denyRequest(request: CibaRequest): void {
    this.processingId.set(request.authReqId);
    this.processingAction.set('deny');

    this.accountService.denyCibaRequest(request.authReqId).subscribe({
      next: () => {
        this.requests.update((requests) => requests.filter((r) => r.authReqId !== request.authReqId));
        this.processingId.set(null);
        this.processingAction.set(null);
      },
      error: (err) => {
        console.error('Failed to deny request:', err);
        this.processingId.set(null);
        this.processingAction.set(null);
        alert(err.error?.error || 'Failed to deny request');
      },
    });
  }

  getScopeDescription(scope: string): string {
    return scopeDescriptions[scope] || scope;
  }

  formatTimeRemaining(expiresAt: string): string {
    const date = new Date(expiresAt);
    const now = new Date();
    const diffMs = date.getTime() - now.getTime();

    if (diffMs <= 0) return 'expired';

    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);

    if (diffMins < 1) return 'in less than a minute';
    if (diffMins < 60) return `in ${diffMins} minute${diffMins === 1 ? '' : 's'}`;
    if (diffHours < 24) return `in ${diffHours} hour${diffHours === 1 ? '' : 's'}`;

    return `in ${Math.floor(diffHours / 24)} day${Math.floor(diffHours / 24) === 1 ? '' : 's'}`;
  }
}
