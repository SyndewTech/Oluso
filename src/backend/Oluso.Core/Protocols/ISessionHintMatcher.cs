namespace Oluso.Core.Protocols;

/// <summary>
/// Decides whether the person signed in by the existing session is the one an authorize
/// request names with <c>id_token_hint</c> or <c>login_hint</c>. A journey reuses the
/// session only for that person; anyone else gets the normal sign-in.
/// </summary>
public interface ISessionHintMatcher
{
    /// <summary>
    /// True when no hint is given, or every hint given names <paramref name="sessionUserId"/>.
    /// An <c>id_token_hint</c> that this server didn't issue to <paramref name="clientId"/>
    /// names nobody, so it doesn't match.
    /// </summary>
    Task<bool> MatchesAsync(
        string sessionUserId,
        string clientId,
        string? pairwiseSubjectSalt,
        string? idTokenHint,
        string? loginHint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The <c>sub</c> of <paramref name="idTokenHint"/> when it is an ID token this server issued to
    /// <paramref name="clientId"/> (signature, issuer and audience hold; expiry is ignored, as a hint may be an
    /// expired token), else null.
    /// </summary>
    Task<string?> ReadIdTokenHintSubjectAsync(string idTokenHint, string clientId, CancellationToken cancellationToken = default);
}
