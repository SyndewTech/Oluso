using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Oluso.Telemetry.OpenTelemetry;

/// <summary>
/// OpenTelemetry implementation of Oluso metrics
/// </summary>
public class OpenTelemetryMetrics : IOlusoMetrics
{
    public const string MeterName = "Oluso";

    private readonly Meter _meter;

    // Token metrics
    private readonly Counter<long> _tokensIssued;
    private readonly Counter<long> _tokenRequestsFailed;
    private readonly Counter<long> _tokensIntrospected;
    private readonly Counter<long> _tokensRevoked;

    // Authentication metrics
    private readonly Counter<long> _authenticationSuccesses;
    private readonly Counter<long> _authenticationFailures;
    private readonly Counter<long> _mfaChallenges;
    private readonly Counter<long> _externalAuthentications;

    // Authorization metrics
    private readonly Counter<long> _authorizationRequests;
    private readonly Counter<long> _consentsGranted;
    private readonly Counter<long> _consentsDenied;

    // Session metrics
    private readonly ObservableGauge<long> _activeSessions;
    private readonly Counter<long> _sessionsCreated;
    private readonly Counter<long> _sessionsEnded;
    private long _activeSessionCount;

    // Client metrics
    private readonly Counter<long> _clientAuthentications;
    private readonly Counter<long> _clientAuthenticationFailures;

    // Device flow metrics
    private readonly Counter<long> _deviceAuthRequests;
    private readonly Counter<long> _deviceCodePolls;

    // Performance metrics
    private readonly Histogram<double> _requestDuration;
    private readonly Histogram<double> _dbOperationDuration;

    // Tenant metrics
    private readonly Counter<long> _tenantOperations;
    private readonly ObservableGauge<long> _activeTenants;
    private long _activeTenantCount;

    public OpenTelemetryMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // Token metrics
        _tokensIssued = _meter.CreateCounter<long>(
            "oluso.tokens.issued",
            "tokens",
            "Number of tokens issued");

        _tokenRequestsFailed = _meter.CreateCounter<long>(
            "oluso.tokens.failed",
            "requests",
            "Number of failed token requests");

        _tokensIntrospected = _meter.CreateCounter<long>(
            "oluso.tokens.introspected",
            "tokens",
            "Number of tokens introspected");

        _tokensRevoked = _meter.CreateCounter<long>(
            "oluso.tokens.revoked",
            "tokens",
            "Number of tokens revoked");

        // Authentication metrics
        _authenticationSuccesses = _meter.CreateCounter<long>(
            "oluso.authentication.success",
            "authentications",
            "Number of successful authentications");

        _authenticationFailures = _meter.CreateCounter<long>(
            "oluso.authentication.failures",
            "authentications",
            "Number of failed authentication attempts");

        _mfaChallenges = _meter.CreateCounter<long>(
            "oluso.mfa.challenges",
            "challenges",
            "Number of MFA challenges");

        _externalAuthentications = _meter.CreateCounter<long>(
            "oluso.authentication.external",
            "authentications",
            "Number of external IdP authentications");

        // Authorization metrics
        _authorizationRequests = _meter.CreateCounter<long>(
            "oluso.authorization.requests",
            "requests",
            "Number of authorization requests");

        _consentsGranted = _meter.CreateCounter<long>(
            "oluso.consent.granted",
            "consents",
            "Number of consents granted");

        _consentsDenied = _meter.CreateCounter<long>(
            "oluso.consent.denied",
            "consents",
            "Number of consents denied");

        // Session metrics
        _activeSessions = _meter.CreateObservableGauge(
            "oluso.sessions.active",
            () => _activeSessionCount,
            "sessions",
            "Number of active sessions");

        _sessionsCreated = _meter.CreateCounter<long>(
            "oluso.sessions.created",
            "sessions",
            "Number of sessions created");

        _sessionsEnded = _meter.CreateCounter<long>(
            "oluso.sessions.ended",
            "sessions",
            "Number of sessions ended");

        // Client metrics
        _clientAuthentications = _meter.CreateCounter<long>(
            "oluso.client.authenticated",
            "authentications",
            "Number of client authentications");

        _clientAuthenticationFailures = _meter.CreateCounter<long>(
            "oluso.client.authentication_failed",
            "authentications",
            "Number of failed client authentications");

        // Device flow metrics
        _deviceAuthRequests = _meter.CreateCounter<long>(
            "oluso.device.authorization_requests",
            "requests",
            "Number of device authorization requests");

        _deviceCodePolls = _meter.CreateCounter<long>(
            "oluso.device.polls",
            "polls",
            "Number of device code polling requests");

        // Performance metrics
        _requestDuration = _meter.CreateHistogram<double>(
            "oluso.request.duration",
            "ms",
            "Request duration in milliseconds");

        _dbOperationDuration = _meter.CreateHistogram<double>(
            "oluso.database.duration",
            "ms",
            "Database operation duration in milliseconds");

        // Tenant metrics
        _tenantOperations = _meter.CreateCounter<long>(
            "oluso.tenant.operations",
            "operations",
            "Number of tenant operations");

        _activeTenants = _meter.CreateObservableGauge(
            "oluso.tenants.active",
            () => _activeTenantCount,
            "tenants",
            "Number of active tenants");
    }

    #region Token Metrics

    public void TokenIssued(string clientId, string grantType, string? tokenType = null, string? scope = null)
    {
        var tags = new TagList
        {
            { "client_id", clientId },
            { "grant_type", grantType }
        };

        if (!string.IsNullOrEmpty(tokenType))
            tags.Add("token_type", tokenType);

        _tokensIssued.Add(1, tags);
    }

    public void TokenRequestFailed(string clientId, string grantType, string error)
    {
        _tokenRequestsFailed.Add(1, new TagList
        {
            { "client_id", clientId },
            { "grant_type", grantType },
            { "error", error }
        });
    }

    public void TokenIntrospected(string clientId, bool isActive)
    {
        _tokensIntrospected.Add(1, new TagList
        {
            { "client_id", clientId },
            { "is_active", isActive.ToString().ToLower() }
        });
    }

    public void TokenRevoked(string clientId, string tokenTypeHint)
    {
        _tokensRevoked.Add(1, new TagList
        {
            { "client_id", clientId },
            { "token_type_hint", tokenTypeHint }
        });
    }

    #endregion

    #region Authentication Metrics

    public void AuthenticationSuccess(string clientId, string method, string? idp = null)
    {
        var tags = new TagList
        {
            { "client_id", clientId },
            { "method", method }
        };

        if (!string.IsNullOrEmpty(idp))
            tags.Add("idp", idp);

        _authenticationSuccesses.Add(1, tags);
    }

    public void AuthenticationFailure(string clientId, string method, string error)
    {
        _authenticationFailures.Add(1, new TagList
        {
            { "client_id", clientId },
            { "method", method },
            { "error", error }
        });
    }

    public void MfaChallenge(string clientId, string method, bool success)
    {
        _mfaChallenges.Add(1, new TagList
        {
            { "client_id", clientId },
            { "method", method },
            { "success", success.ToString().ToLower() }
        });
    }

    public void ExternalAuthentication(string provider, bool success, string? error = null)
    {
        var tags = new TagList
        {
            { "provider", provider },
            { "success", success.ToString().ToLower() }
        };

        if (!string.IsNullOrEmpty(error))
            tags.Add("error", error);

        _externalAuthentications.Add(1, tags);
    }

    #endregion

    #region Authorization Metrics

    public void AuthorizationRequest(string clientId, string responseType, bool success)
    {
        _authorizationRequests.Add(1, new TagList
        {
            { "client_id", clientId },
            { "response_type", responseType },
            { "success", success.ToString().ToLower() }
        });
    }

    public void ConsentGranted(string clientId, string subjectId, int scopeCount)
    {
        _consentsGranted.Add(1, new TagList
        {
            { "client_id", clientId },
            { "scope_count", scopeCount.ToString() }
        });
    }

    public void ConsentDenied(string clientId, string subjectId)
    {
        _consentsDenied.Add(1, new TagList
        {
            { "client_id", clientId }
        });
    }

    #endregion

    #region Session Metrics

    public void SetActiveSessions(long count, string? tenantId = null)
    {
        _activeSessionCount = count;
    }

    public void SessionCreated(string clientId, string? tenantId = null)
    {
        var tags = new TagList { { "client_id", clientId } };
        if (!string.IsNullOrEmpty(tenantId))
            tags.Add("tenant_id", tenantId);

        _sessionsCreated.Add(1, tags);
        Interlocked.Increment(ref _activeSessionCount);
    }

    public void SessionEnded(string clientId, string reason, string? tenantId = null)
    {
        var tags = new TagList
        {
            { "client_id", clientId },
            { "reason", reason }
        };
        if (!string.IsNullOrEmpty(tenantId))
            tags.Add("tenant_id", tenantId);

        _sessionsEnded.Add(1, tags);
        Interlocked.Decrement(ref _activeSessionCount);
    }

    #endregion

    #region Client Metrics

    public void ClientAuthenticated(string clientId, string method)
    {
        _clientAuthentications.Add(1, new TagList
        {
            { "client_id", clientId },
            { "method", method }
        });
    }

    public void ClientAuthenticationFailed(string clientId, string method, string error)
    {
        _clientAuthenticationFailures.Add(1, new TagList
        {
            { "client_id", clientId },
            { "method", method },
            { "error", error }
        });
    }

    #endregion

    #region Device Flow Metrics

    public void DeviceAuthorizationRequest(string clientId)
    {
        _deviceAuthRequests.Add(1, new TagList { { "client_id", clientId } });
    }

    public void DeviceCodePoll(string clientId, string status)
    {
        _deviceCodePolls.Add(1, new TagList
        {
            { "client_id", clientId },
            { "status", status }
        });
    }

    #endregion

    #region Performance Metrics

    public void RecordRequestDuration(string endpoint, double durationMs, int statusCode)
    {
        _requestDuration.Record(durationMs, new TagList
        {
            { "endpoint", endpoint },
            { "status_code", statusCode.ToString() }
        });
    }

    public void RecordDbOperationDuration(string operation, double durationMs, bool success)
    {
        _dbOperationDuration.Record(durationMs, new TagList
        {
            { "operation", operation },
            { "success", success.ToString().ToLower() }
        });
    }

    #endregion

    #region Tenant Metrics

    public void TenantOperation(string tenantId, string operation)
    {
        _tenantOperations.Add(1, new TagList
        {
            { "tenant_id", tenantId },
            { "operation", operation }
        });
    }

    public void SetActiveTenants(long count)
    {
        _activeTenantCount = count;
    }

    #endregion
}
