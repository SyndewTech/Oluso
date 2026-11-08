import { Injectable, computed, inject, signal } from '@angular/core';
import { LocaleService, type CurrencyInfo } from './locale.service';
import { formatMinorUnits, fromMinorUnits, toMinorUnits } from '../utils/minor-units';

export interface CurrencyOption extends CurrencyInfo {
  /** "NGN — Nigerian Naira", ready for a select option. */
  label: string;
}

/**
 * The platform's currency list (GET api/locale/currencies), loaded once and
 * shared by every currency picker so no module keeps a copy of its own. Empty
 * until the first load completes; a failed load stays empty and is logged —
 * there is deliberately no hardcoded fallback list.
 */
@Injectable({ providedIn: 'root' })
export class CurrencyCatalogService {
  private readonly locale = inject(LocaleService);
  private readonly _currencies = signal<CurrencyOption[]>([]);
  private readonly _loaded = signal(false);
  private loading = false;

  /** Every supported currency, frequently used ones first. */
  readonly currencies = this._currencies.asReadonly();
  readonly loaded = this._loaded.asReadonly();
  /** The frequently used subset, for compact pickers. */
  readonly common = computed(() => this._currencies().filter((c) => c.common));

  constructor() {
    this.load();
  }

  load(): void {
    if (this.loading) return;
    this.loading = true;
    this.locale.getSupportedCurrencies().subscribe({
      next: (list) => {
        this._currencies.set(list.map((c) => ({ ...c, label: `${c.code} — ${c.name}` })));
        this._loaded.set(true);
        this.loading = false;
      },
      error: (err) => {
        console.error('Failed to load the platform currency list', err);
        this.loading = false;
      },
    });
  }

  /**
   * The currency's ISO 4217 decimal places (0 for JPY, 3 for KWD), from the platform list.
   * Throws for an unknown code or before the list has loaded: there is no default exponent.
   */
  exponent(code: string): number {
    if (!this._loaded()) throw new Error(`The currency list hasn't loaded yet, so ${code}'s minor units are unknown.`);
    const currency = this.find(code);
    if (!currency) throw new Error(`'${code}' is not a supported currency code.`);
    return currency.decimalPlaces;
  }

  /**
   * The currency's decimal places, or null while the list is still loading or for an unknown code.
   * For display paths that render nothing until the exponent is known rather than guess one.
   */
  tryExponent(code: string | null | undefined): number | null {
    return this._loaded() ? (this.find(code)?.decimalPlaces ?? null) : null;
  }

  /** An amount in major units (12.345) as whole minor units (12345 for KWD), rounded half away from zero. */
  toMinor(amount: number, code: string): number {
    return toMinorUnits(amount, this.exponent(code));
  }

  /** Whole minor units as an amount in major units. */
  fromMinor(minorUnits: number, code: string): number {
    return fromMinorUnits(minorUnits, this.exponent(code));
  }

  /** Whether amounts can be shown or entered in the currency: it's set and its decimal places are known. */
  isKnown(code: string | null | undefined): boolean {
    return this.tryExponent(code) != null;
  }

  /** The organisation's currency, or null while it isn't set or its decimal places aren't loaded. */
  tenantCurrency(): string | null {
    const code = this.locale.currencyCode();
    return code && this.isKnown(code) ? code : null;
  }

  /**
   * Minor units shown in the currency at its own decimal places, in the tenant's locale; empty while the amount,
   * the currency or its decimal places aren't known. `wholeUnits` drops the decimals for summary figures.
   */
  formatMinor(minorUnits: number | null | undefined, code: string | null | undefined, options: { wholeUnits?: boolean } = {}): string {
    return formatMinorUnits(minorUnits, code, this.tryExponent(code), this.locale.locale(), options.wholeUnits ?? false);
  }

  /**
   * An amount already in major units (12.345) shown in the currency at its own decimal places, in the tenant's
   * locale; empty while the currency or its decimal places aren't known. The TypeScript twin of `localeCurrency`.
   */
  formatMajor(amount: number | null | undefined, code: string | null | undefined, options: { wholeUnits?: boolean } = {}): string {
    const exponent = this.tryExponent(code);
    if (amount == null || !code || exponent == null) return '';
    const digits = options.wholeUnits ? 0 : exponent;
    return new Intl.NumberFormat(this.locale.locale(), {
      style: 'currency',
      currency: code,
      currencyDisplay: 'narrowSymbol',
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    }).format(amount);
  }

  /** Minor units as major units for a form field; null when there is no amount or the currency isn't known. */
  tryFromMinor(minorUnits: number | null | undefined, code: string | null | undefined): number | null {
    const exponent = this.tryExponent(code);
    return minorUnits == null || exponent == null ? null : fromMinorUnits(minorUnits, exponent);
  }

  /**
   * A form field's major units as minor units of the currency; undefined when the field is empty or not a number,
   * or the currency or its decimal places aren't known.
   */
  tryToMinor(major: number | string | null | undefined, code: string | null | undefined): number | undefined {
    if (major === null || major === undefined || major === '') return undefined;
    const value = Number(major);
    const exponent = this.tryExponent(code);
    if (!Number.isFinite(value) || exponent == null) return undefined;
    return toMinorUnits(value, exponent);
  }

  find(code: string | null | undefined): CurrencyOption | undefined {
    if (!code) return undefined;
    const upper = code.toUpperCase();
    return this._currencies().find((c) => c.code === upper);
  }
}
