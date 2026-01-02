using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing API Resources
/// </summary>
[Route("api/admin/api-resources")]
public class ApiResourcesController : AdminBaseController
{
    private readonly IResourceStore _resourceStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<ApiResourcesController> _logger;

    public ApiResourcesController(
        ITenantContext tenantContext,
        IResourceStore resourceStore,
        IOlusoEventService eventService,
        ILogger<ApiResourcesController> logger)
        : base(tenantContext)
    {
        _resourceStore = resourceStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all API resources
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApiResourceDto>>> GetAll(CancellationToken cancellationToken)
    {
        var resources = await _resourceStore.GetAllApiResourcesAsync(cancellationToken);
        var dtos = resources.Select(MapToDto);
        return Ok(dtos);
    }

    /// <summary>
    /// Get API resource by ID
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResourceDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var resource = await _resourceStore.GetApiResourceByIdAsync(id, cancellationToken);
        if (resource == null)
            return NotFound();

        return Ok(MapToDto(resource));
    }

    /// <summary>
    /// Get API resource by name
    /// </summary>
    [HttpGet("by-name/{name}")]
    public async Task<ActionResult<ApiResourceDto>> GetByName(string name, CancellationToken cancellationToken)
    {
        var resources = await _resourceStore.FindApiResourcesByNameAsync(new[] { name }, cancellationToken);
        var resource = resources.FirstOrDefault();
        if (resource == null)
            return NotFound();

        return Ok(MapToDto(resource));
    }

    /// <summary>
    /// Create a new API resource
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResourceDto>> Create(
        [FromBody] CreateApiResourceRequest request,
        CancellationToken cancellationToken)
    {
        var existingResources = await _resourceStore.FindApiResourcesByNameAsync(new[] { request.Name }, cancellationToken);
        if (existingResources.Any())
        {
            return Conflict(new { error = $"API resource '{request.Name}' already exists" });
        }

        var resource = new ApiResource
        {
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Enabled = request.Enabled ?? true,
            ShowInDiscoveryDocument = request.ShowInDiscoveryDocument ?? true,
            AllowedAccessTokenSigningAlgorithms = request.AllowedAccessTokenSigningAlgorithms,
            RequireResourceIndicator = request.RequireResourceIndicator ?? false,
            Scopes = request.Scopes?.Select(s => new ApiResourceScope { Scope = s }).ToList()
                ?? new List<ApiResourceScope>(),
            UserClaims = request.UserClaims?.Select(c => new ApiResourceClaim { Type = c }).ToList()
                ?? new List<ApiResourceClaim>()
        };

        var created = await _resourceStore.AddApiResourceAsync(resource, cancellationToken);

        _logger.LogInformation("Created API resource: {Name}", created.Name);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminApiResourceCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = created.Id.ToString(),
            ResourceName = created.Name,
            ApiResourceName = created.Name
        }, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
    }

    /// <summary>
    /// Update an API resource
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResourceDto>> Update(
        int id,
        [FromBody] UpdateApiResourceRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetApiResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        if (request.DisplayName != null) existing.DisplayName = request.DisplayName;
        if (request.Description != null) existing.Description = request.Description;
        if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;
        if (request.ShowInDiscoveryDocument.HasValue) existing.ShowInDiscoveryDocument = request.ShowInDiscoveryDocument.Value;
        if (request.AllowedAccessTokenSigningAlgorithms != null) existing.AllowedAccessTokenSigningAlgorithms = request.AllowedAccessTokenSigningAlgorithms;
        if (request.RequireResourceIndicator.HasValue) existing.RequireResourceIndicator = request.RequireResourceIndicator.Value;

        if (request.Scopes != null)
        {
            existing.Scopes.Clear();
            foreach (var scope in request.Scopes)
            {
                existing.Scopes.Add(new ApiResourceScope { Scope = scope });
            }
        }

        if (request.UserClaims != null)
        {
            existing.UserClaims.Clear();
            foreach (var claim in request.UserClaims)
            {
                existing.UserClaims.Add(new ApiResourceClaim { Type = claim });
            }
        }

        var updated = await _resourceStore.UpdateApiResourceAsync(existing, cancellationToken);

        _logger.LogInformation("Updated API resource: {Name}", updated.Name);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminApiResourceUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = updated.Id.ToString(),
            ResourceName = updated.Name,
            ApiResourceName = updated.Name
        }, cancellationToken);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete an API resource
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetApiResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        await _resourceStore.DeleteApiResourceAsync(id, cancellationToken);

        _logger.LogInformation("Deleted API resource: {Name}", existing.Name);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminApiResourceDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = id.ToString(),
            ResourceName = existing.Name,
            ApiResourceName = existing.Name
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Add a scope to an API resource
    /// </summary>
    [HttpPost("{id:int}/scopes")]
    public async Task<ActionResult<ApiResourceDto>> AddScope(
        int id,
        [FromBody] AddScopeRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetApiResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        // Check if scope already exists
        if (existing.Scopes.Any(s => s.Scope == request.ScopeName))
        {
            return Conflict(new { error = $"Scope '{request.ScopeName}' is already assigned to this resource" });
        }

        existing.Scopes.Add(new ApiResourceScope { Scope = request.ScopeName });
        var updated = await _resourceStore.UpdateApiResourceAsync(existing, cancellationToken);

        _logger.LogInformation("Added scope '{Scope}' to API resource: {Name}", request.ScopeName, updated.Name);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Remove a scope from an API resource
    /// </summary>
    [HttpDelete("{id:int}/scopes/{scopeName}")]
    public async Task<ActionResult<ApiResourceDto>> RemoveScope(
        int id,
        string scopeName,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetApiResourceByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        var scope = existing.Scopes.FirstOrDefault(s => s.Scope == scopeName);
        if (scope == null)
        {
            return NotFound(new { error = $"Scope '{scopeName}' is not assigned to this resource" });
        }

        existing.Scopes.Remove(scope);
        var updated = await _resourceStore.UpdateApiResourceAsync(existing, cancellationToken);

        _logger.LogInformation("Removed scope '{Scope}' from API resource: {Name}", scopeName, updated.Name);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Get all available API scopes that can be assigned to resources
    /// </summary>
    [HttpGet("available-scopes")]
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

    private static ApiResourceDto MapToDto(ApiResource resource) => new()
    {
        Id = resource.Id,
        Name = resource.Name,
        DisplayName = resource.DisplayName,
        Description = resource.Description,
        Enabled = resource.Enabled,
        ShowInDiscoveryDocument = resource.ShowInDiscoveryDocument,
        AllowedAccessTokenSigningAlgorithms = resource.AllowedAccessTokenSigningAlgorithms,
        RequireResourceIndicator = resource.RequireResourceIndicator,
        Scopes = resource.Scopes.Select(s => s.Scope).ToList(),
        UserClaims = resource.UserClaims.Select(c => c.Type).ToList(),
        Created = resource.Created,
        Updated = resource.Updated
    };
}

#region DTOs

public class ApiResourceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public bool ShowInDiscoveryDocument { get; set; }
    public string? AllowedAccessTokenSigningAlgorithms { get; set; }
    public bool RequireResourceIndicator { get; set; }
    public List<string> Scopes { get; set; } = new();
    public List<string> UserClaims { get; set; } = new();
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreateApiResourceRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    public string? AllowedAccessTokenSigningAlgorithms { get; set; }
    public bool? RequireResourceIndicator { get; set; }
    public List<string>? Scopes { get; set; }
    public List<string>? UserClaims { get; set; }
}

public class UpdateApiResourceRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    public string? AllowedAccessTokenSigningAlgorithms { get; set; }
    public bool? RequireResourceIndicator { get; set; }
    public List<string>? Scopes { get; set; }
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
