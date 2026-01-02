using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing Tenants (super-admin only)
/// </summary>
[Route("api/admin/tenants")]
[Authorize(Policy = "SuperAdmin")]
public class TenantsController : AdminBaseController
{
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(
        ITenantStore tenantStore,
        ILogger<TenantsController> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _tenantStore = tenantStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all tenants
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TenantDto>>> GetAll(CancellationToken cancellationToken)
    {
        var tenants = await _tenantStore.GetAllAsync(cancellationToken);
        return Ok(tenants.Select(MapToDto));
    }

    /// <summary>
    /// Get tenant by ID
    /// </summary>
    [HttpGet("{tenantId}")]
    public async Task<ActionResult<TenantDto>> GetById(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();
        return Ok(MapToDto(tenant));
    }

    /// <summary>
    /// Get tenant by identifier (subdomain/path)
    /// </summary>
    [HttpGet("by-identifier/{identifier}")]
    public async Task<ActionResult<TenantDto>> GetByIdentifier(string identifier, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdentifierAsync(identifier, cancellationToken);
        if (tenant == null)
            return NotFound();
        return Ok(MapToDto(tenant));
    }

    /// <summary>
    /// Create a new tenant
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TenantDto>> Create(
        [FromBody] CreateTenantRequest request,
        CancellationToken cancellationToken)
    {
        // Check for existing identifier
        var existing = await _tenantStore.GetByIdentifierAsync(request.Identifier, cancellationToken);
        if (existing != null)
        {
            return Conflict(new { error = $"Tenant with identifier '{request.Identifier}' already exists" });
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Identifier = request.Identifier,
            Description = request.Description,
            Enabled = true
        };

        var created = await _tenantStore.CreateAsync(tenant, cancellationToken);

        _logger.LogInformation("Created tenant: {TenantId} ({Name})", created.Id, created.Name);

        return CreatedAtAction(nameof(GetById), new { tenantId = created.Id }, MapToDto(created));
    }

    /// <summary>
    /// Update a tenant
    /// </summary>
    [HttpPut("{tenantId}")]
    public async Task<ActionResult<TenantDto>> Update(
        string tenantId,
        [FromBody] UpdateTenantRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (request.Name != null) tenant.Name = request.Name;
        if (request.DisplayName != null) tenant.DisplayName = request.DisplayName;
        if (request.Description != null) tenant.Description = request.Description;
        if (request.Enabled.HasValue) tenant.Enabled = request.Enabled.Value;
        if (request.Configuration != null) tenant.Configuration = request.Configuration;

        var updated = await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Updated tenant: {TenantId}", tenantId);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete a tenant
    /// </summary>
    [HttpDelete("{tenantId}")]
    public async Task<IActionResult> Delete(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        await _tenantStore.DeleteAsync(tenantId, cancellationToken);

        _logger.LogInformation("Deleted tenant: {TenantId}", tenantId);

        return NoContent();
    }

    /// <summary>
    /// Enable a tenant
    /// </summary>
    [HttpPost("{tenantId}/enable")]
    public async Task<IActionResult> Enable(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        tenant.Enabled = true;
        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Enabled tenant: {TenantId}", tenantId);

        return Ok(new { enabled = true });
    }

    /// <summary>
    /// Disable a tenant
    /// </summary>
    [HttpPost("{tenantId}/disable")]
    public async Task<IActionResult> Disable(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        tenant.Enabled = false;
        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogWarning("Disabled tenant: {TenantId}", tenantId);

        return Ok(new { enabled = false });
    }

    /// <summary>
    /// Get tenant protocol configuration
    /// </summary>
    [HttpGet("{tenantId}/protocol-configuration")]
    public async Task<ActionResult<TenantProtocolConfigurationDto>> GetProtocolConfiguration(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        return Ok(MapProtocolConfigToDto(tenant.ProtocolConfiguration));
    }

    /// <summary>
    /// Update tenant protocol configuration
    /// </summary>
    [HttpPut("{tenantId}/protocol-configuration")]
    public async Task<ActionResult<TenantProtocolConfigurationDto>> UpdateProtocolConfiguration(
        string tenantId,
        [FromBody] UpdateTenantProtocolConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        // Create or update protocol configuration
        tenant.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenantId };
        var config = tenant.ProtocolConfiguration;

        if (request.AllowedGrantTypes != null)
            config.AllowedGrantTypesJson = SerializeJsonArray(request.AllowedGrantTypes);
        if (request.AllowedResponseTypes != null)
            config.AllowedResponseTypesJson = SerializeJsonArray(request.AllowedResponseTypes);
        if (request.AllowedTokenEndpointAuthMethods != null)
            config.AllowedTokenEndpointAuthMethodsJson = SerializeJsonArray(request.AllowedTokenEndpointAuthMethods);
        if (request.SubjectTypesSupported != null)
            config.SubjectTypesSupportedJson = SerializeJsonArray(request.SubjectTypesSupported);
        if (request.IdTokenSigningAlgValuesSupported != null)
            config.IdTokenSigningAlgValuesSupportedJson = SerializeJsonArray(request.IdTokenSigningAlgValuesSupported);
        if (request.CodeChallengeMethodsSupported != null)
            config.CodeChallengeMethodsSupportedJson = SerializeJsonArray(request.CodeChallengeMethodsSupported);
        if (request.DPoPSigningAlgValuesSupported != null)
            config.DPoPSigningAlgValuesSupportedJson = SerializeJsonArray(request.DPoPSigningAlgValuesSupported);

        if (request.RequirePushedAuthorizationRequests.HasValue)
            config.RequirePushedAuthorizationRequests = request.RequirePushedAuthorizationRequests.Value;
        if (request.RequirePkce.HasValue)
            config.RequirePkce = request.RequirePkce.Value;
        if (request.AllowPlainPkce.HasValue)
            config.AllowPlainPkce = request.AllowPlainPkce.Value;
        if (request.RequireDPoP.HasValue)
            config.RequireDPoP = request.RequireDPoP.Value;
        if (request.ClaimsParameterSupported.HasValue)
            config.ClaimsParameterSupported = request.ClaimsParameterSupported.Value;
        if (request.RequestParameterSupported.HasValue)
            config.RequestParameterSupported = request.RequestParameterSupported.Value;
        if (request.RequestUriParameterSupported.HasValue)
            config.RequestUriParameterSupported = request.RequestUriParameterSupported.Value;
        if (request.FrontchannelLogoutSupported.HasValue)
            config.FrontchannelLogoutSupported = request.FrontchannelLogoutSupported.Value;
        if (request.BackchannelLogoutSupported.HasValue)
            config.BackchannelLogoutSupported = request.BackchannelLogoutSupported.Value;

        config.Updated = DateTime.UtcNow;
        tenant.Updated = DateTime.UtcNow;

        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Updated protocol configuration for tenant: {TenantId}", tenantId);

        return Ok(MapProtocolConfigToDto(config));
    }

    /// <summary>
    /// Delete tenant protocol configuration (reset to defaults)
    /// </summary>
    [HttpDelete("{tenantId}/protocol-configuration")]
    public async Task<IActionResult> DeleteProtocolConfiguration(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        tenant.ProtocolConfiguration = null;
        tenant.Updated = DateTime.UtcNow;

        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Reset protocol configuration for tenant: {TenantId}", tenantId);

        return NoContent();
    }

    private static TenantDto MapToDto(Tenant tenant) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        DisplayName = tenant.DisplayName,
        Identifier = tenant.Identifier,
        Description = tenant.Description,
        Enabled = tenant.Enabled,
        Configuration = tenant.Configuration,
        Created = tenant.Created,
        Updated = tenant.Updated
    };

    private static TenantProtocolConfigurationDto MapProtocolConfigToDto(TenantProtocolConfiguration? config)
    {
        if (config == null)
        {
            return new TenantProtocolConfigurationDto();
        }

        return new TenantProtocolConfigurationDto
        {
            AllowedGrantTypes = DeserializeJsonArray(config.AllowedGrantTypesJson),
            AllowedResponseTypes = DeserializeJsonArray(config.AllowedResponseTypesJson),
            AllowedTokenEndpointAuthMethods = DeserializeJsonArray(config.AllowedTokenEndpointAuthMethodsJson),
            SubjectTypesSupported = DeserializeJsonArray(config.SubjectTypesSupportedJson),
            IdTokenSigningAlgValuesSupported = DeserializeJsonArray(config.IdTokenSigningAlgValuesSupportedJson),
            CodeChallengeMethodsSupported = DeserializeJsonArray(config.CodeChallengeMethodsSupportedJson),
            DPoPSigningAlgValuesSupported = DeserializeJsonArray(config.DPoPSigningAlgValuesSupportedJson),
            RequirePushedAuthorizationRequests = config.RequirePushedAuthorizationRequests,
            RequirePkce = config.RequirePkce,
            AllowPlainPkce = config.AllowPlainPkce,
            RequireDPoP = config.RequireDPoP,
            ClaimsParameterSupported = config.ClaimsParameterSupported,
            RequestParameterSupported = config.RequestParameterSupported,
            RequestUriParameterSupported = config.RequestUriParameterSupported,
            FrontchannelLogoutSupported = config.FrontchannelLogoutSupported,
            BackchannelLogoutSupported = config.BackchannelLogoutSupported,
            Created = config.Created,
            Updated = config.Updated
        };
    }

    private static List<string>? DeserializeJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? SerializeJsonArray(List<string>? list)
    {
        if (list == null || list.Count == 0) return null;
        return System.Text.Json.JsonSerializer.Serialize(list);
    }
}

#region DTOs

public class TenantDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public string? Configuration { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreateTenantRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
}

public class UpdateTenantRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public string? Configuration { get; set; }
}

public class TenantProtocolConfigurationDto
{
    public List<string>? AllowedGrantTypes { get; set; }
    public List<string>? AllowedResponseTypes { get; set; }
    public List<string>? AllowedTokenEndpointAuthMethods { get; set; }
    public List<string>? SubjectTypesSupported { get; set; }
    public List<string>? IdTokenSigningAlgValuesSupported { get; set; }
    public List<string>? CodeChallengeMethodsSupported { get; set; }
    public List<string>? DPoPSigningAlgValuesSupported { get; set; }
    public bool RequirePushedAuthorizationRequests { get; set; }
    public bool RequirePkce { get; set; }
    public bool AllowPlainPkce { get; set; }
    public bool RequireDPoP { get; set; }
    public bool ClaimsParameterSupported { get; set; }
    public bool RequestParameterSupported { get; set; } = true;
    public bool RequestUriParameterSupported { get; set; } = true;
    public bool FrontchannelLogoutSupported { get; set; } = true;
    public bool BackchannelLogoutSupported { get; set; } = true;
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class UpdateTenantProtocolConfigurationRequest
{
    public List<string>? AllowedGrantTypes { get; set; }
    public List<string>? AllowedResponseTypes { get; set; }
    public List<string>? AllowedTokenEndpointAuthMethods { get; set; }
    public List<string>? SubjectTypesSupported { get; set; }
    public List<string>? IdTokenSigningAlgValuesSupported { get; set; }
    public List<string>? CodeChallengeMethodsSupported { get; set; }
    public List<string>? DPoPSigningAlgValuesSupported { get; set; }
    public bool? RequirePushedAuthorizationRequests { get; set; }
    public bool? RequirePkce { get; set; }
    public bool? AllowPlainPkce { get; set; }
    public bool? RequireDPoP { get; set; }
    public bool? ClaimsParameterSupported { get; set; }
    public bool? RequestParameterSupported { get; set; }
    public bool? RequestUriParameterSupported { get; set; }
    public bool? FrontchannelLogoutSupported { get; set; }
    public bool? BackchannelLogoutSupported { get; set; }
}


#endregion
