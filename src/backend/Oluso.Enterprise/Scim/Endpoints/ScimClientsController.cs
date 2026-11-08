using Oluso.Enterprise.Scim.Models.Admin;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Outbound;
using Oluso.Enterprise.Scim.Services;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// Admin API for managing SCIM clients
/// </summary>
[Route("api/admin/scim/clients")]
[RequirePermission("scim.read")]
public class ScimClientsController : AdminBaseController
{
    private readonly IScimClientStore _clientStore;
    private readonly IScimProvisioningLogStore _logStore;
    private readonly IScimAttributeMappingStore _mappingStore;
    private readonly IClientStore _oauthClientStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IScimUserMapper _userMapper;
    private readonly ILogger<ScimClientsController> _logger;

    public ScimClientsController(
        IScimClientStore clientStore,
        IScimProvisioningLogStore logStore,
        IScimAttributeMappingStore mappingStore,
        IClientStore oauthClientStore,
        IIssuerResolver issuerResolver,
        IScimUserMapper userMapper,
        ILogger<ScimClientsController> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _clientStore = clientStore;
        _logStore = logStore;
        _mappingStore = mappingStore;
        _oauthClientStore = oauthClientStore;
        _issuerResolver = issuerResolver;
        _userMapper = userMapper;
        _logger = logger;
    }

    /// <summary>
    /// The tenant-specific SCIM 2.0 base URL to configure in an upstream
    /// provisioning client. Built from the resolved issuer (tenant custom
    /// domain / subdomain) rather than the admin portal's own origin, so the
    /// URL points at the tenant's own host.
    /// </summary>
    [HttpGet("endpoint-url")]
    public async Task<IActionResult> GetEndpointUrl()
    {
        var issuer = await _issuerResolver.GetIssuerAsync();
        return Ok(new { url = $"{issuer.TrimEnd('/')}/scim/v2" });
    }

    private static readonly string[] ConfidentialAuthMethods =
    {
        "client_secret_basic", "client_secret_post", "client_secret_jwt",
        "private_key_jwt", "tls_client_auth", "self_signed_tls_client_auth"
    };

    /// <summary>
    /// Validates that an OAuth client may be linked to a SCIM client for the
    /// client-credentials auth path: it must exist in this tenant, allow the
    /// client_credentials grant, be confidential, and not already be linked to
    /// a different SCIM client. Returns an error message, or null when valid.
    /// </summary>
    private async Task<string?> ValidateOAuthClientLinkAsync(string tenantId, string oauthClientId, string? currentScimClientId)
    {
        var client = await _oauthClientStore.FindClientByIdAsync(oauthClientId);
        if (client == null || client.TenantId != tenantId)
            return $"OAuth client '{oauthClientId}' was not found in this tenant";

        if (!client.AllowedGrantTypes.Any(g => g.GrantType == "client_credentials"))
            return $"OAuth client '{oauthClientId}' must allow the client_credentials grant";

        var isConfidential = client.RequireClientSecret
            || (client.TokenEndpointAuthMethod != null && ConfidentialAuthMethods.Contains(client.TokenEndpointAuthMethod));
        if (!isConfidential)
            return $"OAuth client '{oauthClientId}' must be a confidential client (it cannot use client_credentials as a public client)";

        var existingLink = await _clientStore.GetByOAuthClientIdAsync(tenantId, oauthClientId);
        if (existingLink != null && existingLink.Id != currentScimClientId)
            return $"OAuth client '{oauthClientId}' is already linked to another SCIM client";

        return null;
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
            OAuthClientId = c.OAuthClientId,
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
    [RequirePermission("scim.write")]
    public async Task<IActionResult> CreateClient([FromBody] CreateScimClientRequest request)
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest("Tenant context required");

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");

        if (!string.IsNullOrWhiteSpace(request.OAuthClientId))
        {
            var error = await ValidateOAuthClientLinkAsync(tenantId, request.OAuthClientId, null);
            if (error != null)
                return BadRequest(error);
        }

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
            DefaultRoleId = request.DefaultRoleId,
            OAuthClientId = string.IsNullOrWhiteSpace(request.OAuthClientId) ? null : request.OAuthClientId
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
    [RequirePermission("scim.write")]
    public async Task<IActionResult> UpdateClient(string id, [FromBody] ScimClientResponse request)
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest("Tenant context required");

        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.OAuthClientId))
        {
            var error = await ValidateOAuthClientLinkAsync(tenantId, request.OAuthClientId, client.Id);
            if (error != null)
                return BadRequest(error);
        }

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
        client.OAuthClientId = string.IsNullOrWhiteSpace(request.OAuthClientId) ? null : request.OAuthClientId;
        client.UpdatedAt = DateTime.UtcNow;

        await _clientStore.UpdateAsync(client);

        _logger.LogInformation("SCIM client updated: {ClientId}", id);

        return Ok(ToResponse(client));
    }

    /// <summary>
    /// Rotate the API token for a SCIM client
    /// </summary>
    [HttpPost("{id}/rotate-token")]
    [RequirePermission("scim.write")]
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
    [RequirePermission("scim.write")]
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
    public async Task<ActionResult<PagedResult<ScimClientLogResponse>>> GetLogs(string id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var logs = await _logStore.GetByClientAsync(id, (page - 1) * pageSize, pageSize);
        var total = await _logStore.GetCountByClientAsync(id);

        return Ok(PagedResult<ScimClientLogResponse>.Create(
            logs.Select(l => new ScimClientLogResponse
            {
                Id = l.Id,
                Method = l.Method,
                Path = l.Path,
                ResourceType = l.ResourceType,
                ResourceId = l.ResourceId,
                Operation = l.Operation,
                StatusCode = l.StatusCode,
                Success = l.Success,
                ErrorMessage = l.ErrorMessage,
                ClientIp = l.ClientIp,
                DurationMs = l.DurationMs,
                Timestamp = l.Timestamp,
            }).ToList(),
            total, page, pageSize));
    }

    #region Attribute Mappings

    /// <summary>
    /// Value lists the mapping form is built from: writable internal
    /// properties, transformations, and the identity-provider presets.
    /// </summary>
    [HttpGet("mapping-options")]
    public IActionResult GetMappingOptions()
    {
        return Ok(new ScimMappingOptionsResponse
        {
            InternalProperties = ScimInboundTargets.All.ToList(),
            Transformations = ScimOutboundTransformations.All.ToList(),
            Presets = MappingPresets.Keys.ToList(),
            Defaults = ScimInboundDefaultMappings.Rows.Select(ToResponse).ToList(),
        });
    }

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
        return Ok(mappings.Select(ToResponse));
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

        return Ok(ToResponse(mapping));
    }

    /// <summary>
    /// Create a new attribute mapping
    /// </summary>
    [HttpPost("{id}/mappings")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> CreateMapping(string id, [FromBody] SaveScimAttributeMappingRequest request)
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest("Tenant context required");

        var client = await _clientStore.GetByIdAsync(id);
        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var error = ValidateMapping(request);
        if (error != null)
            return BadRequest(error);

        var existing = await _mappingStore.GetByClientAsync(id);
        if (existing.Any(m => string.Equals(m.ScimAttribute, request.ScimAttribute.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict($"A mapping for '{request.ScimAttribute.Trim()}' already exists");

        var mapping = new ScimAttributeMapping
        {
            TenantId = tenantId,
            ScimClientId = id,
        };
        CopyToEntity(request, mapping);

        await _mappingStore.CreateAsync(mapping);

        _logger.LogInformation("Created attribute mapping {MappingId} for SCIM client {ClientId}", mapping.Id, id);

        return Ok(ToResponse(mapping));
    }

    /// <summary>
    /// Update an attribute mapping
    /// </summary>
    [HttpPut("{id}/mappings/{mappingId}")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> UpdateMapping(string id, string mappingId, [FromBody] SaveScimAttributeMappingRequest request)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var mapping = await _mappingStore.GetByIdAsync(mappingId);
        if (mapping == null || mapping.ScimClientId != id)
            return NotFound();

        var error = ValidateMapping(request);
        if (error != null)
            return BadRequest(error);

        var existing = await _mappingStore.GetByClientAsync(id);
        if (existing.Any(m => m.Id != mappingId && string.Equals(m.ScimAttribute, request.ScimAttribute.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict($"A mapping for '{request.ScimAttribute.Trim()}' already exists");

        CopyToEntity(request, mapping);
        mapping.UpdatedAt = DateTime.UtcNow;

        await _mappingStore.UpdateAsync(mapping);

        _logger.LogInformation("Updated attribute mapping {MappingId} for SCIM client {ClientId}", mappingId, id);

        return Ok(ToResponse(mapping));
    }

    /// <summary>
    /// Delete an attribute mapping
    /// </summary>
    [HttpDelete("{id}/mappings/{mappingId}")]
    [RequirePermission("scim.write")]
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
    /// Apply an identity provider's preset mappings, skipping attributes
    /// that already have one. The standard preset is what runs when a
    /// client has no mappings at all.
    /// </summary>
    [HttpPost("{id}/mappings/defaults")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> ApplyDefaultMappings(string id, [FromBody] ApplyDefaultMappingsRequest? request)
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest("Tenant context required");

        var client = await _clientStore.GetByIdAsync(id);
        if (client == null || client.TenantId != tenantId)
            return NotFound();

        var preset = request?.Provider?.Trim().ToLowerInvariant() ?? "standard";
        if (!MappingPresets.TryGetValue(preset, out var rows))
            return BadRequest($"Unknown preset '{preset}'. Use one of {string.Join(", ", MappingPresets.Keys)}");

        var existingMappings = await _mappingStore.GetByClientAsync(id);
        var existingAttributes = existingMappings.Select(m => m.ScimAttribute).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<ScimAttributeMapping>();
        foreach (var row in rows)
        {
            if (existingAttributes.Contains(row.ScimAttribute))
                continue;

            var mapping = new ScimAttributeMapping
            {
                TenantId = tenantId,
                ScimClientId = id,
                ScimAttribute = row.ScimAttribute,
                InternalProperty = row.InternalProperty,
                Expression = row.Expression,
                IsRequired = row.IsRequired,
                Priority = row.Priority,
                IsEnabled = true,
            };

            await _mappingStore.CreateAsync(mapping);
            created.Add(mapping);
        }

        _logger.LogInformation("Applied {Count} preset mappings ({Preset}) to SCIM client {ClientId} (skipped {Skipped} existing)",
            created.Count, preset, id, rows.Count - created.Count);

        return Ok(created.Select(ToResponse));
    }

    /// <summary>
    /// Dry run: what the client's mappings would write for a sample SCIM
    /// resource. Nothing is stored.
    /// </summary>
    [HttpPost("{id}/mappings/preview")]
    public async Task<IActionResult> PreviewMappings(string id, [FromBody] ScimMappingPreviewRequest request)
    {
        var tenantId = GetTenantId();
        var client = await _clientStore.GetByIdAsync(id);

        if (client == null || client.TenantId != tenantId)
            return NotFound();

        if (request.Resource == null)
            return BadRequest("resource is required");

        var mode = request.Mode?.Trim().ToLowerInvariant() switch
        {
            "replace" or "put" => ScimInboundMode.Replace,
            "patch" => ScimInboundMode.Patch,
            _ => ScimInboundMode.Create,
        };

        var mappings = await _mappingStore.GetByClientAsync(id);
        var preview = _userMapper.Preview(request.Resource, client, mappings, mode);

        return Ok(new ScimMappingPreviewResponse
        {
            Success = preview.Success,
            Error = preview.Error,
            Fields = preview.Fields.ToDictionary(kv => kv.Key, kv => kv.Value),
            UsingDefaults = mappings.All(m => !m.IsEnabled),
        });
    }

    private sealed record PresetRow(string ScimAttribute, string InternalProperty, bool IsRequired = false, int Priority = 0, string? Expression = null);

    private const string Enterprise = ScimConstants.Schemas.EnterpriseUser;

    private static readonly IReadOnlyList<PresetRow> StandardPreset = ScimInboundDefaultMappings.Rows
        .Select(m => new PresetRow(m.ScimAttribute, m.InternalProperty, m.IsRequired, m.Priority, m.Expression))
        .ToList();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<PresetRow>> MappingPresets =
        new Dictionary<string, IReadOnlyList<PresetRow>>(StringComparer.OrdinalIgnoreCase)
        {
            ["standard"] = StandardPreset,
            ["azure"] = StandardPreset.Concat(new PresetRow[]
            {
                new("title", "custom:jobTitle"),
                new($"{Enterprise}:department", "custom:department"),
                new($"{Enterprise}:employeeNumber", "custom:employeeNumber"),
                new($"{Enterprise}:manager.value", "custom:managerId"),
            }).ToList(),
            ["okta"] = StandardPreset.Concat(new PresetRow[]
            {
                new("title", "custom:jobTitle"),
                new("preferredLanguage", ScimInboundTargets.Locale, Priority: -1),
                new($"{Enterprise}:department", "custom:department"),
                new($"{Enterprise}:employeeNumber", "custom:employeeNumber"),
            }).ToList(),
            ["google"] = StandardPreset.Concat(new PresetRow[]
            {
                new("organizations[primary eq true].title", "custom:jobTitle"),
                new("organizations[primary eq true].department", "custom:department"),
            }).ToList(),
        };

    private static string? ValidateMapping(SaveScimAttributeMappingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ScimAttribute))
            return "SCIM attribute is required";

        var property = request.InternalProperty?.Trim();
        if (string.IsNullOrEmpty(property))
            return "Internal property is required";
        if (!ScimInboundTargets.IsKnown(property))
            return $"Unknown internal property '{property}'. Use one of {string.Join(", ", ScimInboundTargets.All)} or custom:<key>";

        var expression = Blank(request.Expression);
        if (expression != null)
        {
            var parseError = ScimExpressionEngine.ValidateTemplate(expression);
            if (parseError != null)
                return $"Expression does not parse: {parseError}";
        }

        var condition = Blank(request.Condition);
        if (condition != null)
        {
            var parseError = ScimExpressionEngine.ValidateExpression(condition);
            if (parseError != null)
                return $"Condition does not parse: {parseError}";
        }

        var transformation = NormalizeTransformation(request.Transformation);
        if (transformation != null && !ScimOutboundTransformations.All.Contains(transformation))
            return $"Unknown transformation '{request.Transformation}'";

        return null;
    }

    private static void CopyToEntity(SaveScimAttributeMappingRequest request, ScimAttributeMapping mapping)
    {
        mapping.ScimAttribute = request.ScimAttribute.Trim();
        mapping.InternalProperty = request.InternalProperty!.Trim();
        mapping.Expression = Blank(request.Expression);
        mapping.Condition = Blank(request.Condition);
        mapping.IsRequired = request.IsRequired;
        mapping.DefaultValue = Blank(request.DefaultValue);
        mapping.Transformation = NormalizeTransformation(request.Transformation);
        mapping.Priority = request.Priority;
        mapping.IsEnabled = request.IsEnabled;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeTransformation(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(normalized) || normalized == ScimOutboundTransformations.None ? null : normalized;
    }

    private static ScimAttributeMappingResponse ToResponse(ScimAttributeMapping m) => new()
    {
        Id = m.Id,
        ScimClientId = m.ScimClientId,
        ScimAttribute = m.ScimAttribute,
        InternalProperty = m.InternalProperty,
        Expression = m.Expression,
        Condition = m.Condition,
        IsRequired = m.IsRequired,
        DefaultValue = m.DefaultValue,
        Transformation = m.Transformation,
        Priority = m.Priority,
        IsEnabled = m.IsEnabled,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
    };

    #endregion

    /// <summary>
    /// Gets the tenant ID from the tenant context (set by middleware from X-Tenant-ID header or JWT claims)
    /// </summary>
    private string? GetTenantId()
    {
        return TenantId;
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
            OAuthClientId = client.OAuthClientId,
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

    /// <summary>Optional OAuth client (ClientId) to allow client-credentials JWT auth.</summary>
    public string? OAuthClientId { get; set; }
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
    public string? OAuthClientId { get; set; }
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

public class SaveScimAttributeMappingRequest
{
    public string ScimAttribute { get; set; } = string.Empty;
    public string? InternalProperty { get; set; }
    /// <summary>Scriban template computing the value; read from the attribute path when empty.</summary>
    public string? Expression { get; set; }
    /// <summary>Scriban boolean expression; the mapping is skipped when false.</summary>
    public string? Condition { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? Transformation { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class ScimAttributeMappingResponse
{
    public string Id { get; set; } = string.Empty;
    public string ScimClientId { get; set; } = string.Empty;
    public string ScimAttribute { get; set; } = string.Empty;
    public string InternalProperty { get; set; } = string.Empty;
    public string? Expression { get; set; }
    public string? Condition { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? Transformation { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ScimMappingOptionsResponse
{
    public List<string> InternalProperties { get; set; } = new();
    public List<string> Transformations { get; set; } = new();
    public List<string> Presets { get; set; } = new();
    /// <summary>The mappings in force when a client has none configured.</summary>
    public List<ScimAttributeMappingResponse> Defaults { get; set; } = new();
}

public class ScimMappingPreviewRequest
{
    /// <summary>A SCIM User resource as an identity provider would send it.</summary>
    public System.Text.Json.Nodes.JsonObject? Resource { get; set; }
    /// <summary>create (default), replace or patch.</summary>
    public string? Mode { get; set; }
}

public class ScimMappingPreviewResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    /// <summary>Internal property to the value that would be written; null means cleared.</summary>
    public Dictionary<string, string?> Fields { get; set; } = new();
    public bool UsingDefaults { get; set; }
}

public class ApplyDefaultMappingsRequest
{
    public string? Provider { get; set; }
}
