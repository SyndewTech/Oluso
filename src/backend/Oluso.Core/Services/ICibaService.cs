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
