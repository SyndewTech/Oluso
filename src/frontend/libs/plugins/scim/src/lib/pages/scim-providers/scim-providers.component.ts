import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { IconComponent, ModalComponent } from '@syndew/oluso-ui-core';
import { ScimService } from '../../services/scim.service';
import {
  ScimProvider,
  ScimProviderLog,
  ScimProviderFlavor,
  ScimProviderOptions,
  ScimProviderAuthType,
} from '../../models/scim-provider.model';

@Component({
  selector: 'lib-scim-providers',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, IconComponent, ModalComponent],
  template: `
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex justify-between items-start">
        <div>
          <h1 class="text-2xl font-semibold text-gray-900">SCIM Providers</h1>
          <p class="text-gray-500 mt-1">
            Downstream systems this tenant pushes its users and groups to over SCIM 2.0.
            For identity providers that push into this tenant, see
            <a routerLink="/scim/clients" class="text-primary-600 hover:underline">SCIM Clients</a>.
          </p>
        </div>
        <button
          (click)="openCreate()"
          class="inline-flex items-center px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 transition-colors"
        >
          <ui-icon name="plus" class="h-5 w-5 mr-2" />
          Add Provider
        </button>
      </div>

      @if (error()) {
        <div class="bg-red-50 text-red-700 p-4 rounded-md">{{ error() }}</div>
      }

      <!-- Create Modal -->
      <ui-modal
        [open]="showCreateModal()"
        title="Add SCIM Provider"
        size="lg"
        [scrollable]="true"
        (closed)="showCreateModal.set(false)"
      >
            <form (ngSubmit)="handleCreate()">
              <div class="space-y-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Name</label>
                  <input
                    type="text"
                    [(ngModel)]="createForm.name"
                    name="name"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    placeholder="e.g., Mail server, Wiki"
                    required
                  />
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Type</label>
                  <select
                    [(ngModel)]="createForm.flavor"
                    name="flavor"
                    (ngModelChange)="onFlavorChange($event)"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  >
                    @for (f of flavors(); track f.key) {
                      <option [value]="f.key">{{ f.displayName }}</option>
                    }
                  </select>
                  @if (selectedFlavor(); as f) {
                    <p class="text-xs text-gray-500 mt-1">{{ f.description }}</p>
                    @if (f.notes) {
                      <div class="mt-2 bg-blue-50 border border-blue-200 text-blue-800 text-xs rounded-md p-3">{{ f.notes }}</div>
                    }
                  }
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">SCIM base URL</label>
                  <input
                    type="url"
                    [(ngModel)]="createForm.baseUrl"
                    name="baseUrl"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent font-mono text-sm"
                    placeholder="https://mail.example.org/scim/v2"
                    required
                  />
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Authentication</label>
                  <select
                    [(ngModel)]="createForm.authType"
                    name="authType"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  >
                    <option value="bearer">Bearer token / API key</option>
                    <option value="oauth_client_credentials">OAuth 2.0 client credentials</option>
                  </select>
                </div>

                @if (createForm.authType === 'oauth_client_credentials') {
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Token endpoint</label>
                    <input
                      type="url"
                      [(ngModel)]="createForm.oauthTokenEndpoint"
                      name="oauthTokenEndpoint"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent font-mono text-sm"
                      placeholder="https://idp.example.org/connect/token"
                    />
                  </div>
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Client ID</label>
                    <input
                      type="text"
                      [(ngModel)]="createForm.oauthClientId"
                      name="oauthClientId"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    />
                  </div>
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Scope (optional)</label>
                    <input
                      type="text"
                      [(ngModel)]="createForm.oauthScope"
                      name="oauthScope"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    />
                  </div>
                }

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">
                    {{ createForm.authType === 'bearer' ? 'Bearer token / API key' : 'Client secret' }}
                  </label>
                  <div class="relative">
                    <input
                      [type]="showSecret() ? 'text' : 'password'"
                      [(ngModel)]="createForm.secret"
                      name="secret"
                      autocomplete="new-password"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 pr-10 focus:ring-2 focus:ring-primary-500 focus:border-transparent font-mono text-sm"
                      placeholder="Can be set later from the provider page"
                    />
                    <button
                      type="button"
                      (click)="showSecret.set(!showSecret())"
                      class="absolute inset-y-0 right-0 px-3 text-gray-500 hover:text-gray-700"
                      [title]="showSecret() ? 'Hide' : 'Show'"
                    >
                      <ui-icon [name]="showSecret() ? 'eye-slash' : 'eye'" class="h-4 w-4" />
                    </button>
                  </div>
                  <p class="text-xs text-gray-500 mt-1">Stored encrypted. It is never shown again after saving.</p>
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Description (optional)</label>
                  <input
                    type="text"
                    [(ngModel)]="createForm.description"
                    name="description"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  />
                </div>
              </div>

              @if (createError()) {
                <div class="mt-4 bg-red-50 text-red-700 p-3 rounded-md text-sm">{{ createError() }}</div>
              }

              <div class="flex justify-end gap-2 mt-6">
                <button
                  type="button"
                  (click)="showCreateModal.set(false)"
                  class="px-4 py-2 text-gray-700 hover:bg-gray-100 rounded-md transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  [disabled]="creating()"
                  class="px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 disabled:opacity-50 transition-colors"
                >
                  {{ creating() ? 'Creating...' : 'Create' }}
                </button>
              </div>
            </form>
      </ui-modal>

      <!-- Activity Modal -->
      <ui-modal
        [open]="activityProvider() !== null"
        [title]="'Activity: ' + (activityProvider()?.name ?? '')"
        size="xl"
        [scrollable]="true"
        (closed)="closeActivity()"
      >
        <div class="space-y-4">
          <div class="flex flex-wrap items-center justify-between gap-3">
            <div class="inline-flex rounded-md border border-gray-300 p-0.5 text-sm" role="group" aria-label="Show">
              <button type="button" (click)="setActivityErrorsOnly(false)" class="rounded px-3 py-1"
                      [class.bg-gray-100]="!activityErrorsOnly()" [class.font-medium]="!activityErrorsOnly()"
                      [attr.aria-pressed]="!activityErrorsOnly()">All calls</button>
              <button type="button" (click)="setActivityErrorsOnly(true)" class="rounded px-3 py-1"
                      [class.bg-gray-100]="activityErrorsOnly()" [class.font-medium]="activityErrorsOnly()"
                      [attr.aria-pressed]="activityErrorsOnly()">Errors only</button>
            </div>
            <div class="flex items-center gap-3">
              <button type="button" (click)="loadActivity()" class="text-gray-600 hover:text-gray-900 p-1" title="Refresh">
                <ui-icon name="refresh" class="h-5 w-5" />
              </button>
              @if (activityProvider(); as ap) {
                <a [routerLink]="['/scim/providers', ap.id]" class="text-sm text-primary-600 hover:underline">Open provider setup</a>
              }
            </div>
          </div>

          @if (activityError()) {
            <div class="bg-red-50 text-red-700 p-3 rounded-md text-sm">{{ activityError() }}</div>
          } @else if (activityLoading() && activityLogs().length === 0) {
            <p class="py-6 text-center text-sm text-gray-500">Loading...</p>
          } @else if (activityLogs().length === 0) {
            <p class="py-6 text-center text-sm text-gray-500">
              {{ activityErrorsOnly() ? 'No failed calls.' : 'No calls made yet.' }}
            </p>
          } @else {
            <div class="divide-y divide-gray-100" [class.opacity-60]="activityLoading()">
              @for (l of activityLogs(); track l.id) {
                <div class="flex items-start gap-3 py-3 text-sm">
                  <ui-icon [name]="l.success ? 'check-circle' : 'x-circle'" class="h-5 w-5 mt-0.5 shrink-0" [class.text-green-600]="l.success" [class.text-red-600]="!l.success" />
                  <div class="flex-1 min-w-0">
                    <div class="font-mono text-xs break-all">
                      <span class="font-semibold">{{ l.method }}</span> {{ l.path }}
                      <span class="text-gray-400">→ {{ l.statusCode }} · {{ l.durationMs }}ms</span>
                    </div>
                    <div class="text-xs text-gray-500">{{ l.operation }} {{ l.resourceType || '' }} {{ l.internalId || '' }}</div>
                    @if (l.errorMessage) {
                      <div class="mt-1 whitespace-pre-wrap break-words text-xs text-red-700">{{ l.errorMessage }}</div>
                    }
                  </div>
                  <div class="text-xs text-gray-400 whitespace-nowrap">{{ formatDate(l.timestamp) }}</div>
                </div>
              }
            </div>
          }

          @if (activityTotal() > 0) {
            <div class="flex items-center justify-between border-t border-gray-200 pt-3 text-sm text-gray-600">
              <span>Page {{ activityPage() }} of {{ activityTotalPages() }} · {{ activityTotal() }} {{ activityTotal() === 1 ? 'call' : 'calls' }}</span>
              <div class="flex gap-2">
                <button type="button" (click)="goToActivityPage(activityPage() - 1)" [disabled]="activityPage() <= 1 || activityLoading()"
                        class="px-3 py-1 rounded-md border border-gray-300 hover:bg-gray-50 disabled:opacity-50">Previous</button>
                <button type="button" (click)="goToActivityPage(activityPage() + 1)" [disabled]="activityPage() >= activityTotalPages() || activityLoading()"
                        class="px-3 py-1 rounded-md border border-gray-300 hover:bg-gray-50 disabled:opacity-50">Next</button>
              </div>
            </div>
          }
        </div>
      </ui-modal>

      <!-- Providers Table -->
      <div class="bg-white shadow rounded-lg overflow-hidden">
        @if (loading()) {
          <div class="p-8 text-center text-gray-500">Loading...</div>
        } @else {
          <table class="min-w-full divide-y divide-gray-200">
            <thead class="bg-gray-50">
              <tr>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Target</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Syncs</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Last Activity</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Stats</th>
                <th class="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-200">
              @if (providers().length === 0) {
                <tr>
                  <td colspan="7" class="px-6 py-8 text-center text-gray-500">
                    No providers configured. Add one to start pushing users to a downstream system.
                  </td>
                </tr>
              } @else {
                @for (p of providers(); track p.id) {
                  <tr>
                    <td class="px-6 py-4">
                      <a [routerLink]="['/scim/providers', p.id]" class="text-primary-600 hover:underline font-medium">
                        {{ p.name }}
                      </a>
                      <p class="text-xs text-gray-500">{{ flavorName(p.flavor) }}</p>
                    </td>
                    <td class="px-6 py-4">
                      <code class="text-xs text-gray-700 break-all">{{ p.baseUrl }}</code>
                    </td>
                    <td class="px-6 py-4">
                      <span
                        class="inline-flex px-2 py-1 text-xs rounded-full"
                        [class.bg-green-100]="p.isEnabled && p.hasSecret"
                        [class.text-green-800]="p.isEnabled && p.hasSecret"
                        [class.bg-yellow-100]="p.isEnabled && !p.hasSecret"
                        [class.text-yellow-800]="p.isEnabled && !p.hasSecret"
                        [class.bg-gray-100]="!p.isEnabled"
                        [class.text-gray-800]="!p.isEnabled"
                      >
                        {{ !p.isEnabled ? 'Disabled' : (p.hasSecret ? 'Enabled' : 'No secret') }}
                      </span>
                    </td>
                    <td class="px-6 py-4 text-sm text-gray-700">
                      <span class="inline-flex gap-1">
                        @if (p.syncUsers) {
                          <span class="px-2 py-0.5 text-xs rounded bg-gray-100 text-gray-700">Users</span>
                        }
                        @if (p.syncGroups) {
                          <span class="px-2 py-0.5 text-xs rounded bg-gray-100 text-gray-700">Groups ({{ p.groupSource }})</span>
                        }
                      </span>
                    </td>
                    <td class="px-6 py-4 text-sm text-gray-500">
                      @if (p.lastErrorAt && (!p.lastSuccessAt || p.lastErrorAt > p.lastSuccessAt)) {
                        <span class="text-red-600" [title]="p.lastError || ''">Error {{ formatDate(p.lastErrorAt) }}</span>
                      } @else if (p.lastSuccessAt) {
                        {{ formatDate(p.lastSuccessAt) }}
                      } @else {
                        Never
                      }
                    </td>
                    <td class="px-6 py-4 text-sm">
                      <button type="button" (click)="openActivity(p, false)" class="text-green-600 hover:underline" title="Show activity">{{ p.successCount }} ok</button>
                      /
                      <button type="button" (click)="openActivity(p, true)" class="text-red-600 hover:underline" title="Show errors">{{ p.errorCount }} errors</button>
                    </td>
                    <td class="px-6 py-4 text-right">
                      <button (click)="handleDelete(p)" class="text-red-600 hover:text-red-900 p-1" title="Delete">
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
export class ScimProvidersComponent implements OnInit {
  private readonly scimService = inject(ScimService);
  private readonly router = inject(Router);

  providers = signal<ScimProvider[]>([]);
  options = signal<ScimProviderOptions | null>(null);
  flavors = computed<ScimProviderFlavor[]>(() => this.options()?.flavors ?? []);
  loading = signal(true);
  error = signal<string | null>(null);
  showCreateModal = signal(false);
  creating = signal(false);
  createError = signal<string | null>(null);
  showSecret = signal(false);
  selectedFlavorKey = signal('generic');
  selectedFlavor = computed(() => this.flavors().find((f) => f.key === this.selectedFlavorKey()) ?? null);

  createForm = this.emptyForm();

  activityProvider = signal<ScimProvider | null>(null);
  activityErrorsOnly = signal(false);
  activityLogs = signal<ScimProviderLog[]>([]);
  activityPage = signal(1);
  activityTotal = signal(0);
  activityTotalPages = signal(1);
  activityLoading = signal(false);
  activityError = signal<string | null>(null);
  private readonly activityPageSize = 25;
  private activityRequest?: Subscription;

  ngOnInit(): void {
    this.scimService.getProviderOptions().subscribe({
      next: (o) => this.options.set(o),
      error: (err) => console.error('Failed to load provider options:', err),
    });
    this.loadProviders();
  }

  loadProviders(): void {
    this.loading.set(true);
    this.scimService.getProviders().subscribe({
      next: (data) => {
        this.providers.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load SCIM providers');
        console.error(err);
        this.loading.set(false);
      },
    });
  }

  openCreate(): void {
    this.createForm = this.emptyForm();
    this.selectedFlavorKey.set(this.createForm.flavor);
    this.createError.set(null);
    this.showSecret.set(false);
    this.showCreateModal.set(true);
  }

  onFlavorChange(key: string): void {
    this.selectedFlavorKey.set(key);
  }

  handleCreate(): void {
    this.creating.set(true);
    this.createError.set(null);
    const f = this.createForm;
    this.scimService
      .createProvider({
        name: f.name,
        description: f.description || undefined,
        flavor: f.flavor,
        baseUrl: f.baseUrl,
        authType: f.authType,
        oauthTokenEndpoint: f.authType === 'oauth_client_credentials' ? f.oauthTokenEndpoint || undefined : undefined,
        oauthClientId: f.authType === 'oauth_client_credentials' ? f.oauthClientId || undefined : undefined,
        oauthScope: f.authType === 'oauth_client_credentials' ? f.oauthScope || undefined : undefined,
        secret: f.secret || undefined,
      })
      .subscribe({
        next: (created) => {
          this.creating.set(false);
          this.showCreateModal.set(false);
          this.router.navigate(['/scim/providers', created.id]);
        },
        error: (err) => {
          this.creating.set(false);
          this.createError.set(typeof err?.error === 'string' ? err.error : 'Failed to create provider');
          console.error('Failed to create provider:', err);
        },
      });
  }

  handleDelete(p: ScimProvider): void {
    if (!confirm(`Delete provider "${p.name}"? Remote accounts it created are left as they are.`)) return;

    this.scimService.deleteProvider(p.id).subscribe({
      next: () => this.providers.update((list) => list.filter((x) => x.id !== p.id)),
      error: (err) => {
        this.error.set('Failed to delete provider');
        console.error('Failed to delete provider:', err);
      },
    });
  }

  openActivity(p: ScimProvider, errorsOnly: boolean): void {
    this.activityProvider.set(p);
    this.activityErrorsOnly.set(errorsOnly);
    this.activityLogs.set([]);
    this.activityTotal.set(0);
    this.activityPage.set(1);
    this.loadActivity();
  }

  closeActivity(): void {
    this.activityRequest?.unsubscribe();
    this.activityProvider.set(null);
  }

  setActivityErrorsOnly(errorsOnly: boolean): void {
    if (this.activityErrorsOnly() === errorsOnly) return;
    this.activityErrorsOnly.set(errorsOnly);
    this.activityPage.set(1);
    this.loadActivity();
  }

  goToActivityPage(page: number): void {
    this.activityPage.set(page);
    this.loadActivity();
  }

  loadActivity(): void {
    const p = this.activityProvider();
    if (!p) return;
    this.activityRequest?.unsubscribe();
    this.activityLoading.set(true);
    this.activityError.set(null);
    this.activityRequest = this.scimService
      .getProviderLogs(p.id, this.activityPage(), this.activityPageSize, this.activityErrorsOnly() ? false : undefined)
      .subscribe({
        next: (r) => {
          this.activityLogs.set(r.items);
          this.activityTotal.set(r.totalCount);
          this.activityTotalPages.set(Math.max(1, r.totalPages));
          this.activityLoading.set(false);
        },
        error: (err) => {
          this.activityError.set('Failed to load activity');
          this.activityLoading.set(false);
          console.error('Failed to load provider activity:', err);
        },
      });
  }

  flavorName(key: string): string {
    return this.flavors().find((f) => f.key === key)?.displayName ?? key;
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }

  private emptyForm() {
    return {
      name: '',
      description: '',
      flavor: 'generic',
      baseUrl: '',
      authType: 'bearer' as ScimProviderAuthType,
      oauthTokenEndpoint: '',
      oauthClientId: '',
      oauthScope: '',
      secret: '',
    };
  }
}
