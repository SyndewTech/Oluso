namespace Oluso.Protocols.DPoP;

/// <summary>
/// The access token a request presents in its <c>Authorization</c> header and the scheme it uses:
/// <c>Bearer</c> (RFC 6750) or <c>DPoP</c> (RFC 9449).
/// </summary>
public readonly record struct PresentedAccessToken(string Scheme, string Token)
{
    public const string BearerScheme = "Bearer";
    public const string DPoPScheme = "DPoP";

    public bool IsDPoP => Scheme == DPoPScheme;

    /// <summary>The token in an <c>Authorization: Bearer …</c> or <c>Authorization: DPoP …</c> header, else null.</summary>
    public static PresentedAccessToken? FromAuthorizationHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        foreach (var scheme in new[] { BearerScheme, DPoPScheme })
        {
            if (!header.StartsWith(scheme + " ", StringComparison.OrdinalIgnoreCase)) continue;
            var token = header[(scheme.Length + 1)..].Trim();
            return token.Length == 0 ? null : new PresentedAccessToken(scheme, token);
        }
        return null;
    }
}
