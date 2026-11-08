import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  CardComponent,
  ButtonComponent,
  IconComponent,
  TAB_CONTEXT,
  TabContext,
  FileUploadService,
} from '@oluso/ui-core';
import { SettingsService } from '../../services/settings.service';
import { BrandingSettings, DEFAULT_BRANDING_SETTINGS } from '../../models/server-settings.model';

/**
 * Branding settings tab — logo / favicon upload, brand colours, support
 * contact, custom CSS, custom footer text. Reads / writes the
 * <c>BrandingSettings</c> tenant-config section via SettingsController.
 * Logo + favicon are public assets (shown on anonymous login / journey pages),
 * uploaded server-side under <c>public/branding</c> so they get a stable,
 * non-expiring public URL.
 */
@Component({
  selector: 'admin-branding-settings-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, IconComponent],
  template: `
    @if (loading()) {
      <div class="flex items-center justify-center py-12">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
      </div>
    } @else {
      <div class="space-y-6">
        <div class="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <div class="flex items-start gap-3">
            <ui-icon name="information-circle" class="h-5 w-5 text-blue-500 mt-0.5 flex-shrink-0" />
            <div class="text-sm text-blue-800">
              <p class="font-medium">Tenant Branding</p>
              <p class="mt-1">Shown on this tenant's login / journey pages and embedded auth widgets. Uploaded assets are stored under the platform's file store and served from a URL.</p>
            </div>
          </div>
        </div>

        @if (error()) {
          <div class="bg-red-50 border border-red-200 rounded-md p-4">
            <p class="text-sm text-red-600">{{ error() }}</p>
          </div>
        }

        @if (successMessage()) {
          <div class="bg-green-50 border border-green-200 rounded-md p-4">
            <div class="flex items-center gap-2">
              <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
              <p class="text-sm text-green-700">{{ successMessage() }}</p>
            </div>
          </div>
        }

        <!-- Logo + favicon uploads -->
        <ui-card>
          <div class="p-6 space-y-6">
            <h3 class="text-lg font-medium text-gray-900">Logo & Favicon</h3>

            <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <!-- Logo -->
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-2">Logo</label>
                <div class="flex items-start gap-4">
                  <div class="w-32 h-32 border-2 border-dashed border-gray-300 rounded-lg flex items-center justify-center bg-gray-50 overflow-hidden flex-shrink-0">
                    @if (formData.logoUrl) {
                      <img [src]="formData.logoUrl" alt="Logo preview" class="max-w-full max-h-full object-contain" />
                    } @else {
                      <ui-icon name="photo" class="h-10 w-10 text-gray-300" />
                    }
                  </div>
                  <div class="flex-1 min-w-0">
                    <input
                      #logoInput
                      type="file"
                      accept="image/png,image/jpeg,image/svg+xml,image/webp"
                      (change)="onLogoSelected($event)"
                      class="hidden"
                    />
                    <ui-button
                      variant="secondary"
                      [disabled]="uploadingLogo()"
                      (clicked)="logoInput.click()"
                    >
                      @if (uploadingLogo()) {
                        <ui-icon name="arrow-path" class="h-4 w-4 animate-spin mr-2" />
                        Uploading…
                      } @else {
                        <ui-icon name="arrow-up-tray" class="h-4 w-4 mr-2" />
                        {{ formData.logoUrl ? 'Replace' : 'Upload' }} logo
                      }
                    </ui-button>
                    @if (formData.logoUrl) {
                      <ui-button variant="secondary" (clicked)="clearLogo()" class="ml-2">
                        Remove
                      </ui-button>
                    }
                    <p class="mt-2 text-xs text-gray-500">PNG, JPEG, SVG, or WebP. Recommended: SVG or 512×512 PNG.</p>
                    <p class="mt-1 text-xs text-gray-400 truncate" *ngIf="formData.logoUrl">{{ formData.logoUrl }}</p>
                  </div>
                </div>
              </div>

              <!-- Favicon -->
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-2">Favicon</label>
                <div class="flex items-start gap-4">
                  <div class="w-32 h-32 border-2 border-dashed border-gray-300 rounded-lg flex items-center justify-center bg-gray-50 overflow-hidden flex-shrink-0">
                    @if (formData.faviconUrl) {
                      <img [src]="formData.faviconUrl" alt="Favicon preview" class="max-w-full max-h-full object-contain" />
                    } @else {
                      <ui-icon name="bookmark" class="h-10 w-10 text-gray-300" />
                    }
                  </div>
                  <div class="flex-1 min-w-0">
                    <input
                      #faviconInput
                      type="file"
                      accept="image/png,image/x-icon,image/svg+xml"
                      (change)="onFaviconSelected($event)"
                      class="hidden"
                    />
                    <ui-button
                      variant="secondary"
                      [disabled]="uploadingFavicon()"
                      (clicked)="faviconInput.click()"
                    >
                      @if (uploadingFavicon()) {
                        <ui-icon name="arrow-path" class="h-4 w-4 animate-spin mr-2" />
                        Uploading…
                      } @else {
                        <ui-icon name="arrow-up-tray" class="h-4 w-4 mr-2" />
                        {{ formData.faviconUrl ? 'Replace' : 'Upload' }} favicon
                      }
                    </ui-button>
                    @if (formData.faviconUrl) {
                      <ui-button variant="secondary" (clicked)="clearFavicon()" class="ml-2">
                        Remove
                      </ui-button>
                    }
                    <p class="mt-2 text-xs text-gray-500">PNG, ICO, or SVG. Recommended: 32×32 or 64×64.</p>
                    <p class="mt-1 text-xs text-gray-400 truncate" *ngIf="formData.faviconUrl">{{ formData.faviconUrl }}</p>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </ui-card>

        <!-- Colours -->
        <ui-card>
          <div class="p-6 space-y-4">
            <h3 class="text-lg font-medium text-gray-900">Brand Colours</h3>

            <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Primary</label>
                <div class="flex items-center gap-2">
                  <input
                    type="color"
                    [ngModel]="formData.primaryColor || '#1A56FF'"
                    (ngModelChange)="formData.primaryColor = $event; onFormChange()"
                    class="h-10 w-12 rounded border border-gray-300 cursor-pointer"
                  />
                  <input
                    type="text"
                    [(ngModel)]="formData.primaryColor"
                    (ngModelChange)="onFormChange()"
                    placeholder="#1A56FF"
                    class="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm font-mono"
                  />
                </div>
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Secondary</label>
                <div class="flex items-center gap-2">
                  <input
                    type="color"
                    [ngModel]="formData.secondaryColor || '#00A880'"
                    (ngModelChange)="formData.secondaryColor = $event; onFormChange()"
                    class="h-10 w-12 rounded border border-gray-300 cursor-pointer"
                  />
                  <input
                    type="text"
                    [(ngModel)]="formData.secondaryColor"
                    (ngModelChange)="onFormChange()"
                    placeholder="#00A880"
                    class="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm font-mono"
                  />
                </div>
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Background</label>
                <div class="flex items-center gap-2">
                  <input
                    type="color"
                    [ngModel]="formData.backgroundColor || '#F8F6F1'"
                    (ngModelChange)="formData.backgroundColor = $event; onFormChange()"
                    class="h-10 w-12 rounded border border-gray-300 cursor-pointer"
                  />
                  <input
                    type="text"
                    [(ngModel)]="formData.backgroundColor"
                    (ngModelChange)="onFormChange()"
                    placeholder="#F8F6F1"
                    class="flex-1 rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm font-mono"
                  />
                </div>
              </div>
            </div>
          </div>
        </ui-card>

        <!-- Company info & contact -->
        <ui-card>
          <div class="p-6 space-y-4">
            <h3 class="text-lg font-medium text-gray-900">Company Information</h3>

            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Company name</label>
                <input
                  type="text"
                  [(ngModel)]="formData.companyName"
                  (ngModelChange)="onFormChange()"
                  placeholder="Acme Inc."
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Support email</label>
                <input
                  type="email"
                  [(ngModel)]="formData.supportEmail"
                  (ngModelChange)="onFormChange()"
                  placeholder="support@acme.com"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Terms of service URL</label>
                <input
                  type="url"
                  [(ngModel)]="formData.termsOfServiceUrl"
                  (ngModelChange)="onFormChange()"
                  placeholder="https://acme.com/terms"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Privacy policy URL</label>
                <input
                  type="url"
                  [(ngModel)]="formData.privacyPolicyUrl"
                  (ngModelChange)="onFormChange()"
                  placeholder="https://acme.com/privacy"
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
              <div class="sm:col-span-2">
                <label class="block text-sm font-medium text-gray-700 mb-1">Footer text</label>
                <input
                  type="text"
                  [(ngModel)]="formData.footerText"
                  (ngModelChange)="onFormChange()"
                  placeholder="© Acme Inc. All rights reserved."
                  class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                />
              </div>
            </div>
          </div>
        </ui-card>

        <!-- Custom CSS -->
        <ui-card>
          <div class="p-6 space-y-2">
            <h3 class="text-lg font-medium text-gray-900">Custom CSS</h3>
            <p class="text-sm text-gray-500">Injected into auth pages after the base stylesheet. Use sparingly — broken CSS breaks login.</p>
            <textarea
              [(ngModel)]="formData.customCss"
              (ngModelChange)="onFormChange()"
              placeholder=":root { --brand-radius: 0.25rem; }"
              rows="6"
              class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm font-mono"
            ></textarea>
          </div>
        </ui-card>

        <div class="flex justify-end">
          <ui-button (clicked)="save()" [disabled]="saving() || !hasChanges()">
            @if (saving()) {
              <ui-icon name="arrow-path" class="h-4 w-4 animate-spin mr-2" />
              Saving…
            } @else {
              Save changes
            }
          </ui-button>
        </div>
      </div>
    }
  `,
})
export class BrandingSettingsTabComponent implements OnInit {
  private readonly tabContext: TabContext = inject(TAB_CONTEXT);
  private readonly settingsService = inject(SettingsService);
  private readonly fileUploadService = inject(FileUploadService);

  loading = signal(true);
  saving = signal(false);
  uploadingLogo = signal(false);
  uploadingFavicon = signal(false);
  hasChanges = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  formData: BrandingSettings = { ...DEFAULT_BRANDING_SETTINGS };

  ngOnInit(): void {
    this.loadData();
  }

  private loadData(): void {
    this.loading.set(true);
    this.error.set(null);
    this.settingsService.getBrandingSettings().subscribe({
      next: (settings) => {
        this.formData = { ...DEFAULT_BRANDING_SETTINGS, ...settings };
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load branding settings:', err);
        this.error.set('Could not load branding settings.');
        this.loading.set(false);
      },
    });
  }

  onFormChange(): void {
    this.hasChanges.set(true);
    this.error.set(null);
    this.successMessage.set(null);
  }

  onLogoSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.uploadingLogo.set(true);
    this.error.set(null);
    this.fileUploadService.upload(file, 'public/branding', { access: 'public' }).subscribe({
      next: (result) => {
        this.formData.logoUrl = result.imageUrl;
        this.uploadingLogo.set(false);
        this.onFormChange();
        (event.target as HTMLInputElement).value = '';
      },
      error: (err) => {
        console.error('Logo upload failed:', err);
        this.error.set('Logo upload failed. Try a different file.');
        this.uploadingLogo.set(false);
      },
    });
  }

  onFaviconSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.uploadingFavicon.set(true);
    this.error.set(null);
    this.fileUploadService.upload(file, 'public/branding', { access: 'public' }).subscribe({
      next: (result) => {
        this.formData.faviconUrl = result.imageUrl;
        this.uploadingFavicon.set(false);
        this.onFormChange();
        (event.target as HTMLInputElement).value = '';
      },
      error: (err) => {
        console.error('Favicon upload failed:', err);
        this.error.set('Favicon upload failed. Try a different file.');
        this.uploadingFavicon.set(false);
      },
    });
  }

  clearLogo(): void {
    this.formData.logoUrl = undefined;
    this.onFormChange();
  }

  clearFavicon(): void {
    this.formData.faviconUrl = undefined;
    this.onFormChange();
  }

  save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.successMessage.set(null);
    this.settingsService.updateBrandingSettings(this.formData).subscribe({
      next: (updated) => {
        this.formData = { ...DEFAULT_BRANDING_SETTINGS, ...updated };
        this.saving.set(false);
        this.hasChanges.set(false);
        this.successMessage.set('Branding settings saved.');
      },
      error: (err) => {
        console.error('Failed to save branding settings:', err);
        this.error.set('Save failed. Try again.');
        this.saving.set(false);
      },
    });
  }
}
