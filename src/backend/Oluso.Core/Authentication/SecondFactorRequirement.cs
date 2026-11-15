namespace Oluso.Core.Authentication;

/// <summary>
/// Whether a sign-in, by the authentication methods it recorded (amr, RFC 8176), still needs a second factor before
/// a session or token is issued. One rule for the standard pages, journeys and codes issued to pages.
/// </summary>
public static class SecondFactorRequirement
{
    /// <summary>Methods that are, or include, a second factor: MFA itself, one-time codes, recovery codes, passkeys.</summary>
    private static readonly HashSet<string> SecondFactorMethods = new(StringComparer.Ordinal)
    {
        "mfa", "otp", "recovery_code", "hwk", "swk"
    };

    private const string Password = "pwd";

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
