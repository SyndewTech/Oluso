/**
 * Whole-number amounts in a currency's minor units, given its exponent (decimal places).
 * The exponent comes from the platform currency list; see CurrencyCatalogService.exponent.
 */

/** 12.345 at exponent 3 → 12345; rounds half away from zero without binary-float drift (1.005 → 101 at exponent 2). */
export function toMinorUnits(amount: number, exponent: number): number {
  assertExponent(exponent);
  const sign = amount < 0 ? -1 : 1;
  const shifted = Number(`${Math.abs(amount)}e${exponent}`);
  return sign * Math.round(shifted) || 0;
}

/** 12345 at exponent 3 → 12.345. */
export function fromMinorUnits(minorUnits: number, exponent: number): number {
  assertExponent(exponent);
  return Number(`${minorUnits}e-${exponent}`);
}

/** Shown when an amount is entered before the organisation has a currency. */
export const TENANT_CURRENCY_REQUIRED = "Set your organisation's currency in Settings → Locale before entering amounts.";

/**
 * Minor units shown in their currency at its own decimal places ("¥12,345", "KWD 12.345"), or an empty string
 * when the amount, the currency or its decimal places aren't known: nothing is shown rather than a guess at two
 * decimals. `wholeUnits` drops the decimals for summary figures.
 */
export function formatMinorUnits(
  minorUnits: number | null | undefined,
  currencyCode: string | null | undefined,
  exponent: number | null | undefined,
  locale: string,
  wholeUnits = false,
): string {
  if (minorUnits == null || !currencyCode || exponent == null) return '';
  const digits = wholeUnits ? 0 : exponent;
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: currencyCode,
    // narrowSymbol forces the actual currency glyph (₦, ₹, R$) even when the locale would emit the ISO code.
    currencyDisplay: 'narrowSymbol',
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(fromMinorUnits(minorUnits, exponent));
}

function assertExponent(exponent: number): void {
  if (!Number.isInteger(exponent) || exponent < 0) throw new Error(`${exponent} is not a currency exponent.`);
}
