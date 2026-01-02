using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Dashboard API for admin UI statistics
/// </summary>
[Route("api/admin/dashboard")]
public class DashboardController : AdminBaseController
{
    private readonly IClientStore _clientStore;
    private readonly IResourceStore _resourceStore;
    private readonly IOlusoUserService _userService;
    private readonly IServerSideSessionStore _sessionStore;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IClientStore clientStore,
        IResourceStore resourceStore,
        IOlusoUserService userService,
        ILogger<DashboardController> logger,
        ITenantContext tenantContext,
        IServerSideSessionStore sessionStore,
        IAuditLogService auditLogService) : base(tenantContext)
    {
        _clientStore = clientStore;
        _resourceStore = resourceStore;
        _userService = userService;
        _sessionStore = sessionStore;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>
    /// Get dashboard statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats(CancellationToken cancellationToken)
    {
        var clients = await _clientStore.GetAllClientsAsync(cancellationToken);
        var apiResources = await _resourceStore.GetAllApiResourcesAsync(cancellationToken);
        var identityResources = await _resourceStore.GetAllIdentityResourcesAsync(cancellationToken);

        // Get user count
        var usersResult = await _userService.GetUsersAsync(new UsersQuery { Page = 1, PageSize = 1 }, cancellationToken);

        // Get active sessions count (optional - may not be configured)
        var activeSessionsCount = 0;
        if (_sessionStore != null)
        {
            var (_, totalSessions) = await _sessionStore.GetAllSessionsAsync(0, 1, cancellationToken);
            activeSessionsCount = totalSessions;
        }

        // Get recent logins count (last 24 hours)
        var recentLoginsCount = 0;
        if (_auditLogService?.IsEnabled == true)
        {
            var loginQuery = new AuditLogQuery
            {
                Action = "Login",
                StartDate = DateTime.UtcNow.AddHours(-24),
                Success = true,
                Page = 1,
                PageSize = 1
            };
            var loginResult = await _auditLogService.QueryAsync(loginQuery, cancellationToken);
            recentLoginsCount = loginResult.TotalCount;
        }

        var stats = new DashboardStatsDto
        {
            ClientsCount = clients.Count(),
            UsersCount = usersResult.TotalCount,
            ApiResourcesCount = apiResources.Count(),
            IdentityResourcesCount = identityResources.Count(),
            ActiveSessionsCount = activeSessionsCount,
            RecentLoginsCount = recentLoginsCount
        };

        return Ok(stats);
    }
}

public class DashboardStatsDto
{
    public int ClientsCount { get; set; }
    public int UsersCount { get; set; }
    public int ApiResourcesCount { get; set; }
    public int IdentityResourcesCount { get; set; }
    public int ActiveSessionsCount { get; set; }
    public int RecentLoginsCount { get; set; }
}
