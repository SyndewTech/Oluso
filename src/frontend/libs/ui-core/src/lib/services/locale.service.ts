import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export interface LocaleSettings {
  currencyCode: string;
  currencySymbol: string;
  dateFormat: string;
  numberFormat: string;
  locale: string;
  timezone?: string;
}

export interface CurrencyInfo {
  code: string;
  name: string;
  symbol: string;
  decimalPlaces: number;
  /** Frequently used; listed first in pickers. */
  common?: boolean;
}

export interface CountryInfo {
  /** ISO 3166-1 alpha-2 */
  code: string;
  name: string;
  /** Calling code without the leading "+" */
  dialCode: string;
  flag: string;
}

export interface LocaleInfo {
  code: string;
  name: string;
  dateFormat: string;
  numberFormat: string;
}

export const DEFAULT_LOCALE_SETTINGS: LocaleSettings = {
  currencyCode: '',
  currencySymbol: '',
  dateFormat: 'MM/DD/YYYY',
  numberFormat: '1,234.56',
  locale: 'en-US',
};

export type LocaleDateInput = string | number | Date | null | undefined;

/** Display styles, named like Angular's DatePipe presets plus two short forms used in list cells. */
export type LocaleDateStyle =
  | 'shortDate' | 'mediumDate' | 'longDate' | 'fullDate'
  | 'shortTime' | 'mediumTime'
  | 'short' | 'medium' | 'long' | 'full'
  | 'monthDay' | 'monthDayTime'
  | 'weekdayDate' | 'weekdayDateTime' | 'weekdayLongDate';

const DATE_STYLE_OPTIONS: Record<LocaleDateStyle, Intl.DateTimeFormatOptions> = {
  shortDate: { year: 'numeric', month: '2-digit', day: '2-digit' },
  mediumDate: { year: 'numeric', month: 'short', day: 'numeric' },
  longDate: { year: 'numeric', month: 'long', day: 'numeric' },
  fullDate: { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' },
  shortTime: { hour: 'numeric', minute: '2-digit' },
  mediumTime: { hour: 'numeric', minute: '2-digit', second: '2-digit' },
  short: { year: 'numeric', month: '2-digit', day: '2-digit', hour: 'numeric', minute: '2-digit' },
  medium: { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' },
  long: { year: 'numeric', month: 'long', day: 'numeric', hour: 'numeric', minute: '2-digit', timeZoneName: 'short' },
  full: { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric', hour: 'numeric', minute: '2-digit', timeZoneName: 'short' },
  monthDay: { month: 'short', day: 'numeric' },
  monthDayTime: { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' },
  weekdayDate: { weekday: 'short', month: 'short', day: 'numeric' },
  weekdayDateTime: { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' },
  weekdayLongDate: { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' },
};

const CALENDAR_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * Locale Service
 *
 * Signal-based locale settings management, shared across all apps.
 * Uses the /api/locale endpoint which is accessible to any authenticated user.
 *
 * Admin-specific CRUD (get/update by tenantId) is in TenantService (@oluso/admin-ui).
 */
@Injectable({ providedIn: 'root' })
export class LocaleService {
  private readonly http = inject(HttpClient);
  private readonly _settings = signal<LocaleSettings>(DEFAULT_LOCALE_SETTINGS);

  private _apiBaseUrl = '/api/locale';

  readonly settings = this._settings.asReadonly();
  readonly currencyCode = computed(() => this._settings().currencyCode);
  readonly currencySymbol = computed(() => this._settings().currencySymbol);
  readonly locale = computed(() => this._settings().locale);
  readonly dateFormat = computed(() => this._settings().dateFormat);
  readonly numberFormat = computed(() => this._settings().numberFormat);
  private readonly _displayTimeZone = signal<string | null>(null);

  /**
   * The time zone times are shown in (IANA): the zone an app has set for its own pages (see
   * {@link setDisplayTimeZone}), else the tenant's pinned zone. Undefined means "the viewer's own zone".
   */
  readonly timezone = computed(() => this._displayTimeZone() ?? this._settings().timezone);

  /**
   * Shows times in <paramref name="zone"/> while an app's pages are open, e.g. a clinic whose day and times follow its own
   * zone; null returns to the tenant's zone. The app clears it when the user leaves its pages.
   */
  setDisplayTimeZone(zone: string | null): void {
    this._displayTimeZone.set(zone);
  }

  /**
   * Formats a date for display using the tenant locale and, for date-only
   * styles, the tenant's configured day/month/year order. Instants (ISO
   * strings with a time, Date objects, epoch numbers) are shown in the tenant
   * time zone when one is set, otherwise in the viewer's browser zone.
   * Calendar dates ("2026-09-20", the backend's DateOnly) are shown as that
   * day and are never shifted by a time zone.
   */
  formatDate(value: LocaleDateInput, style: LocaleDateStyle = 'mediumDate', timeZone?: string): string {
    if (value === null || value === undefined || value === '') return '';

    const calendarDate = typeof value === 'string' ? CALENDAR_DATE.exec(value) : null;
    const date = calendarDate
      ? new Date(Number(calendarDate[1]), Number(calendarDate[2]) - 1, Number(calendarDate[3]))
      : new Date(value);
    if (Number.isNaN(date.getTime())) return '';

    const zone = calendarDate ? undefined : timeZone || this.timezone() || undefined;
    const options: Intl.DateTimeFormatOptions = { ...DATE_STYLE_OPTIONS[style], timeZone: zone };
    const locale = this.locale();

    if (style === 'shortDate' || style === 'short') {
      const datePart = this.shortDatePart(date, locale, zone);
      if (style === 'shortDate') return datePart;
      const timePart = new Intl.DateTimeFormat(locale, { ...DATE_STYLE_OPTIONS.shortTime, timeZone: zone }).format(date);
      return `${datePart}, ${timePart}`;
    }

    return new Intl.DateTimeFormat(locale, options).format(date);
  }

  /**
   * Money formatter in the tenant locale. The currency is the one given, else the
   * tenant's; when neither is set the amount is formatted as a plain number
   * rather than under a made-up currency.
   */
  moneyFormat(currencyCode?: string | null, options: Intl.NumberFormatOptions = {}): Intl.NumberFormat {
    const currency = currencyCode || this.currencyCode();
    if (!currency) {
      const plain: Intl.NumberFormatOptions = { ...options };
      delete plain.style;
      delete plain.currency;
      delete plain.currencyDisplay;
      plain.minimumFractionDigits = options.minimumFractionDigits ?? Math.min(2, options.maximumFractionDigits ?? 2);
      plain.maximumFractionDigits = options.maximumFractionDigits ?? Math.max(2, plain.minimumFractionDigits);
      return new Intl.NumberFormat(this.locale(), plain);
    }
    return new Intl.NumberFormat(this.locale(), { currencyDisplay: 'narrowSymbol', ...options, style: 'currency', currency });
  }

  /** Numeric day/month/year in the tenant's configured order (MM/DD/YYYY, DD/MM/YYYY, YYYY-MM-DD…). */
  private shortDatePart(date: Date, locale: string, zone: string | undefined): string {
    const parts = new Intl.DateTimeFormat(locale, { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: zone }).formatToParts(date);
    const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '';
    const pattern = this.dateFormat();
    if (!pattern) return new Intl.DateTimeFormat(locale, { ...DATE_STYLE_OPTIONS.shortDate, timeZone: zone }).format(date);
    return pattern
      .replace(/YYYY/i, get('year'))
      .replace(/YY/i, get('year').slice(-2))
      .replace(/MM/, get('month'))
      .replace(/DD/i, get('day'));
  }

  /**
   * Configure the API base URL for the locale endpoint.
   * e.g., 'http://localhost:5050/api/locale'
   */
  setApiBaseUrl(url: string): void {
    this._apiBaseUrl = url;
  }

  /** Cache the current locale settings (called on app init or after admin save) */
  setCurrentSettings(settings: LocaleSettings): void {
    this._settings.set(settings);
  }

  /**
   * Load the current tenant's locale settings.
   * Tenant is resolved from the JWT token on the backend.
   */
  loadCurrentLocale(): Observable<LocaleSettings> {
    return this.http.get<LocaleSettings>(this._apiBaseUrl).pipe(
      tap(settings => this._settings.set(settings))
    );
  }

  /** Get supported currencies (static reference data) */
  getSupportedCurrencies(): Observable<CurrencyInfo[]> {
    return this.http.get<CurrencyInfo[]>(`${this._apiBaseUrl}/currencies`);
  }

  /** Get supported locales (static reference data) */
  getSupportedLocales(): Observable<LocaleInfo[]> {
    return this.http.get<LocaleInfo[]>(`${this._apiBaseUrl}/locales`);
  }

  /** Get supported countries with calling codes (static reference data) */
  getSupportedCountries(): Observable<CountryInfo[]> {
    return this.http.get<CountryInfo[]>(`${this._apiBaseUrl}/countries`);
  }
}
