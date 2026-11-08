namespace Oluso.Core.Services;

/// <summary>
/// A tenant's reply-to inbox takes effect only after someone confirms it from a
/// link sent to that inbox, so a typo or someone else's address never receives
/// the tenant's customer replies.
/// </summary>
public interface IReplyToVerificationService
{
    Task<ReplyToStatus> GetStatusAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Sends a confirmation link to <paramref name="address"/>; the current reply-to stays in use until it is confirmed.</summary>
    Task<ReplyToRequestResult> RequestAsync(string tenantId, string address, CancellationToken cancellationToken = default);

    Task<ReplyToRequestResult> ResendAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<ReplyToStatus> CancelPendingAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Removes the confirmed reply-to and any pending one.</summary>
    Task<ReplyToStatus> RemoveAsync(string tenantId, CancellationToken cancellationToken = default);
}

public sealed record ReplyToStatus(string? ReplyToAddress, DateTime? VerifiedAt, string? PendingAddress, DateTime? PendingSentAt);

public sealed record ReplyToRequestResult(bool Sent, string? Error, ReplyToStatus Status);
