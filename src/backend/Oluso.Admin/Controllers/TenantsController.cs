using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing Tenants (SuperAdmin or OrgAdmin)
/// </summary>
[Route("api/admin/tenants")]
[Authorize(Policy = "SuperAdminOrOrgAdmin")]
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
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult<IEnumerable<TenantDto>>> GetAll(CancellationToken cancellationToken)
    {
        var tenants = await _tenantStore.GetAllAsync(cancellationToken);
        return Ok(tenants.Select(MapToDto));
    }

    /// <summary>
    /// Get tenant by ID
    /// </summary>
    [HttpGet("{tenantId}")]
    [RequirePermission(AdminPermissions.TenantsRead)]
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
    [RequirePermission(AdminPermissions.TenantsRead)]
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
    [RequirePermission(AdminPermissions.TenantsWrite)]
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
    [RequirePermission(AdminPermissions.TenantsWrite)]
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
    [RequirePermission(AdminPermissions.TenantsDelete)]
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
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
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
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
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
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
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
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
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

        // Dynamic Client Registration (RFC 7591)
        if (request.EnableDynamicClientRegistration.HasValue)
            config.EnableDynamicClientRegistration = request.EnableDynamicClientRegistration.Value;
        if (request.AllowOpenDynamicRegistration.HasValue)
            config.AllowOpenDynamicRegistration = request.AllowOpenDynamicRegistration.Value;
        if (request.DynamicRegistrationAllowedScopes != null)
            config.DynamicRegistrationAllowedScopesJson = SerializeJsonArray(request.DynamicRegistrationAllowedScopes);
        if (request.DynamicRegistrationAllowedGrantTypes != null)
            config.DynamicRegistrationAllowedGrantTypesJson = SerializeJsonArray(request.DynamicRegistrationAllowedGrantTypes);
        if (request.DynamicRegistrationRequirePkce.HasValue)
            config.DynamicRegistrationRequirePkce = request.DynamicRegistrationRequirePkce.Value;
        if (request.DynamicRegistrationMaxRedirectUris.HasValue)
            config.DynamicRegistrationMaxRedirectUris = request.DynamicRegistrationMaxRedirectUris.Value;

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
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
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

    /// <summary>
    /// Get tenant password policy
    /// </summary>
    [HttpGet("{tenantId}/password-policy")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult<PasswordPolicyDto>> GetPasswordPolicy(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        return Ok(MapPasswordPolicyToDto(tenant.PasswordPolicy));
    }

    /// <summary>
    /// Update tenant password policy
    /// </summary>
    [HttpPut("{tenantId}/password-policy")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<PasswordPolicyDto>> UpdatePasswordPolicy(
        string tenantId,
        [FromBody] UpdatePasswordPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        // Create or update password policy
        tenant.PasswordPolicy ??= new TenantPasswordPolicy { TenantId = tenantId };
        var policy = tenant.PasswordPolicy;

        if (request.MinimumLength.HasValue)
            policy.MinimumLength = request.MinimumLength.Value;
        if (request.MaximumLength.HasValue)
            policy.MaximumLength = request.MaximumLength.Value;
        if (request.RequireDigit.HasValue)
            policy.RequireDigit = request.RequireDigit.Value;
        if (request.RequireLowercase.HasValue)
            policy.RequireLowercase = request.RequireLowercase.Value;
        if (request.RequireUppercase.HasValue)
            policy.RequireUppercase = request.RequireUppercase.Value;
        if (request.RequireNonAlphanumeric.HasValue)
            policy.RequireNonAlphanumeric = request.RequireNonAlphanumeric.Value;
        if (request.RequiredUniqueChars.HasValue)
            policy.RequiredUniqueChars = request.RequiredUniqueChars.Value;
        if (request.PasswordHistoryCount.HasValue)
            policy.PasswordHistoryCount = request.PasswordHistoryCount.Value;
        if (request.PasswordExpirationDays.HasValue)
            policy.PasswordExpirationDays = request.PasswordExpirationDays.Value;
        if (request.MaxFailedAttempts.HasValue)
            policy.MaxFailedAttempts = request.MaxFailedAttempts.Value;
        if (request.LockoutDurationMinutes.HasValue)
            policy.LockoutDurationMinutes = request.LockoutDurationMinutes.Value;
        if (request.BlockCommonPasswords.HasValue)
            policy.BlockCommonPasswords = request.BlockCommonPasswords.Value;
        if (request.CheckBreachedPasswords.HasValue)
            policy.CheckBreachedPasswords = request.CheckBreachedPasswords.Value;
        if (request.CustomRegexPattern != null)
            policy.CustomRegexPattern = request.CustomRegexPattern;
        if (request.CustomRegexErrorMessage != null)
            policy.CustomRegexErrorMessage = request.CustomRegexErrorMessage;

        tenant.Updated = DateTime.UtcNow;

        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Updated password policy for tenant: {TenantId}", tenantId);

        return Ok(MapPasswordPolicyToDto(policy));
    }

    /// <summary>
    /// Delete tenant password policy (reset to defaults)
    /// </summary>
    [HttpDelete("{tenantId}/password-policy")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> DeletePasswordPolicy(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        tenant.PasswordPolicy = null;
        tenant.Updated = DateTime.UtcNow;

        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Reset password policy for tenant: {TenantId}", tenantId);

        return NoContent();
    }

    private static TenantDto MapToDto(Tenant tenant) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        DisplayName = tenant.DisplayName,
        Identifier = tenant.Identifier,
        Description = tenant.Description,
        CustomDomain = tenant.CustomDomain,
        Enabled = tenant.Enabled,
        Configuration = tenant.Configuration,
        CreatedAt = tenant.Created,
        UpdatedAt = tenant.Updated,
        PlanId = tenant.PlanId,
        PlanExpiresAt = tenant.PlanExpiresAt,
        AllowSelfRegistration = tenant.AllowSelfRegistration,
        RequireTermsAcceptance = tenant.RequireTermsAcceptance,
        TermsOfServiceUrl = tenant.TermsOfServiceUrl,
        PrivacyPolicyUrl = tenant.PrivacyPolicyUrl,
        RequireEmailVerification = tenant.RequireEmailVerification,
        AllowedEmailDomains = tenant.AllowedEmailDomains,
        UseJourneyFlow = tenant.UseJourneyFlow
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
            // Dynamic Client Registration (RFC 7591)
            EnableDynamicClientRegistration = config.EnableDynamicClientRegistration,
            AllowOpenDynamicRegistration = config.AllowOpenDynamicRegistration,
            DynamicRegistrationAllowedScopes = DeserializeJsonArray(config.DynamicRegistrationAllowedScopesJson),
            DynamicRegistrationAllowedGrantTypes = DeserializeJsonArray(config.DynamicRegistrationAllowedGrantTypesJson),
            DynamicRegistrationRequirePkce = config.DynamicRegistrationRequirePkce,
            DynamicRegistrationMaxRedirectUris = config.DynamicRegistrationMaxRedirectUris,
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

    private static PasswordPolicyDto MapPasswordPolicyToDto(TenantPasswordPolicy? policy)
    {
        if (policy == null)
        {
            // Return defaults when no policy is set
            return new PasswordPolicyDto();
        }

        return new PasswordPolicyDto
        {
            MinimumLength = policy.MinimumLength,
            MaximumLength = policy.MaximumLength,
            RequireDigit = policy.RequireDigit,
            RequireLowercase = policy.RequireLowercase,
            RequireUppercase = policy.RequireUppercase,
            RequireNonAlphanumeric = policy.RequireNonAlphanumeric,
            RequiredUniqueChars = policy.RequiredUniqueChars,
            PasswordHistoryCount = policy.PasswordHistoryCount,
            PasswordExpirationDays = policy.PasswordExpirationDays,
            MaxFailedAttempts = policy.MaxFailedAttempts,
            LockoutDurationMinutes = policy.LockoutDurationMinutes,
            BlockCommonPasswords = policy.BlockCommonPasswords,
            CheckBreachedPasswords = policy.CheckBreachedPasswords,
            CustomRegexPattern = policy.CustomRegexPattern,
            CustomRegexErrorMessage = policy.CustomRegexErrorMessage
        };
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
    public string? CustomDomain { get; set; }
    public bool Enabled { get; set; }
    public string? Configuration { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? PlanId { get; set; }
    public DateTime? PlanExpiresAt { get; set; }
    public bool AllowSelfRegistration { get; set; }
    public bool RequireTermsAcceptance { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public bool RequireEmailVerification { get; set; }
    public string? AllowedEmailDomains { get; set; }
    public bool UseJourneyFlow { get; set; }
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

    // Dynamic Client Registration (RFC 7591)
    public bool EnableDynamicClientRegistration { get; set; }
    public bool AllowOpenDynamicRegistration { get; set; }
    public List<string>? DynamicRegistrationAllowedScopes { get; set; }
    public List<string>? DynamicRegistrationAllowedGrantTypes { get; set; }
    public bool DynamicRegistrationRequirePkce { get; set; } = true;
    public int DynamicRegistrationMaxRedirectUris { get; set; } = 10;

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

    // Dynamic Client Registration (RFC 7591)
    public bool? EnableDynamicClientRegistration { get; set; }
    public bool? AllowOpenDynamicRegistration { get; set; }
    public List<string>? DynamicRegistrationAllowedScopes { get; set; }
    public List<string>? DynamicRegistrationAllowedGrantTypes { get; set; }
    public bool? DynamicRegistrationRequirePkce { get; set; }
    public int? DynamicRegistrationMaxRedirectUris { get; set; }
}

public class PasswordPolicyDto
{
    public int MinimumLength { get; set; } = 8;
    public int MaximumLength { get; set; } = 128;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; } = true;
    public int RequiredUniqueChars { get; set; } = 4;
    public int PasswordHistoryCount { get; set; } = 0;
    public int PasswordExpirationDays { get; set; } = 0;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
    public bool BlockCommonPasswords { get; set; } = true;
    public bool CheckBreachedPasswords { get; set; } = false;
    public string? CustomRegexPattern { get; set; }
    public string? CustomRegexErrorMessage { get; set; }
}

public class UpdatePasswordPolicyRequest
{
    public int? MinimumLength { get; set; }
    public int? MaximumLength { get; set; }
    public bool? RequireDigit { get; set; }
    public bool? RequireLowercase { get; set; }
    public bool? RequireUppercase { get; set; }
    public bool? RequireNonAlphanumeric { get; set; }
    public int? RequiredUniqueChars { get; set; }
    public int? PasswordHistoryCount { get; set; }
    public int? PasswordExpirationDays { get; set; }
    public int? MaxFailedAttempts { get; set; }
    public int? LockoutDurationMinutes { get; set; }
    public bool? BlockCommonPasswords { get; set; }
    public bool? CheckBreachedPasswords { get; set; }
    public string? CustomRegexPattern { get; set; }
    public string? CustomRegexErrorMessage { get; set; }
}

#endregion
