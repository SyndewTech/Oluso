import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonComponent, IconComponent, TAB_CONTEXT, TabContext } from '@oluso/ui-core';
import { Fido2Service } from '../../services/fido2.service';
import { Fido2Credential } from '../../models/fido2.model';

@Component({
  selector: 'fido2-user-passkeys-tab',
  standalone: true,
  imports: [CommonModule, ButtonComponent, IconComponent],
  template: `
    <div class="bg-white shadow rounded-lg">
      <div class="px-4 py-5 sm:p-6">
        <div class="flex items-center justify-between mb-4">
          <h3 class="text-lg font-medium leading-6 text-gray-900">
            Passkeys / FIDO2 Credentials
          </h3>
          <span class="text-sm text-gray-500">
            {{ credentials().length }} registered
          </span>
        </div>

        @if (loading()) {
          <div class="animate-pulse space-y-4">
            <div class="h-4 bg-gray-200 rounded w-1/4"></div>
            <div class="h-10 bg-gray-200 rounded"></div>
            <div class="h-10 bg-gray-200 rounded"></div>
          </div>
        } @else if (error()) {
          <div class="text-red-600">{{ error() }}</div>
        } @else if (credentials().length > 0) {
          <div class="space-y-3">
            @for (credential of credentials(); track credential.id) {
              <div class="flex items-center justify-between p-4 bg-gray-50 rounded-lg">
                <div class="flex items-center gap-4">
                  <div class="flex-shrink-0">
                    @if (credential.authenticatorType.toLowerCase().includes('platform')) {
                      <ui-icon name="device-phone-mobile" class="h-5 w-5 text-gray-400" />
                    } @else if (credential.authenticatorType.toLowerCase().includes('cross-platform') || credential.authenticatorType.toLowerCase().includes('security-key')) {
                      <ui-icon name="key" class="h-5 w-5 text-gray-400" />
                    } @else {
                      <ui-icon name="fingerprint" class="h-5 w-5 text-gray-400" />
                    }
                  </div>
                  <div>
                    <p class="text-sm font-medium text-gray-900">
                      {{ credential.displayName || 'Unnamed Passkey' }}
                    </p>
                    <div class="flex items-center gap-3 mt-1">
                      <span class="text-xs text-gray-500">
                        Created: {{ formatDate(credential.createdAt) }}
                      </span>
                      @if (credential.lastUsedAt) {
                        <span class="text-xs text-gray-500">
                          Last used: {{ formatDate(credential.lastUsedAt) }}
                        </span>
                      }
                      <span [class]="'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ' +
                        (credential.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-800')">
                        {{ credential.isActive ? 'Active' : 'Inactive' }}
                      </span>
                      @if (credential.isBackedUp) {
                        <span class="inline-flex items-center rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-800">
                          Backed up
                        </span>
                      }
                    </div>
                    <p class="text-xs text-gray-400 mt-1 font-mono">
                      {{ credential.authenticatorType }}
                    </p>
                  </div>
                </div>

                <div class="flex items-center gap-2">
                  @if (deleteConfirm() === credential.id) {
                    <ui-button
                      variant="danger"
                      size="sm"
                      [disabled]="deleting()"
                      (clicked)="confirmDelete(credential.id)"
                    >
                      {{ deleting() ? 'Deleting...' : 'Confirm' }}
                    </ui-button>
                    <ui-button
                      variant="secondary"
                      size="sm"
                      (clicked)="cancelDelete()"
                    >
                      Cancel
                    </ui-button>
                  } @else {
                    <button
                      (click)="startDelete(credential.id)"
                      class="p-2 text-gray-400 hover:text-red-600 rounded hover:bg-gray-100"
                      title="Delete passkey"
                    >
                      <ui-icon name="trash" class="h-4 w-4" />
                    </button>
                  }
                </div>
              </div>
            }
          </div>
        } @else {
          <div class="text-center py-8">
            <ui-icon name="fingerprint" class="mx-auto h-12 w-12 text-gray-300" />
            <h3 class="mt-2 text-sm font-medium text-gray-900">No passkeys</h3>
            <p class="mt-1 text-sm text-gray-500">
              This user has not registered any FIDO2 passkeys.
            </p>
          </div>
        }
      </div>
    </div>
  `,
})
export class UserPasskeysTabComponent implements OnInit {
  private readonly fido2Service = inject(Fido2Service);
  private readonly tabContext = inject(TAB_CONTEXT, { optional: true });

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly credentials = signal<Fido2Credential[]>([]);
  readonly deleteConfirm = signal<string | null>(null);
  readonly deleting = signal(false);

  ngOnInit(): void {
    this.loadCredentials();
  }

  loadCredentials(): void {
    const userId = this.tabContext?.entityId;
    if (!userId) {
      this.error.set('User ID not available');
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.fido2Service.getUserCredentials(userId).subscribe({
      next: (credentials) => {
        this.credentials.set(credentials);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading credentials:', err);
        this.error.set('Failed to load passkeys');
        this.loading.set(false);
      },
    });
  }

  startDelete(credentialId: string): void {
    this.deleteConfirm.set(credentialId);
  }

  cancelDelete(): void {
    this.deleteConfirm.set(null);
  }

  confirmDelete(credentialId: string): void {
    const userId = this.tabContext?.entityId;
    if (!userId) return;

    this.deleting.set(true);
    this.fido2Service.deleteUserCredential(userId, credentialId).subscribe({
      next: () => {
        this.credentials.update((creds) => creds.filter((c) => c.id !== credentialId));
        this.deleteConfirm.set(null);
        this.deleting.set(false);
      },
      error: (err) => {
        console.error('Error deleting credential:', err);
        this.deleting.set(false);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
