using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Dto.SenderProfiles;

namespace Oluso.Core.Api;

/// <summary>
/// Workspace-facing read-only list of active sender profiles. Used by
/// surfaces that let an operator pick which profile to send through
/// (e.g. a campaign compose surface). Credentials are intentionally not part of
/// the response shape — admin manages them on /api/admin/sender-profiles.
/// </summary>
[Route("api/workspace/sender-profiles")]
public class WorkspaceSenderProfilesController : WorkspaceBaseController
{
    private readonly ITenantContext _tenantContext;
    private readonly ISenderProfileStore _store;

    public WorkspaceSenderProfilesController(
        ITenantContext tenantContext,
        ISenderProfileStore store) : base(tenantContext)
    {
        _tenantContext = tenantContext;
        _store = store;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkspaceSenderProfileDto>>> List(CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
            return Ok(Array.Empty<WorkspaceSenderProfileDto>());

        var profiles = await _store.ListAsync(_tenantContext.TenantId, cancellationToken);
        var dtos = profiles
            .Where(p => p.IsActive)
            .Select(p => new WorkspaceSenderProfileDto
            {
                Id = p.Id,
                Name = p.Name,
                FromAddress = p.FromAddress,
                FromName = p.FromName,
                IsDefault = p.IsDefault,
            })
            .ToList();
        return Ok(dtos);
    }
}

