namespace Oluso.Core.Services;

/// <summary>
/// Service for sending backchannel logout notifications to clients.
/// Implements OpenID Connect Back-Channel Logout 1.0 specification.
/// </summary>
public interface IBackchannelLogoutService
{
    /// <summary>
    /// Sends backchannel logout notifications to all clients that have active sessions for the user.
    /// </summary>
    /// <param name="subjectId">The subject ID of the user logging out</param>
    /// <param name="sessionId">The session ID (optional, but required if client requires session in logout token)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Results of the logout notifications</returns>
    Task<BackchannelLogoutResult> SendLogoutNotificationsAsync(
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends backchannel logout notification to a specific client.
    /// </summary>
    /// <param name="clientId">The client ID to notify</param>
    /// <param name="subjectId">The subject ID of the user logging out</param>
    /// <param name="sessionId">The session ID (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Whether the notification was successful</returns>
    Task<bool> SendLogoutNotificationAsync(
        string clientId,
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of backchannel logout operation
/// </summary>
public class BackchannelLogoutResult
{
    /// <summary>
    /// Whether all notifications were successful
    /// </summary>
    public bool Success => FailedClients.Count == 0;

    /// <summary>
    /// Number of clients notified successfully
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// List of clients that failed to receive logout notification
    /// </summary>
    public List<BackchannelLogoutFailure> FailedClients { get; set; } = new();

    /// <summary>
    /// Total number of clients that were attempted
    /// </summary>
    public int TotalAttempted => SuccessCount + FailedClients.Count;
}

/// <summary>
/// Details of a failed backchannel logout notification
/// </summary>
public class BackchannelLogoutFailure
{
    /// <summary>
    /// Client ID that failed
    /// </summary>
    public string ClientId { get; set; } = default!;

    /// <summary>
    /// The backchannel logout URI that was attempted
    /// </summary>
    public string? BackChannelLogoutUri { get; set; }

    /// <summary>
    /// Error message describing the failure
    /// </summary>
    public string Error { get; set; } = default!;

    /// <summary>
    /// HTTP status code if applicable
    /// </summary>
    public int? StatusCode { get; set; }
}

/// <summary>
/// Service for generating logout tokens for backchannel logout
/// </summary>
public interface ILogoutTokenGenerator
{
    /// <summary>
    /// Generates a logout token JWT for backchannel logout.
    /// </summary>
    /// <param name="clientId">The client ID (audience)</param>
    /// <param name="subjectId">The subject ID of the user logging out</param>
    /// <param name="sessionId">The session ID (optional, but required if client requires session)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The signed JWT logout token</returns>
    Task<string> GenerateLogoutTokenAsync(
        string clientId,
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default);
}
