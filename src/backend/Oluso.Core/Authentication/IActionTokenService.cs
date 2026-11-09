namespace Oluso.Core.Authentication;

/// <summary>
/// Result of a successful <see cref="IActionTokenService.RedeemAsync"/> call.
/// Carries everything the caller needs to authorise the action without
/// having to re-load the row themselves.
/// </summary>
public sealed record ActionTokenClaim(
    string TenantId,
    string UserId,
    string Action,
    string SubjectId);

/// <summary>
/// Mints and redeems single-use, short-lived <see cref="Oluso.Core.Domain.Entities.ActionToken"/>s
/// that authorise a specific user to perform a specific action on a specific
/// subject — without holding an interactive session. The keystone for magic-
/// link decide flows (approver emails, vendor confirmations, candidate offer
/// acceptance, etc.).
///
/// <para>Implementation is responsible for: generating cryptographically
/// secure raw tokens, storing only the hash in the DB, and atomically
/// marking tokens consumed on first redeem so the same link can't be
/// replayed.</para>
/// </summary>
public interface IActionTokenService
{
    /// <summary>
    /// Issue a token. Returns the raw token value — caller embeds this in
    /// the URL / message body. The raw value never lives anywhere
    /// persistent; only its SHA-256 hash is stored.
    /// </summary>
    /// <param name="tenantId">Tenant the token belongs to.</param>
    /// <param name="userId">The only user permitted to redeem.</param>
    /// <param name="action">Namespaced action key, e.g. <c>approval.task.decide</c>.</param>
    /// <param name="subjectId">Subject identifier, e.g. a task id.</param>
    /// <param name="ttl">Time until the token expires.</param>
    Task<string> IssueAsync(
        string tenantId,
        string userId,
        string action,
        string subjectId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to redeem a raw token. Returns the claim on success, or null
    /// when the token is missing, expired, or already consumed. The
    /// implementation atomically marks the token consumed before returning
    /// — repeated redeems of the same raw value return null.
    /// </summary>
    Task<ActionTokenClaim?> RedeemAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspect a token without consuming it. Used by preview endpoints
    /// that need to show "what does this link do?" before the user clicks
    /// the Approve / Reject button. Returns null on the same failure modes
    /// as <see cref="RedeemAsync"/>.
    /// </summary>
    Task<ActionTokenClaim?> PeekAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a <see cref="RedeemAsync"/> whose action then failed, so the link
    /// is not lost to an error the recipient did not cause.
    /// </summary>
    Task ReleaseAsync(string rawToken, CancellationToken cancellationToken = default);
}
