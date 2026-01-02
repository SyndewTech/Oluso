using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// Admin API for managing SCIM clients
/// </summary>
[Route("api/admin/scim/clients")]
public class ScimClientsController : AdminBaseController
{
    private readonly IScimClientStore _clientStore;
    private readonly IScimProvisioningLogStore _logStore;
    private readonly IScimAttributeMappingStore _mappingStore;
    private readonly ILogger<ScimClientsController> _logger;

    public ScimClientsController(
        IScimClientStore clientStore,
        IScimProvisioningLogStore logStore,
        IScimAttributeMappingStore mappingStore,
        ILogger<ScimClientsController> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _clientStore = clientStore;
        _logStore = logStore;
        _mappingStore = mappingStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all SCIM clients for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetClients()
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest("Tenant context required");

        var clients = await _clientStore.GetByTenantAsync(tenantId);

        var response = clients.Select(c => new ScimClientResponse
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            IsEnabled = c.IsEnabled,
            TokenCreatedAt = c.TokenCreatedAt,
            TokenExpiresAt = c.TokenExpiresAt,
            AllowedIpRanges = c.AllowedIpRanges,
            RateLimitPerMinute = c.RateLimitPerMinute,
            CanCreateUsers = c.CanCreateUsers,
            CanUpdateUsers = c.CanUpdateUsers,
            CanDeleteUsers = c.CanDeleteUsers,
            CanManageGroups = c.CanManageGroups,
            DefaultRoleId = c.DefaultRoleId,
            CreatedAt = c.CreatedAt,
            LastActivityAt = c.LastActivityAt,
            SuccessCount = c.SuccessCount,
            ErrorCount = c.ErrorCount
        });

        return Ok(response);
    }

    /// <summary>
    /// Get a specific SCIM client
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetClient(string id)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        return Ok(ToResponse(client));
    }

    /// <summary>
    /// Create a new SCIM client
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateClient([FromBody] CreateScimClientRequest request)
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest("Tenant context required");

        // Generate a secure token
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        var client = new ScimClient
        {
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            TokenHash = tokenHash,
            TokenExpiresAt = request.TokenExpiresAt,
            AllowedIpRanges = request.AllowedIpRanges,
            RateLimitPerMinute = request.RateLimitPerMinute,
            CanCreateUsers = request.CanCreateUsers,
            CanUpdateUsers = request.CanUpdateUsers,
            CanDeleteUsers = request.CanDeleteUsers,
            CanManageGroups = request.CanManageGroups,
            DefaultRoleId = request.DefaultRoleId
        };

        await _clientStore.CreateAsync(client);

        _logger.LogInformation("SCIM client created: {ClientId} for tenant {TenantId}", client.Id, tenantId);

        // Return the token only once - it cannot be retrieved again
        return Ok(new CreateScimClientResponse
        {
            Client = ToResponse(client),
            Token = token // Only returned on creation!
        });
    }

    /// <summary>
    /// Update a SCIM client
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateClient(string id, [FromBody] UpdateScimClientRequest request)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        client.Name = request.Name;
        client.Description = request.Description;
        client.IsEnabled = request.IsEnabled;
        client.TokenExpiresAt = request.TokenExpiresAt;
        client.AllowedIpRanges = request.AllowedIpRanges;
        client.RateLimitPerMinute = request.RateLimitPerMinute;
        client.CanCreateUsers = request.CanCreateUsers;
        client.CanUpdateUsers = request.CanUpdateUsers;
        client.CanDeleteUsers = request.CanDeleteUsers;
        client.CanManageGroups = request.CanManageGroups;
        client.DefaultRoleId = request.DefaultRoleId;
        client.UpdatedAt = DateTime.UtcNow;

        await _clientStore.UpdateAsync(client);

        _logger.LogInformation("SCIM client updated: {ClientId}", id);

        return Ok(ToResponse(client));
    }

    /// <summary>
    /// Rotate the API token for a SCIM client
    /// </summary>
    [HttpPost("{id}/rotate-token")]
    public async Task<IActionResult> RotateToken(string id)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        // Generate new token
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        client.TokenHash = tokenHash;
        client.TokenCreatedAt = DateTime.UtcNow;
        client.UpdatedAt = DateTime.UtcNow;

        await _clientStore.UpdateAsync(client);

        _logger.LogInformation("SCIM client token rotated: {ClientId}", id);

        return Ok(new { Token = token });
    }

    /// <summary>
    /// Delete a SCIM client
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteClient(string id)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        await _clientStore.DeleteAsync(id);

        _logger.LogInformation("SCIM client deleted: {ClientId}", id);

        return NoContent();
    }

    /// <summary>
    /// Get provisioning logs for a SCIM client
    /// </summary>
    [HttpGet("{id}/logs")]
    public async Task<IActionResult> GetLogs(string id, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var logs = await _logStore.GetByClientAsync(id, skip, take);
        var total = await _logStore.GetCountByClientAsync(id);

        return Ok(new
        {
            Total = total,
            Skip = skip,
            Take = take,
            Items = logs.Select(l => new
            {
                l.Id,
                l.Method,
                l.Path,
                l.ResourceType,
                l.ResourceId,
                l.Operation,
                l.StatusCode,
                l.Success,
                l.ErrorMessage,
                l.ClientIp,
                l.DurationMs,
                l.Timestamp
            })
        });
    }

    #region Attribute Mappings

    /// <summary>
    /// Get all attribute mappings for a SCIM client
    /// </summary>
    [HttpGet("{id}/mappings")]
    public async Task<IActionResult> GetMappings(string id)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var mappings = await _mappingStore.GetByClientAsync(id);

        return Ok(mappings.Select(m => new ScimAttributeMappingResponse
        {
            Id = m.Id,
            ScimClientId = m.ScimClientId,
            ScimAttribute = m.ScimAttribute,
            InternalProperty = m.InternalProperty,
            Direction = m.Direction,
            IsRequired = m.IsRequired,
            DefaultValue = m.DefaultValue,
            Transformation = m.Transformation,
            Priority = m.Priority,
            IsEnabled = m.IsEnabled,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt
        }));
    }

    /// <summary>
    /// Get a specific attribute mapping
    /// </summary>
    [HttpGet("{id}/mappings/{mappingId}")]
    public async Task<IActionResult> GetMapping(string id, string mappingId)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var mapping = await _mappingStore.GetByIdAsync(mappingId);
        if (mapping == null || mapping.ScimClientId != id)
            return NotFound();

        return Ok(new ScimAttributeMappingResponse
        {
            Id = mapping.Id,
            ScimClientId = mapping.ScimClientId,
            ScimAttribute = mapping.ScimAttribute,
            InternalProperty = mapping.InternalProperty,
            Direction = mapping.Direction,
            IsRequired = mapping.IsRequired,
            DefaultValue = mapping.DefaultValue,
            Transformation = mapping.Transformation,
            Priority = mapping.Priority,
            IsEnabled = mapping.IsEnabled,
            CreatedAt = mapping.CreatedAt,
            UpdatedAt = mapping.UpdatedAt
        });
    }

    /// <summary>
    /// Create a new attribute mapping
    /// </summary>
    [HttpPost("{id}/mappings")]
    public async Task<IActionResult> CreateMapping(string id, [FromBody] CreateScimAttributeMappingRequest request)
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest("Tenant context required");

        var client = await _clientStore.GetByIdAsync(id);
        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var mapping = new ScimAttributeMapping
        {
            TenantId = tenantId,
            ScimClientId = id,
            ScimAttribute = request.ScimAttribute,
            InternalProperty = request.InternalProperty,
            Direction = request.Direction ?? "inbound",
            IsRequired = request.IsRequired ?? false,
            DefaultValue = request.DefaultValue,
            Transformation = request.Transformation,
            Priority = request.Priority ?? 0,
            IsEnabled = request.IsEnabled ?? true
        };

        await _mappingStore.CreateAsync(mapping);

        _logger.LogInformation("Created attribute mapping {MappingId} for SCIM client {ClientId}", mapping.Id, id);

        return Ok(new ScimAttributeMappingResponse
        {
            Id = mapping.Id,
            ScimClientId = mapping.ScimClientId,
            ScimAttribute = mapping.ScimAttribute,
            InternalProperty = mapping.InternalProperty,
            Direction = mapping.Direction,
            IsRequired = mapping.IsRequired,
            DefaultValue = mapping.DefaultValue,
            Transformation = mapping.Transformation,
            Priority = mapping.Priority,
            IsEnabled = mapping.IsEnabled,
            CreatedAt = mapping.CreatedAt,
            UpdatedAt = mapping.UpdatedAt
        });
    }

    /// <summary>
    /// Update an attribute mapping
    /// </summary>
    [HttpPut("{id}/mappings/{mappingId}")]
    public async Task<IActionResult> UpdateMapping(string id, string mappingId, [FromBody] UpdateScimAttributeMappingRequest request)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var mapping = await _mappingStore.GetByIdAsync(mappingId);
        if (mapping == null || mapping.ScimClientId != id)
            return NotFound();

        mapping.ScimAttribute = request.ScimAttribute ?? mapping.ScimAttribute;
        mapping.InternalProperty = request.InternalProperty ?? mapping.InternalProperty;
        mapping.Direction = request.Direction ?? mapping.Direction;
        mapping.IsRequired = request.IsRequired ?? mapping.IsRequired;
        mapping.DefaultValue = request.DefaultValue;
        mapping.Transformation = request.Transformation;
        mapping.Priority = request.Priority ?? mapping.Priority;
        mapping.IsEnabled = request.IsEnabled ?? mapping.IsEnabled;
        mapping.UpdatedAt = DateTime.UtcNow;

        await _mappingStore.UpdateAsync(mapping);

        _logger.LogInformation("Updated attribute mapping {MappingId} for SCIM client {ClientId}", mappingId, id);

        return Ok(new ScimAttributeMappingResponse
        {
            Id = mapping.Id,
            ScimClientId = mapping.ScimClientId,
            ScimAttribute = mapping.ScimAttribute,
            InternalProperty = mapping.InternalProperty,
            Direction = mapping.Direction,
            IsRequired = mapping.IsRequired,
            DefaultValue = mapping.DefaultValue,
            Transformation = mapping.Transformation,
            Priority = mapping.Priority,
            IsEnabled = mapping.IsEnabled,
            CreatedAt = mapping.CreatedAt,
            UpdatedAt = mapping.UpdatedAt
        });
    }

    /// <summary>
    /// Delete an attribute mapping
    /// </summary>
    [HttpDelete("{id}/mappings/{mappingId}")]
    public async Task<IActionResult> DeleteMapping(string id, string mappingId)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var mapping = await _mappingStore.GetByIdAsync(mappingId);
        if (mapping == null || mapping.ScimClientId != id)
            return NotFound();

        await _mappingStore.DeleteAsync(mappingId);

        _logger.LogInformation("Deleted attribute mapping {MappingId} for SCIM client {ClientId}", mappingId, id);

        return NoContent();
    }

    /// <summary>
    /// Apply default attribute mappings for a provider
    /// </summary>
    [HttpPost("{id}/mappings/defaults")]
    public async Task<IActionResult> ApplyDefaultMappings(string id, [FromBody] ApplyDefaultMappingsRequest? request)
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest("Tenant context required");

        var client = await _clientStore.GetByIdAsync(id);
        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var provider = request?.Provider?.ToLowerInvariant() ?? "standard";
        var defaultMappings = GetDefaultMappings(provider);

        var createdMappings = new List<ScimAttributeMapping>();
        foreach (var (scimAttr, internalProp, required) in defaultMappings)
        {
            var mapping = new ScimAttributeMapping
            {
                TenantId = tenantId,
                ScimClientId = id,
                ScimAttribute = scimAttr,
                InternalProperty = internalProp,
                Direction = "inbound",
                IsRequired = required,
                Priority = 0,
                IsEnabled = true
            };

            await _mappingStore.CreateAsync(mapping);
            createdMappings.Add(mapping);
        }

        _logger.LogInformation("Applied {Count} default mappings for provider {Provider} to SCIM client {ClientId}",
            createdMappings.Count, provider, id);

        return Ok(createdMappings.Select(m => new ScimAttributeMappingResponse
        {
            Id = m.Id,
            ScimClientId = m.ScimClientId,
            ScimAttribute = m.ScimAttribute,
            InternalProperty = m.InternalProperty,
            Direction = m.Direction,
            IsRequired = m.IsRequired,
            DefaultValue = m.DefaultValue,
            Transformation = m.Transformation,
            Priority = m.Priority,
            IsEnabled = m.IsEnabled,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt
        }));
    }

    private static List<(string ScimAttribute, string InternalProperty, bool Required)> GetDefaultMappings(string provider)
    {
        // Standard SCIM 2.0 mappings that work for most providers
        var mappings = new List<(string, string, bool)>
        {
            ("userName", "UserName", true),
            ("name.givenName", "FirstName", false),
            ("name.familyName", "LastName", false),
            ("displayName", "DisplayName", false),
            ("emails[type eq \"work\"].value", "Email", true),
            ("active", "IsActive", false),
            ("phoneNumbers[type eq \"work\"].value", "PhoneNumber", false),
        };

        // Add provider-specific mappings
        switch (provider)
        {
            case "azure":
                mappings.Add(("externalId", "ExternalId", false));
                mappings.Add(("urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department", "Department", false));
                mappings.Add(("urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:manager.value", "ManagerId", false));
                break;
            case "okta":
                mappings.Add(("externalId", "ExternalId", false));
                mappings.Add(("locale", "Locale", false));
                mappings.Add(("timezone", "TimeZone", false));
                break;
            case "google":
                mappings.Add(("externalId", "ExternalId", false));
                mappings.Add(("organizations[primary eq true].department", "Department", false));
                break;
        }

        return mappings;
    }

    #endregion

    private string? GetTenantId()
    {
        return User.FindFirst("tenant_id")?.Value;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static ScimClientResponse ToResponse(ScimClient client)
    {
        return new ScimClientResponse
        {
            Id = client.Id,
            Name = client.Name,
            Description = client.Description,
            IsEnabled = client.IsEnabled,
            TokenCreatedAt = client.TokenCreatedAt,
            TokenExpiresAt = client.TokenExpiresAt,
            AllowedIpRanges = client.AllowedIpRanges,
            RateLimitPerMinute = client.RateLimitPerMinute,
            CanCreateUsers = client.CanCreateUsers,
            CanUpdateUsers = client.CanUpdateUsers,
            CanDeleteUsers = client.CanDeleteUsers,
            CanManageGroups = client.CanManageGroups,
            DefaultRoleId = client.DefaultRoleId,
            CreatedAt = client.CreatedAt,
            LastActivityAt = client.LastActivityAt,
            SuccessCount = client.SuccessCount,
            ErrorCount = client.ErrorCount
        };
    }
}

public class CreateScimClientRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? TokenExpiresAt { get; set; }
    public string? AllowedIpRanges { get; set; }
    public int RateLimitPerMinute { get; set; } = 60;
    public bool CanCreateUsers { get; set; } = true;
    public bool CanUpdateUsers { get; set; } = true;
    public bool CanDeleteUsers { get; set; } = true;
    public bool CanManageGroups { get; set; } = true;
    public string? DefaultRoleId { get; set; }
}

public class UpdateScimClientRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public string? AllowedIpRanges { get; set; }
    public int RateLimitPerMinute { get; set; }
    public bool CanCreateUsers { get; set; }
    public bool CanUpdateUsers { get; set; }
    public bool CanDeleteUsers { get; set; }
    public bool CanManageGroups { get; set; }
    public string? DefaultRoleId { get; set; }
}

public class ScimClientResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime TokenCreatedAt { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public string? AllowedIpRanges { get; set; }
    public int RateLimitPerMinute { get; set; }
    public bool CanCreateUsers { get; set; }
    public bool CanUpdateUsers { get; set; }
    public bool CanDeleteUsers { get; set; }
    public bool CanManageGroups { get; set; }
    public string? DefaultRoleId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
}

public class CreateScimClientResponse
{
    public ScimClientResponse Client { get; set; } = null!;
    public string Token { get; set; } = string.Empty;
}

public class CreateScimAttributeMappingRequest
{
    public string ScimAttribute { get; set; } = string.Empty;
    public string InternalProperty { get; set; } = string.Empty;
    public string? Direction { get; set; }
    public bool? IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? Transformation { get; set; }
    public int? Priority { get; set; }
    public bool? IsEnabled { get; set; }
}

public class UpdateScimAttributeMappingRequest
{
    public string? ScimAttribute { get; set; }
    public string? InternalProperty { get; set; }
    public string? Direction { get; set; }
    public bool? IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? Transformation { get; set; }
    public int? Priority { get; set; }
    public bool? IsEnabled { get; set; }
}

public class ScimAttributeMappingResponse
{
    public string Id { get; set; } = string.Empty;
    public string ScimClientId { get; set; } = string.Empty;
    public string ScimAttribute { get; set; } = string.Empty;
    public string InternalProperty { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? Transformation { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ApplyDefaultMappingsRequest
{
    public string? Provider { get; set; }
}
