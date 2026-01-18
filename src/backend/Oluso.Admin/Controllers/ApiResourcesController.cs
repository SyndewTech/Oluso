using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing Resources (RFC 8707).
/// Resources are protected APIs identified by absolute URIs.
/// </summary>
[Route("api/admin/resources")]
public class ResourcesController : AdminBaseController
{
    private readonly IResourceStore _resourceStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<ResourcesController> _logger;

    public ResourcesController(
        ITenantContext tenantContext,
        IResourceStore resourceStore,
        IOlusoEventService eventService,
        ILogger<ResourcesController> logger)
        : base(tenantContext)
    {
        _resourceStore = resourceStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all resources
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.ResourcesRead)]
    public async Task<ActionResult<IEnumerable<ResourceDto>>> GetAll(CancellationToken cancellationToken)
    {
        var resources = await _resourceStore.GetAllResourcesAsync(cancellationToken);
        var dtos = resources.Select(MapToDto);
        return Ok(dtos);
    }

    /// <summary>
    /// Get resource by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [RequirePermission(AdminPermissions.ResourcesRead)]
    public async Task<ActionResult<ResourceDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var resource = await _resourceStore.GetResourceByIdAsync(id, cancellationToken);
        if (resource == null)
            return NotFound();

        return Ok(MapToDto(resource));
    }

    /// <summary>
    /// Get resource by URI (RFC 8707)
    /// </summary>
    [HttpGet("by-uri")]
    [RequirePermission(AdminPermissions.ResourcesRead)]
    public async Task<ActionResult<ResourceDto>> GetByUri([FromQuery] string uri, CancellationToken cancellationToken)
    {
        var resource = await _resourceStore.FindResourceByUriAsync(uri, cancellationToken);
        if (resource == null)
            return NotFound();

        return Ok(MapToDto(resource));
    }

    /// <summary>
    /// Create a new resource (RFC 8707)
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.ResourcesWrite)]
    public async Task<ActionResult<ResourceDto>> Create(
        [FromBody] CreateResourceRequest request,
        CancellationToken cancellationToken)
    {
        // Validate URI format per RFC 8707
        if (!Uri.TryCreate(request.Uri, UriKind.Absolute, out var uri))
        {
            return BadRequest(new { error = "URI must be an absolute URI" });
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            return BadRequest(new { error = "URI must not contain a fragment component (RFC 8707)" });
        }

        // Check for existing resource with same URI
        var existing = await _resourceStore.FindResourceByUriAsync(request.Uri, cancellationToken);
        if (existing != null)
        {
            return Conflict(new { error = $"Resource with URI '{request.Uri}' already exists" });
        }

        // Validate user claims don't contain protected claim types
        var claimValidationError = ValidateUserClaims(request.UserClaims);
        if (claimValidationError != null)
        {
            return BadRequest(new { error = claimValidationError });
        }

        var resource = new Resource
        {
            Uri = request.Uri,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Enabled = request.Enabled ?? true,
            ShowInDiscoveryDocument = request.ShowInDiscoveryDocument ?? true,
            AllowedScopes = request.AllowedScopes?.Select(s => new ResourceScope { Scope = s }).ToList()
                ?? new List<ResourceScope>(),
            UserClaims = request.UserClaims?.Select(c => new ResourceClaim { Type = c }).ToList()
                ?? new List<ResourceClaim>()
        };

        var created = await _resourceStore.AddResourceAsync(resource, cancellationToken);

        _logger.LogInformation("Created resource: {Uri}", created.Uri);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminResourceCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = created.Id.ToString(),
            ResourceName = created.DisplayName ?? created.Uri,
            ResourceUri = created.Uri
        }, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
    }

    /// <summary>
    /// Update a resource
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(AdminPermissions.ResourcesWrite)]
    public async Task<ActionResult<ResourceDto>> Update(
        int id,
        [FromBody] UpdateResourceRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        // Validate user claims don't contain protected claim types
        var claimValidationError = ValidateUserClaims(request.UserClaims);
        if (claimValidationError != null)
        {
            return BadRequest(new { error = claimValidationError });
        }

        if (request.DisplayName != null) existing.DisplayName = request.DisplayName;
        if (request.Description != null) existing.Description = request.Description;
        if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;
        if (request.ShowInDiscoveryDocument.HasValue) existing.ShowInDiscoveryDocument = request.ShowInDiscoveryDocument.Value;

        if (request.AllowedScopes != null)
        {
            existing.AllowedScopes.Clear();
            foreach (var scope in request.AllowedScopes)
            {
                existing.AllowedScopes.Add(new ResourceScope { Scope = scope });
            }
        }

        if (request.UserClaims != null)
        {
            existing.UserClaims.Clear();
            foreach (var claim in request.UserClaims)
            {
                existing.UserClaims.Add(new ResourceClaim { Type = claim });
            }
        }

        var updated = await _resourceStore.UpdateResourceAsync(existing, cancellationToken);

        _logger.LogInformation("Updated resource: {Uri}", updated.Uri);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminResourceUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = updated.Id.ToString(),
            ResourceName = updated.DisplayName ?? updated.Uri,
            ResourceUri = updated.Uri
        }, cancellationToken);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete a resource
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(AdminPermissions.ResourcesDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        await _resourceStore.DeleteResourceAsync(id, cancellationToken);

        _logger.LogInformation("Deleted resource: {Uri}", existing.Uri);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminResourceDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = id.ToString(),
            ResourceName = existing.DisplayName ?? existing.Uri,
            ResourceUri = existing.Uri
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Add a scope to a resource
    /// </summary>
    [HttpPost("{id:int}/scopes")]
    [RequirePermission(AdminPermissions.ResourcesWrite)]
    public async Task<ActionResult<ResourceDto>> AddScope(
        int id,
        [FromBody] AddScopeRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        // Check if scope already exists
        if (existing.AllowedScopes.Any(s => s.Scope == request.ScopeName))
        {
            return Conflict(new { error = $"Scope '{request.ScopeName}' is already assigned to this resource" });
        }

        existing.AllowedScopes.Add(new ResourceScope { Scope = request.ScopeName });
        var updated = await _resourceStore.UpdateResourceAsync(existing, cancellationToken);

        _logger.LogInformation("Added scope '{Scope}' to resource: {Uri}", request.ScopeName, updated.Uri);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Remove a scope from a resource
    /// </summary>
    [HttpDelete("{id:int}/scopes/{scopeName}")]
    [RequirePermission(AdminPermissions.ResourcesWrite)]
    public async Task<ActionResult<ResourceDto>> RemoveScope(
        int id,
        string scopeName,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        var scope = existing.AllowedScopes.FirstOrDefault(s => s.Scope == scopeName);
        if (scope == null)
        {
            return NotFound(new { error = $"Scope '{scopeName}' is not assigned to this resource" });
        }

        existing.AllowedScopes.Remove(scope);
        var updated = await _resourceStore.UpdateResourceAsync(existing, cancellationToken);

        _logger.LogInformation("Removed scope '{Scope}' from resource: {Uri}", scopeName, updated.Uri);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Get all available API scopes that can be assigned to resources
    /// </summary>
    [HttpGet("available-scopes")]
    [RequirePermission(AdminPermissions.ScopesRead)]
    public async Task<ActionResult<IEnumerable<ApiScopeSummaryDto>>> GetAvailableScopes(CancellationToken cancellationToken)
    {
        var scopes = await _resourceStore.GetAllApiScopesAsync(cancellationToken);
        var summaries = scopes.Select(s => new ApiScopeSummaryDto
        {
            Name = s.Name,
            DisplayName = s.DisplayName,
            Description = s.Description
        });
        return Ok(summaries);
    }

    private static ResourceDto MapToDto(Resource resource) => new()
    {
        Id = resource.Id,
        Uri = resource.Uri,
        DisplayName = resource.DisplayName,
        Description = resource.Description,
        Enabled = resource.Enabled,
        ShowInDiscoveryDocument = resource.ShowInDiscoveryDocument,
        AllowedScopes = resource.AllowedScopes.Select(s => s.Scope).ToList(),
        UserClaims = resource.UserClaims.Select(c => c.Type).ToList(),
        Created = resource.Created,
        Updated = resource.Updated
    };

    /// <summary>
    /// Validates that user claims don't contain protected claim types.
    /// Protected claims include "permissions", "role", "tenant_id", etc.
    /// Returns an error message if validation fails, null if successful.
    /// </summary>
    private static string? ValidateUserClaims(ICollection<string>? claims)
    {
        if (claims == null || claims.Count == 0)
        {
            return null;
        }

        var protectedClaims = claims
            .Where(c => ReservedClaimTypes.IsProtectedFromClientClaims(c))
            .Distinct()
            .ToList();

        if (protectedClaims.Count > 0)
        {
            return $"The following claim types are protected and cannot be added to resources: {string.Join(", ", protectedClaims)}";
        }

        return null;
    }
}

#region DTOs

public class ResourceDto
{
    public int Id { get; set; }
    /// <summary>
    /// The absolute URI identifying this resource (RFC 8707)
    /// </summary>
    public string Uri { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public bool ShowInDiscoveryDocument { get; set; }
    public List<string> AllowedScopes { get; set; } = new();
    public List<string> UserClaims { get; set; } = new();
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreateResourceRequest
{
    /// <summary>
    /// The absolute URI identifying this resource (RFC 8707).
    /// Must be a valid absolute URI without a fragment component.
    /// </summary>
    public string Uri { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    /// <summary>
    /// Scopes that are valid for this resource.
    /// If empty, all scopes are allowed.
    /// </summary>
    public List<string>? AllowedScopes { get; set; }
    public List<string>? UserClaims { get; set; }
}

public class UpdateResourceRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    public List<string>? AllowedScopes { get; set; }
    public List<string>? UserClaims { get; set; }
}

public class AddScopeRequest
{
    public string ScopeName { get; set; } = null!;
}

public class ApiScopeSummaryDto
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}

#endregion

