using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Core.Locale;
using Oluso.Admin.Dto.Tenants;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing Tenants.
/// TenantAdmins can access their own tenant's settings.
/// OrgAdmins can access tenants within their organization(s).
/// SuperAdmins have unrestricted access.
/// </summary>
[Route("api/admin/tenants")]
public class TenantsController : AdminBaseController
{
    private readonly ITenantStore _tenantStore;
    private readonly IRoleStore _roleStore;
    private readonly ITenantDefaultRoleSeeder _tenantDefaultRoleSeeder;
    private readonly ITenantFeatureStore _tenantFeatureStore;
    private readonly IOrganizationFeatureStore _orgFeatureStore;
    private readonly IUserModuleAccessStore _moduleAccessStore;
    private readonly IIdentityProviderStore _identityProviderStore;
    private readonly IOlusoEventService _eventService;
    private readonly ITenantConfigurationService _configService;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(
        ITenantStore tenantStore,
        IRoleStore roleStore,
        ITenantDefaultRoleSeeder tenantDefaultRoleSeeder,
        ITenantFeatureStore tenantFeatureStore,
        IOrganizationFeatureStore orgFeatureStore,
        IUserModuleAccessStore moduleAccessStore,
        IIdentityProviderStore identityProviderStore,
        IOlusoEventService eventService,
        ITenantConfigurationService configService,
        ILogger<TenantsController> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _tenantStore = tenantStore;
        _roleStore = roleStore;
        _tenantDefaultRoleSeeder = tenantDefaultRoleSeeder;
        _tenantFeatureStore = tenantFeatureStore;
        _orgFeatureStore = orgFeatureStore;
        _moduleAccessStore = moduleAccessStore;
        _identityProviderStore = identityProviderStore;
        _eventService = eventService;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Get all tenants (SuperAdmin only — lists all tenants across the system)
    /// </summary>
    [HttpGet]
    [RequireSuperAdmin]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult<IEnumerable<TenantDto>>> GetAll(CancellationToken cancellationToken)
    {
        var tenants = await _tenantStore.GetAllAsync(cancellationToken);
        return Ok(tenants.Select(MapToDto));
    }

    /// <summary>
    /// Get tenants for the current user's organization(s).
    /// OrgAdmins see tenants in their organization(s). SuperAdmins see all.
    /// Supports search by name/identifier and pagination.
    /// </summary>
    [HttpGet("my-organization")]
    [Authorize(Policy = "SuperAdminOrOrgAdmin")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult<PagedResult<TenantDto>>> GetMyOrganizationTenants(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Tenant> tenants;

        if (IsSuperAdmin)
        {
            tenants = await _tenantStore.GetAllAsync(cancellationToken);
        }
        else
        {
            var orgIds = AllOrganizationIds.ToList();
            if (orgIds.Count == 0)
            {
                return Ok(new PagedResult<TenantDto>
                {
                    Items = new List<TenantDto>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = 0
                });
            }

            var allTenants = new List<Tenant>();
            foreach (var orgId in orgIds)
            {
                var orgTenants = await _tenantStore.GetByOrganizationAsync(orgId, cancellationToken);
                allTenants.AddRange(orgTenants);
            }
            tenants = allTenants.DistinctBy(t => t.Id);
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            tenants = tenants.Where(t =>
                (t.Name != null && t.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (t.Identifier != null && t.Identifier.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (t.DisplayName != null && t.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var tenantList = tenants.ToList();
        var totalCount = tenantList.Count;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var items = tenantList
            .OrderByDescending(t => t.Created)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto)
            .ToList();

        return Ok(new PagedResult<TenantDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
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
        if (!CanAccessTenant(tenant))
            return Forbid();
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
        if (!CanAccessTenant(tenant))
            return Forbid();
        return Ok(MapToDto(tenant));
    }

    /// <summary>
    /// Create a new tenant
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "SuperAdminOrOrgAdmin")]
    [RequirePermission(AdminPermissions.TenantsWrite)]
    public async Task<ActionResult<TenantDto>> Create(
        [FromBody] CreateTenantRequest request,
        CancellationToken cancellationToken)
    {
        // Block reserved tenant IDs (starting with underscore)
        if (request.Identifier?.StartsWith("_") == true)
        {
            return BadRequest(new { error = "Tenant identifier cannot start with underscore (reserved for system use)" });
        }

        // Check for existing identifier
        var existing = await _tenantStore.GetByIdentifierAsync(request.Identifier!, cancellationToken);
        if (existing != null)
        {
            return Conflict(new { error = $"Tenant with identifier '{request.Identifier}' already exists" });
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Identifier = request.Identifier!,
            Description = request.Description,
            Enabled = true
        };

        var created = await _tenantStore.CreateAsync(tenant, cancellationToken);

        // Seed default roles for the new tenant. Without an Admin role nobody
        // can sign in to it, so a failure removes the tenant again.
        if (!await SeedDefaultRolesAsync(created.Id, cancellationToken))
        {
            try
            {
                await _tenantStore.DeleteAsync(created.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove tenant {TenantId} after its default roles could not be seeded", created.Id);
            }
            return StatusCode(500, new { error = "Failed to seed default roles for the new tenant" });
        }

        // Raise event to trigger module seeding (email templates, default data)
        await _eventService.RaiseAsync(
            new AdminTenantCreatedEvent(created, AdminUserId!, User.Identity?.Name),
            cancellationToken);

        _logger.LogInformation("Created tenant: {TenantId} ({Name}) with default roles", created.Id, created.Name);

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
        if (!CanAccessTenant(tenant))
            return Forbid();

        // Cross-field validation that needs to know the tenant's IdP roster.
        //   1. DefaultIdpScheme — must reference an IdP enabled for this tenant.
        //   2. Sign-in lockout — at least one of {local login, passkey, an
        //      enabled external IdP} must remain after the update, otherwise
        //      the tenant's login page has no way for users to authenticate.
        // We load the IdP list only when one of the relevant inputs is being
        // touched, to avoid a DB round-trip on every tenant update.
        var requestedScheme = request.DefaultIdpScheme?.Trim();
        var touchesLockoutInputs = request.EnableLocalLogin.HasValue || request.EnablePasskey.HasValue;
        if (!string.IsNullOrWhiteSpace(requestedScheme) || touchesLockoutInputs)
        {
            // GetAllForTenantAsync queries by the path tenantId explicitly
            // rather than the ambient ITenantContext — required for SuperAdmin
            // cross-tenant edits where the logged-in admin's tenant differs
            // from the tenant being edited.
            var providers = (await _identityProviderStore
                .GetAllForTenantAsync(tenantId, cancellationToken))
                .ToList();

            if (!string.IsNullOrWhiteSpace(requestedScheme))
            {
                var match = providers.FirstOrDefault(p =>
                    string.Equals(p.Scheme, requestedScheme, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    return BadRequest(new
                    {
                        error = $"Identity provider '{requestedScheme}' is not configured for this tenant.",
                    });
                }
                if (!match.Enabled)
                {
                    return BadRequest(new
                    {
                        error = $"Identity provider '{requestedScheme}' is disabled — enable it before setting it as the default.",
                    });
                }
                // Normalise to the stored casing so the persisted value
                // exactly matches the IdP's scheme.
                requestedScheme = match.Scheme;
            }

            if (touchesLockoutInputs)
            {
                var effectiveLocalLogin = request.EnableLocalLogin ?? tenant.EnableLocalLogin;
                var effectivePasskey = request.EnablePasskey ?? tenant.EnablePasskey;
                var hasEnabledIdp = providers.Any(p => p.Enabled);

                if (!effectiveLocalLogin && !effectivePasskey && !hasEnabledIdp)
                {
                    return BadRequest(new
                    {
                        error = "Cannot apply this update: it would leave the tenant with no sign-in method. " +
                                "Enable at least one of {local login, passkey, an external identity provider} before disabling the others.",
                    });
                }
            }
        }

        var wasEnabled = tenant.Enabled;

        // Fields available to OrgAdmin and SuperAdmin
        if (request.Name != null) tenant.Name = request.Name;
        if (request.DisplayName != null) tenant.DisplayName = request.DisplayName;
        if (request.Description != null) tenant.Description = request.Description;
        if (request.UseJourneyFlow.HasValue) tenant.UseJourneyFlow = request.UseJourneyFlow.Value;
        if (request.JourneyFallbackToStandard.HasValue) tenant.JourneyFallbackToStandard = request.JourneyFallbackToStandard.Value;
        if (request.AllowSelfRegistration.HasValue) tenant.AllowSelfRegistration = request.AllowSelfRegistration.Value;
        if (request.RequireTermsAcceptance.HasValue) tenant.RequireTermsAcceptance = request.RequireTermsAcceptance.Value;
        if (request.TermsOfServiceUrl != null) tenant.TermsOfServiceUrl = request.TermsOfServiceUrl;
        if (request.PrivacyPolicyUrl != null) tenant.PrivacyPolicyUrl = request.PrivacyPolicyUrl;
        if (request.RequireEmailVerification.HasValue) tenant.RequireEmailVerification = request.RequireEmailVerification.Value;
        if (request.AllowedEmailDomains != null) tenant.AllowedEmailDomains = request.AllowedEmailDomains;
        if (request.EnableLocalLogin.HasValue) tenant.EnableLocalLogin = request.EnableLocalLogin.Value;
        if (request.EnablePasskey.HasValue) tenant.EnablePasskey = request.EnablePasskey.Value;
        if (request.AllowPlatformOrgSignIn.HasValue) tenant.AllowPlatformOrgSignIn = request.AllowPlatformOrgSignIn.Value;
        if (request.DefaultIdpScheme != null)
        {
            tenant.DefaultIdpScheme = string.IsNullOrWhiteSpace(requestedScheme)
                ? null
                : requestedScheme;
        }
        if (request.DefaultSignInPolicyId != null)
        {
            tenant.DefaultSignInPolicyId = string.IsNullOrWhiteSpace(request.DefaultSignInPolicyId)
                ? null
                : request.DefaultSignInPolicyId.Trim();
        }
        if (request.DefaultSsoUiMode != null)
        {
            var requestedUiMode = request.DefaultSsoUiMode.Trim();
            if (string.IsNullOrEmpty(requestedUiMode))
            {
                tenant.DefaultSsoUiMode = null;
            }
            else if (string.Equals(requestedUiMode, "Journey", StringComparison.OrdinalIgnoreCase))
            {
                tenant.DefaultSsoUiMode = "Journey";
            }
            else if (string.Equals(requestedUiMode, "Standalone", StringComparison.OrdinalIgnoreCase))
            {
                tenant.DefaultSsoUiMode = "Standalone";
            }
            else
            {
                return BadRequest(new
                {
                    error = $"DefaultSsoUiMode must be 'Journey' or 'Standalone', got '{requestedUiMode}'.",
                });
            }
        }

        // Visual branding (logo, favicon, colours, CSS, support email, footer text)
        // is updated via /api/admin/settings/branding (BrandingSettings config
        // section), not through this endpoint. Only HideEmbedBranding stays
        // here because it's the SuperAdmin platform-licensing toggle.

        // SuperAdmin-only fields — silently ignored for non-SuperAdmin
        if (IsSuperAdmin)
        {
            if (request.Enabled.HasValue) tenant.Enabled = request.Enabled.Value;
            // Note: raw Configuration assignment removed — use per-section APIs instead
            if (request.HideEmbedBranding.HasValue)
            {
                tenant.Branding ??= new TenantBranding();
                tenant.Branding.HideEmbedBranding = request.HideEmbedBranding.Value;
            }
        }

        var updated = await _tenantStore.UpdateAsync(tenant, cancellationToken);

        await RaiseLifecycleAsync((t, a, n) => new AdminTenantUpdatedEvent(t, a, n), updated, cancellationToken);
        if (wasEnabled != updated.Enabled)
        {
            if (updated.Enabled)
                await RaiseLifecycleAsync((t, a, n) => new AdminTenantReactivatedEvent(t, a, n), updated, cancellationToken);
            else
                await RaiseLifecycleAsync((t, a, n) => new AdminTenantSuspendedEvent(t, a, n), updated, cancellationToken);
        }

        _logger.LogInformation("Updated tenant: {TenantId}", tenantId);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete a tenant (SuperAdmin only)
    /// </summary>
    [HttpDelete("{tenantId}")]
    [RequireSuperAdmin]
    [RequirePermission(AdminPermissions.TenantsDelete)]
    public async Task<IActionResult> Delete(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!string.IsNullOrEmpty(tenant.OrganizationId))
        {
            var orgTenants = await _tenantStore.GetByOrganizationAsync(tenant.OrganizationId, cancellationToken);
            if (orgTenants.Count() <= 1)
                return BadRequest(new { error = "Cannot delete the last tenant in an organization" });
        }

        await _tenantStore.DeleteAsync(tenantId, cancellationToken);

        await RaiseLifecycleAsync((t, a, n) => new AdminTenantDeletedEvent(t, a, n), tenant, cancellationToken);

        _logger.LogInformation("Deleted tenant: {TenantId}", tenantId);

        return NoContent();
    }

    /// <summary>
    /// Enable a tenant
    /// </summary>
    [HttpPost("{tenantId}/enable")]
    [Authorize(Policy = "SuperAdminOrOrgAdmin")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> Enable(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();
        if (!CanAccessTenant(tenant))
            return Forbid();

        var wasEnabled = tenant.Enabled;
        tenant.Enabled = true;
        await _tenantStore.UpdateAsync(tenant, cancellationToken);
        if (!wasEnabled)
            await RaiseLifecycleAsync((t, a, n) => new AdminTenantReactivatedEvent(t, a, n), tenant, cancellationToken);

        _logger.LogInformation("Enabled tenant: {TenantId}", tenantId);

        return Ok(new { enabled = true });
    }

    /// <summary>
    /// Disable a tenant
    /// </summary>
    [HttpPost("{tenantId}/disable")]
    [Authorize(Policy = "SuperAdminOrOrgAdmin")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> Disable(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();
        if (!CanAccessTenant(tenant))
            return Forbid();

        var wasEnabled = tenant.Enabled;
        tenant.Enabled = false;
        await _tenantStore.UpdateAsync(tenant, cancellationToken);
        if (wasEnabled)
            await RaiseLifecycleAsync((t, a, n) => new AdminTenantSuspendedEvent(t, a, n), tenant, cancellationToken);

        _logger.LogWarning("Disabled tenant: {TenantId}", tenantId);

        return Ok(new { enabled = false });
    }

    /// <summary>
    /// Raises a platform tenant lifecycle event with the tenant's snapshot. A failure is
    /// logged, never surfaced: the change itself has already been saved.
    /// </summary>
    private async Task RaiseLifecycleAsync(
        Func<Tenant, string, string?, AdminTenantLifecycleEvent> create, Tenant tenant, CancellationToken cancellationToken)
    {
        AdminTenantLifecycleEvent? evt = null;
        try
        {
            evt = create(tenant, AdminUserId ?? "system", User.Identity?.Name);
            await _eventService.RaiseAsync(evt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to raise {Event} for tenant {TenantId}", evt?.GetType().Name, tenant.Id);
        }
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
        if (!CanAccessTenant(tenant))
            return Forbid();

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
        if (!CanAccessTenant(tenant))
            return Forbid();

        if (ProtocolConfigurationRules.Validate(request.PrefixScopeSuffixMaxLength, request.DynamicRegistrationAllowedScopes) is { } invalid)
        {
            return BadRequest(new { error = "invalid_request", error_description = invalid });
        }

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
        if (request.EnforceOAuth21.HasValue)
            config.EnforceOAuth21 = request.EnforceOAuth21.Value;
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

        // Prefix scopes
        if (request.PrefixScopeSuffixMaxLength.HasValue)
            config.PrefixScopeSuffixMaxLength =
                ProtocolConfigurationRules.StoredPrefixScopeSuffixMaxLength(request.PrefixScopeSuffixMaxLength.Value);

        // Refresh-implying scopes: an empty list is stored as [] (off), unlike the other lists
        if (request.ResetRefreshImplyingScopes == true)
            config.RefreshImplyingScopesJson = null;
        else if (request.RefreshImplyingScopes != null)
            config.RefreshImplyingScopesJson = System.Text.Json.JsonSerializer.Serialize(
                request.RefreshImplyingScopes.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList());

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
        if (!CanAccessTenant(tenant))
            return Forbid();

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
        if (!CanAccessTenant(tenant))
            return Forbid();

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
        if (!CanAccessTenant(tenant))
            return Forbid();

        // Lockout can be tuned but not switched off: a failed-attempt limit below 1 or a zero
        // lockout would leave passwords open to unlimited guessing.
        if (request.MaxFailedAttempts is < 1 or > 20)
            return BadRequest(new { errors = new[] { "maxFailedAttempts must be between 1 and 20" } });
        if (request.LockoutDurationMinutes is < 1 or > 1440)
            return BadRequest(new { errors = new[] { "lockoutDurationMinutes must be between 1 and 1440 (24 hours)" } });

        if (request.PasswordHistoryCount is < 0 or > 24)
            return BadRequest(new { errors = new[] { "passwordHistoryCount must be between 0 and 24" } });
        if (request.PasswordExpirationDays is < 0 or > 3650)
            return BadRequest(new { errors = new[] { "passwordExpirationDays must be between 0 and 3650" } });

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
        if (request.RejectWhenBreachCheckUnavailable.HasValue)
            policy.RejectWhenBreachCheckUnavailable = request.RejectWhenBreachCheckUnavailable.Value;
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
        if (!CanAccessTenant(tenant))
            return Forbid();

        tenant.PasswordPolicy = null;
        tenant.Updated = DateTime.UtcNow;

        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Reset password policy for tenant: {TenantId}", tenantId);

        return NoContent();
    }

    // ============ Locale Endpoints ============

    /// <summary>
    /// Get supported currencies
    /// </summary>
    [HttpGet("locale/currencies")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public ActionResult<IEnumerable<CurrencyInfo>> GetSupportedCurrencies()
    {
        return Ok(SupportedCurrencies.All);
    }

    /// <summary>
    /// Get supported locales
    /// </summary>
    [HttpGet("locale/locales")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public ActionResult<IEnumerable<LocaleInfo>> GetSupportedLocales()
    {
        return Ok(SupportedLocales.All);
    }

    /// <summary>
    /// Get tenant locale settings
    /// </summary>
    [HttpGet("{tenantId}/locale")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult<LocaleSettings>> GetLocaleSettings(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!CanAccessTenant(tenant))
            return Forbid();

        var locale = await _configService.GetSectionAsync<LocaleSettings>(tenantId, "Locale", cancellationToken);
        return Ok(locale ?? LocaleSettings.Default);
    }

    /// <summary>
    /// Update tenant locale settings
    /// </summary>
    [HttpPut("{tenantId}/locale")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<LocaleSettings>> UpdateLocaleSettings(
        string tenantId,
        [FromBody] LocaleSettings request,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();
        if (!CanAccessTenant(tenant))
            return Forbid();

        await _configService.SetSectionAsync(tenantId, "Locale", request, cancellationToken);

        _logger.LogInformation("Updated locale settings for tenant: {TenantId}", tenantId);

        // Lets integrations that mirror tenant settings (e.g. a chat workspace's time zone) follow.
        // A failure is logged, never surfaced: the change itself has already been saved.
        try
        {
            await _eventService.RaiseAsync(new AdminTenantSettingsUpdatedEvent
            {
                TenantId = tenantId,
                ResourceId = tenantId,
                ResourceName = tenant.Name,
                AdminUserId = AdminUserId ?? "system",
                AdminUserName = AdminUserName,
                IpAddress = ClientIp,
                ChangedSettings = new List<string> { "Locale" },
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to raise the settings-updated event for tenant {TenantId}", tenantId);
        }

        return Ok(request);
    }

    // ============ Workspace Apps Endpoints ============

    /// <summary>
    /// Get tenant workspace apps settings
    /// </summary>
    [HttpGet("{tenantId}/workspace-apps")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult<WorkspaceAppsSettings>> GetWorkspaceAppsSettings(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();
        if (!CanAccessTenant(tenant))
            return Forbid();

        var settings = await _configService.GetSectionAsync<WorkspaceAppsSettings>(tenantId, "WorkspaceApps", cancellationToken);
        return Ok(settings ?? WorkspaceAppsSettings.Default);
    }

    /// <summary>
    /// Update tenant workspace apps settings
    /// </summary>
    [HttpPut("{tenantId}/workspace-apps")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<WorkspaceAppsSettings>> UpdateWorkspaceAppsSettings(
        string tenantId,
        [FromBody] WorkspaceAppsSettings request,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();
        if (!CanAccessTenant(tenant))
            return Forbid();

        // Validate: enabled built-in apps must have their feature enabled
        var hasFeatures = await _tenantFeatureStore.HasAnyFeaturesConfiguredAsync(tenantId, cancellationToken);
        if (hasFeatures)
        {
            // Built-in apps are keyed by their module feature key (the
            // workspace-apps catalog uses the feature key as the app id), so
            // the app id is the feature key to validate directly.
            foreach (var (appId, enabled) in request.BuiltInApps)
            {
                if (!enabled) continue;
                var featureEnabled = await _tenantFeatureStore.IsFeatureEnabledAsync(tenantId, appId, cancellationToken);
                if (!featureEnabled)
                {
                    return BadRequest(new { error = $"Cannot enable app '{appId}' because its feature is not enabled for this tenant" });
                }
            }
        }

        await _configService.SetSectionAsync(tenantId, "WorkspaceApps", request, cancellationToken);

        _logger.LogInformation("Updated workspace apps settings for tenant: {TenantId}", tenantId);

        return Ok(request);
    }

    // ============ Tenant Features Endpoints ============

    /// <summary>
    /// Get features for a tenant, including org-level availability.
    /// </summary>
    [HttpGet("{tenantId}/features")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<TenantModuleFeaturesResponse>> GetFeatures(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!CanAccessTenant(tenant))
            return Forbid();

        var tenantFeatures = await _tenantFeatureStore.GetFeaturesAsync(tenantId, cancellationToken);
        var tenantFeatureMap = tenantFeatures.ToDictionary(f => f.FeatureKey, StringComparer.OrdinalIgnoreCase);

        // Get org-level features to show availability
        IReadOnlyList<OrganizationFeature>? orgFeatures = null;
        bool orgHasFeatures = false;
        if (!string.IsNullOrEmpty(tenant.OrganizationId))
        {
            orgHasFeatures = await _orgFeatureStore.HasAnyFeaturesConfiguredAsync(tenant.OrganizationId, cancellationToken);
            if (orgHasFeatures)
                orgFeatures = await _orgFeatureStore.GetFeaturesAsync(tenant.OrganizationId, cancellationToken);
        }
        var orgFeatureMap = orgFeatures?.ToDictionary(f => f.FeatureKey, StringComparer.OrdinalIgnoreCase);

        // Only surface features the org has explicitly enabled. Without an org grant
        // the tenant cannot toggle the feature, so it has no place in tenant settings.
        var features = ModuleFeatures.GetRegistered()
            .Select(def =>
            {
                var orgFeature = orgFeatureMap != null && orgFeatureMap.TryGetValue(def.Key, out var ofv) ? ofv : null;
                var orgEnabled = orgFeature != null
                    && orgFeature.Enabled
                    && (orgFeature.ExpiresAt == null || orgFeature.ExpiresAt > DateTime.UtcNow);

                tenantFeatureMap.TryGetValue(def.Key, out var tf);

                return new TenantFeatureDto
                {
                    Key = def.Key,
                    DisplayName = def.DisplayName,
                    Description = def.Description,
                    Enabled = tf != null && tf.Enabled,
                    OrgEnabled = orgEnabled,
                    GrantedAt = tf?.GrantedAt,
                    MaxSeats = tf?.MaxSeats,
                    OrgMaxSeats = orgFeature?.MaxSeats,
                };
            })
            .Where(f => f.OrgEnabled)
            .ToList();

        return Ok(new TenantModuleFeaturesResponse
        {
            TenantId = tenantId,
            HasFeaturesConfigured = tenantFeatures.Count > 0,
            Features = features,
        });
    }

    /// <summary>
    /// Bulk set features for a tenant. Validates org-level ceiling.
    /// </summary>
    [HttpPut("{tenantId}/features")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> SetTenantFeatures(
        string tenantId,
        [FromBody] SetTenantFeaturesRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!CanAccessTenant(tenant))
            return Forbid();

        // Validate: cannot enable features the org doesn't allow, and tenant seats cannot exceed org seats
        if (!string.IsNullOrEmpty(tenant.OrganizationId))
        {
            var orgHasFeatures = await _orgFeatureStore.HasAnyFeaturesConfiguredAsync(tenant.OrganizationId, cancellationToken);
            if (orgHasFeatures)
            {
                var orgFeatures = await _orgFeatureStore.GetFeaturesAsync(tenant.OrganizationId, cancellationToken);
                foreach (var f in request.Features.Where(f => f.Enabled))
                {
                    var orgFeature = orgFeatures.FirstOrDefault(o => o.FeatureKey.Equals(f.FeatureKey, StringComparison.OrdinalIgnoreCase));
                    if (orgFeature == null || !orgFeature.Enabled)
                        return BadRequest(new { error = $"Feature '{f.FeatureKey}' is not available at the organization level" });

                    // Tenant seat limit cannot exceed org seat limit
                    if (f.MaxSeats != null && orgFeature.MaxSeats != null && f.MaxSeats > orgFeature.MaxSeats)
                        return BadRequest(new { error = $"Tenant seat limit ({f.MaxSeats}) for '{f.FeatureKey}' exceeds organization limit ({orgFeature.MaxSeats})" });
                }
            }
        }

        var before = (await _tenantFeatureStore.GetFeaturesAsync(tenantId, cancellationToken))
            .GroupBy(f => f.FeatureKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Any(f => f.Enabled), StringComparer.OrdinalIgnoreCase);
        var updates = request.Features.Select(f => new TenantFeatureUpdate(f.FeatureKey, f.Enabled, f.MaxSeats));
        await _tenantFeatureStore.SetFeaturesAsync(tenantId, updates, AdminUserId, cancellationToken);

        // The same events as the single enable/disable endpoints, for each feature this save switched.
        foreach (var f in request.Features)
        {
            var was = before.TryGetValue(f.FeatureKey, out var on) && on;
            if (f.Enabled && !was)
                await RaiseTenantFeatureEnabledAsync(tenant, f.FeatureKey, cancellationToken);
            else if (!f.Enabled && was)
                await RaiseTenantFeatureDisabledAsync(tenant, f.FeatureKey, cancellationToken);
        }

        _logger.LogInformation("Set features for tenant {TenantId} by {AdminUserId}: {Features}",
            tenantId, AdminUserId,
            string.Join(", ", request.Features.Where(f => f.Enabled).Select(f => f.FeatureKey)));

        return Ok(new { message = "Features updated" });
    }

    /// <summary>
    /// Enable a single feature for a tenant. Validates org-level ceiling.
    /// </summary>
    [HttpPost("{tenantId}/features/{featureKey}")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> EnableTenantFeature(
        string tenantId,
        string featureKey,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!CanAccessTenant(tenant))
            return Forbid();

        // Validate org ceiling
        if (!string.IsNullOrEmpty(tenant.OrganizationId))
        {
            var orgHasFeatures = await _orgFeatureStore.HasAnyFeaturesConfiguredAsync(tenant.OrganizationId, cancellationToken);
            if (orgHasFeatures)
            {
                var orgEnabled = await _orgFeatureStore.IsFeatureEnabledAsync(tenant.OrganizationId, featureKey, cancellationToken);
                if (!orgEnabled)
                    return BadRequest(new { error = $"Feature '{featureKey}' is not available at the organization level" });
            }
        }

        await _tenantFeatureStore.EnableFeatureAsync(tenantId, featureKey, AdminUserId, cancellationToken);

        _logger.LogInformation("Enabled feature '{Feature}' for tenant {TenantId} by {AdminUserId}",
            featureKey, tenantId, AdminUserId);

        await RaiseTenantFeatureEnabledAsync(tenant, featureKey, cancellationToken);

        return Ok(new { featureKey, enabled = true });
    }

    private Task RaiseTenantFeatureEnabledAsync(Tenant tenant, string featureKey, CancellationToken cancellationToken)
        => _eventService.RaiseAsync(new FeatureEnabledEvent
        {
            TenantId = tenant.Id,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = tenant.Id,
            FeatureKey = featureKey,
            FeatureScope = "tenant",
            OrganizationId = tenant.OrganizationId
        }, cancellationToken);

    private Task RaiseTenantFeatureDisabledAsync(Tenant tenant, string featureKey, CancellationToken cancellationToken)
        => _eventService.RaiseAsync(new FeatureDisabledEvent
        {
            TenantId = tenant.Id,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = tenant.Id,
            FeatureKey = featureKey,
            FeatureScope = "tenant",
            OrganizationId = tenant.OrganizationId
        }, cancellationToken);

    /// <summary>
    /// Disable a single feature for a tenant
    /// </summary>
    [HttpDelete("{tenantId}/features/{featureKey}")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> DisableTenantFeature(
        string tenantId,
        string featureKey,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!CanAccessTenant(tenant))
            return Forbid();

        var wasEnabled = await _tenantFeatureStore.IsFeatureEnabledAsync(tenantId, featureKey, cancellationToken);
        await _tenantFeatureStore.DisableFeatureAsync(tenantId, featureKey, cancellationToken);

        _logger.LogInformation("Disabled feature '{Feature}' for tenant {TenantId} by {AdminUserId}",
            featureKey, tenantId, AdminUserId);

        if (wasEnabled)
            await RaiseTenantFeatureDisabledAsync(tenant, featureKey, cancellationToken);

        return Ok(new { featureKey, enabled = false });
    }

    /// <summary>
    /// Get module seat allocation and usage for a tenant feature.
    /// </summary>
    [HttpGet("{tenantId}/modules/{featureKey}/seats")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult> GetModuleSeats(
        string tenantId,
        string featureKey,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!CanAccessTenant(tenant))
            return Forbid();

        var features = await _tenantFeatureStore.GetFeaturesAsync(tenantId, cancellationToken);
        var feature = features.FirstOrDefault(f => f.FeatureKey == featureKey);
        if (feature == null)
            return NotFound(new { Message = $"Feature '{featureKey}' not found for tenant" });

        var seats = await _moduleAccessStore.GetAccessAsync(tenantId, featureKey, cancellationToken);

        // Include org-level seat cap if applicable
        int? orgMaxSeats = null;
        if (!string.IsNullOrEmpty(tenant.OrganizationId))
        {
            var orgFeatures = await _orgFeatureStore.GetFeaturesAsync(tenant.OrganizationId, cancellationToken);
            orgMaxSeats = orgFeatures.FirstOrDefault(f => f.FeatureKey == featureKey)?.MaxSeats;
        }

        return Ok(new
        {
            featureKey,
            maxSeats = feature.MaxSeats,
            orgMaxSeats,
            usedSeats = seats.Count,
            users = seats.Select(s => new
            {
                s.UserId,
                s.GrantedAt,
                s.GrantedByUserId,
            }),
        });
    }

    /// <summary>
    /// Grant a user a seat in a module.
    /// </summary>
    [HttpPost("{tenantId}/modules/{featureKey}/seats/{userId}")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult> GrantModuleSeat(
        string tenantId,
        string featureKey,
        string userId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!CanAccessTenant(tenant))
            return Forbid();

        var features = await _tenantFeatureStore.GetFeaturesAsync(tenantId, cancellationToken);
        var feature = features.FirstOrDefault(f => f.FeatureKey == featureKey);
        if (feature == null || !feature.Enabled)
            return BadRequest(new { Message = $"Feature '{featureKey}' is not enabled for tenant" });

        if (feature.MaxSeats != null)
        {
            var seatCount = await _moduleAccessStore.GetSeatCountAsync(tenantId, featureKey, cancellationToken);
            if (seatCount >= feature.MaxSeats.Value)
                return Conflict(new { Message = $"Seat limit reached ({feature.MaxSeats.Value})" });
        }

        await _moduleAccessStore.GrantAccessAsync(tenantId, userId, featureKey, AdminUserId, cancellationToken);

        _logger.LogInformation("Granted {FeatureKey} seat to user {UserId} in tenant {TenantId} by {AdminUserId}",
            featureKey, userId, tenantId, AdminUserId);

        return Ok(new { featureKey, userId, granted = true });
    }

    /// <summary>
    /// Revoke a user's seat in a module.
    /// </summary>
    [HttpDelete("{tenantId}/modules/{featureKey}/seats/{userId}")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult> RevokeModuleSeat(
        string tenantId,
        string featureKey,
        string userId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();

        if (!CanAccessTenant(tenant))
            return Forbid();

        await _moduleAccessStore.RevokeAccessAsync(tenantId, userId, featureKey, cancellationToken);

        _logger.LogInformation("Revoked {FeatureKey} seat from user {UserId} in tenant {TenantId} by {AdminUserId}",
            featureKey, userId, tenantId, AdminUserId);

        return Ok(new { featureKey, userId, revoked = true });
    }

    /// <summary>
    /// Checks whether the current caller can access the given tenant.
    /// SuperAdmin: any tenant. OrgAdmin: tenants in their org(s). TenantAdmin: own tenant only.
    /// </summary>
    /// <summary>
    /// Seeds the default roles (Admin, User) for a newly created tenant.
    /// </summary>
    private async Task<bool> SeedDefaultRolesAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _tenantDefaultRoleSeeder.SeedAsync(tenantId, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed default roles for tenant {TenantId}: {Reason}", tenantId, ex.GetBaseException().Message);
            return false;
        }
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
        UseJourneyFlow = tenant.UseJourneyFlow,
        JourneyFallbackToStandard = tenant.JourneyFallbackToStandard,
        HideEmbedBranding = tenant.Branding?.HideEmbedBranding ?? false,
        EnableLocalLogin = tenant.EnableLocalLogin,
        EnablePasskey = tenant.EnablePasskey,
        DefaultIdpScheme = tenant.DefaultIdpScheme,
        DefaultSignInPolicyId = tenant.DefaultSignInPolicyId,
        DefaultSsoUiMode = tenant.DefaultSsoUiMode,
        AllowPlatformOrgSignIn = tenant.AllowPlatformOrgSignIn,
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
            EnforceOAuth21 = config.EnforceOAuth21,
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
            PrefixScopeSuffixMaxLength = Oluso.Core.Services.ApiScopePrefixes.EffectiveDefaultSuffixMaxLength(config.PrefixScopeSuffixMaxLength),
            RefreshImplyingScopes = config.RefreshImplyingScopesJson == null ? null : DeserializeJsonArray(config.RefreshImplyingScopesJson) ?? new List<string>(),
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
            RejectWhenBreachCheckUnavailable = policy.RejectWhenBreachCheckUnavailable,
            CustomRegexPattern = policy.CustomRegexPattern,
            CustomRegexErrorMessage = policy.CustomRegexErrorMessage
        };
    }
}
