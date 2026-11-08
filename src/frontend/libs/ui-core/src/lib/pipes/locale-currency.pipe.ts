import { Pipe, PipeTransform, inject } from '@angular/core';
import { LocaleService } from '../services/locale.service';
import { CurrencyCatalogService } from '../services/currency-catalog.service';

/**
 * An amount in major units (12.345) shown in a currency, in the tenant's locale:
 * `amount | localeCurrency:'KWD'` is "KWD 12.345", `amount | localeCurrency:'JPY'` is "¥12".
 *
 * Written without a currency (`amount | localeCurrency`, or with only digits: `amount | localeCurrency:'1.0-0'`) it
 * uses the organisation's currency. Written with a currency argument it uses only that one: a record whose currency
 * turns out null renders nothing rather than silently taking the organisation's (the same rule as `minorUnits`).
 *
 * The decimal places are the currency's own unless a digits argument ('1.0-0' for whole-unit summaries) says
 * otherwise; a missing amount shows as zero. Until the currency and its decimal places are known it renders nothing
 * rather than guess. For amounts held in minor units use the `minorUnits` pipe.
 */
@Pipe({ name: 'localeCurrency', standalone: true, pure: false })
export class LocaleCurrencyPipe implements PipeTransform {
  private readonly localeService = inject(LocaleService);
  private readonly currencies = inject(CurrencyCatalogService);

  transform(value: number | undefined | null, ...args: [currencyOrDigits?: string | null, digitsInfo?: string]): string {
    let currency: string | null | undefined;
    let digits: string | undefined;
    if (args.length === 0) {
      currency = this.localeService.currencyCode();
    } else if (typeof args[0] === 'string' && args[0].includes('.')) {
      // Digits only: the organisation's currency.
      currency = this.localeService.currencyCode();
      digits = args[0];
    } else {
      currency = args[0];
      digits = args[1];
    }

    const exponent = this.currencies.tryExponent(currency);
    if (!currency || exponent == null) return '';

    let minimumFractionDigits = exponent;
    let maximumFractionDigits = exponent;
    if (digits) {
      const fraction = digits.split('.')[1]?.split('-');
      if (fraction) {
        minimumFractionDigits = Number.parseInt(fraction[0], 10) || 0;
        const max = Number.parseInt(fraction[1] ?? fraction[0], 10);
        maximumFractionDigits = Number.isNaN(max) ? minimumFractionDigits : Math.max(max, minimumFractionDigits);
      }
    }

    return new Intl.NumberFormat(this.localeService.locale(), {
      style: 'currency',
      currency,
      currencyDisplay: 'narrowSymbol',
      minimumFractionDigits,
      maximumFractionDigits,
    }).format(value ?? 0);
  }
}
