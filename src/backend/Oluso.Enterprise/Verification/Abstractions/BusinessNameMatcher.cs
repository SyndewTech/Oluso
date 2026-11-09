using System.Globalization;
using System.Text;

namespace Oluso.Enterprise.Verification;

/// <summary>
/// Tolerant comparison of a name the applicant typed with the name a registry holds.
/// <para>Business names: case, accents and punctuation are ignored, "&amp;" reads as "and",
/// and legal-form and country words (ltd, limited, plc, llc, llp, lp, inc,
/// incorporated, nig, nigeria, the) are dropped wherever they appear, so
/// "Acme Foods (Nig.) Ltd" equals "ACME FOODS NIGERIA LIMITED". The remaining
/// words must match in order.</para>
/// <para>People: the same folding without dropping words; every word of the
/// applicant's first and last name must appear in the listed person's name, in any
/// order, so "Musa John" matches "JOHN MUSA" and "John Adewale Musa".</para>
/// </summary>
public static class BusinessNameMatcher
{
    private static readonly HashSet<string> IgnoredBusinessWords = new(StringComparer.Ordinal)
    {
        "ltd", "limited", "plc", "llc", "llp", "lp", "inc", "incorporated", "nig", "nigeria", "the",
    };

    public static bool BusinessNamesMatch(string? provided, string? registered)
    {
        var a = BusinessTokens(provided);
        var b = BusinessTokens(registered);
        return a.Count > 0 && a.SequenceEqual(b);
    }

    /// <summary>The words compared for a business name, after folding.</summary>
    public static IReadOnlyList<string> BusinessTokens(string? name) =>
        Tokens(name).Where(t => !IgnoredBusinessWords.Contains(t)).ToList();

    public static bool PersonNameMatches(string? firstName, string? lastName, string? listedName)
    {
        var wanted = Tokens($"{firstName} {lastName}");
        if (wanted.Count < 2 || string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return false;
        var listed = Tokens(listedName).ToHashSet(StringComparer.Ordinal);
        return wanted.All(listed.Contains);
    }

    private static List<string> Tokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();

        var decomposed = value.Replace("&", " and ").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
