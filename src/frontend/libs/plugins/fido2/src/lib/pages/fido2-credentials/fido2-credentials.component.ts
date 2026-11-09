import { Component, inject, signal, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, IconComponent, ModalComponent } from '@syndew/oluso-ui-core';
import { Fido2Service } from '../../services/fido2.service';
import { Fido2UserSummary, Fido2Credential } from '../../models/fido2.model';

@Component({
  selector: 'fido2-credentials',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, IconComponent, ModalComponent],
  template: `
    <div class="space-y-6">
      <div class="flex items-center justify-between">
        <h1 class="text-2xl font-bold text-gray-900">FIDO2 Credentials</h1>
        <span class="text-sm text-gray-500">
          {{ totalCount() }} user{{ totalCount() !== 1 ? 's' : '' }} with credentials
        </span>
      </div>

      <!-- Search -->
      <div class="flex items-center space-x-4">
        <div class="flex-1 max-w-md">
          <div class="relative">
            <ui-icon name="search" class="absolute left-3 top-1/2 transform -translate-y-1/2 h-5 w-5 text-gray-400" />
            <input
              type="text"
              placeholder="Search users..."
              [(ngModel)]="search"
              (ngModelChange)="onSearchChange()"
              class="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-md focus:ring-primary-500 focus:border-primary-500"
            />
          </div>
        </div>
      </div>

      @if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4">
          <p class="text-red-700">{{ error() }}</p>
        </div>
      }

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="bg-gray-200 h-16 rounded-lg animate-pulse"></div>
          }
        </div>
      } @else if (users().length === 0) {
        <div class="text-center py-12 bg-white rounded-lg shadow">
          <ui-icon name="fingerprint" class="mx-auto h-12 w-12 text-gray-400" />
          <h3 class="mt-2 text-sm font-medium text-gray-900">No credentials found</h3>
          <p class="mt-1 text-sm text-gray-500">
            {{ search ? 'No users match your search.' : 'No users have registered FIDO2 credentials yet.' }}
          </p>
        </div>
      } @else {
        <div class="bg-white shadow rounded-lg overflow-hidden">
          <ul class="divide-y divide-gray-200">
            @for (user of users(); track user.userId) {
              <li>
                <button
                  (click)="toggleUser(user.userId)"
                  class="w-full px-6 py-4 hover:bg-gray-50 text-left"
                >
                  <div class="flex items-center justify-between">
                    <div class="flex items-center space-x-4">
                      <ui-icon name="fingerprint" class="h-6 w-6 text-gray-400" />
                      <div>
                        <p class="font-medium text-gray-900">
                          {{ user.displayName || user.userName || user.email || user.userId }}
                        </p>
                        @if (user.email && user.displayName) {
                          <p class="text-sm text-gray-500">{{ user.email }}</p>
                        }
                      </div>
                    </div>
                    <div class="flex items-center space-x-4">
                      <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-primary-100 text-primary-800">
                        {{ user.credentialCount }} credential{{ user.credentialCount !== 1 ? 's' : '' }}
                      </span>
                      <ui-icon name="chevron-down" [class]="'h-5 w-5 text-gray-400 transform transition-transform ' + (expandedUser() === user.userId ? 'rotate-180' : '')" />
                    </div>
                  </div>
                </button>

                @if (expandedUser() === user.userId) {
                  <div class="px-6 pb-4 border-t border-gray-100 bg-gray-50">
                    <div class="pt-4">
                      @if (loadingCredentials() === user.userId) {
                        <div class="animate-pulse space-y-2">
                          @for (i of [1, 2]; track i) {
                            <div class="bg-gray-200 h-12 rounded-lg"></div>
                          }
                        </div>
                      } @else if (getUserCredentials(user.userId).length === 0) {
                        <p class="text-sm text-gray-500 italic">No credentials registered</p>
                      } @else {
                        <div class="space-y-2">
                          @for (cred of getUserCredentials(user.userId); track cred.id) {
                            <div class="flex items-center justify-between p-3 bg-white rounded-lg shadow-sm">
                              <div class="flex items-center space-x-3">
                                @if (cred.authenticatorType === 'platform') {
                                  <ui-icon name="device-phone-mobile" class="h-5 w-5 text-gray-400" />
                                } @else {
                                  <ui-icon name="key" class="h-5 w-5 text-gray-400" />
                                }
                                <div>
                                  <p class="text-sm font-medium text-gray-900">
                                    {{ cred.displayName || 'Unnamed credential' }}
                                  </p>
                                  <p class="text-xs text-gray-500">
                                    Created: {{ formatDate(cred.createdAt) }}
                                    @if (cred.lastUsedAt) {
                                      | Last used: {{ formatDate(cred.lastUsedAt) }}
                                    }
                                  </p>
                                </div>
                              </div>
                              <button
                                (click)="openDeleteModal(user.userId, cred)"
                                [disabled]="deleting() === cred.id"
                                class="p-2 text-red-600 hover:text-red-800 hover:bg-red-50 rounded-md disabled:opacity-50"
                                title="Delete credential"
                              >
                                @if (deleting() === cred.id) {
                                  <span class="animate-spin">...</span>
                                } @else {
                                  <ui-icon name="trash" class="h-4 w-4" />
                                }
                              </button>
                            </div>
                          }
                        </div>
                      }
                    </div>
                  </div>
                }
              </li>
            }
          </ul>
        </div>
      }

      <!-- Pagination -->
      @if (totalPages() > 1) {
        <div class="flex items-center justify-between">
          <p class="text-sm text-gray-700">
            Showing page {{ page }} of {{ totalPages() }}
          </p>
          <div class="flex space-x-2">
            <ui-button
              variant="secondary"
              [disabled]="page === 1"
              (clicked)="goToPage(page - 1)"
            >
              Previous
            </ui-button>
            <ui-button
              variant="secondary"
              [disabled]="page === totalPages()"
              (clicked)="goToPage(page + 1)"
            >
              Next
            </ui-button>
          </div>
        </div>
      }

      <!-- Delete Confirmation Modal -->
      <ui-modal
        [open]="showDeleteModal()"
        title="Delete Credential"
        (closed)="closeDeleteModal()"
      >
        <p class="text-sm text-gray-500">
          Are you sure you want to delete this credential? The user will no longer be able to authenticate with this passkey.
        </p>
        <div class="mt-4 flex justify-end gap-3">
          <ui-button variant="secondary" (clicked)="closeDeleteModal()">
            Cancel
          </ui-button>
          <ui-button variant="danger" [disabled]="!!deleting()" (clicked)="confirmDelete()">
            {{ deleting() ? 'Deleting...' : 'Delete' }}
          </ui-button>
        </div>
      </ui-modal>
    </div>
  `,
})
export class Fido2CredentialsComponent implements OnInit {
  private readonly fido2Service = inject(Fido2Service);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly users = signal<Fido2UserSummary[]>([]);
  readonly totalCount = signal(0);
  readonly expandedUser = signal<string | null>(null);
  readonly loadingCredentials = signal<string | null>(null);
  readonly userCredentials = signal<Record<string, Fido2Credential[]>>({});
  readonly deleting = signal<string | null>(null);
  readonly showDeleteModal = signal(false);
  readonly deleteTarget = signal<{ userId: string; credential: Fido2Credential } | null>(null);

  search = '';
  page = 1;
  readonly pageSize = 20;

  readonly totalPages = computed(() => Math.ceil(this.totalCount() / this.pageSize));

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.loading.set(true);
    this.fido2Service
      .getUsersWithCredentials({
        search: this.search || undefined,
        page: this.page,
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (data) => {
          this.users.set(data.users);
          this.totalCount.set(data.totalCount);
          this.error.set(null);
          this.loading.set(false);
        },
        error: (err) => {
          console.error('Error loading users:', err);
          this.error.set('Failed to load users with FIDO2 credentials');
          this.loading.set(false);
        },
      });
  }

  onSearchChange(): void {
    this.page = 1;
    this.loadUsers();
  }

  goToPage(newPage: number): void {
    this.page = newPage;
    this.loadUsers();
  }

  toggleUser(userId: string): void {
    if (this.expandedUser() === userId) {
      this.expandedUser.set(null);
      return;
    }

    this.expandedUser.set(userId);

    const creds = this.userCredentials();
    if (!creds[userId]) {
      this.loadingCredentials.set(userId);
      this.fido2Service.getUserCredentials(userId).subscribe({
        next: (credentials) => {
          this.userCredentials.update((prev) => ({ ...prev, [userId]: credentials }));
          this.loadingCredentials.set(null);
        },
        error: (err) => {
          console.error('Error loading credentials:', err);
          this.loadingCredentials.set(null);
        },
      });
    }
  }

  getUserCredentials(userId: string): Fido2Credential[] {
    return this.userCredentials()[userId] ?? [];
  }

  openDeleteModal(userId: string, credential: Fido2Credential): void {
    this.deleteTarget.set({ userId, credential });
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.deleteTarget.set(null);
  }

  confirmDelete(): void {
    const target = this.deleteTarget();
    if (!target) return;

    this.deleting.set(target.credential.id);
    this.fido2Service.deleteUserCredential(target.userId, target.credential.id).subscribe({
      next: () => {
        this.userCredentials.update((prev) => ({
          ...prev,
          [target.userId]: prev[target.userId]?.filter((c) => c.id !== target.credential.id) ?? [],
        }));
        this.deleting.set(null);
        this.closeDeleteModal();
        this.loadUsers();
      },
      error: (err) => {
        console.error('Error deleting credential:', err);
        this.deleting.set(null);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
