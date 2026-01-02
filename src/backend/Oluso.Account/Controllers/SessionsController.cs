using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for managing user's active sessions
/// </summary>
[Route("api/account/sessions")]
public class SessionsController : AccountBaseController
{
    private readonly IPersistedGrantStore _grantStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ITenantContext tenantContext,
        IPersistedGrantStore grantStore,
        IOlusoEventService eventService,
        ILogger<SessionsController> logger) : base(tenantContext)
    {
        _grantStore = grantStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all active sessions for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SessionListDto>> GetSessions(CancellationToken cancellationToken)
    {
        var filter = new PersistedGrantFilter
        {
            SubjectId = UserId,
            Type = "refresh_token" // Active sessions have refresh tokens
        };

        var grants = await _grantStore.GetAllAsync(filter, cancellationToken);

        // Get current session ID from token
        var currentSessionId = User.FindFirst("sid")?.Value;

        var sessions = grants.Select(g => new SessionDto
        {
            Id = g.Key,
            SessionId = g.SessionId,
            ClientId = g.ClientId,
            ClientName = g.Description, // Would need to look up client name
            CreatedAt = g.CreationTime,
            ExpiresAt = g.Expiration,
            LastActivityAt = g.CreationTime, // Would need activity tracking
            IpAddress = null, // Would need to store in grant data
            UserAgent = null, // Would need to store in grant data
            IsCurrent = g.SessionId == currentSessionId
        }).ToList();

        return Ok(new SessionListDto
        {
            Sessions = sessions,
            TotalCount = sessions.Count
        });
    }

    /// <summary>
    /// Revoke a specific session
    /// </summary>
    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> RevokeSession(string sessionId, CancellationToken cancellationToken)
    {
        // Get current session to prevent self-revocation (optional)
        var currentSessionId = User.FindFirst("sid")?.Value;

        if (sessionId == currentSessionId)
        {
            return BadRequest(new { error = "Cannot revoke current session. Use logout instead." });
        }

        // Find grants for this session
        var filter = new PersistedGrantFilter
        {
            SubjectId = UserId,
            SessionId = sessionId
        };

        var grants = await _grantStore.GetAllAsync(filter, cancellationToken);

        if (!grants.Any())
        {
            return NotFound(new { error = "Session not found" });
        }

        // Remove all grants for this session
        await _grantStore.RemoveAllAsync(filter, cancellationToken);

        _logger.LogInformation("User {UserId} revoked session {SessionId}", UserId, sessionId);

        await _eventService.RaiseAsync(new SessionRevokedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            SessionId = sessionId,
            RevokedBySessionId = currentSessionId,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "Session revoked" });
    }

    /// <summary>
    /// Revoke all sessions except current
    /// </summary>
    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken cancellationToken)
    {
        var currentSessionId = User.FindFirst("sid")?.Value;

        // Get all user's grants
        var filter = new PersistedGrantFilter
        {
            SubjectId = UserId
        };

        var grants = await _grantStore.GetAllAsync(filter, cancellationToken);

        var otherSessionIds = grants
            .Where(g => g.SessionId != currentSessionId)
            .Select(g => g.SessionId)
            .Distinct()
            .ToList();

        // Revoke each other session
        foreach (var sessionId in otherSessionIds)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                await _grantStore.RemoveAllAsync(new PersistedGrantFilter
                {
                    SubjectId = UserId,
                    SessionId = sessionId
                }, cancellationToken);
            }
        }

        _logger.LogInformation("User {UserId} revoked {Count} other sessions", UserId, otherSessionIds.Count);

        await _eventService.RaiseAsync(new AllSessionsRevokedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            RevokedCount = otherSessionIds.Count,
            CurrentSessionId = currentSessionId,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = $"Revoked {otherSessionIds.Count} sessions", count = otherSessionIds.Count });
    }
}

#region DTOs

public class SessionListDto
{
    public List<SessionDto> Sessions { get; set; } = new();
    public int TotalCount { get; set; }
}

public class SessionDto
{
    public string Id { get; set; } = null!;
    public string? SessionId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Location { get; set; }
    public bool IsCurrent { get; set; }
}

#endregion

#region Events

public class SessionRevokedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "SessionRevoked";
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string? RevokedBySessionId { get; set; }
    public string? IpAddress { get; set; }
}

public class AllSessionsRevokedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "AllSessionsRevoked";
    public string? SubjectId { get; set; }
    public int RevokedCount { get; set; }
    public string? CurrentSessionId { get; set; }
    public string? IpAddress { get; set; }
}

#endregion
