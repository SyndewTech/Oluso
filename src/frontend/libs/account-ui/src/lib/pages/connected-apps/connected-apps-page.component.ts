import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent } from '@oluso/ui-core';
import { AccountService } from '../../services/account.service';
import { ConnectedApp, ExternalLogin } from '../../models';

@Component({
  selector: 'account-connected-apps-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <div>
        <h1 class="text-2xl font-semibold text-gray-900">Connected Apps</h1>
        <p class="text-gray-500 mt-1">Applications that have access to your account. You can revoke access at any time.</p>
      </div>

      @if (loading() && loadingExternalLogins()) {
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
        <!-- External Logins / Linked Accounts Section -->
        @if (externalLogins().length > 0) {
          <div class="mb-8">
            <h2 class="text-lg font-semibold text-gray-900 mb-2">Linked Accounts</h2>
            <p class="text-sm text-gray-500 mb-4">Sign in with these external identity providers.</p>
            <div class="space-y-3">
              @for (login of externalLogins(); track login.provider + login.providerKey) {
                <ui-card>
                  <div class="flex items-center justify-between">
                    <div class="flex items-center gap-3">
                      <div class="h-10 w-10 rounded-full bg-gray-100 flex items-center justify-center">
                        <ui-icon name="user" class="h-6 w-6 text-gray-400" />
                      </div>
                      <div>
                        <p class="text-sm font-medium text-gray-900">{{ login.displayName }}</p>
                        <p class="text-xs text-gray-500">{{ login.provider }}</p>
                      </div>
                    </div>
                    <ui-button
                      variant="danger"
                      size="sm"
                      [loading]="unlinkingProvider() === login.provider"
                      (clicked)="confirmUnlink(login)"
                    >
                      Unlink
                    </ui-button>
                  </div>
                </ui-card>
              }
            </div>
          </div>
        }

        <!-- Connected OAuth Apps Section -->
        <div class="mb-4">
          <h2 class="text-lg font-semibold text-gray-900">Connected Applications</h2>
          <p class="text-sm text-gray-500 mt-1">Third-party applications you've authorized.</p>
        </div>

        @if (apps().length === 0) {
          <ui-card>
            <div class="text-center py-8 text-gray-500">
              <ui-icon name="link" class="mx-auto h-12 w-12 text-gray-400" />
              <p class="mt-4">No connected applications</p>
              <p class="text-sm mt-1">Apps that you authorize will appear here</p>
            </div>
          </ui-card>
        } @else {
          <div class="space-y-4">
            @for (app of apps(); track app.clientId) {
              <ui-card>
                <div class="flex items-start justify-between">
                  <div class="flex items-start gap-4">
                    @if (app.logoUri) {
                      <img
                        [src]="app.logoUri"
                        [alt]="app.clientName"
                        class="h-12 w-12 rounded-lg object-contain"
                      />
                    } @else {
                      <div class="h-12 w-12 rounded-lg bg-gray-100 flex items-center justify-center">
                        <ui-icon name="link" class="h-6 w-6 text-gray-400" />
                      </div>
                    }
                    <div class="flex-1">
                      <div class="flex items-center gap-2">
                        <h3 class="font-medium text-gray-900">{{ app.clientName }}</h3>
                        @if (app.hasActiveTokens) {
                          <ui-badge variant="success">Active</ui-badge>
                        }
                      </div>
                      @if (app.description) {
                        <p class="text-sm text-gray-600 mt-1">{{ app.description }}</p>
                      }
                      @if (app.clientUri) {
                        <a
                          [href]="app.clientUri"
                          target="_blank"
                          rel="noopener noreferrer"
                          class="text-sm text-primary-600 hover:underline"
                        >
                          {{ getHostname(app.clientUri) }}
                        </a>
                      }
                      @if (app.scopes && app.scopes.length > 0) {
                        <div class="mt-3">
                          <p class="text-xs font-medium text-gray-500 mb-1">Access granted:</p>
                          <div class="flex flex-wrap gap-1">
                            @for (scope of app.scopes; track scope) {
                              <span class="inline-flex items-center px-2 py-0.5 rounded text-xs bg-gray-100 text-gray-700">
                                {{ scope }}
                              </span>
                            }
                          </div>
                        </div>
                      }
                      <div class="text-xs text-gray-500 mt-3">
                        <span>Connected {{ formatDate(app.firstConnectedAt) }}</span>
                        <span class="mx-2">•</span>
                        <span>Last used {{ formatDate(app.lastUsedAt) }}</span>
                      </div>
                    </div>
                  </div>
                  <ui-button
                    variant="danger"
                    size="sm"
                    [loading]="revokingAppId() === app.clientId"
                    (clicked)="confirmRevoke(app)"
                  >
                    Revoke
                  </ui-button>
                </div>
              </ui-card>
            }
          </div>
        }
      }
    </div>

    <!-- Unlink Confirmation Modal -->
    <ui-modal
      [open]="showUnlinkModal()"
      title="Unlink Account"
      (closed)="showUnlinkModal.set(false)"
    >
      <p class="text-sm text-gray-600">
        Are you sure you want to unlink your <strong>{{ loginToUnlink()?.displayName }}</strong> account?
        You will no longer be able to sign in with this provider.
      </p>

      <div modal-footer class="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="showUnlinkModal.set(false)">Cancel</ui-button>
        <ui-button variant="danger" [loading]="unlinkingProvider() !== null" (clicked)="unlinkAccount()">
          Unlink
        </ui-button>
      </div>
    </ui-modal>

    <!-- Revoke Confirmation Modal -->
    <ui-modal
      [open]="showRevokeModal()"
      title="Revoke Access"
      (closed)="showRevokeModal.set(false)"
    >
      <p class="text-sm text-gray-600">
        Are you sure you want to revoke access for <strong>{{ appToRevoke()?.clientName }}</strong>?
        This will sign you out of the application and remove all permissions.
      </p>

      <div modal-footer class="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="showRevokeModal.set(false)">Cancel</ui-button>
        <ui-button variant="danger" [loading]="revokingAppId() !== null" (clicked)="revokeAccess()">
          Revoke Access
        </ui-button>
      </div>
    </ui-modal>
  `,
})
export class ConnectedAppsPageComponent implements OnInit {
  private readonly accountService = inject(AccountService);

  // Connected Apps state
  readonly loading = signal(true);
  readonly apps = signal<ConnectedApp[]>([]);
  readonly revokingAppId = signal<string | null>(null);
  readonly showRevokeModal = signal(false);
  readonly appToRevoke = signal<ConnectedApp | null>(null);

  // External Logins state
  readonly loadingExternalLogins = signal(true);
  readonly externalLogins = signal<ExternalLogin[]>([]);
  readonly unlinkingProvider = signal<string | null>(null);
  readonly showUnlinkModal = signal(false);
  readonly loginToUnlink = signal<ExternalLogin | null>(null);

  ngOnInit(): void {
    this.loadApps();
    this.loadExternalLogins();
  }

  loadApps(): void {
    this.loading.set(true);
    this.accountService.getConnectedApps().subscribe({
      next: (apps) => {
        this.apps.set(apps);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load connected apps:', err);
        this.loading.set(false);
      },
    });
  }

  loadExternalLogins(): void {
    this.loadingExternalLogins.set(true);
    this.accountService.getExternalLogins().subscribe({
      next: (logins) => {
        this.externalLogins.set(logins);
        this.loadingExternalLogins.set(false);
      },
      error: (err) => {
        console.error('Failed to load external logins:', err);
        this.loadingExternalLogins.set(false);
      },
    });
  }

  // Connected Apps methods
  confirmRevoke(app: ConnectedApp): void {
    this.appToRevoke.set(app);
    this.showRevokeModal.set(true);
  }

  revokeAccess(): void {
    const app = this.appToRevoke();
    if (!app) return;

    this.revokingAppId.set(app.clientId);
    this.accountService.revokeConnectedApp(app.clientId).subscribe({
      next: () => {
        this.apps.update((apps) => apps.filter((a) => a.clientId !== app.clientId));
        this.revokingAppId.set(null);
        this.showRevokeModal.set(false);
        this.appToRevoke.set(null);
      },
      error: (err) => {
        console.error('Failed to revoke access:', err);
        this.revokingAppId.set(null);
        alert('Failed to revoke access');
      },
    });
  }

  // External Logins methods
  confirmUnlink(login: ExternalLogin): void {
    this.loginToUnlink.set(login);
    this.showUnlinkModal.set(true);
  }

  unlinkAccount(): void {
    const login = this.loginToUnlink();
    if (!login) return;

    this.unlinkingProvider.set(login.provider);
    this.accountService.removeExternalLogin(login.provider, login.providerKey).subscribe({
      next: () => {
        this.externalLogins.update((logins) =>
          logins.filter((l) => !(l.provider === login.provider && l.providerKey === login.providerKey))
        );
        this.unlinkingProvider.set(null);
        this.showUnlinkModal.set(false);
        this.loginToUnlink.set(null);
      },
      error: (err) => {
        console.error('Failed to unlink account:', err);
        this.unlinkingProvider.set(null);
        const message = err.error?.error || 'Failed to unlink account';
        alert(message);
      },
    });
  }

  getHostname(url: string): string {
    try {
      return new URL(url).hostname;
    } catch {
      return url;
    }
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return 'today';
    if (diffDays === 1) return 'yesterday';
    if (diffDays < 7) return `${diffDays} days ago`;
    if (diffDays < 30) return `${Math.floor(diffDays / 7)} weeks ago`;

    return date.toLocaleDateString();
  }
}
