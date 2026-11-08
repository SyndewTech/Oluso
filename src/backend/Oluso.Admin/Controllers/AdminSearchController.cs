using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Dto.Search;
using Oluso.Admin.Search;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Search across every record kind the caller may see. Results are grouped by
/// provider; a provider is consulted only when the tenant has its feature and
/// the caller holds its read permission.
/// </summary>
[Route("api/admin/search")]
public class AdminSearchController : AdminBaseController
{
    private readonly IAdminSearchService _search;

    public AdminSearchController(ITenantContext tenantContext, IAdminSearchService search) : base(tenantContext)
    {
        _search = search;
    }

    /// <summary>
    /// Search. Filters are passed as <c>filters[key]=value</c> and apply to the
    /// provider named by <paramref name="provider"/>.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AdminSearchResponse>> Search(
        [FromQuery] string q,
        [FromQuery] string? module = null,
        [FromQuery] string? provider = null,
        [FromQuery] int limit = AdminSearchDefaults.Limit,
        [FromQuery] Dictionary<string, string>? filters = null,
        CancellationToken cancellationToken = default)
    {
        var userId = AdminUserId;
        if (string.IsNullOrEmpty(userId)) return Forbid();

        var request = new AdminSearchRequest
        {
            Query = q ?? string.Empty,
            Module = module,
            ProviderId = provider,
            Filters = filters ?? new Dictionary<string, string>(),
            Limit = limit
        };
        return Ok(await _search.SearchAsync(request, User, userId, TenantContext.TenantId, cancellationToken));
    }

    /// <summary>Providers the caller may search, with their filters.</summary>
    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<AdminSearchProviderDto>>> GetProviders(CancellationToken cancellationToken)
    {
        var userId = AdminUserId;
        if (string.IsNullOrEmpty(userId)) return Forbid();
        return Ok(await _search.GetAvailableProvidersAsync(User, userId, TenantContext.TenantId, cancellationToken));
    }
}
