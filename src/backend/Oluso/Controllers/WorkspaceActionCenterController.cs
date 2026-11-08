using Microsoft.AspNetCore.Mvc;
using Oluso.Core.ActionCenter;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Controllers;

/// <summary>
/// Unified action center for the workspace — aggregates pending items from all modules
/// </summary>
[Route("api/workspace/action-center")]
public class WorkspaceActionCenterController : WorkspaceBaseController
{
    private readonly IActionCenterService _actionCenterService;

    public WorkspaceActionCenterController(
        ITenantContext tenantContext,
        IActionCenterService actionCenterService)
        : base(tenantContext)
    {
        _actionCenterService = actionCenterService;
    }

    /// <summary>
    /// Get action items for the current user visible in the workspace portal.
    /// Admin-only items are excluded; workspace URLs are used where set.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ActionCenterResult>> GetItems(CancellationToken cancellationToken = default)
    {
        var result = await _actionCenterService.GetItemsForUserAsync(TenantId, UserId, cancellationToken);
        return Ok(FilterAndResolve(result, portal: "workspace"));
    }

    /// <summary>
    /// Get total count of workspace-visible action items for the current user.
    /// </summary>
    [HttpGet("count")]
    public async Task<IActionResult> GetCount(CancellationToken cancellationToken = default)
    {
        var result = await _actionCenterService.GetItemsForUserAsync(TenantId, UserId, cancellationToken);
        var filtered = FilterAndResolve(result, portal: "workspace");
        return Ok(new { count = filtered.TotalCount });
    }

    internal static ActionCenterResult FilterAndResolve(ActionCenterResult result, string portal)
    {
        var filtered = new ActionCenterResult();
        foreach (var group in result.Groups)
        {
            var items = group.Items
                .Where(i => portal == "admin"
                    ? i.Scope != ActionItemScope.WorkspaceOnly
                    : i.Scope != ActionItemScope.AdminOnly)
                .Select(i =>
                {
                    i.ActionUrl = portal == "admin"
                        ? i.AdminActionUrl ?? i.ActionUrl
                        : i.WorkspaceActionUrl ?? i.ActionUrl;
                    return i;
                })
                .ToList();

            if (items.Count == 0) continue;

            filtered.Groups.Add(new ActionCenterGroup
            {
                ProviderId = group.ProviderId,
                DisplayName = group.DisplayName,
                Icon = group.Icon,
                Count = items.Count,
                Items = items,
            });
            filtered.TotalCount += items.Count;
        }
        return filtered;
    }
}
