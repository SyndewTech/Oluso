using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers.Service;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Admin.Dto.Scopes;

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
    /// Get all API scopes with pagination
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.ScopesRead)]
    public async Task<ActionResult<PagedResult<ApiScopeDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var scopes = await _resourceStore.GetAllApiScopesAsync(cancellationToken);
        var allDtos = scopes.Select(MapToDto).ToList();

        var items = allDtos.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(PagedResult<ApiScopeDto>.Create(items, allDtos.Count, page, pageSize));
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
        // The oluso: namespace is platform-owned. Letting an admin mint a scope
        // in it would let a tenant-defined name satisfy the Service API policy.
        if (ServiceScopes.IsReserved(request.Name))
        {
            return BadRequest(new
            {
                error = $"'{ServiceScopes.ReservedPrefix}' is a reserved scope namespace. "
                    + "Grant Service API access on the client instead — the scopes are created for you."
            });
        }

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

        var prefixError = ValidatePrefixSettings(request.IsPrefix == true, request.SuffixPattern, request.SuffixMaxLength);
        if (prefixError != null)
        {
            return BadRequest(new { error = prefixError });
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
        SetPrefix(scope, request.IsPrefix == true, request.SuffixPattern, request.SuffixMaxLength);

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
        [FromBody] ApiScopeDto request,
        CancellationToken cancellationToken)
    {
        var existing = await _resourceStore.GetApiScopeByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.NonEditable)
        {
            return BadRequest(new
            {
                error = $"API scope '{existing.Name}' is managed by Oluso and cannot be edited."
            });
        }

        // Validate user claims don't contain protected claim types
        var claimValidationError = ValidateUserClaims(request.UserClaims);
        if (claimValidationError != null)
        {
            return BadRequest(new { error = claimValidationError });
        }

        if (request.IsPrefix.HasValue)
        {
            var prefixError = ValidatePrefixSettings(request.IsPrefix.Value, request.SuffixPattern, request.SuffixMaxLength);
            if (prefixError != null)
            {
                return BadRequest(new { error = prefixError });
            }
        }

        existing.DisplayName = request.DisplayName;
        existing.Description = request.Description;
        existing.Required = request.Required;
        existing.Emphasize = request.Emphasize;
        existing.ShowInDiscoveryDocument = request.ShowInDiscoveryDocument;
        existing.Enabled = request.Enabled;

        if (request.IsPrefix.HasValue)
        {
            SetPrefix(existing, request.IsPrefix.Value, request.SuffixPattern, request.SuffixMaxLength);
        }

        existing.UserClaims.Clear();
        foreach (var claim in request.UserClaims)
        {
            existing.UserClaims.Add(new ApiScopeClaim { Type = claim });
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

        // Deleting a managed scope would break every client that holds it —
        // token issuance rejects scopes with no ApiScope row. Turn Service API
        // access off on the client instead.
        if (existing.NonEditable)
        {
            return BadRequest(new
            {
                error = $"API scope '{existing.Name}' is managed by Oluso and cannot be deleted."
            });
        }

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
        UserClaims = scope.UserClaims.Select(c => c.Type).ToList(),
        NonEditable = scope.NonEditable,
        IsPrefix = ApiScopePrefixes.IsPrefix(scope),
        SuffixPattern = PropertyValue(scope, ApiScopePrefixes.SuffixPatternProperty),
        SuffixMaxLength = int.TryParse(PropertyValue(scope, ApiScopePrefixes.SuffixMaxLengthProperty), out var max) ? max : null
    };

    /// <summary>
    /// Marks or unmarks <paramref name="scope"/> as a prefix scope (<see cref="ApiScopePrefixes"/>) and sets its suffix
    /// rule: an empty pattern or a null length removes that property, and so does turning the prefix off.
    /// </summary>
    private static void SetPrefix(ApiScope scope, bool prefix, string? suffixPattern, int? suffixMaxLength)
    {
        SetProperty(scope, ApiScopePrefixes.MatchProperty, prefix ? ApiScopePrefixes.PrefixMatch : null);
        SetProperty(scope, ApiScopePrefixes.SuffixPatternProperty,
            prefix && !string.IsNullOrWhiteSpace(suffixPattern) ? suffixPattern : null);
        SetProperty(scope, ApiScopePrefixes.SuffixMaxLengthProperty,
            prefix && suffixMaxLength.HasValue ? suffixMaxLength.Value.ToString(CultureInfo.InvariantCulture) : null);
    }

    /// <summary>Sets property <paramref name="key"/> to <paramref name="value"/>, or removes it when null.</summary>
    private static void SetProperty(ApiScope scope, string key, string? value)
    {
        var existing = scope.Properties
            .Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (value == null)
        {
            foreach (var property in existing)
            {
                scope.Properties.Remove(property);
            }
        }
        else if (existing.Count == 0)
        {
            scope.Properties.Add(new ApiScopeProperty { Key = key, Value = value });
        }
        else
        {
            existing[0].Value = value;
        }
    }

    private static string? PropertyValue(ApiScope scope, string key) =>
        scope.Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>
    /// The error for a prefix scope's suffix rule, or null: the pattern must compile (it is anchored and matched with a
    /// timeout at request time, see <see cref="ApiScopePrefixes.TryCompilePattern"/>) and the length must be positive.
    /// Nothing is checked when the scope isn't a prefix scope, since the settings are then dropped.
    /// </summary>
    private static string? ValidatePrefixSettings(bool prefix, string? suffixPattern, int? suffixMaxLength)
    {
        if (!prefix)
            return null;
        if (!string.IsNullOrWhiteSpace(suffixPattern) && !ApiScopePrefixes.TryCompilePattern(suffixPattern, out _))
            return "The allowed suffix pattern is not a valid regular expression.";
        if (suffixMaxLength is <= 0)
            return "The maximum suffix length must be a positive number.";
        return null;
    }

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
