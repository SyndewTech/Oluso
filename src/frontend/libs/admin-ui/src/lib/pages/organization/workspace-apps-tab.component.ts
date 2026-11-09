import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  CardComponent,
  IconComponent,
  IconPickerComponent,
  TAB_CONTEXT,
} from '@syndew/oluso-ui-core';
import { TenantService, WorkspaceAppsSettings, CustomWorkspaceApp, RegisteredModuleInfo } from '../../services/tenant.service';
import { TenantFeature } from '../../models/tenant.model';

interface BuiltInAppDef {
  id: string;
  name: string;
  description: string;
  icon: string;
  color: string;
  featureKey: string;
}

const APP_COLORS = [
  { value: 'slate', label: 'Slate', class: 'bg-slate-500' },
  { value: 'blue', label: 'Blue', class: 'bg-blue-500' },
  { value: 'green', label: 'Green', class: 'bg-emerald-500' },
  { value: 'purple', label: 'Purple', class: 'bg-purple-500' },
  { value: 'orange', label: 'Orange', class: 'bg-orange-500' },
  { value: 'red', label: 'Red', class: 'bg-red-500' },
  { value: 'indigo', label: 'Indigo', class: 'bg-indigo-500' },
  { value: 'teal', label: 'Teal', class: 'bg-teal-500' },
  { value: 'pink', label: 'Pink', class: 'bg-pink-500' },
];

@Component({
  selector: 'admin-workspace-apps-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, IconComponent, IconPickerComponent],
  template: `
    <!-- Built-in Apps -->
    <ui-card class="mb-6">
      <div class="px-6 py-4 border-b border-gray-200">
        <h3 class="text-base font-semibold text-gray-900">Built-in Apps</h3>
        <p class="text-sm text-gray-500 mt-1">Enable or disable built-in workspace applications. Only apps whose module feature is enabled can be activated.</p>
      </div>
      <div class="divide-y divide-gray-100">
        @for (app of builtInApps(); track app.id) {
          <div
            class="flex items-center justify-between px-6 py-4"
            [class.opacity-50]="!isFeatureEnabled(app.featureKey)"
          >
            <div class="flex items-center gap-3">
              <div class="w-10 h-10 rounded-xl flex items-center justify-center" [class]="getColorBgClass(app.color)">
                <ui-icon [name]="app.icon" class="h-5 w-5 text-white" />
              </div>
              <div>
                <p class="text-sm font-medium text-gray-900">{{ app.name }}</p>
                <p class="text-xs text-gray-500">{{ app.description }}</p>
                @if (!isFeatureEnabled(app.featureKey)) {
                  <p class="text-xs text-amber-600 font-medium mt-0.5">Feature not enabled for this tenant</p>
                }
              </div>
            </div>
            <label class="relative inline-flex items-center" [class.cursor-pointer]="isFeatureEnabled(app.featureKey)" [class.cursor-not-allowed]="!isFeatureEnabled(app.featureKey)">
              <input
                type="checkbox"
                class="sr-only peer"
                [checked]="isBuiltInEnabled(app.id)"
                [disabled]="!isFeatureEnabled(app.featureKey)"
                (change)="toggleBuiltIn(app.id)"
              />
              <div class="w-9 h-5 bg-gray-200 peer-focus:outline-none peer-focus:ring-2 peer-focus:ring-indigo-300 rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-indigo-600 peer-disabled:opacity-50"></div>
            </label>
          </div>
        }
      </div>
    </ui-card>

    <!-- Custom External Apps -->
    <ui-card class="mb-6">
      <div class="px-6 py-4 border-b border-gray-200">
        <div class="flex items-center justify-between">
          <div>
            <h3 class="text-base font-semibold text-gray-900">Custom External Apps</h3>
            <p class="text-sm text-gray-500 mt-1">Add external applications to the workspace app switcher.</p>
          </div>
          @if (!showForm()) {
            <button
              type="button"
              class="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 transition-colors"
              (click)="openAddForm()"
            >
              <ui-icon name="plus" class="h-4 w-4" />
              Add App
            </button>
          }
        </div>
      </div>

      <!-- Custom Apps List -->
      @if (settings().customApps.length > 0 && !showForm()) {
        <div class="divide-y divide-gray-100">
          @for (app of settings().customApps; track app.id) {
            <div class="flex items-center justify-between px-6 py-3">
              <div class="flex items-center gap-3 min-w-0">
                <div class="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0" [class]="getColorBgClass(app.color)">
                  <ui-icon [name]="app.icon" class="h-4 w-4 text-white" />
                </div>
                <div class="min-w-0">
                  <p class="text-sm font-medium text-gray-900">{{ app.name }}</p>
                  <p class="text-xs text-gray-400 truncate">{{ app.externalUrl }}</p>
                </div>
              </div>
              <div class="flex items-center gap-1">
                <button
                  type="button"
                  class="p-1.5 text-gray-400 hover:text-gray-600 rounded-lg hover:bg-gray-100 transition-colors"
                  (click)="editApp(app)"
                >
                  <ui-icon name="pencil" class="h-4 w-4" />
                </button>
                <button
                  type="button"
                  class="p-1.5 text-gray-400 hover:text-red-600 rounded-lg hover:bg-red-50 transition-colors"
                  (click)="deleteApp(app.id)"
                >
                  <ui-icon name="trash" class="h-4 w-4" />
                </button>
              </div>
            </div>
          }
        </div>
      }

      @if (settings().customApps.length === 0 && !showForm()) {
        <div class="px-6 py-8 text-center">
          <ui-icon name="squares-2x2" class="h-8 w-8 text-gray-300 mx-auto mb-2" />
          <p class="text-sm text-gray-400">No custom apps configured</p>
        </div>
      }

      <!-- Add/Edit Form -->
      @if (showForm()) {
        <div class="px-6 py-4 bg-gray-50 border-t border-gray-100">
          <h4 class="text-sm font-medium text-gray-800 mb-3">
            {{ editingAppId() ? 'Edit App' : 'Add External App' }}
          </h4>
          <div class="grid grid-cols-2 gap-4">
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">Name *</label>
              <input
                type="text"
                class="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                placeholder="e.g. Slack"
                [(ngModel)]="formData.name"
              />
            </div>
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">URL *</label>
              <input
                type="url"
                class="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                placeholder="https://example.com"
                [(ngModel)]="formData.externalUrl"
              />
            </div>
            <div class="col-span-2">
              <label class="block text-sm font-medium text-gray-700 mb-1">Description</label>
              <input
                type="text"
                class="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                placeholder="Optional description"
                [(ngModel)]="formData.description"
              />
            </div>
            <div>
              <ui-icon-picker [(value)]="formIcon" label="Icon" />
            </div>
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">Color</label>
              <div class="flex flex-wrap gap-2 mt-1">
                @for (c of appColors; track c.value) {
                  <button
                    type="button"
                    class="w-7 h-7 rounded-lg transition-all"
                    [class]="c.class"
                    [class.ring-2]="formData.color === c.value"
                    [class.ring-offset-2]="formData.color === c.value"
                    [class.ring-indigo-500]="formData.color === c.value"
                    [title]="c.label"
                    (click)="formData.color = c.value"
                  ></button>
                }
              </div>
            </div>
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">Order</label>
              <input
                type="number"
                class="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                [(ngModel)]="formData.order"
              />
            </div>
          </div>
          <div class="flex items-center gap-2 mt-4">
            <button
              type="button"
              class="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 transition-colors disabled:opacity-50"
              [disabled]="!formData.name || !formData.externalUrl"
              (click)="saveApp()"
            >
              {{ editingAppId() ? 'Update' : 'Add' }}
            </button>
            <button
              type="button"
              class="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors"
              (click)="cancelForm()"
            >
              Cancel
            </button>
          </div>
        </div>
      }
    </ui-card>

    <!-- App Links -->
    <ui-card class="mb-6">
      <div class="px-6 py-4 border-b border-gray-200">
        <h3 class="text-base font-semibold text-gray-900">App Links</h3>
        <p class="text-sm text-gray-500 mt-1">Where the workspace sends people for the Admin and Account apps. Leave blank to use the host's configured URLs, or the default paths on the same origin.</p>
      </div>
      <div class="px-6 py-4 grid grid-cols-1 md:grid-cols-2 gap-4">
        @for (link of appLinks; track link.id) {
          <div>
            <label [for]="'app-url-' + link.id" class="block text-sm font-medium text-gray-700 mb-1">{{ link.label }}</label>
            <input
              [id]="'app-url-' + link.id"
              type="url"
              class="block w-full rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              [placeholder]="link.placeholder"
              [ngModel]="appUrl(link.id)"
              (ngModelChange)="setAppUrl(link.id, $event)"
            />
            <p class="mt-1 text-xs text-gray-500">{{ link.hint }}</p>
          </div>
        }
      </div>
    </ui-card>

    <!-- Save Button -->
    <div class="flex justify-end">
      <button
        type="button"
        class="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 transition-colors disabled:opacity-50"
        [disabled]="saving() || !hasChanges()"
        (click)="onSave()"
      >
        @if (saving()) {
          <svg class="animate-spin h-4 w-4" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
        }
        Save Changes
      </button>
    </div>

    @if (error()) {
      <div class="mt-4 p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">
        {{ error() }}
      </div>
    }

    @if (success()) {
      <div class="mt-4 p-3 bg-green-50 border border-green-200 rounded-lg text-sm text-green-700">
        Settings saved successfully.
      </div>
    }
  `,
})
export class WorkspaceAppsTabComponent implements OnInit {
  private readonly tabContext = inject(TAB_CONTEXT);
  private readonly tenantService = inject(TenantService);

  readonly appColors = APP_COLORS;
  readonly appLinks = [
    { id: 'admin', label: 'Admin app URL', placeholder: 'https://admin.example.com', hint: 'Opened from the workspace app switcher. Default: /admin' },
    { id: 'account', label: 'Account app URL', placeholder: 'https://account.example.com', hint: 'Opened from "My account" in the workspace. Default: /account' },
  ];

  readonly settings = signal<WorkspaceAppsSettings>({ builtInApps: {}, customApps: [] });
  readonly features = signal<TenantFeature[]>([]);
  readonly registeredModules = signal<RegisteredModuleInfo[]>([]);

  readonly builtInApps = computed<BuiltInAppDef[]>(() => {
    const features = this.features();
    const enabledKeys = new Set(features.filter(f => f.enabled).map(f => f.key));
    const showAll = features.length === 0;
    return this.registeredModules()
      .filter(m => showAll || enabledKeys.has(m.featureKey))
      .map(m => ({
        id: m.featureKey,
        name: m.displayName,
        description: m.description,
        icon: m.iconName ?? 'cube',
        color: m.color ?? 'slate',
        featureKey: m.featureKey,
      }));
  });
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly hasChanges = signal(false);
  readonly error = signal<string | null>(null);
  readonly success = signal(false);

  readonly showForm = signal(false);
  readonly editingAppId = signal<string | null>(null);

  formData: Partial<CustomWorkspaceApp> = this.getEmptyForm();
  formIcon = 'link';

  ngOnInit(): void {
    const tenantId = this.tabContext.entityId;
    if (tenantId) {
      this.loadSettings(tenantId);
      this.loadFeatures(tenantId);
      this.loadModules();
    }
  }

  private loadSettings(tenantId: string): void {
    this.loading.set(true);
    this.tenantService.getWorkspaceAppsSettings(tenantId).subscribe({
      next: (settings) => {
        this.settings.set(settings);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load workspace apps settings:', err);
        this.loading.set(false);
      },
    });
  }

  private loadFeatures(tenantId: string): void {
    this.tenantService.getFeatures(tenantId).subscribe({
      next: (response) => this.features.set(response.features),
      error: (err) => console.error('Failed to load features:', err),
    });
  }

  private loadModules(): void {
    this.tenantService.getRegisteredModules().subscribe({
      next: (response) => this.registeredModules.set(response.modules),
      error: (err) => console.error('Failed to load registered modules:', err),
    });
  }

  isFeatureEnabled(featureKey: string): boolean {
    return this.isFeatureEnabledFromList(this.features(), featureKey);
  }

  private isFeatureEnabledFromList(features: TenantFeature[], featureKey: string): boolean {
    if (features.length === 0) return true; // No features configured = all allowed
    const feature = features.find(f => f.key === featureKey);
    return feature ? feature.enabled : true;
  }

  isBuiltInEnabled(appId: string): boolean {
    return this.settings().builtInApps[appId] !== false;
  }

  toggleBuiltIn(appId: string): void {
    const current = this.settings();
    const builtInApps = { ...current.builtInApps };
    builtInApps[appId] = !this.isBuiltInEnabled(appId);
    this.settings.set({ ...current, builtInApps });
    this.hasChanges.set(true);
    this.success.set(false);
  }

  appUrl(appId: string): string {
    return this.settings().appUrls?.[appId] ?? '';
  }

  setAppUrl(appId: string, value: string): void {
    const current = this.settings();
    const appUrls = { ...(current.appUrls ?? {}) };
    const trimmed = (value ?? '').trim();
    if (trimmed) {
      appUrls[appId] = trimmed;
    } else {
      delete appUrls[appId];
    }
    this.settings.set({ ...current, appUrls });
    this.hasChanges.set(true);
    this.success.set(false);
  }

  openAddForm(): void {
    this.formData = this.getEmptyForm();
    this.formIcon = 'link';
    this.editingAppId.set(null);
    this.showForm.set(true);
  }

  editApp(app: CustomWorkspaceApp): void {
    this.formData = { ...app };
    this.formIcon = app.icon || 'link';
    this.editingAppId.set(app.id);
    this.showForm.set(true);
  }

  cancelForm(): void {
    this.showForm.set(false);
    this.editingAppId.set(null);
    this.formData = this.getEmptyForm();
  }

  saveApp(): void {
    const { name, externalUrl } = this.formData;
    if (!name || !externalUrl) return;

    const current = this.settings();
    const customApps = [...current.customApps];

    // Sync icon from the picker
    this.formData.icon = this.formIcon;

    if (this.editingAppId()) {
      const idx = customApps.findIndex(a => a.id === this.editingAppId());
      if (idx >= 0) {
        customApps[idx] = { ...customApps[idx], ...this.formData } as CustomWorkspaceApp;
      }
    } else {
      const newApp: CustomWorkspaceApp = {
        id: Math.random().toString(36).substring(2, 10),
        name,
        description: this.formData.description,
        externalUrl,
        icon: this.formIcon,
        color: this.formData.color || 'slate',
        order: this.formData.order || 100,
      };
      customApps.push(newApp);
    }

    this.settings.set({ ...current, customApps });
    this.hasChanges.set(true);
    this.success.set(false);
    this.cancelForm();
  }

  deleteApp(appId: string): void {
    const current = this.settings();
    const customApps = current.customApps.filter(a => a.id !== appId);
    this.settings.set({ ...current, customApps });
    this.hasChanges.set(true);
    this.success.set(false);
  }

  onSave(): void {
    const tenantId = this.tabContext.entityId;
    if (!tenantId) return;

    this.saving.set(true);
    this.error.set(null);
    this.success.set(false);

    this.tenantService.updateWorkspaceAppsSettings(tenantId, this.settings()).subscribe({
      next: (saved) => {
        this.settings.set(saved);
        this.saving.set(false);
        this.hasChanges.set(false);
        this.success.set(true);
      },
      error: (err) => {
        console.error('Failed to save workspace apps settings:', err);
        this.saving.set(false);
        this.error.set('Failed to save workspace apps settings. Please try again.');
      },
    });
  }

  getColorBgClass(color: string): string {
    const colorMap: Record<string, string> = {
      slate: 'bg-gradient-to-br from-slate-500 to-slate-700',
      blue: 'bg-gradient-to-br from-blue-500 to-blue-700',
      green: 'bg-gradient-to-br from-emerald-500 to-emerald-700',
      purple: 'bg-gradient-to-br from-purple-500 to-purple-700',
      orange: 'bg-gradient-to-br from-orange-500 to-orange-700',
      red: 'bg-gradient-to-br from-red-500 to-red-700',
      indigo: 'bg-gradient-to-br from-indigo-500 to-indigo-700',
      teal: 'bg-gradient-to-br from-teal-500 to-teal-700',
      pink: 'bg-gradient-to-br from-pink-500 to-pink-700',
    };
    return colorMap[color] || colorMap['slate'];
  }

  private getEmptyForm(): Partial<CustomWorkspaceApp> {
    return { name: '', description: '', externalUrl: '', icon: 'link', color: 'slate', order: 100 };
  }
}
