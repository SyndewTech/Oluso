import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '@syndew/oluso-ui-core';
import { ClientService } from '@syndew/oluso-admin-ui';
import { ScimService } from '../../services/scim.service';
import { ScimClient, ScimProvisioningLog, ScimAttributeMapping } from '../../models/scim.model';

@Component({
  selector: 'lib-scim-client-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, IconComponent],
  template: `
    <div class="space-y-6">
      <!-- Back button -->
      <button
        (click)="goBack()"
        class="inline-flex items-center text-gray-600 hover:text-gray-900 transition-colors"
      >
        <ui-icon name="arrow-left" class="h-4 w-4 mr-2" />
        Back to SCIM Clients
      </button>

      @if (loading()) {
        <div class="p-6 text-center text-gray-500">Loading...</div>
      } @else if (!client()) {
        <div class="p-6 text-center text-gray-500">Client not found</div>
      } @else {
        <h1 class="text-2xl font-semibold text-gray-900">{{ client()!.name }}</h1>

        <!-- Two column layout -->
        <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
          <!-- Left column - Settings -->
          <div class="lg:col-span-2 space-y-6">
            <!-- Settings Form -->
            <div class="bg-white shadow rounded-lg p-6">
              <h2 class="text-lg font-medium mb-4">Settings</h2>

              <div class="grid grid-cols-2 gap-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Name</label>
                  <input
                    type="text"
                    [(ngModel)]="formData.name"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  />
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Description</label>
                  <input
                    type="text"
                    [(ngModel)]="formData.description"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  />
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Allowed IP Ranges</label>
                  <input
                    type="text"
                    [(ngModel)]="formData.allowedIpRanges"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    placeholder="Leave empty to allow all"
                  />
                  <p class="text-xs text-gray-500 mt-1">
                    Comma-separated IPs or ranges (e.g., 192.168.1.*, 10.0.0.1)
                  </p>
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Rate Limit (requests/min)</label>
                  <input
                    type="number"
                    [(ngModel)]="formData.rateLimitPerMinute"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    min="0"
                  />
                </div>

                <div class="col-span-2">
                  <label class="block text-sm font-medium text-gray-700 mb-1">OAuth Client (client credentials)</label>
                  <select
                    [(ngModel)]="formData.oauthClientId"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  >
                    <option value="">None — static token only</option>
                    @for (oc of oauthClients(); track oc.clientId) {
                      <option [value]="oc.clientId">{{ oc.clientName }} ({{ oc.clientId }})</option>
                    }
                  </select>
                  <p class="text-xs text-gray-500 mt-1">
                    Link a confidential OAuth client that allows the client_credentials grant to let
                    identity providers authenticate with a platform-issued access token instead of the
                    static bearer token. Only client-credentials clients are listed.
                  </p>
                </div>
              </div>

              <!-- Permissions -->
              <h3 class="text-sm font-medium text-gray-700 mt-6 mb-3">Permissions</h3>
              <div class="grid grid-cols-2 gap-3">
                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.isEnabled"
                    class="rounded border-gray-300 text-primary-600 mr-2"
                  />
                  <span class="text-sm">Enabled</span>
                </label>
                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.canCreateUsers"
                    class="rounded border-gray-300 text-primary-600 mr-2"
                  />
                  <span class="text-sm">Can Create Users</span>
                </label>
                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.canUpdateUsers"
                    class="rounded border-gray-300 text-primary-600 mr-2"
                  />
                  <span class="text-sm">Can Update Users</span>
                </label>
                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.canDeleteUsers"
                    class="rounded border-gray-300 text-primary-600 mr-2"
                  />
                  <span class="text-sm">Can Delete Users</span>
                </label>
                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.canManageGroups"
                    class="rounded border-gray-300 text-primary-600 mr-2"
                  />
                  <span class="text-sm">Can Manage Groups</span>
                </label>
              </div>

              @if (saveError()) {
                <div class="mt-4 bg-red-50 text-red-700 p-3 rounded-md text-sm">{{ saveError() }}</div>
              }

              <div class="mt-6 flex justify-end">
                <button
                  (click)="handleSave()"
                  [disabled]="saving()"
                  class="px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 disabled:opacity-50 transition-colors"
                >
                  {{ saving() ? 'Saving...' : 'Save Changes' }}
                </button>
              </div>
            </div>

            <!-- Statistics -->
            <div class="bg-white shadow rounded-lg p-6">
              <h2 class="text-lg font-medium mb-4">Statistics</h2>
              <div class="grid grid-cols-4 gap-4">
                <div class="text-center">
                  <div class="text-2xl font-bold text-gray-900">{{ client()!.successCount }}</div>
                  <div class="text-sm text-gray-500">Successful Operations</div>
                </div>
                <div class="text-center">
                  <div class="text-2xl font-bold text-red-600">{{ client()!.errorCount }}</div>
                  <div class="text-sm text-gray-500">Failed Operations</div>
                </div>
                <div class="text-center">
                  <div class="text-sm font-medium text-gray-900">
                    {{ client()!.tokenCreatedAt ? formatDateShort(client()!.tokenCreatedAt) : '-' }}
                  </div>
                  <div class="text-sm text-gray-500">Token Created</div>
                </div>
                <div class="text-center">
                  <div class="text-sm font-medium text-gray-900">
                    {{ client()!.lastActivityAt ? formatDate(client()!.lastActivityAt!) : 'Never' }}
                  </div>
                  <div class="text-sm text-gray-500">Last Activity</div>
                </div>
              </div>
            </div>

            <!-- Recent Logs -->
            <div class="bg-white shadow rounded-lg p-6">
              <div class="flex justify-between items-center mb-4">
                <h2 class="text-lg font-medium">Recent Activity</h2>
                <button
                  (click)="loadLogs()"
                  class="text-gray-600 hover:text-gray-900 transition-colors"
                >
                  <ui-icon name="refresh" class="h-5 w-5" />
                </button>
              </div>

              @if (logs().length === 0) {
                <p class="text-gray-500 text-center py-4">No activity recorded yet</p>
              } @else {
                <div class="space-y-2">
                  @for (log of logs(); track log.id) {
                    <div class="flex items-center justify-between py-2 border-b last:border-0">
                      <div class="flex items-center">
                        @if (log.success) {
                          <ui-icon name="check-circle" class="h-5 w-5 text-green-500 mr-3" />
                        } @else {
                          <ui-icon name="x-circle" class="h-5 w-5 text-red-500 mr-3" />
                        }
                        <div>
                          <span class="font-mono text-sm">{{ log.method }} {{ log.path }}</span>
                          @if (log.errorMessage) {
                            <p class="text-sm text-red-600">{{ log.errorMessage }}</p>
                          }
                        </div>
                      </div>
                      <div class="text-right text-sm text-gray-500">
                        <div>{{ log.statusCode }} - {{ log.durationMs }}ms</div>
                        <div>{{ formatDate(log.timestamp) }}</div>
                      </div>
                    </div>
                  }
                </div>
              }
            </div>
          </div>

          <!-- Right column - Attribute Mappings -->
          <div class="lg:col-span-1">
            <div class="bg-white shadow rounded-lg p-6 sticky top-6">
              <div class="flex justify-between items-center mb-4">
                <h2 class="text-lg font-medium">Attribute Mappings</h2>
                <a
                  [routerLink]="['/scim/clients', clientId, 'mappings']"
                  class="text-sm text-primary-600 hover:text-primary-700"
                >
                  Edit
                </a>
              </div>

              @if (loadingMappings()) {
                <p class="text-gray-500 text-center py-4">Loading...</p>
              } @else if (mappings().length === 0) {
                <div class="text-center py-6">
                  <ui-icon name="clipboard" class="mx-auto h-12 w-12 text-gray-300" />
                  <p class="mt-2 text-sm text-gray-500">No mappings configured</p>
                  <a
                    [routerLink]="['/scim/clients', clientId, 'mappings']"
                    class="mt-2 inline-flex items-center text-sm text-primary-600 hover:text-primary-700"
                  >
                    Configure mappings
                    <ui-icon name="chevron-right" class="ml-1 h-4 w-4" />
                  </a>
                </div>
              } @else {
                <div class="space-y-3 max-h-96 overflow-y-auto">
                  @for (mapping of mappings(); track mapping.id) {
                    <div class="border border-gray-200 rounded-md p-3 text-sm" [class.opacity-50]="!mapping.isEnabled">
                      <div class="flex items-center justify-between mb-1">
                        <span class="font-medium text-gray-900 truncate" [title]="mapping.scimAttribute">
                          {{ mapping.scimAttribute }}
                        </span>
                        @if (mapping.isRequired) {
                          <span class="text-xs bg-red-100 text-red-700 px-1.5 py-0.5 rounded">Required</span>
                        }
                      </div>
                      <div class="flex items-center text-gray-500">
                        <ui-icon name="arrow-right" class="h-3 w-3 mr-1" />
                        <span class="truncate" [title]="mapping.internalProperty">{{ mapping.internalProperty }}</span>
                      </div>
                      @if (mapping.transformation) {
                        <div class="mt-1 text-xs text-gray-400">
                          Transform: {{ mapping.transformation }}
                        </div>
                      }
                    </div>
                  }
                </div>
                <div class="mt-4 pt-3 border-t text-xs text-gray-500 text-center">
                  {{ mappings().length }} mapping{{ mappings().length !== 1 ? 's' : '' }} configured
                </div>
              }
            </div>
          </div>
        </div>
      }
    </div>
  `,
})
export class ScimClientDetailComponent implements OnInit {
  private readonly scimService = inject(ScimService);
  private readonly clientService = inject(ClientService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  client = signal<ScimClient | null>(null);
  logs = signal<ScimProvisioningLog[]>([]);
  mappings = signal<ScimAttributeMapping[]>([]);
  oauthClients = signal<{ clientId: string; clientName: string }[]>([]);
  loading = signal(true);
  loadingMappings = signal(true);
  saving = signal(false);
  saveError = signal<string | null>(null);
  clientId = '';

  formData = {
    name: '',
    description: '',
    isEnabled: true,
    allowedIpRanges: '',
    rateLimitPerMinute: 60,
    canCreateUsers: true,
    canUpdateUsers: true,
    canDeleteUsers: true,
    canManageGroups: true,
    oauthClientId: '',
  };

  ngOnInit(): void {
    this.clientId = this.route.snapshot.paramMap.get('id') || '';
    if (this.clientId) {
      this.loadClient();
      this.loadLogs();
      this.loadMappings();
      this.loadOAuthClients();
    }
  }

  loadOAuthClients(): void {
    this.clientService.getClients().subscribe({
      next: (clients) => {
        this.oauthClients.set(
          clients
            .filter((c) => c.allowedGrantTypes?.includes('client_credentials'))
            .map((c) => ({ clientId: c.clientId, clientName: c.clientName || c.clientId }))
        );
      },
      error: (err) => console.error('Failed to load OAuth clients:', err),
    });
  }

  loadClient(): void {
    this.scimService.getClient(this.clientId).subscribe({
      next: (data) => {
        this.client.set(data);
        this.formData = {
          name: data.name,
          description: data.description || '',
          isEnabled: data.isEnabled,
          allowedIpRanges: data.allowedIpRanges || '',
          rateLimitPerMinute: data.rateLimitPerMinute,
          canCreateUsers: data.canCreateUsers,
          canUpdateUsers: data.canUpdateUsers,
          canDeleteUsers: data.canDeleteUsers,
          canManageGroups: data.canManageGroups,
          oauthClientId: data.oauthClientId || '',
        };
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load client:', err);
        this.loading.set(false);
      },
    });
  }

  loadLogs(): void {
    this.scimService.getLogs(this.clientId, 1, 20).subscribe({
      next: (response) => {
        this.logs.set(response.items);
      },
      error: (err) => {
        console.error('Failed to load logs:', err);
      },
    });
  }

  loadMappings(): void {
    this.loadingMappings.set(true);
    this.scimService.getMappings(this.clientId).subscribe({
      next: (data) => {
        this.mappings.set(data);
        this.loadingMappings.set(false);
      },
      error: (err) => {
        console.error('Failed to load mappings:', err);
        this.loadingMappings.set(false);
      },
    });
  }

  handleSave(): void {
    this.saving.set(true);
    this.saveError.set(null);
    const payload = {
      ...this.client()!,
      ...this.formData,
      oauthClientId: this.formData.oauthClientId || undefined,
    } as ScimClient;
    this.scimService.updateClient(this.clientId, payload).subscribe({
      next: (updated) => {
        this.client.set(updated);
        this.saving.set(false);
      },
      error: (err) => {
        this.saveError.set(
          typeof err?.error === 'string' ? err.error : 'Failed to save changes'
        );
        this.saving.set(false);
      },
    });
  }

  goBack(): void {
    this.router.navigate(['/scim/clients']);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }

  formatDateShort(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
