import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent, ModalComponent } from '@oluso/ui-core';
import { LdapService } from '../../services/ldap.service';
import {
  ServiceAccount,
  ServiceAccountPermission,
  CreateServiceAccountRequest,
} from '../../models/ldap.model';

@Component({
  selector: 'ldap-service-accounts',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent, ModalComponent],
  template: `
    <div class="p-6 space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-bold text-gray-900">Service Accounts</h1>
          <p class="mt-1 text-sm text-gray-500">
            Create service accounts for applications to bind to the LDAP server
          </p>
        </div>
        <button
          (click)="showCreateModal.set(true)"
          class="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500">
          <ui-icon name="plus" class="h-5 w-5 mr-2" />
          Create Service Account
        </button>
      </div>

      <!-- Loading State -->
      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-20 bg-gray-200 rounded-lg animate-pulse"></div>
          }
        </div>
      }

      <!-- Error State -->
      @if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4 flex items-center space-x-2">
          <ui-icon name="warning" class="h-5 w-5 text-red-500" />
          <p class="text-red-700">{{ error() }}</p>
        </div>
      }

      <!-- Empty State -->
      @if (!loading() && accounts().length === 0) {
        <div class="bg-white rounded-lg shadow p-12 text-center">
          <ui-icon name="key" class="h-12 w-12 text-gray-400 mx-auto mb-4" />
          <h3 class="text-lg font-medium text-gray-900 mb-2">No Service Accounts</h3>
          <p class="text-gray-500 mb-6">
            Create a service account to allow applications to authenticate with the LDAP server.
          </p>
          <button
            (click)="showCreateModal.set(true)"
            class="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md text-blue-700 bg-blue-100 hover:bg-blue-200">
            <ui-icon name="plus" class="h-5 w-5 mr-2" />
            Create First Service Account
          </button>
        </div>
      }

      <!-- Table -->
      @if (!loading() && accounts().length > 0) {
        <div class="bg-white rounded-lg shadow overflow-hidden">
          <table class="min-w-full divide-y divide-gray-200">
            <thead class="bg-gray-50">
              <tr>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Name</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Bind DN</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Permission</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Last Used</th>
                <th class="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody class="bg-white divide-y divide-gray-200">
              @for (account of accounts(); track account.id) {
                <tr class="hover:bg-gray-50">
                  <td class="px-6 py-4 whitespace-nowrap">
                    <div>
                      <div class="text-sm font-medium text-gray-900">{{ account.name }}</div>
                      @if (account.description) {
                        <div class="text-sm text-gray-500">{{ account.description }}</div>
                      }
                    </div>
                  </td>
                  <td class="px-6 py-4">
                    <div class="flex items-center">
                      <code class="text-xs bg-gray-100 px-2 py-1 rounded font-mono text-gray-700 max-w-xs truncate">
                        {{ account.bindDn }}
                      </code>
                      <button
                        (click)="copyToClipboard(account.bindDn)"
                        class="ml-2 text-gray-400 hover:text-gray-600"
                        title="Copy Bind DN">
                        <ui-icon name="clipboard-document" class="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                  <td class="px-6 py-4 whitespace-nowrap">
                    <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
                          [class]="getPermissionClass(account.permission)">
                      {{ account.permission }}
                    </span>
                  </td>
                  <td class="px-6 py-4 whitespace-nowrap">
                    @if (account.isExpired) {
                      <span class="inline-flex items-center text-red-600">
                        <ui-icon name="x-circle" class="h-4 w-4 mr-1" />
                        Expired
                      </span>
                    } @else if (account.isEnabled) {
                      <span class="inline-flex items-center text-green-600">
                        <ui-icon name="check-circle" class="h-4 w-4 mr-1" />
                        Active
                      </span>
                    } @else {
                      <span class="inline-flex items-center text-gray-500">
                        <ui-icon name="x-circle" class="h-4 w-4 mr-1" />
                        Disabled
                      </span>
                    }
                  </td>
                  <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {{ account.lastUsedAt ? formatDate(account.lastUsedAt) : 'Never' }}
                  </td>
                  <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <button
                      (click)="openEditModal(account)"
                      class="text-blue-600 hover:text-blue-900 mr-3"
                      title="Edit">
                      <ui-icon name="pencil" class="h-5 w-5" />
                    </button>
                    <button
                      (click)="openPasswordModal(account)"
                      class="text-yellow-600 hover:text-yellow-900 mr-3"
                      title="Reset Password">
                      <ui-icon name="key" class="h-5 w-5" />
                    </button>
                    <button
                      (click)="openDeleteModal(account)"
                      class="text-red-600 hover:text-red-900"
                      title="Delete">
                      <ui-icon name="trash" class="h-5 w-5" />
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <!-- Create Modal -->
      <ui-modal
        [open]="showCreateModal()"
        title="Create Service Account"
        size="2xl"
        [scrollable]="true"
        (closed)="closeCreateModal()"
      >
            <form (ngSubmit)="handleCreate()" class="space-y-6">
              @if (modalError()) {
                <div class="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-700">
                  {{ modalError() }}
                </div>
              }

              <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Name *</label>
                  <input
                    type="text"
                    [(ngModel)]="createForm.name"
                    name="name"
                    required
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                    placeholder="my-app-service" />
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Permission</label>
                  <select
                    [(ngModel)]="createForm.permission"
                    name="permission"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500">
                    <option value="ReadOnly">Read Only</option>
                    <option value="SearchOnly">Search Only</option>
                    <option value="FullRead">Full Read</option>
                  </select>
                </div>
              </div>

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Description</label>
                <input
                  type="text"
                  [(ngModel)]="createForm.description"
                  name="description"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                  placeholder="Service account for HR application" />
              </div>

              <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Password *</label>
                  <div class="relative">
                    <input
                      [type]="showPassword() ? 'text' : 'password'"
                      [(ngModel)]="createForm.password"
                      name="password"
                      required
                      minlength="8"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 pr-10 focus:ring-blue-500 focus:border-blue-500" />
                    <button
                      type="button"
                      (click)="showPassword.set(!showPassword())"
                      class="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600">
                      @if (showPassword()) {
                        <ui-icon name="eye-slash" class="h-5 w-5" />
                      } @else {
                        <ui-icon name="eye" class="h-5 w-5" />
                      }
                    </button>
                  </div>
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Confirm Password *</label>
                  <input
                    [type]="showPassword() ? 'text' : 'password'"
                    [(ngModel)]="createForm.confirmPassword"
                    name="confirmPassword"
                    required
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500" />
                </div>
              </div>

              <div class="border-t border-gray-200 pt-6">
                <h3 class="text-sm font-medium text-gray-900 mb-4">Restrictions (Optional)</h3>

                <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Allowed OUs (one per line)</label>
                    <textarea
                      [(ngModel)]="createForm.allowedOus"
                      name="allowedOus"
                      rows="3"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500 text-sm font-mono"
                      placeholder="ou=users&#10;ou=groups"></textarea>
                  </div>

                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Allowed IP Ranges (one per line)</label>
                    <textarea
                      [(ngModel)]="createForm.allowedIpRanges"
                      name="allowedIpRanges"
                      rows="3"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500 text-sm font-mono"
                      placeholder="10.0.0.0/8&#10;192.168.1.100"></textarea>
                  </div>
                </div>

                <div class="grid grid-cols-1 md:grid-cols-3 gap-6 mt-4">
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Max Search Results</label>
                    <input
                      type="number"
                      [(ngModel)]="createForm.maxSearchResults"
                      name="maxSearchResults"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                      placeholder="1000"
                      min="1" />
                  </div>

                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Rate Limit (binds/min)</label>
                    <input
                      type="number"
                      [(ngModel)]="createForm.rateLimitPerMinute"
                      name="rateLimitPerMinute"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                      placeholder="60"
                      min="1" />
                  </div>

                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Expires At</label>
                    <input
                      type="date"
                      [(ngModel)]="createForm.expiresAt"
                      name="expiresAt"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500" />
                  </div>
                </div>
              </div>

              <div class="flex justify-end space-x-3 pt-4 border-t border-gray-200">
                <button
                  type="button"
                  (click)="closeCreateModal()"
                  class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
                  Cancel
                </button>
                <button
                  type="submit"
                  [disabled]="actionLoading()"
                  class="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md hover:bg-blue-700 disabled:opacity-50">
                  {{ actionLoading() ? 'Creating...' : 'Create Account' }}
                </button>
              </div>
            </form>
      </ui-modal>

      <!-- Edit Modal -->
      <ui-modal
        [open]="showEditModal() && !!selectedAccount()"
        title="Edit Service Account"
        size="2xl"
        [scrollable]="true"
        (closed)="closeEditModal()"
      >
            <form (ngSubmit)="handleUpdate()" class="space-y-6">
              @if (modalError()) {
                <div class="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-700">
                  {{ modalError() }}
                </div>
              }

              <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Name *</label>
                  <input
                    type="text"
                    [(ngModel)]="editForm.name"
                    name="name"
                    required
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500" />
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Permission</label>
                  <select
                    [(ngModel)]="editForm.permission"
                    name="permission"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500">
                    <option value="ReadOnly">Read Only</option>
                    <option value="SearchOnly">Search Only</option>
                    <option value="FullRead">Full Read</option>
                  </select>
                </div>
              </div>

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Description</label>
                <input
                  type="text"
                  [(ngModel)]="editForm.description"
                  name="description"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500" />
              </div>

              <div class="flex items-center">
                <input
                  type="checkbox"
                  id="isEnabled"
                  [(ngModel)]="editForm.isEnabled"
                  name="isEnabled"
                  class="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded" />
                <label for="isEnabled" class="ml-2 block text-sm text-gray-900">Enabled</label>
              </div>

              <div class="border-t border-gray-200 pt-6">
                <h3 class="text-sm font-medium text-gray-900 mb-4">Restrictions (Optional)</h3>

                <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Allowed OUs (one per line)</label>
                    <textarea
                      [(ngModel)]="editForm.allowedOus"
                      name="allowedOus"
                      rows="3"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500 text-sm font-mono"></textarea>
                  </div>

                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Allowed IP Ranges (one per line)</label>
                    <textarea
                      [(ngModel)]="editForm.allowedIpRanges"
                      name="allowedIpRanges"
                      rows="3"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500 text-sm font-mono"></textarea>
                  </div>
                </div>

                <div class="grid grid-cols-1 md:grid-cols-3 gap-6 mt-4">
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Max Search Results</label>
                    <input
                      type="number"
                      [(ngModel)]="editForm.maxSearchResults"
                      name="maxSearchResults"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                      min="1" />
                  </div>

                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Rate Limit (binds/min)</label>
                    <input
                      type="number"
                      [(ngModel)]="editForm.rateLimitPerMinute"
                      name="rateLimitPerMinute"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500"
                      min="1" />
                  </div>

                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Expires At</label>
                    <input
                      type="date"
                      [(ngModel)]="editForm.expiresAt"
                      name="expiresAt"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500" />
                  </div>
                </div>
              </div>

              <div class="flex justify-end space-x-3 pt-4 border-t border-gray-200">
                <button
                  type="button"
                  (click)="closeEditModal()"
                  class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
                  Cancel
                </button>
                <button
                  type="submit"
                  [disabled]="actionLoading()"
                  class="px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md hover:bg-blue-700 disabled:opacity-50">
                  {{ actionLoading() ? 'Saving...' : 'Save Changes' }}
                </button>
              </div>
            </form>
      </ui-modal>

      <!-- Password Reset Modal -->
      <ui-modal
        [open]="showPasswordModal() && !!selectedAccount()"
        title="Reset Password"
        size="md"
        (closed)="closePasswordModal()"
      >
            <p class="text-sm text-gray-500 mb-4">
              Reset password for <span class="font-medium">{{ selectedAccount()?.name }}</span>
            </p>
            <form (ngSubmit)="handleResetPassword()" class="space-y-4">
              @if (modalError()) {
                <div class="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-700">
                  {{ modalError() }}
                </div>
              }

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">New Password</label>
                <div class="relative">
                  <input
                    [type]="showPassword() ? 'text' : 'password'"
                    [(ngModel)]="passwordForm.password"
                    name="password"
                    required
                    minlength="8"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 pr-10 focus:ring-blue-500 focus:border-blue-500" />
                  <button
                    type="button"
                    (click)="showPassword.set(!showPassword())"
                    class="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600">
                    @if (showPassword()) {
                      <ui-icon name="eye-slash" class="h-5 w-5" />
                    } @else {
                      <ui-icon name="eye" class="h-5 w-5" />
                    }
                  </button>
                </div>
              </div>

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Confirm Password</label>
                <input
                  [type]="showPassword() ? 'text' : 'password'"
                  [(ngModel)]="passwordForm.confirmPassword"
                  name="confirmPassword"
                  required
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-blue-500 focus:border-blue-500" />
              </div>

              <div class="flex justify-end space-x-3 pt-4">
                <button
                  type="button"
                  (click)="closePasswordModal()"
                  class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
                  Cancel
                </button>
                <button
                  type="submit"
                  [disabled]="actionLoading()"
                  class="px-4 py-2 text-sm font-medium text-white bg-yellow-600 border border-transparent rounded-md hover:bg-yellow-700 disabled:opacity-50">
                  {{ actionLoading() ? 'Resetting...' : 'Reset Password' }}
                </button>
              </div>
            </form>
      </ui-modal>

      <!-- Delete Confirmation Modal -->
      <ui-modal
        [open]="showDeleteModal() && !!selectedAccount()"
        title="Delete Service Account"
        size="md"
        (closed)="closeDeleteModal()"
      >
            <div>
              <div class="flex items-start">
                <div class="flex-shrink-0">
                  <ui-icon name="warning" class="h-6 w-6 text-red-600" />
                </div>
                <div class="ml-3">
                  <p class="text-sm text-gray-700">
                    Are you sure you want to delete the service account
                    <span class="font-medium">{{ selectedAccount()?.name }}</span>? This action cannot be undone.
                  </p>
                  <p class="text-sm text-gray-500 mt-2">
                    Applications using this account will no longer be able to authenticate.
                  </p>
                </div>
              </div>
            </div>

            <div modal-footer class="px-6 py-4 bg-gray-50 rounded-b-lg flex justify-end space-x-3">
              <button
                (click)="closeDeleteModal()"
                class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
                Cancel
              </button>
              <button
                (click)="handleDelete()"
                [disabled]="actionLoading()"
                class="px-4 py-2 text-sm font-medium text-white bg-red-600 border border-transparent rounded-md hover:bg-red-700 disabled:opacity-50">
                {{ actionLoading() ? 'Deleting...' : 'Delete Account' }}
              </button>
            </div>
      </ui-modal>
    </div>
  `,
})
export class LdapServiceAccountsComponent implements OnInit {
  private readonly ldapService = inject(LdapService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly accounts = signal<ServiceAccount[]>([]);
  readonly actionLoading = signal(false);
  readonly modalError = signal<string | null>(null);
  readonly showPassword = signal(false);

  readonly showCreateModal = signal(false);
  readonly showEditModal = signal(false);
  readonly showPasswordModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly selectedAccount = signal<ServiceAccount | null>(null);

  createForm = {
    name: '',
    description: '',
    password: '',
    confirmPassword: '',
    permission: 'ReadOnly' as ServiceAccountPermission,
    allowedOus: '',
    allowedIpRanges: '',
    maxSearchResults: null as number | null,
    rateLimitPerMinute: null as number | null,
    expiresAt: '',
  };

  editForm = {
    name: '',
    description: '',
    permission: 'ReadOnly' as ServiceAccountPermission,
    isEnabled: true,
    allowedOus: '',
    allowedIpRanges: '',
    maxSearchResults: null as number | null,
    rateLimitPerMinute: null as number | null,
    expiresAt: '',
  };

  passwordForm = {
    password: '',
    confirmPassword: '',
  };

  ngOnInit(): void {
    this.loadAccounts();
  }

  private loadAccounts(): void {
    this.loading.set(true);
    this.ldapService.getServiceAccounts().subscribe({
      next: (data) => {
        this.accounts.set(data);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading service accounts:', err);
        this.error.set('Failed to load service accounts');
        this.loading.set(false);
      },
    });
  }

  getPermissionClass(permission: ServiceAccountPermission): string {
    switch (permission) {
      case 'FullRead':
        return 'bg-purple-100 text-purple-800';
      case 'SearchOnly':
        return 'bg-blue-100 text-blue-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString();
  }

  copyToClipboard(text: string): void {
    navigator.clipboard.writeText(text);
  }

  // Create Modal
  handleCreate(): void {
    this.modalError.set(null);

    if (this.createForm.password !== this.createForm.confirmPassword) {
      this.modalError.set('Passwords do not match');
      return;
    }

    if (this.createForm.password.length < 8) {
      this.modalError.set('Password must be at least 8 characters');
      return;
    }

    const request: CreateServiceAccountRequest = {
      name: this.createForm.name,
      description: this.createForm.description || undefined,
      password: this.createForm.password,
      permission: this.createForm.permission,
      allowedOus: this.createForm.allowedOus ? this.createForm.allowedOus.split('\n').filter(Boolean) : undefined,
      allowedIpRanges: this.createForm.allowedIpRanges ? this.createForm.allowedIpRanges.split('\n').filter(Boolean) : undefined,
      maxSearchResults: this.createForm.maxSearchResults || undefined,
      rateLimitPerMinute: this.createForm.rateLimitPerMinute || undefined,
      expiresAt: this.createForm.expiresAt || undefined,
    };

    this.actionLoading.set(true);
    this.ldapService.createServiceAccount(request).subscribe({
      next: () => {
        this.loadAccounts();
        this.closeCreateModal();
      },
      error: (err) => {
        console.error('Error creating service account:', err);
        this.modalError.set('Failed to create service account');
        this.actionLoading.set(false);
      },
    });
  }

  closeCreateModal(): void {
    this.showCreateModal.set(false);
    this.actionLoading.set(false);
    this.modalError.set(null);
    this.createForm = {
      name: '',
      description: '',
      password: '',
      confirmPassword: '',
      permission: 'ReadOnly',
      allowedOus: '',
      allowedIpRanges: '',
      maxSearchResults: null,
      rateLimitPerMinute: null,
      expiresAt: '',
    };
  }

  // Edit Modal
  openEditModal(account: ServiceAccount): void {
    this.selectedAccount.set(account);
    this.editForm = {
      name: account.name,
      description: account.description ?? '',
      permission: account.permission,
      isEnabled: account.isEnabled,
      allowedOus: account.allowedOus?.join('\n') ?? '',
      allowedIpRanges: account.allowedIpRanges?.join('\n') ?? '',
      maxSearchResults: account.maxSearchResults ?? null,
      rateLimitPerMinute: account.rateLimitPerMinute ?? null,
      expiresAt: account.expiresAt ? account.expiresAt.split('T')[0] : '',
    };
    this.showEditModal.set(true);
  }

  handleUpdate(): void {
    const account = this.selectedAccount();
    if (!account) return;

    this.modalError.set(null);

    const request: ServiceAccount = {
      ...account,
      name: this.editForm.name,
      description: this.editForm.description || undefined,
      isEnabled: this.editForm.isEnabled,
      permission: this.editForm.permission,
      allowedOus: this.editForm.allowedOus ? this.editForm.allowedOus.split('\n').filter(Boolean) : [],
      allowedIpRanges: this.editForm.allowedIpRanges ? this.editForm.allowedIpRanges.split('\n').filter(Boolean) : [],
      maxSearchResults: this.editForm.maxSearchResults || undefined,
      rateLimitPerMinute: this.editForm.rateLimitPerMinute || undefined,
      expiresAt: this.editForm.expiresAt || undefined,
    };

    this.actionLoading.set(true);
    this.ldapService.updateServiceAccount(account.id, request).subscribe({
      next: () => {
        this.loadAccounts();
        this.closeEditModal();
      },
      error: (err) => {
        console.error('Error updating service account:', err);
        this.modalError.set('Failed to update service account');
        this.actionLoading.set(false);
      },
    });
  }

  closeEditModal(): void {
    this.showEditModal.set(false);
    this.selectedAccount.set(null);
    this.actionLoading.set(false);
    this.modalError.set(null);
  }

  // Password Modal
  openPasswordModal(account: ServiceAccount): void {
    this.selectedAccount.set(account);
    this.passwordForm = { password: '', confirmPassword: '' };
    this.showPasswordModal.set(true);
  }

  handleResetPassword(): void {
    const account = this.selectedAccount();
    if (!account) return;

    this.modalError.set(null);

    if (this.passwordForm.password !== this.passwordForm.confirmPassword) {
      this.modalError.set('Passwords do not match');
      return;
    }

    if (this.passwordForm.password.length < 8) {
      this.modalError.set('Password must be at least 8 characters');
      return;
    }

    this.actionLoading.set(true);
    this.ldapService.resetServiceAccountPassword(account.id, { newPassword: this.passwordForm.password }).subscribe({
      next: () => {
        this.closePasswordModal();
      },
      error: (err) => {
        console.error('Error resetting password:', err);
        this.modalError.set('Failed to reset password');
        this.actionLoading.set(false);
      },
    });
  }

  closePasswordModal(): void {
    this.showPasswordModal.set(false);
    this.selectedAccount.set(null);
    this.actionLoading.set(false);
    this.modalError.set(null);
    this.showPassword.set(false);
  }

  // Delete Modal
  openDeleteModal(account: ServiceAccount): void {
    this.selectedAccount.set(account);
    this.showDeleteModal.set(true);
  }

  handleDelete(): void {
    const account = this.selectedAccount();
    if (!account) return;

    this.actionLoading.set(true);
    this.ldapService.deleteServiceAccount(account.id).subscribe({
      next: () => {
        this.loadAccounts();
        this.closeDeleteModal();
      },
      error: (err) => {
        console.error('Error deleting service account:', err);
        this.actionLoading.set(false);
      },
    });
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.selectedAccount.set(null);
    this.actionLoading.set(false);
  }
}
