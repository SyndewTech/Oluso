using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing persisted grants and sessions
/// </summary>
[Route("api/admin/grants")]
public class GrantsController : AdminBaseController
{
    private readonly IPersistedGrantStore _grantStore;
    private readonly IServerSideSessionStore? _sessionStore;
    private readonly IBackchannelLogoutService? _backchannelLogoutService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<GrantsController> _logger;

    public GrantsController(
        IPersistedGrantStore grantStore,
        ITenantContext tenantContext,
        ILogger<GrantsController> logger,
        IServerSideSessionStore? sessionStore = null,
        IBackchannelLogoutService? backchannelLogoutService = null) : base(tenantContext)
    {
        _grantStore = grantStore;
        _sessionStore = sessionStore;
        _backchannelLogoutService = backchannelLogoutService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all persisted grants with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<PersistedGrantDto>>> GetGrants(
        [FromQuery] string? subjectId,
        [FromQuery] string? clientId,
        [FromQuery] string? type,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new PersistedGrantFilter
        {
            SubjectId = subjectId,
            ClientId = clientId,
            Type = type
        };

        var grants = await _grantStore.GetAllAsync(filter, cancellationToken);
        var grantsList = grants.ToList();

        var paged = grantsList
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto)
            .ToList();

        return Ok(new PaginatedResult<PersistedGrantDto>
        {
            Items = paged,
            TotalCount = grantsList.Count,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(grantsList.Count / (double)pageSize)
        });
    }

    /// <summary>
    /// Get a specific persisted grant by key
    /// </summary>
    [HttpGet("{key}")]
    public async Task<ActionResult<PersistedGrantDto>> GetGrant(string key, CancellationToken cancellationToken)
    {
        var grant = await _grantStore.GetAsync(key, cancellationToken);
        if (grant == null)
            return NotFound();

        return Ok(MapToDto(grant));
    }

    /// <summary>
    /// Revoke a persisted grant
    /// </summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> RevokeGrant(string key, CancellationToken cancellationToken)
    {
        await _grantStore.RemoveAsync(key, cancellationToken);
        _logger.LogInformation("Revoked persisted grant: {Key}", key);
        return NoContent();
    }

    /// <summary>
    /// Revoke all grants for a subject
    /// </summary>
    [HttpDelete("by-subject/{subjectId}")]
    public async Task<IActionResult> RevokeGrantsBySubject(string subjectId, CancellationToken cancellationToken)
    {
        await _grantStore.RemoveAllAsync(new PersistedGrantFilter { SubjectId = subjectId }, cancellationToken);
        _logger.LogInformation("Revoked all grants for subject: {SubjectId}", subjectId);
        return NoContent();
    }

    /// <summary>
    /// Revoke all grants for a client
    /// </summary>
    [HttpDelete("by-client/{clientId}")]
    public async Task<IActionResult> RevokeGrantsByClient(string clientId, CancellationToken cancellationToken)
    {
        await _grantStore.RemoveAllAsync(new PersistedGrantFilter { ClientId = clientId }, cancellationToken);
        _logger.LogInformation("Revoked all grants for client: {ClientId}", clientId);
        return NoContent();
    }

    #region Sessions

    /// <summary>
    /// Get all server-side sessions with optional filtering
    /// </summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<PaginatedResult<ServerSideSessionDto>>> GetSessions(
        [FromQuery] string? subjectId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (_sessionStore == null)
        {
            return Ok(new PaginatedResult<ServerSideSessionDto>
            {
                Items = new List<ServerSideSessionDto>(),
                TotalCount = 0,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = 0
            });
        }

        IReadOnlyList<ServerSideSession> sessions;
        int totalCount;

        if (!string.IsNullOrEmpty(subjectId))
        {
            sessions = await _sessionStore.GetSessionsBySubjectAsync(subjectId, cancellationToken);
            totalCount = sessions.Count;
            sessions = sessions
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
        else
        {
            // Get all sessions with pagination
            var result = await _sessionStore.GetAllSessionsAsync(
                skip: (pageNumber - 1) * pageSize,
                take: pageSize,
                cancellationToken);
            sessions = result.Sessions;
            totalCount = result.TotalCount;
        }

        var items = sessions.Select(MapSessionToDto).ToList();

        return Ok(new PaginatedResult<ServerSideSessionDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Get a specific session by key
    /// </summary>
    [HttpGet("sessions/{key}")]
    public async Task<ActionResult<ServerSideSessionDto>> GetSession(string key, CancellationToken cancellationToken)
    {
        if (_sessionStore == null)
            return NotFound("Server-side sessions not enabled");

        var session = await _sessionStore.GetSessionAsync(key, cancellationToken);
        if (session == null)
            return NotFound();

        return Ok(MapSessionToDto(session));
    }

    /// <summary>
    /// Revoke a session by key (with optional backchannel logout)
    /// </summary>
    [HttpDelete("sessions/{key}")]
    public async Task<IActionResult> RevokeSession(
        string key,
        [FromQuery] bool sendBackchannelLogout = true,
        CancellationToken cancellationToken = default)
    {
        if (_sessionStore == null)
            return NotFound("Server-side sessions not enabled");

        var session = await _sessionStore.GetSessionAsync(key, cancellationToken);
        if (session == null)
            return NotFound();

        // Send backchannel logout notifications if requested
        if (sendBackchannelLogout && _backchannelLogoutService != null && !string.IsNullOrEmpty(session.SubjectId))
        {
            try
            {
                await _backchannelLogoutService.SendLogoutNotificationsAsync(
                    session.SubjectId, session.SessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send backchannel logout for session {Key}", key);
            }
        }

        await _sessionStore.DeleteSessionAsync(key, cancellationToken);
        _logger.LogInformation("Revoked session: {Key} for subject {SubjectId}", key, session.SubjectId);
        return NoContent();
    }

    /// <summary>
    /// Revoke all sessions for a subject (with optional backchannel logout)
    /// </summary>
    [HttpDelete("sessions/by-subject/{subjectId}")]
    public async Task<IActionResult> RevokeSessionsBySubject(
        string subjectId,
        [FromQuery] bool sendBackchannelLogout = true,
        CancellationToken cancellationToken = default)
    {
        if (_sessionStore == null)
            return NotFound("Server-side sessions not enabled");

        // Send backchannel logout notifications if requested
        if (sendBackchannelLogout && _backchannelLogoutService != null)
        {
            try
            {
                await _backchannelLogoutService.SendLogoutNotificationsAsync(subjectId, null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send backchannel logout for subject {SubjectId}", subjectId);
            }
        }

        await _sessionStore.DeleteSessionsBySubjectAsync(subjectId, cancellationToken);
        _logger.LogInformation("Revoked all sessions for subject: {SubjectId}", subjectId);
        return NoContent();
    }

    #endregion

    private static PersistedGrantDto MapToDto(PersistedGrant grant) => new()
    {
        Key = grant.Key,
        Type = grant.Type,
        SubjectId = grant.SubjectId,
        SessionId = grant.SessionId,
        ClientId = grant.ClientId,
        Description = grant.Description,
        CreationTime = grant.CreationTime,
        Expiration = grant.Expiration,
        ConsumedTime = grant.ConsumedTime
    };

    private static ServerSideSessionDto MapSessionToDto(ServerSideSession session) => new()
    {
        Id = session.Id,
        Key = session.Key,
        Scheme = session.Scheme,
        SubjectId = session.SubjectId,
        SessionId = session.SessionId,
        DisplayName = session.DisplayName,
        Created = session.Created,
        Renewed = session.Renewed,
        Expires = session.Expires
    };
}

#region DTOs

public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class PersistedGrantDto
{
    public string Key { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string ClientId { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? Expiration { get; set; }
    public DateTime? ConsumedTime { get; set; }
}

public class ServerSideSessionDto
{
    public int Id { get; set; }
    public string Key { get; set; } = null!;
    public string Scheme { get; set; } = null!;
    public string SubjectId { get; set; } = null!;
    public string? SessionId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime Created { get; set; }
    public DateTime Renewed { get; set; }
    public DateTime? Expires { get; set; }
}

#endregion
