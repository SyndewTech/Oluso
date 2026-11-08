using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Admin.Dto.Resources;
using Oluso.Admin.Dto.Scopes;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing Identity Resources (OIDC scopes like openid, profile, email)
/// </summary>
[Route("api/admin/identity-resources")]
public class IdentityResourcesController : AdminBaseController
{
    private readonly IResourceStore _resourceStore;
    private readonly ILogger<IdentityResourcesController> _logger;

    public IdentityResourcesController(
        ITenantContext tenantContext,
        IResourceStore resourceStore,
        ILogger<IdentityResourcesController> logger)
        : base(tenantContext)
    {
        _resourceStore = resourceStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all identity resources
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.IdentityResourcesRead)]
    public async Task<ActionResult<IEnumerable<IdentityResourceDto>>> GetAll(CancellationToken cancellationToken)
    {
        var registered = (await _resourceStore.GetAllIdentityResourcesAsync(cancellationToken)).Select(MapToDto).ToList();
        var builtIn = OidcConstants.Scopes.StandardIdentity
            .Where(name => !registered.Any(r => string.Equals(r.Name, name, StringComparison.Ordinal)))
            .Select(name => new IdentityResourceDto
            {
                Name = name,
                DisplayName = name,
                Enabled = true,
                ShowInDiscoveryDocument = true,
                Required = name == OidcConstants.Scopes.OpenId,
                UserClaims = IdentityScopeClaimResolver.StandardScopeClaimTypes.TryGetValue(name, out var claims)
                    ? claims.ToList()
                    : new List<string>(),
                BuiltIn = true,
            });
        return Ok(builtIn.Concat(registered));
    }

    /// <summary>
    /// Get identity resource by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [RequirePermission(AdminPermissions.IdentityResourcesRead)]
    public async Task<ActionResult<IdentityResourceDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var resource = await _resourceStore.GetIdentityResourceByIdAsync(id, cancellationToken);
        if (resource == null)
            return NotFound();

        return Ok(MapToDto(resource));
    }

    /// <summary>
    /// Get identity resource by name
    /// </summary>
    [HttpGet("by-name/{name}")]
    [RequirePermission(AdminPermissions.IdentityResourcesRead)]
    public async Task<ActionResult<IdentityResourceDto>> GetByName(string name, CancellationToken cancellationToken)
    {
        var resources = await _resourceStore.FindIdentityResourcesByScopeNameAsync(new[] { name }, cancellationToken);
        var resource = resources.FirstOrDefault();
        if (resource == null)
            return NotFound();

        return Ok(MapToDto(resource));
    }

    /// <summary>
    /// Create a new identity resource
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.IdentityResourcesWrite)]
    public async Task<ActionResult<IdentityResourceDto>> Create(
        [FromBody] CreateIdentityResourceRequest request,
        CancellationToken cancellationToken)
    {
        var existingResources = await _resourceStore.FindIdentityResourcesByScopeNameAsync(new[] { request.Name }, cancellationToken);
        if (existingResources.Any())
        {
            return Conflict(new { error = $"Identity resource '{request.Name}' already exists" });
        }

        var resource = new IdentityResource
        {
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Description = request.Description,
            Required = request.Required ?? false,
            Emphasize = request.Emphasize ?? false,
            ShowInDiscoveryDocument = request.ShowInDiscoveryDocument ?? true,
            Enabled = request.Enabled ?? true,
            UserClaims = request.UserClaims?.Select(c => new IdentityResourceClaim { Type = c }).ToList()
                ?? new List<IdentityResourceClaim>()
        };

        var created = await _resourceStore.AddIdentityResourceAsync(resource, cancellationToken);

        _logger.LogInformation("Created identity resource: {Name}", created.Name);

        return CreatedAtAction(nameof(GetByName), new { name = created.Name }, MapToDto(created));
    }

    /// <summary>
    /// Update an identity resource
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(AdminPermissions.IdentityResourcesWrite)]
    public async Task<ActionResult<IdentityResourceDto>> Update(
        int id,
        [FromBody] IdentityResourceDto request,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetIdentityResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        existing.DisplayName = request.DisplayName;
        existing.Description = request.Description;
        existing.Required = request.Required;
        existing.Emphasize = request.Emphasize;
        existing.ShowInDiscoveryDocument = request.ShowInDiscoveryDocument;
        existing.Enabled = request.Enabled;

        existing.UserClaims.Clear();
        foreach (var claim in request.UserClaims)
        {
            existing.UserClaims.Add(new IdentityResourceClaim { Type = claim });
        }

        var updated = await _resourceStore.UpdateIdentityResourceAsync(existing, cancellationToken);

        _logger.LogInformation("Updated identity resource: {Name}", updated.Name);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete an identity resource
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(AdminPermissions.IdentityResourcesDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetIdentityResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        // Prevent deletion of standard OIDC scopes
        if (OidcConstants.Scopes.StandardIdentity.Append(OidcConstants.Scopes.OfflineAccess)
            .Contains(existing.Name, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Cannot delete standard OIDC scope" });
        }

        await _resourceStore.DeleteIdentityResourceAsync(id, cancellationToken);

        _logger.LogInformation("Deleted identity resource: {Name}", existing.Name);

        return NoContent();
    }

    private static IdentityResourceDto MapToDto(IdentityResource resource) => new()
    {
        Id = resource.Id,
        Name = resource.Name,
        DisplayName = resource.DisplayName,
        Description = resource.Description,
        Required = resource.Required,
        Emphasize = resource.Emphasize,
        ShowInDiscoveryDocument = resource.ShowInDiscoveryDocument,
        Enabled = resource.Enabled,
        UserClaims = resource.UserClaims.Select(c => c.Type).ToList(),
        Created = resource.Created,
        Updated = resource.Updated
    };
}
