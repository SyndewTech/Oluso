using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing API Scopes
/// </summary>
[Route("api/admin/api-scopes")]
public class ApiScopesController : AdminBaseController
{
    private readonly IResourceStore _resourceStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<ApiScopesController> _logger;

    public ApiScopesController(
        ITenantContext tenantContext,
        IResourceStore resourceStore,
        IOlusoEventService eventService,
        ILogger<ApiScopesController> logger)
        : base(tenantContext)
    {
        _resourceStore = resourceStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all API scopes
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApiScopeDto>>> GetAll(CancellationToken cancellationToken)
    {
        var scopes = await _resourceStore.GetAllApiScopesAsync(cancellationToken);
        var dtos = new List<ApiScopeDto>();
        foreach (var scope in scopes)
        {
            dtos.Add(await MapToDtoWithResourcesAsync(scope, cancellationToken));
        }
        return Ok(dtos);
    }

    /// <summary>
    /// Get API scope by ID
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiScopeDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var scope = await _resourceStore.GetApiScopeByIdAsync(id, cancellationToken);
        if (scope == null)
            return NotFound();

        return Ok(await MapToDtoWithResourcesAsync(scope, cancellationToken));
    }

    /// <summary>
    /// Get API scope by name
    /// </summary>
    [HttpGet("by-name/{name}")]
    public async Task<ActionResult<ApiScopeDto>> GetByName(string name, CancellationToken cancellationToken)
    {
        var scopes = await _resourceStore.FindApiScopesByNameAsync(new[] { name }, cancellationToken);
        var scope = scopes.FirstOrDefault();
        if (scope == null)
            return NotFound();

        return Ok(MapToDto(scope));
    }

    /// <summary>
    /// Create a new API scope
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiScopeDto>> Create(
        [FromBody] CreateApiScopeRequest request,
        CancellationToken cancellationToken)
    {
        var existingScopes = await _resourceStore.FindApiScopesByNameAsync(new[] { request.Name }, cancellationToken);
        if (existingScopes.Any())
        {
            return Conflict(new { error = $"API scope '{request.Name}' already exists" });
        }

        var scope = new ApiScope
        {
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Description = request.Description,
            Required = request.Required ?? false,
            Emphasize = request.Emphasize ?? false,
            ShowInDiscoveryDocument = request.ShowInDiscoveryDocument ?? true,
            Enabled = request.Enabled ?? true,
            UserClaims = request.UserClaims?.Select(c => new ApiScopeClaim { Type = c }).ToList()
                ?? new List<ApiScopeClaim>()
        };

        var created = await _resourceStore.AddApiScopeAsync(scope, cancellationToken);

        _logger.LogInformation("Created API scope: {Name}", created.Name);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminApiScopeCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = created.Id.ToString(),
            ResourceName = created.Name,
            ApiScopeName = created.Name
        }, cancellationToken);

        return CreatedAtAction(nameof(GetByName), new { name = created.Name }, MapToDto(created));
    }

    /// <summary>
    /// Update an API scope
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiScopeDto>> Update(
        int id,
        [FromBody] UpdateApiScopeRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetApiScopeByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        if (request.DisplayName != null) existing.DisplayName = request.DisplayName;
        if (request.Description != null) existing.Description = request.Description;
        if (request.Required.HasValue) existing.Required = request.Required.Value;
        if (request.Emphasize.HasValue) existing.Emphasize = request.Emphasize.Value;
        if (request.ShowInDiscoveryDocument.HasValue) existing.ShowInDiscoveryDocument = request.ShowInDiscoveryDocument.Value;
        if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;

        if (request.UserClaims != null)
        {
            existing.UserClaims.Clear();
            foreach (var claim in request.UserClaims)
            {
                existing.UserClaims.Add(new ApiScopeClaim { Type = claim });
            }
        }

        var updated = await _resourceStore.UpdateApiScopeAsync(existing, cancellationToken);

        // Handle API resource associations if provided
        if (request.ApiResourceNames != null)
        {
            await UpdateScopeResourceAssociationsAsync(existing.Name, request.ApiResourceNames, cancellationToken);
        }

        _logger.LogInformation("Updated API scope: {Name}", updated.Name);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminApiScopeUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = updated.Id.ToString(),
            ResourceName = updated.Name,
            ApiScopeName = updated.Name
        }, cancellationToken);

        return Ok(await MapToDtoWithResourcesAsync(updated, cancellationToken));
    }

    /// <summary>
    /// Updates which API resources contain this scope
    /// </summary>
    private async Task UpdateScopeResourceAssociationsAsync(
        string scopeName,
        List<string> desiredResourceNames,
        CancellationToken cancellationToken)
    {
        // Get all resources
        var allResources = await _resourceStore.GetAllApiResourcesAsync(cancellationToken);

        foreach (var resource in allResources)
        {
            var hasScope = resource.Scopes.Any(s => s.Scope == scopeName);
            var shouldHaveScope = desiredResourceNames.Contains(resource.Name);

            if (shouldHaveScope && !hasScope)
            {
                // Add scope to resource
                resource.Scopes.Add(new ApiResourceScope { Scope = scopeName });
                await _resourceStore.UpdateApiResourceAsync(resource, cancellationToken);
                _logger.LogInformation("Added scope '{Scope}' to resource '{Resource}'", scopeName, resource.Name);
            }
            else if (!shouldHaveScope && hasScope)
            {
                // Remove scope from resource
                var scopeToRemove = resource.Scopes.First(s => s.Scope == scopeName);
                resource.Scopes.Remove(scopeToRemove);
                await _resourceStore.UpdateApiResourceAsync(resource, cancellationToken);
                _logger.LogInformation("Removed scope '{Scope}' from resource '{Resource}'", scopeName, resource.Name);
            }
        }
    }

    /// <summary>
    /// Delete an API scope
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetApiScopeByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        await _resourceStore.DeleteApiScopeAsync(id, cancellationToken);

        _logger.LogInformation("Deleted API scope: {Name}", existing.Name);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminApiScopeDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = id.ToString(),
            ResourceName = existing.Name,
            ApiScopeName = existing.Name
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Get all available API resources that can have scopes assigned
    /// </summary>
    [HttpGet("available-resources")]
    public async Task<ActionResult<IEnumerable<ApiResourceSummaryDto>>> GetAvailableResources(CancellationToken cancellationToken)
    {
        var resources = await _resourceStore.GetAllApiResourcesAsync(cancellationToken);
        var summaries = resources.Select(r => new ApiResourceSummaryDto
        {
            Name = r.Name,
            DisplayName = r.DisplayName,
            Description = r.Description,
            ScopeCount = r.Scopes.Count
        });
        return Ok(summaries);
    }

    private async Task<ApiScopeDto> MapToDtoWithResourcesAsync(ApiScope scope, CancellationToken cancellationToken)
    {
        // Get all resources that contain this scope
        var resources = await _resourceStore.FindApiResourcesByScopeNameAsync(new[] { scope.Name }, cancellationToken);
        var resourceNames = resources.Select(r => r.Name).ToList();

        return new ApiScopeDto
        {
            Id = scope.Id,
            Name = scope.Name,
            DisplayName = scope.DisplayName,
            Description = scope.Description,
            Required = scope.Required,
            Emphasize = scope.Emphasize,
            ShowInDiscoveryDocument = scope.ShowInDiscoveryDocument,
            Enabled = scope.Enabled,
            UserClaims = scope.UserClaims.Select(c => c.Type).ToList(),
            ApiResourceNames = resourceNames
        };
    }

    private static ApiScopeDto MapToDto(ApiScope scope) => new()
    {
        Id = scope.Id,
        Name = scope.Name,
        DisplayName = scope.DisplayName,
        Description = scope.Description,
        Required = scope.Required,
        Emphasize = scope.Emphasize,
        ShowInDiscoveryDocument = scope.ShowInDiscoveryDocument,
        Enabled = scope.Enabled,
        UserClaims = scope.UserClaims.Select(c => c.Type).ToList()
    };
}

#region DTOs

public class ApiScopeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
    public bool Emphasize { get; set; }
    public bool ShowInDiscoveryDocument { get; set; }
    public bool Enabled { get; set; }
    public List<string> UserClaims { get; set; } = new();
    public List<string> ApiResourceNames { get; set; } = new();
}

public class CreateApiScopeRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Required { get; set; }
    public bool? Emphasize { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? UserClaims { get; set; }
}

public class UpdateApiScopeRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Required { get; set; }
    public bool? Emphasize { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? UserClaims { get; set; }
    public List<string>? ApiResourceNames { get; set; }
}

public class ApiResourceSummaryDto
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public int ScopeCount { get; set; }
}

#endregion
