using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols;
using Oluso.Core.Services;

namespace Oluso.Services;

public sealed class SignInAuditor : ISignInAuditor
{
    private const string UnknownClient = "unknown";

    private readonly IOlusoEventService _events;
    private readonly ITenantContext? _tenantContext;
    private readonly IProtocolStateStore? _protocolState;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<SignInAuditor> _logger;

    public SignInAuditor(
        IOlusoEventService events,
        ILogger<SignInAuditor> logger,
        ITenantContext? tenantContext = null,
        IProtocolStateStore? protocolState = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _events = events;
        _logger = logger;
        _tenantContext = tenantContext;
        _protocolState = protocolState;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task RecordSuccessAsync(SignInAudit audit, CancellationToken cancellationToken = default)
    {
        try
        {
            await _events.RaiseAsync(new UserSignedInEvent
            {
                TenantId = audit.TenantId ?? _tenantContext?.TenantId,
                SubjectId = audit.SubjectId,
                Username = audit.Username ?? audit.SubjectId,
                ClientId = await ResolveClientIdAsync(audit.ClientId, audit.ReturnUrl, cancellationToken),
                AuthenticationMethod = audit.AuthenticationMethod,
                IpAddress = audit.IpAddress ?? CurrentIpAddress(),
                SessionId = audit.SessionId
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record sign-in for subject {SubjectId}", audit.SubjectId);
        }
    }

    public async Task RecordFailureAsync(SignInFailureAudit audit, CancellationToken cancellationToken = default)
    {
        try
        {
            await _events.RaiseAsync(new UserSignInFailedEvent
            {
                TenantId = audit.TenantId ?? _tenantContext?.TenantId,
                Username = audit.Username,
                ClientId = await ResolveClientIdAsync(audit.ClientId, audit.ReturnUrl, cancellationToken),
                FailureReason = audit.FailureReason,
                IpAddress = audit.IpAddress ?? CurrentIpAddress()
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record sign-in failure for {Username}", audit.Username);
        }
    }

    private async Task<string> ResolveClientIdAsync(string? clientId, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(clientId))
        {
            return clientId;
        }

        var correlationId = ExtractCorrelationId(returnUrl);
        if (string.IsNullOrEmpty(correlationId) || _protocolState == null)
        {
            return UnknownClient;
        }

        var state = await _protocolState.GetAsync(correlationId, cancellationToken);
        return string.IsNullOrEmpty(state?.ClientId) ? UnknownClient : state.ClientId;
    }

    private static string? ExtractCorrelationId(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)) return null;
        var queryStart = returnUrl.IndexOf('?');
        if (queryStart < 0) return null;

        var query = QueryHelpers.ParseQuery(returnUrl[queryStart..]);
        return query.TryGetValue("correlation_id", out var value) ? value.ToString() : null;
    }

    private string? CurrentIpAddress()
    {
        var http = _httpContextAccessor?.HttpContext;
        if (http == null) return null;

        var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(forwarded) ? http.Connection.RemoteIpAddress?.ToString() : forwarded;
    }
}
