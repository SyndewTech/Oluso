namespace Oluso.Core.Protocols.Models;

/// <summary>
/// The Authentication Context Class Reference (acr) values this server asserts (OpenID Connect Core 1.0 section 2:
/// an absolute URI SHOULD be used). They are the same in every deployment and documented in docs/acr.md.
/// </summary>
public static class AuthenticationContextClasses
{
    /// <summary>One authentication factor.</summary>
    public const string SingleFactor = "https://oluso.io/acr/sfa";

    /// <summary>Two or more independent authentication factors (RFC 8176 <c>mfa</c>).</summary>
    public const string MultiFactor = "https://oluso.io/acr/mfa";

    private const string MultiFactorMethod = "mfa";

    public static readonly IReadOnlyList<string> Supported = new[] { SingleFactor, MultiFactor };

    /// <summary>
    /// The acr of a sign-in: the one it recorded, if any, otherwise the class its authentication methods (amr)
    /// meet. A sign-in with no recorded methods asserts none.
    /// </summary>
    public static string? Resolve(string? acr, IEnumerable<string>? amr)
    {
        if (!string.IsNullOrEmpty(acr))
            return acr;

        var methods = amr?.ToList();
        if (methods is null || methods.Count == 0)
            return null;

        return methods.Contains(MultiFactorMethod, StringComparer.Ordinal) ? MultiFactor : SingleFactor;
    }
}
