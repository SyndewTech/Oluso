using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Protocols;

/// <summary>
/// Which clients took part in a user's sign-in session (the OP session, its <c>sid</c>): each time a client gets an
/// authorization response in a session, a <see cref="GrantType"/> grant records it. When the session ends, these are
/// the clients that are told (Back-Channel and Front-Channel Logout 1.0) and whose session-bound grants end with it;
/// another sign-in, on another device or browser, has another sid and is not affected.
/// </summary>
public static class SessionClients
{
    /// <summary>The persisted grant type of a session participation record.</summary>
    public const string GrantType = "session_client";

    /// <summary>
    /// How long a record is kept after the client's last authorization response in the session: past the longest
    /// sign-in session, so a session still running is never missing a client. Ending the session removes them.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(31);

    /// <summary>Records that <paramref name="clientId"/> got an authorization response in the session.</summary>
    public static Task RecordAsync(
        IPersistedGrantStore grants,
        string? subjectId,
        string? sessionId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(clientId))
        {
            return Task.CompletedTask;
        }

        var now = DateTime.UtcNow;
        return grants.StoreAsync(new PersistedGrant
        {
            Key = Key(sessionId, clientId),
            Type = GrantType,
            SubjectId = subjectId,
            SessionId = sessionId,
            ClientId = clientId,
            CreationTime = now,
            Expiration = now + Lifetime,
            Data = "{}"
        }, cancellationToken);
    }

    /// <summary>One record per session and client.</summary>
    public static string Key(string sessionId, string clientId) =>
        "sc:" + Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId + "\n" + clientId)));
}
