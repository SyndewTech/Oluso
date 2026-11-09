import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardComponent, ButtonComponent, IconComponent, TAB_CONTEXT } from '@syndew/oluso-ui-core';
import { AuthStore } from '@syndew/oluso-auth';
import {
  LocaleService,
  LocaleSettings,
  CurrencyInfo,
  LocaleInfo,
  DEFAULT_LOCALE_SETTINGS,
} from '@syndew/oluso-ui-core';
import { TenantService } from '../../services/tenant.service';

const COMMON_TIMEZONES = [
  'UTC',
  'America/New_York',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Toronto',
  'America/Sao_Paulo',
  'America/Mexico_City',
  'Europe/London',
  'Europe/Paris',
  'Europe/Berlin',
  'Africa/Lagos',
  'Africa/Nairobi',
  'Africa/Johannesburg',
  'Africa/Accra',
  'Asia/Dubai',
  'Asia/Kolkata',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Asia/Shanghai',
  'Australia/Sydney',
  'Pacific/Auckland',
];

const DATE_FORMATS = [
  'MM/DD/YYYY',
  'DD/MM/YYYY',
  'YYYY-MM-DD',
  'DD.MM.YYYY',
  'YYYY/MM/DD',
];

const NUMBER_FORMATS = [
  '1,234.56',
  '1.234,56',
  '1 234,56',
  '1,23,456.78',
];

@Component({
  selector: 'admin-locale-settings-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, IconComponent],
  template: `
    <div class="space-y-6">
      @if (loading()) {
        <div class="flex items-center justify-center h-32">
          <div class="text-gray-500">Loading locale settings...</div>
        </div>
      } @else {
        <div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <!-- Region & Currency -->
          <ui-card>
            <div class="px-4 py-5 sm:p-6 space-y-4">
              <div class="flex items-center gap-2 mb-4">
                <ui-icon name="globe-alt" class="h-5 w-5 text-gray-400" />
                <h3 class="text-lg font-medium text-gray-900">Region & Currency</h3>
              </div>
              <p class="text-sm text-gray-500 -mt-2 mb-4">Set your organization's primary locale and currency</p>

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Region / Locale</label>
                <select
                  [ngModel]="formData().locale"
                  (ngModelChange)="onLocaleChange($event)"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-primary-500 focus:border-primary-500"
                >
                  @for (loc of locales(); track loc.code) {
                    <option [ngValue]="loc.code">{{ loc.name }} ({{ loc.code }})</option>
                  }
                </select>
                <p class="mt-1 text-xs text-gray-500">Selecting a locale auto-fills date and number formats</p>
              </div>

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Currency</label>
                <select
                  [ngModel]="formData().currencyCode"
                  (ngModelChange)="onCurrencyChange($event)"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-primary-500 focus:border-primary-500"
                >
                  @for (cur of currencies(); track cur.code) {
                    <option [ngValue]="cur.code">{{ cur.symbol }} {{ cur.code }} — {{ cur.name }}</option>
                  }
                </select>
              </div>

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Timezone</label>
                <select
                  [ngModel]="formData().timezone"
                  (ngModelChange)="onFieldChange('timezone', $event)"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-primary-500 focus:border-primary-500"
                >
                  <option [ngValue]="null">Auto-detect</option>
                  @for (tz of timezones; track tz) {
                    <option [ngValue]="tz">{{ tz }}</option>
                  }
                </select>
              </div>
            </div>
          </ui-card>

          <!-- Formats -->
          <ui-card>
            <div class="px-4 py-5 sm:p-6 space-y-4">
              <div class="flex items-center gap-2 mb-4">
                <ui-icon name="calculator" class="h-5 w-5 text-gray-400" />
                <h3 class="text-lg font-medium text-gray-900">Formats</h3>
              </div>
              <p class="text-sm text-gray-500 -mt-2 mb-4">Configure date and number display formats</p>

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Date Format</label>
                <select
                  [ngModel]="formData().dateFormat"
                  (ngModelChange)="onFieldChange('dateFormat', $event)"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-primary-500 focus:border-primary-500"
                >
                  @for (fmt of dateFormats; track fmt) {
                    <option [ngValue]="fmt">{{ fmt }}</option>
                  }
                </select>
              </div>

              <div>
                <label class="block text-sm font-medium text-gray-700 mb-1">Number Format</label>
                <select
                  [ngModel]="formData().numberFormat"
                  (ngModelChange)="onFieldChange('numberFormat', $event)"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-primary-500 focus:border-primary-500"
                >
                  @for (fmt of numberFormats; track fmt) {
                    <option [ngValue]="fmt">{{ fmt }}</option>
                  }
                </select>
              </div>
            </div>
          </ui-card>

          <!-- Live Preview -->
          <ui-card class="lg:col-span-2">
            <div class="px-4 py-5 sm:p-6 space-y-4">
              <div class="flex items-center gap-2 mb-4">
                <ui-icon name="eye" class="h-5 w-5 text-gray-400" />
                <h3 class="text-lg font-medium text-gray-900">Preview</h3>
              </div>
              <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div class="bg-gray-50 rounded-lg p-4">
                  <p class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">Currency</p>
                  <p class="text-lg font-semibold text-gray-900">{{ previewCurrency() }}</p>
                </div>
                <div class="bg-gray-50 rounded-lg p-4">
                  <p class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">Date</p>
                  <p class="text-lg font-semibold text-gray-900">{{ previewDate() }}</p>
                </div>
                <div class="bg-gray-50 rounded-lg p-4">
                  <p class="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">Number</p>
                  <p class="text-lg font-semibold text-gray-900">{{ previewNumber() }}</p>
                </div>
              </div>
            </div>
          </ui-card>
        </div>

        <!-- Actions -->
        <div class="flex justify-end gap-2">
          @if (hasChanges()) {
            <span class="text-sm text-gray-500 self-center mr-2">You have unsaved changes</span>
          }
          <ui-button (clicked)="onSave()" [disabled]="!hasChanges() || saving()">
            {{ saving() ? 'Saving...' : 'Save Settings' }}
          </ui-button>
        </div>
      }

      @if (error()) {
        <div class="rounded-md border border-red-200 bg-red-50 p-4">
          <p class="text-sm text-red-700">{{ error() }}</p>
        </div>
      }
    </div>
  `,
})
export class LocaleSettingsTabComponent implements OnInit {
  private readonly tabContext = inject(TAB_CONTEXT);
  private readonly localeService = inject(LocaleService);
  private readonly tenantService = inject(TenantService);
  private readonly authStore = inject(AuthStore);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly hasChanges = signal(false);
  readonly error = signal<string | null>(null);

  readonly currencies = signal<CurrencyInfo[]>([]);
  readonly locales = signal<LocaleInfo[]>([]);
  readonly formData = signal<LocaleSettings>({ ...DEFAULT_LOCALE_SETTINGS });

  readonly timezones = COMMON_TIMEZONES;
  readonly dateFormats = DATE_FORMATS;
  readonly numberFormats = NUMBER_FORMATS;

  readonly previewCurrency = computed(() => {
    const data = this.formData();
    try {
      return new Intl.NumberFormat(data.locale, {
        style: 'currency',
        currency: data.currencyCode,
      }).format(1500.5);
    } catch {
      return `${data.currencySymbol}1,500.50`;
    }
  });

  readonly previewDate = computed(() => {
    const data = this.formData();
    try {
      return new Intl.DateTimeFormat(data.locale).format(new Date(2025, 0, 26));
    } catch {
      return '01/26/2025';
    }
  });

  readonly previewNumber = computed(() => {
    const data = this.formData();
    try {
      return new Intl.NumberFormat(data.locale).format(1234567.89);
    } catch {
      return '1,234,567.89';
    }
  });

  /**
   * Resolves the tenant ID from tab context.
   * Returns null when no explicit tenant (e.g., global settings with entityId='server').
   */
  private get tenantId(): string | null {
    const id = this.tabContext.entityId;
    if (!id || id === 'server') {
      return this.authStore.currentTenantId() ?? null;
    }
    return id;
  }

  ngOnInit(): void {
    this.loadData();
  }

  private loadData(): void {
    this.loading.set(true);

    // Load reference data
    this.localeService.getSupportedCurrencies().subscribe({
      next: (currencies) => this.currencies.set(currencies),
      error: (err) => console.error('Failed to load currencies:', err),
    });

    this.localeService.getSupportedLocales().subscribe({
      next: (locales) => this.locales.set(locales),
      error: (err) => console.error('Failed to load locales:', err),
    });

    // Load current settings — use tenant-specific admin API when we have a tenantId,
    // otherwise fall back to /api/locale which resolves tenant from JWT
    const tid = this.tenantId;
    const settings$ = tid
      ? this.tenantService.getLocaleSettings(tid)
      : this.localeService.loadCurrentLocale();

    settings$.subscribe({
      next: (settings) => {
        this.formData.set({ ...settings });
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load locale settings:', err);
        this.loading.set(false);
      },
    });
  }

  onLocaleChange(localeCode: string): void {
    // Auto-fill date/number format from locale reference data
    const localeRef = this.locales().find((l) => l.code === localeCode);
    this.formData.update((data) => ({
      ...data,
      locale: localeCode,
      ...(localeRef && {
        dateFormat: localeRef.dateFormat,
        numberFormat: localeRef.numberFormat,
      }),
    }));
    this.hasChanges.set(true);
  }

  onCurrencyChange(currencyCode: string): void {
    const currencyRef = this.currencies().find((c) => c.code === currencyCode);
    this.formData.update((data) => ({
      ...data,
      currencyCode,
      currencySymbol: currencyRef?.symbol ?? data.currencySymbol,
    }));
    this.hasChanges.set(true);
  }

  onFieldChange(field: keyof LocaleSettings, value: string): void {
    this.formData.update((data) => ({ ...data, [field]: value }));
    this.hasChanges.set(true);
  }

  onSave(): void {
    const tid = this.tenantId;
    if (!tid) {
      this.error.set('No tenant selected. Please select a tenant first.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    this.tenantService.updateLocaleSettings(tid, this.formData()).subscribe({
      next: (settings) => {
        this.formData.set({ ...settings });
        this.localeService.setCurrentSettings(settings);
        this.saving.set(false);
        this.hasChanges.set(false);
      },
      error: (err) => {
        console.error('Failed to save locale settings:', err);
        this.error.set('Failed to save locale settings. Please try again.');
        this.saving.set(false);
      },
    });
  }
}
