namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Single-use, short-lived token that authorises a specific user to perform
/// a specific action on a specific subject — without holding an interactive
/// session. The keystone for "magic link" flows: approver clicks Approve
/// in an email, vendor confirms an invoice from a portal link, candidate
/// accepts an offer without logging in, etc.
///
/// <para>The column stores the SHA-256 hash of the issued token, not the
/// raw bytes — so a DB leak doesn't expose still-active tokens. The raw
/// value (returned by <c>IActionTokenService.IssueAsync</c>) lives only in
/// the URL or message body it was minted for.</para>
///
/// <para>Tokens are scoped to <see cref="Action"/> + <see cref="SubjectId"/>
/// so a token issued for "approve task X" cannot be redeemed against task Y
/// or to perform "reject task X". Callers decide the granularity (e.g., one
/// generic "decide task X" token vs separate approve / reject tokens).</para>
/// </summary>
public class ActionToken : TenantEntity
{
    /// <summary>Internal primary key — opaque, not the token value itself.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// SHA-256 hash of the raw token. The raw token never lives in the DB.
    /// </summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>User this token authorises (the only acceptable redeemer).</summary>
    public string UserId { get; set; } = null!;

    /// <summary>
    /// Action key the token unlocks (e.g. <c>approval.task.decide</c>).
    /// Callers should namespace by module to avoid collisions.
    /// </summary>
    public string Action { get; set; } = null!;

    /// <summary>
    /// Subject the action applies to (e.g. the approval task id). The
    /// redeemer can't repoint a token at a different subject.
    /// </summary>
    public string SubjectId { get; set; } = null!;

    /// <summary>UTC. Tokens past <see cref="ExpiresAt"/> are not redeemable.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Set on first successful redeem. Subsequent redeems with the same
    /// raw token must fail — that's the "single-use" guarantee.
    /// </summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>When the token was issued.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
