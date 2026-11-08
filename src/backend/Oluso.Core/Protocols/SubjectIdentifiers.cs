using System.Security.Cryptography;
using System.Text;

namespace Oluso.Core.Protocols;

/// <summary>
/// The <c>sub</c> a client sees for a user (OpenID Connect Core 1.0 §8). Tokens are issued
/// with it, and an <c>id_token_hint</c> a client sends back carries it, so both sides must
/// compute it the same way.
/// </summary>
public static class SubjectIdentifiers
{
    /// <summary>
    /// The user's id for a public-subject client; for a pairwise client (one with a salt),
    /// Base64URL(HMAC-SHA256(salt, clientId + userId)), so clients can't correlate users.
    /// </summary>
    public static string ForClient(string userId, string clientId, string? pairwiseSalt)
    {
        if (string.IsNullOrEmpty(pairwiseSalt))
        {
            return userId;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pairwiseSalt));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{clientId}{userId}"));

        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
