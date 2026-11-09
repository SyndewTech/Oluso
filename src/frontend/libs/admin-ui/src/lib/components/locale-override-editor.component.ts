import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, IconComponent } from '@syndew/oluso-ui-core';
import { LocaleOverridesService } from '../services/locale-overrides.service';

interface StringEntry {
  key: string;
  defaultValue: string;
  overrideValue: string;
  isOverridden: boolean;
}

@Component({
  selector: 'admin-locale-override-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, IconComponent],
  template: `
    <div class="space-y-4">
      <!-- Top bar -->
      <div class="flex items-center gap-3">
        <select
          [ngModel]="selectedLocale()"
          (ngModelChange)="onLocaleChange($event)"
          class="px-3 py-1.5 text-sm border border-gray-300 rounded-md bg-white"
        >
          @for (loc of availableLocales(); track loc) {
            <option [value]="loc">{{ loc }}</option>
          }
        </select>
        <div class="flex-1 relative">
          <input
            type="text"
            [ngModel]="searchQuery()"
            (ngModelChange)="searchQuery.set($event)"
            placeholder="Search keys or values..."
            class="w-full pl-8 pr-3 py-1.5 text-sm border border-gray-300 rounded-md"
          />
          <svg class="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
          </svg>
        </div>
        @if (overriddenCount() > 0) {
          <span class="px-2 py-0.5 text-xs font-medium rounded-full bg-primary-100 text-primary-700">
            {{ overriddenCount() }} override{{ overriddenCount() === 1 ? '' : 's' }}
          </span>
        }
        <label class="flex items-center gap-1.5 text-xs text-gray-600 whitespace-nowrap">
          <input
            type="checkbox"
            [ngModel]="showOverridesOnly()"
            (ngModelChange)="showOverridesOnly.set($event)"
            class="rounded border-gray-300 text-primary-600"
          />
          Overrides only
        </label>
      </div>

      <!-- Loading / Error -->
      @if (loading()) {
        <div class="text-center py-8">
          <p class="text-sm text-gray-400">Loading strings...</p>
        </div>
      } @else if (error()) {
        <div class="p-3 bg-red-50 border border-red-200 rounded-md">
          <p class="text-sm text-red-600">{{ error() }}</p>
        </div>
      } @else {
        <!-- String list -->
        <div class="border border-gray-200 rounded-md overflow-hidden">
          <div class="grid grid-cols-[minmax(180px,1fr)_2fr_2fr_40px] gap-0 bg-gray-50 border-b border-gray-200 px-3 py-1.5">
            <span class="text-[11px] font-medium text-gray-500 uppercase">Key</span>
            <span class="text-[11px] font-medium text-gray-500 uppercase">Default</span>
            <span class="text-[11px] font-medium text-gray-500 uppercase">Override</span>
            <span></span>
          </div>
          <div class="max-h-[500px] overflow-y-auto divide-y divide-gray-100">
            @for (entry of filteredEntries(); track entry.key) {
              <div
                class="grid grid-cols-[minmax(180px,1fr)_2fr_2fr_40px] gap-0 px-3 py-1.5 items-center hover:bg-gray-50"
                [class.border-l-2]="entry.isOverridden"
                [class.border-l-green-500]="entry.isOverridden"
              >
                <span class="text-xs font-mono text-gray-600 truncate pr-2" [title]="entry.key">{{ entry.key }}</span>
                <span class="text-xs text-gray-400 truncate pr-2" [title]="entry.defaultValue">{{ entry.defaultValue }}</span>
                <input
                  type="text"
                  [ngModel]="entry.overrideValue"
                  (ngModelChange)="updateOverride(entry.key, $event)"
                  [placeholder]="entry.defaultValue"
                  class="w-full px-2 py-0.5 text-xs border border-gray-200 rounded focus:border-primary-400 focus:ring-1 focus:ring-primary-200"
                  [class.border-green-300]="entry.isOverridden"
                />
                @if (entry.isOverridden) {
                  <button
                    type="button"
                    (click)="resetKey(entry.key)"
                    class="text-gray-400 hover:text-red-500 text-xs px-1 justify-self-center"
                    title="Reset to default"
                  >
                    ×
                  </button>
                } @else {
                  <span></span>
                }
              </div>
            }
            @if (filteredEntries().length === 0) {
              <div class="px-3 py-6 text-center">
                <p class="text-sm text-gray-400">
                  @if (searchQuery() || showOverridesOnly()) {
                    No strings match the current filter.
                  } @else {
                    No strings available for this locale.
                  }
                </p>
              </div>
            }
          </div>
        </div>

        <!-- Save bar -->
        <div class="flex items-center justify-between">
          <p class="text-xs text-gray-400">
            {{ filteredEntries().length }} of {{ allEntries().length }} strings shown
          </p>
          <button
            type="button"
            [disabled]="!hasChanges() || saving()"
            (click)="onSave()"
            class="px-4 py-1.5 text-sm font-medium rounded-md text-white bg-primary-600 hover:bg-primary-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {{ saving() ? 'Saving...' : 'Save Overrides' }}
          </button>
        </div>
      }
    </div>
  `,
})
export class LocaleOverrideEditorComponent implements OnInit {
  private readonly service = inject(LocaleOverridesService);

  readonly scope = input.required<'tenant' | 'journey'>();
  readonly scopeId = input.required<string>();

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly hasChanges = signal(false);

  readonly availableLocales = signal<string[]>(['en']);
  readonly selectedLocale = signal<string>('en');
  readonly searchQuery = signal<string>('');
  readonly showOverridesOnly = signal(false);

  readonly defaults = signal<Record<string, string>>({});
  readonly overrides = signal<Record<string, string>>({});
  readonly editBuffer = signal<Record<string, string>>({});

  readonly allEntries = computed<StringEntry[]>(() => {
    const defs = this.defaults();
    const edits = this.editBuffer();
    return Object.entries(defs).map(([key, defaultValue]) => ({
      key,
      defaultValue,
      overrideValue: edits[key] ?? '',
      isOverridden: !!edits[key] && edits[key] !== defaultValue,
    }));
  });

  readonly filteredEntries = computed<StringEntry[]>(() => {
    const query = this.searchQuery().toLowerCase();
    const overridesOnly = this.showOverridesOnly();
    return this.allEntries().filter((entry) => {
      if (overridesOnly && !entry.isOverridden) return false;
      if (!query) return true;
      return (
        entry.key.toLowerCase().includes(query) ||
        entry.defaultValue.toLowerCase().includes(query) ||
        entry.overrideValue.toLowerCase().includes(query)
      );
    });
  });

  readonly overriddenCount = computed(() => this.allEntries().filter((e) => e.isOverridden).length);

  ngOnInit(): void {
    this.service.getAvailableLocales().subscribe({
      next: (locales) => {
        this.availableLocales.set(locales.length > 0 ? locales : ['en']);
        this.loadStrings(this.selectedLocale());
      },
      error: () => this.loadStrings(this.selectedLocale()),
    });
  }

  onLocaleChange(locale: string): void {
    this.selectedLocale.set(locale);
    this.loadStrings(locale);
  }

  private loadStrings(locale: string): void {
    this.loading.set(true);
    this.error.set(null);

    const data$ =
      this.scope() === 'tenant'
        ? this.service.getTenantOverrides(locale)
        : this.service.getJourneyOverrides(this.scopeId(), locale);

    data$.subscribe({
      next: (response) => {
        this.defaults.set(response.defaults);
        this.overrides.set(response.overrides);
        this.editBuffer.set({ ...response.overrides });
        this.loading.set(false);
        this.hasChanges.set(false);
      },
      error: (err) => {
        this.error.set(err?.message ?? 'Failed to load strings');
        this.loading.set(false);
      },
    });
  }

  updateOverride(key: string, value: string): void {
    this.editBuffer.update((buf) => {
      const copy = { ...buf };
      if (value && value !== this.defaults()[key]) {
        copy[key] = value;
      } else {
        delete copy[key];
      }
      return copy;
    });
    this.hasChanges.set(true);
  }

  resetKey(key: string): void {
    this.editBuffer.update((buf) => {
      const copy = { ...buf };
      delete copy[key];
      return copy;
    });
    this.hasChanges.set(true);
  }

  onSave(): void {
    this.saving.set(true);
    const cleaned: Record<string, string> = {};
    const defs = this.defaults();
    for (const [key, value] of Object.entries(this.editBuffer())) {
      if (value && value !== defs[key]) {
        cleaned[key] = value;
      }
    }

    const save$ =
      this.scope() === 'tenant'
        ? this.service.saveTenantOverrides(this.selectedLocale(), cleaned)
        : this.service.saveJourneyOverrides(this.scopeId(), this.selectedLocale(), cleaned);

    save$.subscribe({
      next: () => {
        this.overrides.set({ ...cleaned });
        this.saving.set(false);
        this.hasChanges.set(false);
      },
      error: (err) => {
        this.error.set(err?.message ?? 'Failed to save overrides');
        this.saving.set(false);
      },
    });
  }
}
