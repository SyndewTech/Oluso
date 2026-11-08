import { Pipe, PipeTransform, inject } from '@angular/core';
import { LocaleService } from '../services/locale.service';

/**
 * ISO currency code → its narrow symbol for the tenant locale (GBP → £, NGN → ₦).
 * Falls back to the code itself when the locale has no symbol for it. With no
 * argument, uses the tenant's currency. Meant for input prefixes and labels such
 * as "Credit Limit (£)" so forms never hardcode a currency glyph.
 */
@Pipe({ name: 'currencySymbol', standalone: true, pure: false })
export class CurrencySymbolPipe implements PipeTransform {
  private readonly locale = inject(LocaleService, { optional: true });

  transform(currencyCode?: string | null): string {
    const code = currencyCode || this.locale?.currencyCode();
    if (!code) return '';
    try {
      const part = new Intl.NumberFormat(this.locale?.locale() || undefined, {
        style: 'currency',
        currency: code,
        currencyDisplay: 'narrowSymbol',
      })
        .formatToParts(0)
        .find((p) => p.type === 'currency');
      return part?.value || code;
    } catch {
      return code;
    }
  }
}
