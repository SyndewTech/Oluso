import { Pipe, PipeTransform, inject } from '@angular/core';
import { CurrencyCatalogService } from '../services/currency-catalog.service';

/**
 * An amount in a currency's minor units shown in that currency, at the currency's own decimal places:
 * `12345 | minorUnits:'KWD'` is "KWD 12.345", `12345 | minorUnits:'JPY'` is "¥12,345". Written without a currency
 * (`value | minorUnits`) it uses the organisation's currency; a currency argument that turns out null is not
 * replaced by it. A third argument `true` drops the decimals for summary figures. Until the currency and its
 * decimal places are known it renders nothing rather than guess.
 */
@Pipe({ name: 'minorUnits', standalone: true, pure: false })
export class MinorUnitsPipe implements PipeTransform {
  private readonly currencies = inject(CurrencyCatalogService);

  transform(valueInMinorUnits: number | undefined | null, ...args: [currencyCode?: string | null, wholeUnits?: boolean]): string {
    const currency = args.length === 0 ? this.currencies.tenantCurrency() : args[0];
    return this.currencies.formatMinor(valueInMinorUnits, currency, { wholeUnits: args[1] ?? false });
  }
}
