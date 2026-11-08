using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Services;

/// <summary>
/// CIBA (Client Initiated Backchannel Authentication) service interface.
/// Implements OpenID Connect Client Initiated Backchannel Authentication Flow.
/// </summary>
public interface ICibaService
{
    /// <summary>
    /// Initiates a CIBA authentication request
    /// </summary>
    Task<CibaAuthenticationResult> AuthenticateAsync(
        CibaAuthenticationRequest request,
        ValidatedClient client,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a CIBA request (for polling)
    /// </summary>
    Task<CibaStatusResult> GetStatusAsync(
        string authReqId,
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a CIBA request (called when user consents)
    /// </summary>
    Task<bool> ApproveRequestAsync(
        string authReqId,
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Denies a CIBA request (called when user denies)
    /// </summary>
    Task<bool> DenyRequestAsync(
        string authReqId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// CIBA authentication request
/// </summary>
public class CibaAuthenticationRequest
{
    public string ClientId { get; set; } = default!;
    public string? Scope { get; set; }
    public string? LoginHint { get; set; }
    public string? LoginHintToken { get; set; }
    public string? IdTokenHint { get; set; }
    public string? BindingMessage { get; set; }
    public string? UserCode { get; set; }
    public string? AcrValues { get; set; }
    public int? RequestedExpiry { get; set; }
    public string? ClientNotificationToken { get; set; }
}

/// <summary>
/// CIBA authentication result
/// </summary>
public class CibaAuthenticationResult
{
    public bool IsSuccess { get; set; }
    public string? AuthReqId { get; set; }
    public int ExpiresIn { get; set; }
    public int Interval { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }

    public static CibaAuthenticationResult Success(string authReqId, int expiresIn, int interval) => new()
    {
        IsSuccess = true,
        AuthReqId = authReqId,
        ExpiresIn = expiresIn,
        Interval = interval
    };

    public static CibaAuthenticationResult Failure(string error, string? description = null) => new()
    {
        IsSuccess = false,
        Error = error,
        ErrorDescription = description
    };
}

/// <summary>
/// CIBA status result for token endpoint polling
/// </summary>
public class CibaStatusResult
{
    public CibaRequestStatus Status { get; set; }
    public string? SessionId { get; set; }
    public string? SubjectId { get; set; }
    public IEnumerable<string>? Scopes { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
    public int? Interval { get; set; }
}

/// <summary>
/// Service for notifying users of CIBA requests (e.g., push notification, email, SMS)
/// </summary>
public interface ICibaUserNotificationService
{
    /// <summary>
    /// Notifies a user that they have a pending CIBA authentication request
    /// </summary>
    Task NotifyUserAsync(CibaRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// One way of telling a user about a pending CIBA request (realtime, push, email, …). The
/// default <see cref="ICibaUserNotificationService"/> runs every registered channel in
/// registration order; a channel that fails doesn't stop the others.
/// </summary>
public interface ICibaNotificationChannel
{
    /// <summary>Stable channel name, recorded in <see cref="CibaUserNotification.DeliveredVia"/>.</summary>
    string Name { get; }

    /// <summary>
    /// Delivers the notification. Returns true when it was handed to a delivery service for the
    /// user, false when this channel isn't available for them (not configured, no address).
    /// </summary>
    Task<bool> NotifyAsync(CibaUserNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>A pending CIBA request resolved for delivery to the user who must approve it.</summary>
public sealed class CibaUserNotification
{
    public required CibaRequest Request { get; init; }

    /// <summary>The tenant the request (and the approving user) belongs to.</summary>
    public required string TenantId { get; init; }

    public required OlusoUserInfo User { get; init; }

    /// <summary>The requesting application's display name.</summary>
    public required string ClientName { get; init; }

    /// <summary>
    /// Channels that have already delivered, in order. A later channel can act as a fallback
    /// by checking this (e.g. email only when push didn't go out).
    /// </summary>
    public ISet<string> DeliveredVia { get; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Names shared by the server's CIBA user notifications and the clients that receive them
/// (the mobile app's CIBA module listens for <see cref="RealtimeChannel"/> and handles pushes
/// whose <c>type</c> is <see cref="PushType"/>).
/// </summary>
public static class CibaNotifications
{
    /// <summary>User-scoped realtime channel carrying CIBA request updates.</summary>
    public const string RealtimeChannel = "ciba";

    /// <summary>Realtime event raised when a new request awaits the user's decision.</summary>
    public const string RequestedEvent = "ciba.requested";

    /// <summary>Push <c>type</c>/<c>channel</c> data value routed to the mobile CIBA handler.</summary>
    public const string PushType = "ciba";

    /// <summary>Action-token action for the emailed approve/deny link.</summary>
    public const string DecideAction = "ciba.request.decide";
}
