import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonComponent, IconComponent, ModalComponent } from '@oluso/ui-core';
import { SamlService } from '../../services/saml.service';
import { SamlServiceProvider, SamlTestResult } from '../../models/saml.model';

@Component({
  selector: 'saml-service-providers-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ButtonComponent, IconComponent, ModalComponent],
  template: `
    <div class="space-y-6">
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-bold text-gray-900">SAML Service Providers</h1>
          <p class="mt-1 text-sm text-gray-500">Applications that use this system as their SAML Identity Provider</p>
        </div>
        <a routerLink="/saml/service-providers/new">
          <ui-button>
            <ui-icon name="plus" class="-ml-1 mr-2 h-5 w-5" />
            Add Service Provider
          </ui-button>
        </a>
      </div>

      <!-- Filters -->
      <div class="flex items-center space-x-4">
        <div class="flex-1 max-w-md">
          <div class="relative">
            <ui-icon name="search" class="absolute left-3 top-1/2 transform -translate-y-1/2 h-5 w-5 text-gray-400" />
            <input
              type="text"
              [(ngModel)]="search"
              (ngModelChange)="onSearchChange()"
              placeholder="Search by Entity ID, name, or description..."
              class="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-md focus:ring-primary-500 focus:border-primary-500"
            />
          </div>
        </div>
        <label class="flex items-center space-x-2 text-sm text-gray-700">
          <input
            type="checkbox"
            [(ngModel)]="showDisabled"
            (ngModelChange)="loadServiceProviders()"
            class="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
          />
          <span>Show disabled</span>
        </label>
      </div>

      @if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4">
          <p class="text-red-700">{{ error() }}</p>
        </div>
      }

      <!-- Test Result Toast -->
      @if (testResult()) {
        <div [class]="testResult()?.success ? 'bg-green-50 border-green-200' : 'bg-red-50 border-red-200'" class="rounded-lg p-4 border">
          <div class="flex items-center justify-between">
            <div class="flex items-center space-x-2">
              @if (testResult()?.success) {
                <ui-icon name="check-circle" class="h-5 w-5 text-green-600" />
              } @else {
                <ui-icon name="x-circle" class="h-5 w-5 text-red-600" />
              }
              <span [class]="testResult()?.success ? 'text-green-700' : 'text-red-700'">{{ testResult()?.message }}</span>
            </div>
            <button (click)="testResult.set(null)" class="text-gray-400 hover:text-gray-600">&times;</button>
          </div>
        </div>
      }

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="bg-gray-200 h-24 rounded-lg animate-pulse"></div>
          }
        </div>
      } @else if (filteredProviders().length === 0) {
        <div class="text-center py-12 bg-white rounded-lg shadow">
          <ui-icon name="building" class="mx-auto h-12 w-12 text-gray-400" />
          <h3 class="mt-2 text-sm font-medium text-gray-900">No Service Providers</h3>
          <p class="mt-1 text-sm text-gray-500">
            {{ search ? 'No service providers match your search.' : 'Get started by adding a SAML Service Provider.' }}
          </p>
          @if (!search) {
            <div class="mt-6">
              <a routerLink="/saml/service-providers/new">
                <ui-button>Add Service Provider</ui-button>
              </a>
            </div>
          }
        </div>
      } @else {
        <div class="bg-white shadow overflow-hidden rounded-lg">
          <table class="min-w-full divide-y divide-gray-200">
            <thead class="bg-gray-50">
              <tr>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Service Provider</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Configuration</th>
                <th class="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Last Accessed</th>
                <th class="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody class="bg-white divide-y divide-gray-200">
              @for (sp of filteredProviders(); track sp.id) {
                <tr class="hover:bg-gray-50">
                  <td class="px-6 py-4">
                    <div class="flex items-center">
                      <ui-icon name="building" class="h-8 w-8 text-gray-400 flex-shrink-0" />
                      <div class="ml-4">
                        <div class="text-sm font-medium text-gray-900">{{ sp.displayName || sp.entityId }}</div>
                        <div class="text-sm text-gray-500 max-w-xs truncate" [title]="sp.entityId">{{ sp.entityId }}</div>
                        @if (sp.description) {
                          <div class="text-xs text-gray-400 max-w-xs truncate">{{ sp.description }}</div>
                        }
                      </div>
                    </div>
                  </td>
                  <td class="px-6 py-4 whitespace-nowrap">
                    <span [class]="sp.enabled ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-800'" class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium">
                      {{ sp.enabled ? 'Enabled' : 'Disabled' }}
                    </span>
                  </td>
                  <td class="px-6 py-4">
                    <div class="flex items-center space-x-2 text-xs text-gray-500">
                      @if (sp.metadataUrl) {
                        <span class="bg-blue-100 text-blue-800 px-2 py-0.5 rounded">Metadata</span>
                      } @else {
                        <span class="bg-gray-100 text-gray-600 px-2 py-0.5 rounded">Manual</span>
                      }
                      @if (sp.encryptAssertions) {
                        <span class="bg-purple-100 text-purple-800 px-2 py-0.5 rounded">Encrypted</span>
                      }
                      @if (sp.requireSignedAuthnRequests) {
                        <span class="bg-yellow-100 text-yellow-800 px-2 py-0.5 rounded">Signed Requests</span>
                      }
                    </div>
                  </td>
                  <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {{ sp.lastAccessed ? formatDate(sp.lastAccessed) : 'Never' }}
                  </td>
                  <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <div class="flex items-center justify-end space-x-2">
                      <button (click)="handleTest(sp)" [disabled]="actionLoading() === sp.id" class="p-1.5 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded" title="Test configuration">
                        <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z" />
                        </svg>
                      </button>
                      <button (click)="handleToggle(sp)" [disabled]="actionLoading() === sp.id || sp.nonEditable" [class]="sp.enabled ? 'text-gray-400 hover:text-yellow-600 hover:bg-yellow-50' : 'text-gray-400 hover:text-green-600 hover:bg-green-50'" class="p-1.5 rounded" [class.opacity-50]="sp.nonEditable" [class.cursor-not-allowed]="sp.nonEditable" [title]="sp.enabled ? 'Disable' : 'Enable'">
                        @if (sp.enabled) {
                          <ui-icon name="pause-circle" class="h-4 w-4" />
                        } @else {
                          <ui-icon name="play-circle" class="h-4 w-4" />
                        }
                      </button>
                      <a [routerLink]="['/saml/service-providers', sp.id]" class="p-1.5 text-gray-400 hover:text-primary-600 hover:bg-primary-50 rounded" title="Edit">
                        <ui-icon name="pencil" class="h-4 w-4" />
                      </a>
                      <button (click)="confirmDelete(sp)" [disabled]="actionLoading() === sp.id || sp.nonEditable" class="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded" [class.opacity-50]="sp.nonEditable" [class.cursor-not-allowed]="sp.nonEditable" title="Delete">
                        <ui-icon name="trash" class="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <!-- Delete Confirmation Modal -->
      <ui-modal [open]="showDeleteModal()" title="Delete Service Provider" (closed)="showDeleteModal.set(false)">
        <p class="text-sm text-gray-500">
          Are you sure you want to delete "{{ deleteTarget()?.displayName || deleteTarget()?.entityId }}"?
          This action cannot be undone.
        </p>
        <div class="mt-4 flex justify-end gap-3">
          <ui-button variant="secondary" (clicked)="showDeleteModal.set(false)">Cancel</ui-button>
          <ui-button variant="danger" [disabled]="!!actionLoading()" (clicked)="handleDelete()">
            {{ actionLoading() ? 'Deleting...' : 'Delete' }}
          </ui-button>
        </div>
      </ui-modal>
    </div>
  `,
})
export class ServiceProvidersPageComponent implements OnInit {
  private readonly samlService = inject(SamlService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly serviceProviders = signal<SamlServiceProvider[]>([]);
  readonly actionLoading = signal<number | null>(null);
  readonly testResult = signal<SamlTestResult | null>(null);
  readonly showDeleteModal = signal(false);
  readonly deleteTarget = signal<SamlServiceProvider | null>(null);

  search = '';
  showDisabled = true;

  readonly filteredProviders = () => {
    if (!this.search) return this.serviceProviders();
    const searchLower = this.search.toLowerCase();
    return this.serviceProviders().filter(
      (sp) =>
        sp.entityId.toLowerCase().includes(searchLower) ||
        sp.displayName?.toLowerCase().includes(searchLower) ||
        sp.description?.toLowerCase().includes(searchLower)
    );
  };

  ngOnInit(): void {
    this.loadServiceProviders();
  }

  loadServiceProviders(): void {
    this.loading.set(true);
    this.samlService.getAll(this.showDisabled).subscribe({
      next: (data) => {
        this.serviceProviders.set(data);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading service providers:', err);
        this.error.set('Failed to load Service Providers');
        this.loading.set(false);
      },
    });
  }

  onSearchChange(): void {
    // Filtering is handled reactively
  }

  handleToggle(sp: SamlServiceProvider): void {
    this.actionLoading.set(sp.id);
    this.samlService.toggle(sp.id).subscribe({
      next: () => {
        this.loadServiceProviders();
        this.actionLoading.set(null);
      },
      error: (err) => {
        console.error('Error toggling service provider:', err);
        this.actionLoading.set(null);
      },
    });
  }

  confirmDelete(sp: SamlServiceProvider): void {
    this.deleteTarget.set(sp);
    this.showDeleteModal.set(true);
  }

  handleDelete(): void {
    const sp = this.deleteTarget();
    if (!sp) return;

    this.actionLoading.set(sp.id);
    this.samlService.delete(sp.id).subscribe({
      next: () => {
        this.showDeleteModal.set(false);
        this.deleteTarget.set(null);
        this.loadServiceProviders();
        this.actionLoading.set(null);
      },
      error: (err) => {
        console.error('Error deleting service provider:', err);
        this.actionLoading.set(null);
      },
    });
  }

  handleTest(sp: SamlServiceProvider): void {
    this.actionLoading.set(sp.id);
    this.testResult.set(null);
    this.samlService.test(sp.id).subscribe({
      next: (result) => {
        this.testResult.set(result);
        this.actionLoading.set(null);
      },
      error: () => {
        this.testResult.set({ success: false, message: 'Failed to run test' });
        this.actionLoading.set(null);
      },
    });
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString();
  }
}
