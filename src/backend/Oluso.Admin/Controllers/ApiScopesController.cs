using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
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
    [RequirePermission(AdminPermissions.ScopesRead)]
    public async Task<ActionResult<IEnumerable<ApiScopeDto>>> GetAll(CancellationToken cancellationToken)
    {
        var scopes = await _resourceStore.GetAllApiScopesAsync(cancellationToken);
        var dtos = scopes.Select(MapToDto);
        return Ok(dtos);
    }

    /// <summary>
    /// Get API scope by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [RequirePermission(AdminPermissions.ScopesRead)]
    public async Task<ActionResult<ApiScopeDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var scope = await _resourceStore.GetApiScopeByIdAsync(id, cancellationToken);
        if (scope == null)
            return NotFound();

        return Ok(MapToDto(scope));
    }

    /// <summary>
    /// Get API scope by name
    /// </summary>
    [HttpGet("by-name/{name}")]
    [RequirePermission(AdminPermissions.ScopesRead)]
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
    [RequirePermission(AdminPermissions.ScopesWrite)]
    public async Task<ActionResult<ApiScopeDto>> Create(
        [FromBody] CreateApiScopeRequest request,
        CancellationToken cancellationToken)
    {
        var existingScopes = await _resourceStore.FindApiScopesByNameAsync(new[] { request.Name }, cancellationToken);
        if (existingScopes.Any())
        {
            return Conflict(new { error = $"API scope '{request.Name}' already exists" });
        }

        // Validate user claims don't contain protected claim types
        var claimValidationError = ValidateUserClaims(request.UserClaims);
        if (claimValidationError != null)
        {
            return BadRequest(new { error = claimValidationError });
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
    [RequirePermission(AdminPermissions.ScopesWrite)]
    public async Task<ActionResult<ApiScopeDto>> Update(
        int id,
        [FromBody] UpdateApiScopeRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetApiScopeByIdAsync(id, cancellationToken);
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

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete an API scope
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(AdminPermissions.ScopesDelete)]
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
            return $"The following claim types are protected and cannot be added to API scopes: {string.Join(", ", protectedClaims)}";
        }

        return null;
    }
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
}

#endregion
