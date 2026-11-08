using System.Text.Json;

namespace Oluso.Core.Protocols.SenderConstraint;

/// <summary>
/// What an access token is bound to, from its <c>cnf</c> claim: a DPoP key (<c>jkt</c>, RFC 9449) and/or a
/// client certificate (<c>x5t#S256</c>, RFC 8705).
/// </summary>
public sealed record TokenConfirmation(string? Jkt, string? CertificateThumbprint)
{
    public static readonly TokenConfirmation None = new(null, null);

    /// <summary>The confirmation in a <c>cnf</c> claim value; <see cref="None"/> when there is none.</summary>
    public static TokenConfirmation Parse(string? cnfClaimValue)
    {
        if (string.IsNullOrWhiteSpace(cnfClaimValue)) return None;
        using var cnf = JsonDocument.Parse(cnfClaimValue);
        if (cnf.RootElement.ValueKind != JsonValueKind.Object) return None;
        return new TokenConfirmation(Member(cnf.RootElement, "jkt"), Member(cnf.RootElement, "x5t#S256"));
    }

    private static string? Member(JsonElement cnf, string name) =>
        cnf.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
