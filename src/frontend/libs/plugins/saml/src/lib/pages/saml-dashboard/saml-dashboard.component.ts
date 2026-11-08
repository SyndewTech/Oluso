import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ButtonComponent, IconComponent } from '@oluso/ui-core';
import { SamlService } from '../../services/saml.service';
import { SamlIdpInfo, SamlServiceProvider } from '../../models/saml.model';
import { forkJoin } from 'rxjs';

@Component({
  selector: 'saml-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, ButtonComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <div class="flex items-center justify-between">
        <h1 class="text-2xl font-bold text-gray-900">SAML Identity Provider</h1>
        <a routerLink="/saml/service-providers">
          <ui-button>Manage Service Providers</ui-button>
        </a>
      </div>

      @if (loading()) {
        <div class="space-y-6">
          <div class="h-8 w-48 bg-gray-200 rounded animate-pulse"></div>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
            @for (i of [1, 2]; track i) {
              <div class="h-48 bg-gray-200 rounded-lg animate-pulse"></div>
            }
          </div>
        </div>
      } @else if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4">
          <p class="text-red-700">{{ error() }}</p>
        </div>
      } @else {
        <!-- IdP Status -->
        <div [class]="idpInfo()?.enabled ? 'bg-green-50 border border-green-200' : 'bg-yellow-50 border border-yellow-200'" class="rounded-lg p-4">
          <div class="flex items-center space-x-2">
            @if (idpInfo()?.enabled) {
              <ui-icon name="check-circle" class="h-5 w-5 text-green-600" />
              <span class="text-green-700 font-medium">SAML IdP is enabled</span>
            } @else {
              <ui-icon name="x-circle" class="h-5 w-5 text-yellow-600" />
              <span class="text-yellow-700 font-medium">SAML IdP is disabled</span>
            }
          </div>
        </div>

        <!-- IdP Configuration Info -->
        @if (idpInfo()) {
          <div class="bg-white shadow rounded-lg">
            <div class="px-6 py-4 border-b border-gray-200">
              <h2 class="text-lg font-medium text-gray-900">IdP Configuration</h2>
              <p class="mt-1 text-sm text-gray-500">Share these URLs with Service Providers to configure SSO</p>
            </div>
            <div class="px-6 py-4 space-y-4">
              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">Entity ID (Issuer):</span>
                  <div class="flex items-center space-x-2 mt-1">
                    <code class="text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded truncate">{{ idpInfo()?.entityId }}</code>
                  </div>
                </div>
                <button (click)="copyToClipboard(idpInfo()?.entityId || '', 'entityId')" class="ml-4 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded">
                  @if (copied() === 'entityId') {
                    <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
                  } @else {
                    <ui-icon name="clipboard-document" class="h-5 w-5" />
                  }
                </button>
              </div>

              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">SSO URL:</span>
                  <div class="flex items-center space-x-2 mt-1">
                    <code class="text-sm text-gray-900 bg-gray-100 px-2 py-1 rounded truncate">{{ idpInfo()?.singleSignOnServiceUrl }}</code>
                  </div>
                </div>
                <button (click)="copyToClipboard(idpInfo()?.singleSignOnServiceUrl || '', 'ssoUrl')" class="ml-4 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded">
                  @if (copied() === 'ssoUrl') {
                    <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
                  } @else {
                    <ui-icon name="clipboard-document" class="h-5 w-5" />
                  }
                </button>
              </div>

              <div class="flex items-center justify-between">
                <div class="flex-1 min-w-0">
                  <span class="text-sm font-medium text-gray-500">Metadata URL:</span>
                  <div class="flex items-center space-x-2 mt-1">
                    <a [href]="idpInfo()?.metadataUrl" target="_blank" rel="noopener noreferrer" class="text-sm text-primary-600 hover:text-primary-700 truncate">
                      {{ idpInfo()?.metadataUrl }}
                    </a>
                  </div>
                </div>
                <button (click)="copyToClipboard(idpInfo()?.metadataUrl || '', 'metadataUrl')" class="ml-4 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded">
                  @if (copied() === 'metadataUrl') {
                    <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
                  } @else {
                    <ui-icon name="clipboard-document" class="h-5 w-5" />
                  }
                </button>
              </div>

              <div class="pt-2">
                <span class="text-sm font-medium text-gray-500">Supported Name ID Formats:</span>
                <ul class="mt-1 text-sm text-gray-700 list-disc list-inside">
                  @for (format of idpInfo()?.supportedNameIdFormats || []; track format) {
                    <li>{{ getFormatName(format) }}</li>
                  }
                </ul>
              </div>
            </div>
          </div>
        }

        <!-- Service Provider Stats -->
        <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
          <a routerLink="/saml/service-providers" class="bg-white shadow rounded-lg p-6 hover:shadow-md transition-shadow">
            <div class="flex items-center">
              <div class="flex-shrink-0">
                <ui-icon name="building" class="h-8 w-8 text-gray-400" />
              </div>
              <div class="ml-4">
                <p class="text-sm font-medium text-gray-500">Total Service Providers</p>
                <p class="text-2xl font-semibold text-gray-900">{{ serviceProviders().length }}</p>
              </div>
            </div>
          </a>

          <div class="bg-white shadow rounded-lg p-6">
            <div class="flex items-center">
              <div class="flex-shrink-0">
                <ui-icon name="check-circle" class="h-8 w-8 text-green-500" />
              </div>
              <div class="ml-4">
                <p class="text-sm font-medium text-gray-500">Enabled</p>
                <p class="text-2xl font-semibold text-gray-900">{{ enabledCount() }}</p>
              </div>
            </div>
          </div>

          <div class="bg-white shadow rounded-lg p-6">
            <div class="flex items-center">
              <div class="flex-shrink-0">
                <ui-icon name="x-circle" class="h-8 w-8 text-gray-400" />
              </div>
              <div class="ml-4">
                <p class="text-sm font-medium text-gray-500">Disabled</p>
                <p class="text-2xl font-semibold text-gray-900">{{ disabledCount() }}</p>
              </div>
            </div>
          </div>
        </div>

        <!-- Recent Service Providers -->
        @if (serviceProviders().length > 0) {
          <div class="bg-white shadow rounded-lg">
            <div class="px-6 py-4 border-b border-gray-200">
              <h2 class="text-lg font-medium text-gray-900">Service Providers</h2>
            </div>
            <ul class="divide-y divide-gray-200">
              @for (sp of serviceProviders().slice(0, 5); track sp.id) {
                <li class="px-6 py-4 hover:bg-gray-50">
                  <a [routerLink]="['/saml/service-providers', sp.id]" class="flex items-center justify-between">
                    <div class="flex items-center space-x-3">
                      <ui-icon name="building" class="h-6 w-6 text-gray-400" />
                      <div>
                        <p class="font-medium text-gray-900">{{ sp.displayName || sp.entityId }}</p>
                        <p class="text-sm text-gray-500 truncate max-w-md">{{ sp.entityId }}</p>
                      </div>
                    </div>
                    <span [class]="sp.enabled ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-800'" class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium">
                      {{ sp.enabled ? 'Enabled' : 'Disabled' }}
                    </span>
                  </a>
                </li>
              }
            </ul>
            @if (serviceProviders().length > 5) {
              <div class="px-6 py-3 border-t border-gray-200 bg-gray-50">
                <a routerLink="/saml/service-providers" class="text-sm text-primary-600 hover:text-primary-700">
                  View all {{ serviceProviders().length }} service providers
                </a>
              </div>
            }
          </div>
        } @else {
          <div class="text-center py-12 bg-white rounded-lg shadow">
            <ui-icon name="building" class="mx-auto h-12 w-12 text-gray-400" />
            <h3 class="mt-2 text-sm font-medium text-gray-900">No Service Providers</h3>
            <p class="mt-1 text-sm text-gray-500">Get started by adding a SAML Service Provider.</p>
            <div class="mt-6">
              <a routerLink="/saml/service-providers/new">
                <ui-button>Add Service Provider</ui-button>
              </a>
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class SamlDashboardComponent implements OnInit {
  private readonly samlService = inject(SamlService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly idpInfo = signal<SamlIdpInfo | null>(null);
  readonly serviceProviders = signal<SamlServiceProvider[]>([]);
  readonly copied = signal<string | null>(null);

  readonly enabledCount = () => this.serviceProviders().filter((sp) => sp.enabled).length;
  readonly disabledCount = () => this.serviceProviders().length - this.enabledCount();

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.loading.set(true);
    forkJoin({
      idpInfo: this.samlService.getIdpInfo(),
      serviceProviders: this.samlService.getAll(),
    }).subscribe({
      next: (data) => {
        this.idpInfo.set(data.idpInfo);
        this.serviceProviders.set(data.serviceProviders);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading SAML data:', err);
        this.error.set('Failed to load SAML configuration');
        this.loading.set(false);
      },
    });
  }

  copyToClipboard(value: string, label: string): void {
    navigator.clipboard.writeText(value).then(() => {
      this.copied.set(label);
      setTimeout(() => this.copied.set(null), 2000);
    });
  }

  getFormatName(format: string): string {
    return format.split(':').pop() || format;
  }
}
