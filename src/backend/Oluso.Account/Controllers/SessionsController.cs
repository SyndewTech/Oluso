using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Account.Dto.Sessions;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Account.Events;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for managing user's active sessions
/// </summary>
[Route("api/account/sessions")]
public class SessionsController : AccountBaseController
{
    private readonly IPersistedGrantStore _grantStore;
    private readonly IOlusoEventService _eventService;
    private readonly IClientStore _clientStore;
    private readonly IServerSideSessionStore? _sessionStore;
    private readonly IResourceServerLogoutNotifier? _resourceServers;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ITenantContext tenantContext,
        IPersistedGrantStore grantStore,
        IOlusoEventService eventService,
        IClientStore clientStore,
        ILogger<SessionsController> logger,
        IServerSideSessionStore? sessionStore = null,
        IResourceServerLogoutNotifier? resourceServers = null) : base(tenantContext)
    {
        _grantStore = grantStore;
        _eventService = eventService;
        _clientStore = clientStore;
        _sessionStore = sessionStore;
        _resourceServers = resourceServers;
        _logger = logger;
    }

    /// <summary>
    /// Get all active sessions for the current user
    /// </summary>
    /// <summary>
    /// One row per login session: the server-side cookie session (where and when the
    /// user signed in) with the applications that hold live tokens under it. Refresh
    /// tokens issued outside a cookie session (older logins, non-browser flows) are
    /// listed by their own session id so they can still be revoked.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SessionListDto>> GetSessions(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var currentSessionId = User.FindFirst("sid")?.Value;

        var grants = await _grantStore.GetAllAsync(new PersistedGrantFilter
        {
            SubjectId = UserId,
            Type = "refresh_token"
        }, cancellationToken);

        // Rotated (one-time-use) tokens leave predecessors behind under the same
        // session until they expire: keep the newest live token per session + client.
        var liveGrants = grants
            .Where(g => g.Expiration == null || g.Expiration > now)
            .GroupBy(g => (Session: g.SessionId ?? g.Key, g.ClientId))
            .Select(group => group.OrderByDescending(g => g.CreationTime).First())
            .ToList();

        var clientNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var clientId in liveGrants.Select(g => g.ClientId).Distinct())
        {
            var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
            clientNames[clientId] = string.IsNullOrWhiteSpace(client?.ClientName) ? clientId : client!.ClientName!;
        }

        var grantsBySession = liveGrants
            .GroupBy(g => g.SessionId ?? g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var sessions = new List<SessionDto>();
        var covered = new HashSet<string>(StringComparer.Ordinal);

        if (_sessionStore != null)
        {
            var loginSessions = await _sessionStore.GetUserSessionsAsync(UserId, cancellationToken);
            foreach (var login in loginSessions.Where(l => l.Expires == null || l.Expires > now))
            {
                grantsBySession.TryGetValue(login.SessionId, out var sessionGrants);
                sessionGrants ??= new List<PersistedGrant>();
                covered.Add(login.SessionId);

                var clients = sessionGrants.Select(g => clientNames[g.ClientId]).Distinct().OrderBy(n => n).ToList();
                sessions.Add(new SessionDto
                {
                    Id = login.SessionId,
                    SessionId = login.SessionId,
                    ClientId = sessionGrants.Count == 1 ? sessionGrants[0].ClientId : null,
                    ClientName = clients.Count > 0 ? string.Join(", ", clients) : null,
                    Clients = clients,
                    CreatedAt = login.Created,
                    LastActivityAt = login.Renewed,
                    ExpiresAt = login.Expires,
                    IpAddress = login.IpAddress,
                    UserAgent = login.UserAgent,
                    IsCurrent = login.SessionId == currentSessionId
                });
            }
        }

        // Token sessions with no cookie session behind them.
        foreach (var (sessionId, sessionGrants) in grantsBySession.Where(kv => !covered.Contains(kv.Key)))
        {
            var newest = sessionGrants.OrderByDescending(g => g.CreationTime).First();
            var clients = sessionGrants.Select(g => clientNames[g.ClientId]).Distinct().OrderBy(n => n).ToList();
            sessions.Add(new SessionDto
            {
                Id = sessionId,
                SessionId = newest.SessionId,
                ClientId = sessionGrants.Count == 1 ? newest.ClientId : null,
                ClientName = string.Join(", ", clients),
                Clients = clients,
                CreatedAt = sessionGrants.Min(g => g.CreationTime),
                LastActivityAt = newest.CreationTime,
                ExpiresAt = sessionGrants.Max(g => g.Expiration),
                IsCurrent = newest.SessionId != null && newest.SessionId == currentSessionId
            });
        }

        sessions = sessions
            .OrderByDescending(s => s.IsCurrent)
            .ThenByDescending(s => s.LastActivityAt ?? s.CreatedAt)
            .ToList();

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

        var filter = new PersistedGrantFilter
        {
            SubjectId = UserId,
            SessionId = sessionId
        };
        var grants = await _grantStore.GetAllAsync(filter, cancellationToken);

        // A login session may exist without any app tokens yet; it is still revocable.
        var loginSession = _sessionStore == null ? null : await _sessionStore.GetBySessionIdAsync(sessionId, cancellationToken);
        if (loginSession != null && loginSession.SubjectId != UserId)
        {
            loginSession = null;
        }

        if (!grants.Any() && loginSession == null)
        {
            // Tokens issued before session ids were stamped are keyed by their own grant key.
            var byKey = await _grantStore.GetAsync(sessionId, cancellationToken);
            if (byKey == null || byKey.SubjectId != UserId)
            {
                return NotFound(new { error = "Session not found" });
            }
            await _grantStore.RemoveAsync(sessionId, cancellationToken);
        }
        else
        {
            if (grants.Any())
            {
                await _grantStore.RemoveAllAsync(filter, cancellationToken);
                // Resource servers keeping their own sessions (a Matrix homeserver) hear that this one ended.
                if (_resourceServers != null)
                    await _resourceServers.NotifyAsync(grants, cancellationToken);
            }
            // Ending the cookie session signs that browser out, not just its app tokens.
            if (loginSession != null && _sessionStore != null)
            {
                await _sessionStore.DeleteBySessionIdAsync(sessionId, cancellationToken);
            }
        }

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
            .Select(g => g.SessionId ?? g.Key)
            .Distinct()
            .ToList();

        // Revoke each other session's tokens
        foreach (var sessionId in otherSessionIds)
        {
            await _grantStore.RemoveAllAsync(new PersistedGrantFilter
            {
                SubjectId = UserId,
                SessionId = sessionId
            }, cancellationToken);
        }
        if (_resourceServers != null)
            await _resourceServers.NotifyAsync(grants.Where(g => g.SessionId != null && g.SessionId != currentSessionId), cancellationToken);
        // Grants that predate session ids can only be removed by key.
        foreach (var orphan in grants.Where(g => g.SessionId == null))
        {
            await _grantStore.RemoveAsync(orphan.Key, cancellationToken);
        }
        // End the other browser sessions as well, so those devices are signed out.
        if (_sessionStore != null)
        {
            var loginSessions = await _sessionStore.GetUserSessionsAsync(UserId, cancellationToken);
            foreach (var login in loginSessions.Where(l => l.SessionId != currentSessionId))
            {
                await _sessionStore.DeleteBySessionIdAsync(login.SessionId, cancellationToken);
                otherSessionIds.Add(login.SessionId);
            }
            otherSessionIds = otherSessionIds.Distinct().ToList();
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