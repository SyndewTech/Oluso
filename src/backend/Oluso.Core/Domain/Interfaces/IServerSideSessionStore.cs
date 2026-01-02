using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Store for server-side user sessions.
/// Enables session management, backchannel logout, and session introspection.
/// </summary>
public interface IServerSideSessionStore
{
    /// <summary>
    /// Creates a new server-side session
    /// </summary>
    Task<ServerSideSession> CreateSessionAsync(ServerSideSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a session by its key
    /// </summary>
    Task<ServerSideSession?> GetSessionAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a session by session ID
    /// </summary>
    Task<ServerSideSession?> GetBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sessions for a subject (user)
    /// </summary>
    Task<IReadOnlyList<ServerSideSession>> GetSessionsBySubjectAsync(string subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active sessions for a subject with client information
    /// </summary>
    Task<IReadOnlyList<UserSession>> GetUserSessionsAsync(string subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing session (e.g., renewal, data update)
    /// </summary>
    Task UpdateSessionAsync(ServerSideSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session by its key
    /// </summary>
    Task DeleteSessionAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session by session ID
    /// </summary>
    Task DeleteBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all sessions for a subject (user logout from all devices)
    /// </summary>
    Task DeleteSessionsBySubjectAsync(string subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sessions for a subject and client combination
    /// </summary>
    Task<IReadOnlyList<ServerSideSession>> GetSessionsBySubjectAndClientAsync(
        string subjectId,
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all clients that have active sessions for a subject
    /// </summary>
    Task<IReadOnlyList<string>> GetClientIdsBySubjectAsync(string subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired sessions
    /// </summary>
    Task<int> RemoveExpiredSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session count for a subject
    /// </summary>
    Task<int> GetSessionCountAsync(string subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sessions with pagination
    /// </summary>
    Task<(IReadOnlyList<ServerSideSession> Sessions, int TotalCount)> GetAllSessionsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a user session with additional metadata
/// </summary>
public class UserSession
{
    public string SessionId { get; set; } = default!;
    public string SubjectId { get; set; } = default!;
    public string? DisplayName { get; set; }
    public DateTime Created { get; set; }
    public DateTime Renewed { get; set; }
    public DateTime? Expires { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Store for CIBA (Client Initiated Backchannel Authentication) requests
/// </summary>
public interface ICibaStore
{
    /// <summary>
    /// Stores a new CIBA request
    /// </summary>
    Task StoreRequestAsync(CibaRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a CIBA request by auth_req_id
    /// </summary>
    Task<CibaRequest?> GetByAuthReqIdAsync(string authReqId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending CIBA requests for a user
    /// </summary>
    Task<IReadOnlyList<CibaRequest>> GetPendingBySubjectAsync(string subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a CIBA request (e.g., when user approves/denies)
    /// </summary>
    Task UpdateRequestAsync(CibaRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a CIBA request
    /// </summary>
    Task RemoveRequestAsync(string authReqId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired CIBA requests
    /// </summary>
    Task<int> RemoveExpiredRequestsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// CIBA request entity
/// </summary>
public class CibaRequest : TenantEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Unique identifier for this CIBA request (auth_req_id)
    /// </summary>
    public string AuthReqId { get; set; } = default!;

    /// <summary>
    /// Client making the request
    /// </summary>
    public string ClientId { get; set; } = default!;

    /// <summary>
    /// Subject identifier (user being authenticated)
    /// </summary>
    public string SubjectId { get; set; } = default!;

    /// <summary>
    /// Login hint used to identify the user
    /// </summary>
    public string? LoginHint { get; set; }

    /// <summary>
    /// Login hint token if provided
    /// </summary>
    public string? LoginHintToken { get; set; }

    /// <summary>
    /// ID token hint if provided
    /// </summary>
    public string? IdTokenHint { get; set; }

    /// <summary>
    /// Binding message to display to user
    /// </summary>
    public string? BindingMessage { get; set; }

    /// <summary>
    /// User code for user interaction
    /// </summary>
    public string? UserCode { get; set; }

    /// <summary>
    /// Requested scopes (space-separated)
    /// </summary>
    public string RequestedScopes { get; set; } = default!;

    /// <summary>
    /// ACR values requested
    /// </summary>
    public string? AcrValues { get; set; }

    /// <summary>
    /// Current status of the request
    /// </summary>
    public CibaRequestStatus Status { get; set; } = CibaRequestStatus.Pending;

    /// <summary>
    /// When the request was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the request expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Interval for polling (seconds)
    /// </summary>
    public int Interval { get; set; } = 5;

    /// <summary>
    /// When the user completed authentication (approved/denied)
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Session ID if authentication was successful
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Error code if request failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Error description if request failed
    /// </summary>
    public string? ErrorDescription { get; set; }

    /// <summary>
    /// Client notification token for ping/push modes
    /// </summary>
    public string? ClientNotificationToken { get; set; }

    /// <summary>
    /// Requested token delivery mode
    /// </summary>
    public CibaTokenDeliveryMode TokenDeliveryMode { get; set; } = CibaTokenDeliveryMode.Poll;

    /// <summary>
    /// Gets the scopes as a list
    /// </summary>
    public IEnumerable<string> GetScopes() =>
        string.IsNullOrEmpty(RequestedScopes)
            ? Enumerable.Empty<string>()
            : RequestedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// CIBA request status
/// </summary>
public enum CibaRequestStatus
{
    /// <summary>Request pending user action</summary>
    Pending,
    /// <summary>User approved the request</summary>
    Approved,
    /// <summary>User denied the request</summary>
    Denied,
    /// <summary>Request expired</summary>
    Expired,
    /// <summary>Request was consumed (tokens issued)</summary>
    Consumed
}

/// <summary>
/// CIBA token delivery mode
/// </summary>
public enum CibaTokenDeliveryMode
{
    /// <summary>Client polls the token endpoint</summary>
    Poll,
    /// <summary>Server pings client notification endpoint, client fetches tokens</summary>
    Ping,
    /// <summary>Server pushes tokens to client notification endpoint</summary>
    Push
}
