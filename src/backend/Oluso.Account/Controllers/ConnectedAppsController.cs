using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for managing connected applications (OAuth consents)
/// </summary>
[Route("api/account/connected-apps")]
public class ConnectedAppsController : AccountBaseController
{
    private readonly IPersistedGrantStore _grantStore;
    private readonly IClientStore _clientStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<ConnectedAppsController> _logger;

    public ConnectedAppsController(
        ITenantContext tenantContext,
        IPersistedGrantStore grantStore,
        IClientStore clientStore,
        IOlusoEventService eventService,
        ILogger<ConnectedAppsController> logger) : base(tenantContext)
    {
        _grantStore = grantStore;
        _clientStore = clientStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all connected applications (apps with active grants/consents)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ConnectedAppListDto>> GetConnectedApps(CancellationToken cancellationToken)
    {
        var filter = new PersistedGrantFilter
        {
            SubjectId = UserId
        };

        var grants = await _grantStore.GetAllAsync(filter, cancellationToken);

        // Group by client ID
        var groupedByClient = grants
            .GroupBy(g => g.ClientId)
            .ToList();

        var connectedApps = new List<ConnectedAppDto>();

        foreach (var group in groupedByClient)
        {
            var clientId = group.Key;
            if (string.IsNullOrEmpty(clientId))
                continue;

            var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
            if (client == null)
                continue;

            var latestGrant = group.OrderByDescending(g => g.CreationTime).First();
            var grantTypes = group.Select(g => g.Type).Distinct().ToList();

            // Extract scopes from grants
            var scopes = new List<string>();
            foreach (var grant in group)
            {
                if (!string.IsNullOrEmpty(grant.Data))
                {
                    // Would parse grant data to extract scopes
                }
            }

            connectedApps.Add(new ConnectedAppDto
            {
                ClientId = clientId,
                ClientName = client.ClientName ?? clientId,
                ClientUri = client.ClientUri,
                LogoUri = client.LogoUri,
                Description = client.Description,
                FirstConnectedAt = group.Min(g => g.CreationTime),
                LastUsedAt = latestGrant.CreationTime,
                GrantTypes = grantTypes,
                Scopes = scopes,
                HasActiveTokens = group.Any(g => g.Type == "refresh_token" && (g.Expiration == null || g.Expiration > DateTime.UtcNow))
            });
        }

        return Ok(new ConnectedAppListDto
        {
            Apps = connectedApps,
            TotalCount = connectedApps.Count
        });
    }

    /// <summary>
    /// Get details for a specific connected application
    /// </summary>
    [HttpGet("{clientId}")]
    public async Task<ActionResult<ConnectedAppDetailDto>> GetConnectedApp(
        string clientId,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return NotFound(new { error = "Application not found" });
        }

        var filter = new PersistedGrantFilter
        {
            SubjectId = UserId,
            ClientId = clientId
        };

        var grants = await _grantStore.GetAllAsync(filter, cancellationToken);

        if (!grants.Any())
        {
            return NotFound(new { error = "No connection to this application" });
        }

        var grantDetails = grants.Select(g => new GrantDetailDto
        {
            Type = g.Type,
            CreatedAt = g.CreationTime,
            ExpiresAt = g.Expiration,
            SessionId = g.SessionId
        }).ToList();

        return Ok(new ConnectedAppDetailDto
        {
            ClientId = clientId,
            ClientName = client.ClientName ?? clientId,
            ClientUri = client.ClientUri,
            LogoUri = client.LogoUri,
            Description = client.Description,
            FirstConnectedAt = grants.Min(g => g.CreationTime),
            Grants = grantDetails,
            Scopes = new List<ScopeInfoDto>() // Would populate from consent store
        });
    }

    /// <summary>
    /// Revoke access for a connected application
    /// </summary>
    [HttpDelete("{clientId}")]
    public async Task<IActionResult> RevokeApp(string clientId, CancellationToken cancellationToken)
    {
        var filter = new PersistedGrantFilter
        {
            SubjectId = UserId,
            ClientId = clientId
        };

        var grants = await _grantStore.GetAllAsync(filter, cancellationToken);

        if (!grants.Any())
        {
            return NotFound(new { error = "No connection to this application" });
        }

        // Remove all grants for this client
        await _grantStore.RemoveAllAsync(filter, cancellationToken);

        _logger.LogInformation("User {UserId} revoked access for client {ClientId}", UserId, clientId);

        await _eventService.RaiseAsync(new AppAccessRevokedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            ClientId = clientId,
            RevokedGrantCount = grants.Count(),
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "Application access revoked" });
    }
}

#region DTOs

public class ConnectedAppListDto
{
    public List<ConnectedAppDto> Apps { get; set; } = new();
    public int TotalCount { get; set; }
}

public class ConnectedAppDto
{
    public string ClientId { get; set; } = null!;
    public string ClientName { get; set; } = null!;
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public string? Description { get; set; }
    public DateTime FirstConnectedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public List<string> GrantTypes { get; set; } = new();
    public List<string> Scopes { get; set; } = new();
    public bool HasActiveTokens { get; set; }
}

public class ConnectedAppDetailDto
{
    public string ClientId { get; set; } = null!;
    public string ClientName { get; set; } = null!;
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public string? Description { get; set; }
    public DateTime FirstConnectedAt { get; set; }
    public List<GrantDetailDto> Grants { get; set; } = new();
    public List<ScopeInfoDto> Scopes { get; set; } = new();
}

public class GrantDetailDto
{
    public string Type { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? SessionId { get; set; }
}

public class ScopeInfoDto
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
}

#endregion

#region Events

public class AppAccessRevokedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "AppAccessRevoked";
    public string? SubjectId { get; set; }
    public string? ClientId { get; set; }
    public int RevokedGrantCount { get; set; }
    public string? IpAddress { get; set; }
}

#endregion
