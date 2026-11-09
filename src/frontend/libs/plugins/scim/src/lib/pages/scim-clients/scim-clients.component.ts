import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { IconComponent, ModalComponent } from '@syndew/oluso-ui-core';
import { ScimService } from '../../services/scim.service';
import { ScimClient } from '../../models/scim.model';

@Component({
  selector: 'lib-scim-clients',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, IconComponent, ModalComponent],
  template: `
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex justify-between items-start">
        <div>
          <h1 class="text-2xl font-semibold text-gray-900">SCIM Provisioning</h1>
          <p class="text-gray-500 mt-1">
            Manage SCIM clients for automated user provisioning from external identity providers
          </p>
        </div>
        <button
          (click)="showCreateModal.set(true)"
          class="inline-flex items-center px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 transition-colors"
        >
          <ui-icon name="plus" class="h-5 w-5 mr-2" />
          Add SCIM Client
        </button>
      </div>

      <!-- Token Display Modal -->
      <ui-modal
        [open]="!!newToken()"
        title="SCIM API Token"
        size="lg"
        [closeOnBackdrop]="false"
        (closed)="newToken.set(null)"
      >
        <p class="text-sm text-gray-600 mb-4">
          Copy this token now. You won't be able to see it again.
        </p>
        <div class="bg-gray-100 p-4 rounded-md font-mono text-sm break-all mb-4">
          {{ newToken() }}
        </div>
        <div class="flex justify-end gap-2">
          <button
            (click)="copyToken()"
            class="inline-flex items-center px-4 py-2 bg-gray-100 rounded-md hover:bg-gray-200 transition-colors"
          >
            @if (copiedToken()) {
              <ui-icon name="check" class="h-5 w-5 mr-2 text-green-600" />
              Copied!
            } @else {
              <ui-icon name="clipboard-document" class="h-5 w-5 mr-2" />
              Copy
            }
          </button>
          <button
            (click)="newToken.set(null)"
            class="px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 transition-colors"
          >
            Done
          </button>
        </div>
      </ui-modal>

      <!-- Create Modal -->
      <ui-modal
        [open]="showCreateModal()"
        title="Create SCIM Client"
        size="md"
        (closed)="showCreateModal.set(false)"
      >
            <form (ngSubmit)="handleCreate()">
              <div class="mb-4">
                <label class="block text-sm font-medium text-gray-700 mb-1">Name</label>
                <input
                  type="text"
                  [(ngModel)]="createForm.name"
                  name="name"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  placeholder="e.g., HR System, Identity Directory"
                  required
                />
              </div>
              <div class="mb-4">
                <label class="block text-sm font-medium text-gray-700 mb-1">Description (optional)</label>
                <input
                  type="text"
                  [(ngModel)]="createForm.description"
                  name="description"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  placeholder="Optional description"
                />
              </div>
              <div class="flex justify-end gap-2">
                <button
                  type="button"
                  (click)="showCreateModal.set(false)"
                  class="px-4 py-2 text-gray-700 hover:bg-gray-100 rounded-md transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  class="px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 transition-colors"
                >
                  Create
                </button>
              </div>
            </form>
      </ui-modal>

      @if (error()) {
        <div class="bg-red-50 text-red-700 p-4 rounded-md">{{ error() }}</div>
      }

      <!-- SCIM Endpoint Info -->
      <div class="bg-blue-50 border border-blue-200 rounded-md p-4">
        <h3 class="font-medium text-blue-900 mb-2">SCIM Endpoint URL</h3>
        <code class="text-sm text-blue-700">{{ scimEndpointUrl }}</code>
        <p class="text-sm text-blue-600 mt-2">
          Use this URL as the SCIM 2.0 base endpoint when configuring an upstream provisioning client.
        </p>
      </div>

      <!-- Clients Table -->
      <div class="bg-white shadow rounded-lg overflow-hidden">
        @if (loading()) {
          <div class="p-8 text-center text-gray-500">Loading...</div>
        } @else {
          <table class="min-w-full divide-y divide-gray-200">
            <thead class="bg-gray-50">
              <tr>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Last Activity</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Stats</th>
                <th class="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-200">
              @if (clients().length === 0) {
                <tr>
                  <td colspan="5" class="px-6 py-8 text-center text-gray-500">
                    No SCIM clients configured. Create one to enable provisioning.
                  </td>
                </tr>
              } @else {
                @for (client of clients(); track client.id) {
                  <tr>
                    <td class="px-6 py-4">
                      <a
                        [routerLink]="['/scim/clients', client.id]"
                        class="text-primary-600 hover:underline font-medium"
                      >
                        {{ client.name }}
                      </a>
                      @if (client.description) {
                        <p class="text-sm text-gray-500">{{ client.description }}</p>
                      }
                    </td>
                    <td class="px-6 py-4">
                      <span
                        class="inline-flex px-2 py-1 text-xs rounded-full"
                        [class.bg-green-100]="client.isEnabled"
                        [class.text-green-800]="client.isEnabled"
                        [class.bg-gray-100]="!client.isEnabled"
                        [class.text-gray-800]="!client.isEnabled"
                      >
                        {{ client.isEnabled ? 'Enabled' : 'Disabled' }}
                      </span>
                    </td>
                    <td class="px-6 py-4 text-sm text-gray-500">
                      {{ client.lastActivityAt ? formatDate(client.lastActivityAt) : 'Never' }}
                    </td>
                    <td class="px-6 py-4 text-sm">
                      <span class="text-green-600">{{ client.successCount }} ok</span>
                      /
                      <span class="text-red-600">{{ client.errorCount }} errors</span>
                    </td>
                    <td class="px-6 py-4 text-right">
                      <button
                        (click)="handleRotateToken(client.id)"
                        class="text-gray-600 hover:text-gray-900 p-1"
                        title="Rotate Token"
                      >
                        <ui-icon name="refresh" class="h-5 w-5" />
                      </button>
                      <button
                        (click)="handleDelete(client.id)"
                        class="text-red-600 hover:text-red-900 p-1 ml-2"
                        title="Delete"
                      >
                        <ui-icon name="trash" class="h-5 w-5" />
                      </button>
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
})
export class ScimClientsComponent implements OnInit {
  private readonly scimService = inject(ScimService);

  clients = signal<ScimClient[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  showCreateModal = signal(false);
  newToken = signal<string | null>(null);
  copiedToken = signal(false);

  createForm = {
    name: '',
    description: '',
  };

  scimEndpointUrl = '';

  ngOnInit(): void {
    this.scimService.getScimEndpointUrl().subscribe({
      next: (res) => this.scimEndpointUrl = res.url,
      error: (err) => console.error('Failed to load SCIM endpoint URL:', err),
    });
    this.loadClients();
  }

  loadClients(): void {
    this.loading.set(true);
    this.scimService.getClients().subscribe({
      next: (data) => {
        this.clients.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load SCIM clients');
        console.error(err);
        this.loading.set(false);
      },
    });
  }

  handleCreate(): void {
    this.scimService
      .createClient({
        name: this.createForm.name,
        description: this.createForm.description || undefined,
      })
      .subscribe({
        next: (response) => {
          this.newToken.set(response.token);
          this.clients.update((clients) => [...clients, response.client]);
          this.showCreateModal.set(false);
          this.createForm = { name: '', description: '' };
        },
        error: (err) => {
          console.error('Failed to create client:', err);
        },
      });
  }

  handleDelete(id: string): void {
    if (!confirm('Are you sure you want to delete this SCIM client?')) return;

    this.scimService.deleteClient(id).subscribe({
      next: () => {
        this.clients.update((clients) => clients.filter((c) => c.id !== id));
      },
      error: (err) => {
        console.error('Failed to delete client:', err);
      },
    });
  }

  handleRotateToken(id: string): void {
    if (!confirm('Rotate the API token? The old token will stop working immediately.')) return;

    this.scimService.rotateToken(id).subscribe({
      next: (response) => {
        this.newToken.set(response.token);
      },
      error: (err) => {
        console.error('Failed to rotate token:', err);
      },
    });
  }

  copyToken(): void {
    const token = this.newToken();
    if (token) {
      navigator.clipboard.writeText(token);
      this.copiedToken.set(true);
      setTimeout(() => this.copiedToken.set(false), 2000);
    }
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }
}
