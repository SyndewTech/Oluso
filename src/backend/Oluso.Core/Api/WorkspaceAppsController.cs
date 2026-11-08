using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Api;

/// <summary>
/// Workspace apps endpoint — returns the current tenant's workspace apps settings.
/// Accessible by any authenticated workspace user (no tenantId parameter needed,
/// resolved from the token automatically).
/// </summary>
[Route("api/workspace/apps")]
public class WorkspaceAppsController : WorkspaceBaseController
{
    private readonly ITenantSettingsProvider _settingsProvider;

    public WorkspaceAppsController(
        ITenantContext tenantContext,
        ITenantSettingsProvider settingsProvider) : base(tenantContext)
    {
        _settingsProvider = settingsProvider;
    }

    /// <summary>
    /// Get the current tenant's workspace apps settings
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<WorkspaceAppsSettings>> GetWorkspaceApps(CancellationToken cancellationToken)
    {
        var settings = await _settingsProvider.GetWorkspaceAppsSettingsAsync(cancellationToken);
        return Ok(settings);
    }
}
