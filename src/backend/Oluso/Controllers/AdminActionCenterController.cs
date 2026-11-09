using Microsoft.AspNetCore.Mvc;
using Oluso.Core.ActionCenter;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Controllers;

/// <summary>
/// Unified action center for the admin portal — aggregates pending items from all modules.
/// Workspace-only items are excluded; admin URLs are used where set.
/// </summary>
[Route("api/admin/action-center")]
public class AdminActionCenterController : AdminBaseController
{
    private readonly IActionCenterService _actionCenterService;

    public AdminActionCenterController(
        ITenantContext tenantContext,
        IActionCenterService actionCenterService)
        : base(tenantContext)
    {
        _actionCenterService = actionCenterService;
    }

    /// <summary>
    /// Get action items for the current admin user, grouped by provider.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ActionCenterResult>> GetItems(CancellationToken cancellationToken = default)
    {
        var result = await _actionCenterService.GetItemsForUserAsync(TenantId, UserId, cancellationToken);
        return Ok(WorkspaceActionCenterController.FilterAndResolve(result, portal: "admin"));
    }

    /// <summary>
    /// Get total count of admin-visible action items for the current user.
    /// </summary>
    [HttpGet("count")]
    public async Task<IActionResult> GetCount(CancellationToken cancellationToken = default)
    {
        var result = await _actionCenterService.GetItemsForUserAsync(TenantId, UserId, cancellationToken);
        var filtered = WorkspaceActionCenterController.FilterAndResolve(result, portal: "admin");
        return Ok(new { count = filtered.TotalCount });
    }
}
