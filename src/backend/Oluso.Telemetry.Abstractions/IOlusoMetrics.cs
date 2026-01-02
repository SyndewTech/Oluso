namespace Oluso.Telemetry;

/// <summary>
/// Interface for Oluso metrics collection.
/// Implement this interface to integrate with your preferred metrics backend.
/// </summary>
public interface IOlusoMetrics
{
    #region Token Metrics

    /// <summary>
    /// Record a token issued event
    /// </summary>
    void TokenIssued(string clientId, string grantType, string? tokenType = null, string? scope = null);

    /// <summary>
    /// Record a token request failure
    /// </summary>
    void TokenRequestFailed(string clientId, string grantType, string error);

    /// <summary>
    /// Record token introspection
    /// </summary>
    void TokenIntrospected(string clientId, bool isActive);

    /// <summary>
    /// Record token revocation
    /// </summary>
    void TokenRevoked(string clientId, string tokenTypeHint);

    #endregion

    #region Authentication Metrics

    /// <summary>
    /// Record a successful authentication
    /// </summary>
    void AuthenticationSuccess(string clientId, string method, string? idp = null);

    /// <summary>
    /// Record a failed authentication attempt
    /// </summary>
    void AuthenticationFailure(string clientId, string method, string error);

    /// <summary>
    /// Record MFA challenge
    /// </summary>
    void MfaChallenge(string clientId, string method, bool success);

    /// <summary>
    /// Record external IdP authentication
    /// </summary>
    void ExternalAuthentication(string provider, bool success, string? error = null);

    #endregion

    #region Authorization Metrics

    /// <summary>
    /// Record authorization request
    /// </summary>
    void AuthorizationRequest(string clientId, string responseType, bool success);

    /// <summary>
    /// Record consent grant
    /// </summary>
    void ConsentGranted(string clientId, string subjectId, int scopeCount);

    /// <summary>
    /// Record consent denial
    /// </summary>
    void ConsentDenied(string clientId, string subjectId);

    #endregion

    #region Session Metrics

    /// <summary>
    /// Record active session count (gauge)
    /// </summary>
    void SetActiveSessions(long count, string? tenantId = null);

    /// <summary>
    /// Record session creation
    /// </summary>
    void SessionCreated(string clientId, string? tenantId = null);

    /// <summary>
    /// Record session termination
    /// </summary>
    void SessionEnded(string clientId, string reason, string? tenantId = null);

    #endregion

    #region Client Metrics

    /// <summary>
    /// Record client authentication
    /// </summary>
    void ClientAuthenticated(string clientId, string method);

    /// <summary>
    /// Record client authentication failure
    /// </summary>
    void ClientAuthenticationFailed(string clientId, string method, string error);

    #endregion

    #region Device Flow Metrics

    /// <summary>
    /// Record device authorization request
    /// </summary>
    void DeviceAuthorizationRequest(string clientId);

    /// <summary>
    /// Record device code poll
    /// </summary>
    void DeviceCodePoll(string clientId, string status);

    #endregion

    #region Performance Metrics

    /// <summary>
    /// Record request duration
    /// </summary>
    void RecordRequestDuration(string endpoint, double durationMs, int statusCode);

    /// <summary>
    /// Record database operation duration
    /// </summary>
    void RecordDbOperationDuration(string operation, double durationMs, bool success);

    #endregion

    #region Tenant Metrics

    /// <summary>
    /// Record tenant operation
    /// </summary>
    void TenantOperation(string tenantId, string operation);

    /// <summary>
    /// Set active tenant count (gauge)
    /// </summary>
    void SetActiveTenants(long count);

    #endregion
}

/// <summary>
/// No-op implementation of metrics (used when no telemetry provider is configured)
/// </summary>
public class NullOlusoMetrics : IOlusoMetrics
{
    public static readonly NullOlusoMetrics Instance = new();

    public void TokenIssued(string clientId, string grantType, string? tokenType = null, string? scope = null) { }
    public void TokenRequestFailed(string clientId, string grantType, string error) { }
    public void TokenIntrospected(string clientId, bool isActive) { }
    public void TokenRevoked(string clientId, string tokenTypeHint) { }
    public void AuthenticationSuccess(string clientId, string method, string? idp = null) { }
    public void AuthenticationFailure(string clientId, string method, string error) { }
    public void MfaChallenge(string clientId, string method, bool success) { }
    public void ExternalAuthentication(string provider, bool success, string? error = null) { }
    public void AuthorizationRequest(string clientId, string responseType, bool success) { }
    public void ConsentGranted(string clientId, string subjectId, int scopeCount) { }
    public void ConsentDenied(string clientId, string subjectId) { }
    public void SetActiveSessions(long count, string? tenantId = null) { }
    public void SessionCreated(string clientId, string? tenantId = null) { }
    public void SessionEnded(string clientId, string reason, string? tenantId = null) { }
    public void ClientAuthenticated(string clientId, string method) { }
    public void ClientAuthenticationFailed(string clientId, string method, string error) { }
    public void DeviceAuthorizationRequest(string clientId) { }
    public void DeviceCodePoll(string clientId, string status) { }
    public void RecordRequestDuration(string endpoint, double durationMs, int statusCode) { }
    public void RecordDbOperationDuration(string operation, double durationMs, bool success) { }
    public void TenantOperation(string tenantId, string operation) { }
    public void SetActiveTenants(long count) { }
}
