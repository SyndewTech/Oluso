using System.Security.Claims;

namespace Oluso.Core.Authentication;

/// <summary>
/// Whether a sign-in, by the authentication methods it recorded (amr, RFC 8176), still needs a second factor before
/// a session or token is issued. One rule for the standard pages, journeys and codes issued to pages.
/// </summary>
public static class SecondFactorRequirement
{
    /// <summary>
    /// Methods that are, or include, a second factor: MFA itself (also recorded by a passkey that verified the user,
    /// see <see cref="PasskeyAuthenticationMethods"/>), one-time codes and recovery codes. Possession of a key alone
    /// (<c>hwk</c>) is one factor.
    /// </summary>
    private static readonly HashSet<string> SecondFactorMethods = new(StringComparer.Ordinal)
    {
        "mfa", "otp", "recovery_code"
    };

    private const string Password = "pwd";

    /// <summary>
    /// Whether an external provider's sign-in reports multi-factor authentication in its amr (RFC 8176), as one claim
    /// per method or one space-separated claim: that sign-in had its second factor.
    /// </summary>
    public static bool ProviderReportsMfa(IEnumerable<Claim> providerClaims) =>
        providerClaims.Any(c =>
            (c.Type == "amr" || c.Type == "http://schemas.microsoft.com/claims/authnmethodsreferences")
            && c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("mfa", StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="methods"/> include a second factor.</summary>
    public static bool IsMet(IEnumerable<string>? methods) =>
        methods?.Any(SecondFactorMethods.Contains) == true;

    /// <summary>
    /// Whether a sign-in by <paramref name="methods"/> must still pass a second factor: none was used, and either the
    /// tenant requires one (password and external sign-ins alike, and a sign-in that recorded no method) or it was a
    /// password sign-in by a user who turned MFA on.
    /// </summary>
    public static bool IsMissing(bool tenantRequiresMfa, bool userHasMfa, IEnumerable<string>? methods)
    {
        var recorded = methods?.ToList() ?? new List<string>();
        if (IsMet(recorded))
        {
            return false;
        }

        return tenantRequiresMfa || (userHasMfa && recorded.Contains(Password, StringComparer.Ordinal));
    }
}
