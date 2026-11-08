namespace Oluso.Core.Locale;

/// <summary>
/// Whole-number amounts in a currency's minor units (cents for USD, none for JPY, fils for KWD),
/// using the ISO 4217 exponents in <see cref="SupportedCurrencies"/>. An unknown or blank currency
/// code is an error: there is no default exponent.
/// </summary>
public static class MinorUnits
{
    /// <summary>The currency's number of decimal places.</summary>
    /// <exception cref="ArgumentException">The code isn't a supported currency.</exception>
    public static int Exponent(string currencyCode) =>
        SupportedCurrencies.GetByCode(currencyCode)?.DecimalPlaces
        ?? throw new ArgumentException($"'{currencyCode}' is not a supported currency code.", nameof(currencyCode));

    /// <summary>An amount in major units (12.345) as minor units (12345 for KWD), rounded half away from zero.</summary>
    public static long ToMinor(decimal amount, string currencyCode) =>
        (long)Math.Round(amount * Scale(Exponent(currencyCode)), 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// An amount in major units rounded, half away from zero, to the currency's own decimal places:
    /// 12.3456 is 12.35 in GBP, 12 in JPY and 12.346 in KWD.
    /// </summary>
    public static decimal Round(decimal amount, string currencyCode) =>
        Math.Round(amount, Exponent(currencyCode), MidpointRounding.AwayFromZero);

    /// <summary>Minor units as an amount in major units.</summary>
    public static decimal FromMinor(long minorUnits, string currencyCode) =>
        minorUnits / Scale(Exponent(currencyCode));

    /// <summary>
    /// Minor units as a plain number in the currency's own decimal places, grouped, invariant culture:
    /// 123456 is "1,234.56" in GBP, "123,456" in JPY and "123.456" in KWD. No symbol or code.
    /// </summary>
    public static string Format(long minorUnits, string currencyCode) =>
        FromMinor(minorUnits, currencyCode).ToString("N" + Exponent(currencyCode), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Converts minor units of <paramref name="fromCurrency"/> into minor units of <paramref name="toCurrency"/>,
    /// where <paramref name="rate"/> is the price of one unit of the target currency in the source currency
    /// (1 <paramref name="toCurrency"/> = <paramref name="rate"/> <paramref name="fromCurrency"/>).
    /// Rounded half away from zero to a whole minor unit of the target currency.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The rate is zero or negative.</exception>
    public static long Convert(long minorUnits, string fromCurrency, string toCurrency, decimal rate)
    {
        if (rate <= 0m)
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "An exchange rate must be positive.");

        var shift = Exponent(toCurrency) - Exponent(fromCurrency);
        if (rate == 1m && shift == 0)
            return minorUnits;

        var scaled = shift >= 0 ? minorUnits * Scale(shift) : minorUnits / Scale(-shift);
        return (long)Math.Round(scaled / rate, 0, MidpointRounding.AwayFromZero);
    }

    private static decimal Scale(int exponent)
    {
        var scale = 1m;
        for (var i = 0; i < exponent; i++) scale *= 10m;
        return scale;
    }
}
