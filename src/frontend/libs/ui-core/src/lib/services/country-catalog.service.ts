import { Injectable, inject, signal } from '@angular/core';
import { LocaleService, type CountryInfo } from './locale.service';

export interface CountryOption extends CountryInfo {
  /** "🇳🇬 Nigeria (+234)", ready for a select option. */
  label: string;
}

/**
 * The platform's country list (GET api/locale/countries), loaded once and shared
 * by every country or dial-code picker. Empty until the first load completes; a
 * failed load stays empty and is logged — no hardcoded fallback list.
 */
@Injectable({ providedIn: 'root' })
export class CountryCatalogService {
  private readonly locale = inject(LocaleService);
  private readonly _countries = signal<CountryOption[]>([]);
  private readonly _loaded = signal(false);
  private loading = false;

  readonly countries = this._countries.asReadonly();
  readonly loaded = this._loaded.asReadonly();

  constructor() {
    this.load();
  }

  load(): void {
    if (this.loading) return;
    this.loading = true;
    this.locale.getSupportedCountries().subscribe({
      next: (list) => {
        this._countries.set(list.map((c) => ({ ...c, label: `${c.flag} ${c.name} (+${c.dialCode})` })));
        this._loaded.set(true);
        this.loading = false;
      },
      error: (err) => {
        console.error('Failed to load the platform country list', err);
        this.loading = false;
      },
    });
  }

  find(code: string | null | undefined): CountryOption | undefined {
    if (!code) return undefined;
    const upper = code.toUpperCase();
    return this._countries().find((c) => c.code === upper);
  }
}
