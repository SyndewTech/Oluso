using System.Text.RegularExpressions;

namespace Oluso.Core.Locale;

/// <summary>
/// Turns a country choice plus a locally written number into an E.164 value
/// (<c>+2348031234567</c>) and back again for re-rendering. Deliberately small:
/// it validates shape (digits, length, calling code), not per-country numbering
/// plans.
/// </summary>
public static class PhoneNumberInput
{
    public const string FormatInternational = "international";
    public const string FormatLocal = "local";

    /// <summary>Suffix of the form input that carries the selected country for a phone field.</summary>
    public const string CountryInputSuffix = "__country";

    private static readonly Regex E164 = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);
    private static readonly Regex LocalShape = new(@"^[\d\s\-\+\(\)\.]+$", RegexOptions.Compiled);

    /// <summary>
    /// Countries whose national numbers keep their leading 0 in international
    /// format; everywhere else the 0 is a trunk prefix and is dropped.
    /// </summary>
    private static readonly HashSet<string> KeepsLeadingZero = new(StringComparer.OrdinalIgnoreCase) { "IT", "VA", "SM" };

    /// <summary>
    /// Countries preferred when several share a calling code (needed to split an
    /// E.164 value back into country + national number).
    /// </summary>
    private static readonly Dictionary<string, string> PreferredForDialCode = new()
    {
        ["1"] = "US", ["7"] = "RU", ["39"] = "IT", ["44"] = "GB", ["47"] = "NO", ["61"] = "AU",
        ["212"] = "MA", ["262"] = "RE", ["590"] = "GP", ["599"] = "CW",
    };

    public static bool IsInternational(string? phoneFormat) =>
        !string.Equals(phoneFormat, FormatLocal, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidE164(string? value) => !string.IsNullOrEmpty(value) && E164.IsMatch(value);

    /// <summary>
    /// A number typed as it would be dialled locally, in the given country.
    /// </summary>
    public static bool IsValidLocal(string? value) =>
        !string.IsNullOrWhiteSpace(value) && LocalShape.IsMatch(value) && value.Count(char.IsDigit) is >= 6 and <= 15;

    /// <summary>
    /// Compose an E.164 number from a country and a national number. A number already
    /// written with a "+" is accepted as-is (after validation) regardless of the country.
    /// </summary>
    public static bool TryCompose(string? countryCode, string? nationalInput, out string e164, out string error)
    {
        e164 = string.Empty;
        error = string.Empty;

        var raw = (nationalInput ?? string.Empty).Trim();
        if (raw.StartsWith('+'))
        {
            var full = "+" + new string(raw.Where(char.IsDigit).ToArray());
            if (!IsValidE164(full))
            {
                error = "Enter a valid international phone number";
                return false;
            }

            e164 = full;
            return true;
        }

        var country = SupportedCountries.GetByCode(countryCode);
        if (country == null)
        {
            error = "Select the country code for this phone number";
            return false;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length > 1 && digits[0] == '0' && !KeepsLeadingZero.Contains(country.Code))
        {
            digits = digits[1..];
        }

        if (digits.Length == 0)
        {
            error = "Enter a phone number";
            return false;
        }

        var composed = "+" + country.DialCode + digits;
        if (!IsValidE164(composed))
        {
            error = "Enter a valid phone number for the selected country";
            return false;
        }

        e164 = composed;
        return true;
    }

    /// <summary>
    /// Split an E.164 number into the country it belongs to and the national part,
    /// for re-rendering a phone input. Null country when the value is not E.164.
    /// </summary>
    public static (CountryInfo? Country, string National) Split(string? e164)
    {
        if (!IsValidE164(e164))
        {
            return (null, e164 ?? string.Empty);
        }

        var digits = e164![1..];
        CountryInfo? best = null;
        foreach (var candidate in SupportedCountries.All)
        {
            if (!digits.StartsWith(candidate.DialCode, StringComparison.Ordinal)) continue;
            if (best == null || candidate.DialCode.Length > best.DialCode.Length)
            {
                best = candidate;
            }
            else if (candidate.DialCode.Length == best.DialCode.Length
                && PreferredForDialCode.TryGetValue(candidate.DialCode, out var preferred)
                && string.Equals(preferred, candidate.Code, StringComparison.OrdinalIgnoreCase))
            {
                best = candidate;
            }
        }

        return best == null ? (null, e164) : (best, digits[best.DialCode.Length..]);
    }
}
