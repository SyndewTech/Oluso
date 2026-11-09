using Oluso.Core.Locale;

namespace Oluso.Core.Payments;

/// <summary>An amount Stripe can't represent exactly in the charge currency; the charge or refund is refused rather than rounded.</summary>
public sealed class StripeAmountException : InvalidOperationException
{
    public StripeAmountException(string message) : base(message) { }
}

/// <summary>
/// Stripe's own amount representation, which differs from ISO 4217 for some currencies
/// (https://docs.stripe.com/currencies, "Specify amounts in API requests"):
/// <list type="bullet">
/// <item>Amounts are integers in the currency's smallest unit.</item>
/// <item>Stripe's zero-decimal currencies (including MGA, which ISO gives two decimals) take whole units.</item>
/// <item>ISK and UGX are whole-unit currencies that Stripe still represents with two decimals that are always 00.</item>
/// <item>Every other currency uses its ISO minor units (<see cref="MinorUnits"/>).</item>
/// </list>
/// An amount finer than the representation allows (a fraction of a yen, of a króna, or of a cent) is refused.
/// </summary>
public static class StripeAmounts
{
    /// <summary>Stripe's zero-decimal list, less UGX, which its "Special cases" table represents with two decimals.</summary>
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    /// <summary>Whole units only, sent as a two-decimal value ending in 00 ("Special cases": ISK, UGX).</summary>
    private static readonly HashSet<string> WholeUnitsAsTwoDecimals = new(StringComparer.OrdinalIgnoreCase) { "ISK", "UGX" };

    /// <summary>The integer amount Stripe expects for <paramref name="amount"/> in <paramref name="currency"/>.</summary>
    /// <exception cref="StripeAmountException">The amount has a fraction the currency can't carry on Stripe.</exception>
    /// <exception cref="ArgumentException">The currency isn't a supported ISO 4217 code.</exception>
    public static long ToStripe(decimal amount, string currency)
    {
        var code = currency.Trim().ToUpperInvariant();
        if (ZeroDecimal.Contains(code))
            return Whole(amount, code);
        if (WholeUnitsAsTwoDecimals.Contains(code))
            return Whole(amount, code) * 100;

        var scaled = amount * Scale(MinorUnits.Exponent(code));
        if (scaled != decimal.Truncate(scaled))
            throw new StripeAmountException($"{amount} {code} has more decimal places than {code} allows, so it can't be charged exactly.");
        return (long)scaled;
    }

    /// <summary>The integer amount Stripe expects for an amount held in ISO minor units of <paramref name="currency"/>.</summary>
    /// <exception cref="StripeAmountException">The amount has a fraction the currency can't carry on Stripe.</exception>
    public static long FromMinorUnits(long minorUnits, string currency) => ToStripe(MinorUnits.FromMinor(minorUnits, currency), currency);

    /// <summary>An integer amount from Stripe back in ISO minor units of <paramref name="currency"/>.</summary>
    public static long ToMinorUnits(long stripeAmount, string currency) => MinorUnits.ToMinor(FromStripe(stripeAmount, currency), currency);

    /// <summary>An integer amount from Stripe (a session total, a refund) back in major units.</summary>
    public static decimal FromStripe(long stripeAmount, string currency)
    {
        var code = currency.Trim().ToUpperInvariant();
        if (ZeroDecimal.Contains(code))
            return stripeAmount;
        if (WholeUnitsAsTwoDecimals.Contains(code))
            return stripeAmount / 100m;
        return stripeAmount / Scale(MinorUnits.Exponent(code));
    }

    private static long Whole(decimal amount, string code) =>
        amount == decimal.Truncate(amount)
            ? (long)amount
            : throw new StripeAmountException($"Stripe can't charge fractions of {code}; {amount} {code} must be a whole amount.");

    private static decimal Scale(int exponent)
    {
        var scale = 1m;
        for (var i = 0; i < exponent; i++) scale *= 10m;
        return scale;
    }
}
