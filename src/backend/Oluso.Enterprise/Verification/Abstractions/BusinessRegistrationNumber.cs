using System.Text.RegularExpressions;

namespace Oluso.Enterprise.Verification;

/// <summary>
/// A registration number in a country's canonical form, so the same business typed two
/// ways ("rc 1234567", "RC-1234567", "1234567" + type RC) is one record.
/// </summary>
public sealed record BusinessRegistrationNumber(string Country, string Canonical, string? Type, string Number)
{
    /// <summary>
    /// Nigerian CAC registration types: the prefix the registry puts before the number.
    /// RC = company, BN = business name, IT = incorporated trustees, LP = limited
    /// partnership, LLP = limited liability partnership.
    /// </summary>
    public static readonly IReadOnlyList<string> NigerianTypes = new[] { "RC", "BN", "IT", "LP", "LLP" };

    private static readonly Regex Separators = new(@"[\s\-/._]", RegexOptions.Compiled);
    private static readonly Regex NigerianShape = new(@"^(LLP|LP|RC|BN|IT)?(\d{1,12})$", RegexOptions.Compiled);
    private static readonly Regex GenericShape = new(@"^[A-Z0-9]{2,40}$", RegexOptions.Compiled);

    /// <summary>
    /// Parses what the applicant typed. Returns null with <paramref name="error"/> set to
    /// <c>registration_type_required</c> or <c>invalid_registration_number</c> when it can't.
    /// </summary>
    public static BusinessRegistrationNumber? TryParse(string country, string? input, string? type, out string? error)
    {
        error = null;
        var compact = Separators.Replace(input ?? string.Empty, string.Empty).ToUpperInvariant();
        var code = country.Trim().ToUpperInvariant();
        if (compact.Length == 0)
        {
            error = "invalid_registration_number";
            return null;
        }

        if (code == "NG")
        {
            var match = NigerianShape.Match(compact);
            if (!match.Success)
            {
                error = "invalid_registration_number";
                return null;
            }

            var prefix = match.Groups[1].Success ? match.Groups[1].Value : null;
            var chosen = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToUpperInvariant();
            if (chosen != null && !NigerianTypes.Contains(chosen))
            {
                error = "invalid_registration_number";
                return null;
            }

            var resolved = prefix ?? chosen;
            if (resolved == null)
            {
                error = "registration_type_required";
                return null;
            }

            var digits = match.Groups[2].Value;
            return new BusinessRegistrationNumber(code, resolved + digits, resolved, digits);
        }

        if (!GenericShape.IsMatch(compact))
        {
            error = "invalid_registration_number";
            return null;
        }

        var genericType = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToUpperInvariant();
        return new BusinessRegistrationNumber(code, compact, genericType, compact);
    }
}
