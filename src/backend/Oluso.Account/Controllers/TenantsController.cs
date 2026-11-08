using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Account.Dto.Tenants;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.SenderConstraint;
using Oluso.Core.Services;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for multi-tenant user tenant management.
/// Allows users who belong to multiple tenants to view and switch contexts.
/// </summary>
[Route("api/account/tenants")]
public class TenantsController : AccountBaseController
{
    private readonly ITenantStore _tenantStore;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly IProfileService _profileService;
    private readonly IClientStore _clientStore;
    private readonly ITokenService _tokenService;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(
        ITenantContext tenantContext,
        ITenantStore tenantStore,
        ITenantSettingsProvider settingsProvider,
        IProfileService profileService,
        IClientStore clientStore,
        ITokenService tokenService,
        ILogger<TenantsController> logger) : base(tenantContext)
    {
        _tenantStore = tenantStore;
        _settingsProvider = settingsProvider;
        _profileService = profileService;
        _clientStore = clientStore;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Get all tenants the current user belongs to
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TenantListDto>> GetUserTenants(CancellationToken cancellationToken)
    {
        // The same set CanAccessTenant allows: home and member tenants, plus every
        // tenant of an organization the user owns or administers.
        var candidates = new List<Core.Domain.Entities.Tenant>();
        foreach (var tenantId in GetUserTenantIds())
        {
            var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
            if (tenant != null) candidates.Add(tenant);
        }
        foreach (var organizationId in GetLedOrganizationIds())
        {
            candidates.AddRange(await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken));
        }

        var tenants = new List<UserTenantDto>();
        foreach (var tenant in candidates.DistinctBy(t => t.Id))
        {
            if (tenant.Enabled)
            {
                var branding = await _settingsProvider.GetBrandingSettingsAsync(tenant.Id, cancellationToken);
                tenants.Add(new UserTenantDto
                {
                    Id = tenant.Id,
                    Identifier = tenant.Identifier,
                    Name = tenant.Name,
                    DisplayName = tenant.DisplayName ?? tenant.Name,
                    LogoUrl = branding.LogoUrl,
                    IsCurrent = tenant.Id == TenantId
                });
            }
        }

        return Ok(new TenantListDto
        {
            Tenants = tenants,
            CurrentTenantId = TenantId,
            IsMultiTenant = tenants.Count > 1
        });
    }

    /// <summary>
    /// Get current tenant details
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult<UserTenantDto>> GetCurrentTenant(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(TenantId))
        {
            return Ok(new UserTenantDto
            {
                Id = "default",
                Identifier = "default",
                Name = "Default",
                DisplayName = "Default Organization",
                IsCurrent = true
            });
        }

        var tenant = await _tenantStore.GetByIdAsync(TenantId, cancellationToken);
        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        var branding = await _settingsProvider.GetBrandingSettingsAsync(tenant.Id, cancellationToken);
        return Ok(new UserTenantDto
        {
            Id = tenant.Id,
            Identifier = tenant.Identifier,
            Name = tenant.Name,
            DisplayName = tenant.DisplayName ?? tenant.Name,
            LogoUrl = branding.LogoUrl,
            IsCurrent = true
        });
    }

    /// <summary>
    /// Validate that user can access a specific tenant.
    /// Used by frontend before switching tenant context.
    /// </summary>
    [HttpGet("{tenantId}/validate")]
    public async Task<ActionResult<TenantValidationDto>> ValidateTenantAccess(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var userTenants = GetUserTenantIds();

        if (!userTenants.Contains(tenantId))
        {
            return Ok(new TenantValidationDto
            {
                IsValid = false,
                Error = "You do not have access to this organization"
            });
        }

        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            return Ok(new TenantValidationDto
            {
                IsValid = false,
                Error = "Organization not found"
            });
        }

        if (!tenant.Enabled)
        {
            return Ok(new TenantValidationDto
            {
                IsValid = false,
                Error = "Organization is disabled"
            });
        }

        var branding = await _settingsProvider.GetBrandingSettingsAsync(tenant.Id, cancellationToken);
        return Ok(new TenantValidationDto
        {
            IsValid = true,
            Tenant = new UserTenantDto
            {
                Id = tenant.Id,
                Identifier = tenant.Identifier,
                Name = tenant.Name,
                DisplayName = tenant.DisplayName ?? tenant.Name,
                LogoUrl = branding.LogoUrl,
                IsCurrent = tenant.Id == TenantId
            }
        });
    }

    /// <summary>
    /// Whether the caller may operate in <paramref name="tenant"/>.
    /// Mirrors TenantResolutionMiddleware.ValidateAuthenticatedAccessAsync.
    /// </summary>
    private bool CanAccessTenant(Core.Domain.Entities.Tenant tenant)
    {
        // The middleware strips super_admin when a SuperAdmin operates inside a
        // non-platform tenant, but leaves their roles intact — so the role is the
        // reliable signal here, not the claim.
        var isSuperAdmin = User.FindAll("role")
            .Concat(User.FindAll(System.Security.Claims.ClaimTypes.Role))
            .Any(c => c.Value.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)
                   || c.Value.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase));

        if (isSuperAdmin) return true;

        if (GetUserTenantIds().Contains(tenant.Id)) return true;

        // OrgAdmins reach every tenant under an organization they administer,
        // without needing a TenantMembership row for each one.
        //
        // Keyed on org_roles, not org_ids: the OrgAdmin role and is_org_admin are
        // platform-wide, while org_ids lists every organization the user belongs
        // to at ANY role — so org_ids would hand an admin of one organization the
        // run of another where they are only a member.
        if (!string.IsNullOrEmpty(tenant.OrganizationId))
        {
            var orgRole = GetOrgRoleFromClaims(tenant.OrganizationId);
            if (orgRole != null
                && (orgRole.Equals("owner", StringComparison.OrdinalIgnoreCase)
                    || orgRole.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The caller's role in one specific organization, from the org_roles claim.
    /// </summary>
    private string? GetOrgRoleFromClaims(string organizationId) =>
        GetOrgRolesFromClaims().GetValueOrDefault(organizationId);

    /// <summary>Organizations the caller owns or administers.</summary>
    private IEnumerable<string> GetLedOrganizationIds() =>
        GetOrgRolesFromClaims()
            .Where(r => r.Value.Equals("owner", StringComparison.OrdinalIgnoreCase)
                     || r.Value.Equals("admin", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Key);

    /// <summary>
    /// Every organization role the caller holds: the org_roles JSON claim
    /// (tokens) first, then the org_role "orgId:role" claims (cookie sessions).
    /// </summary>
    private Dictionary<string, string> GetOrgRolesFromClaims()
    {
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);

        var orgRolesClaim = User.FindFirst("org_roles")?.Value;
        if (!string.IsNullOrEmpty(orgRolesClaim))
        {
            try
            {
                var orgRoles = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, string>>(orgRolesClaim);
                foreach (var (organizationId, role) in orgRoles ?? new Dictionary<string, string>())
                {
                    roles[organizationId] = role;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Invalid JSON — fall through to the cookie format.
            }
        }

        foreach (var claim in User.FindAll("org_role"))
        {
            var parts = claim.Value.Split(':', 2);
            if (parts.Length == 2)
            {
                roles.TryAdd(parts[0], parts[1]);
            }
        }

        return roles;
    }

    /// <summary>
    /// Switch tenant context and mint an access token scoped to the target.
    /// </summary>
    /// <remarks>
    /// Call with <c>X-Tenant-Id</c> already set to <paramref name="tenantId"/>:
    /// TenantResolutionMiddleware resolves it, checks it against the caller's
    /// <c>allowed_tenants</c>, and establishes the tenant context before this
    /// runs — so by here the target is already authorised and ambient.
    /// <para>
    /// A fresh token is required rather than just the header, because roles are
    /// per-tenant assignments and role claims are read straight off the
    /// principal in many places (HasAnyRole, IsSuperAdmin, the middleware's
    /// TenantUserRole derivation, the SPAs). Keeping the old token would leave
    /// all of those describing the previous tenant. Permissions and module seats
    /// come along for the same reason.
    /// </para>
    /// </remarks>
    [HttpPost("{tenantId}/switch")]
    public async Task<ActionResult<TenantSwitchDto>> SwitchTenant(
        string tenantId,
        [FromServices] IClientUserAccess clientUserAccess,
        CancellationToken cancellationToken)
    {
        // Compare against the middleware's resolved tenant, NOT the base
        // controller's TenantId — that property only returns the resolved tenant
        // when it appears in the caller's allowed_tenants, and falls back to the
        // token's tenant otherwise. SuperAdmins are admitted by the middleware's
        // own bypass without holding a TenantMembership, so for them it always
        // fell back and every switch looked like a mismatch.
        var resolvedTenantId = TenantContext.TenantId;

        if (!string.Equals(tenantId, resolvedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error = "tenant_mismatch",
                error_description =
                    "Set the X-Tenant-Id header to the tenant being switched to before calling this endpoint."
            });
        }

        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            return NotFound(new { error = "tenant_not_found" });
        }

        if (!tenant.Enabled)
        {
            return BadRequest(new { error = "tenant_disabled", error_description = "This organization is disabled." });
        }

        // Re-check access here rather than leaning solely on the middleware.
        // This endpoint mints tokens, so it should not become an oracle if path
        // matching or middleware ordering ever changes — and machine clients are
        // explicitly exempt from the middleware's gate, yet carry no subject to
        // mint for. Mirrors ValidateAuthenticatedAccessAsync: SuperAdmin, an
        // allowed_tenants entry, or OrgAdmin over the tenant's organization.
        if (string.IsNullOrEmpty(UserId))
        {
            return Unauthorized(new { error = "no_subject", error_description = "Only user tokens can switch tenant." });
        }

        if (!CanAccessTenant(tenant))
        {
            _logger.LogWarning("User {UserId} denied tenant switch to {TenantId}", UserId, tenant.Id);
            return Forbid();
        }

        var clientId = User.FindFirst("client_id")?.Value;
        if (string.IsNullOrEmpty(clientId))
        {
            return BadRequest(new { error = "invalid_token", error_description = "Token has no client_id." });
        }

        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return BadRequest(new { error = "invalid_client" });
        }

        // The client's user restrictions hold in the tenant being switched to: a permission held only in the old
        // tenant doesn't carry over.
        var denial = await clientUserAccess.FindDenialAsync(
            client.ClientId, ClientUserRestrictions.From(client), UserId, tenant.Id, cancellationToken);
        if (denial != null)
        {
            _logger.LogWarning("User {UserId} denied tenant switch to {TenantId} for client {ClientId}: {Reason}",
                UserId, tenant.Id, client.ClientId, denial);
            return StatusCode(403, new { error = "access_denied", error_description = denial });
        }

        // Carry the original grant's scopes; switching tenant must never widen them.
        var scopes = User.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct()
            .ToList();

        // Claims resolve against the ambient tenant context, which the middleware
        // has already pointed at the target — so roles, allowed_tenants and
        // module_seats all come back scoped to it.
        var claims = await _profileService.GetProfileClaimsAsync(
            UserId,
            client,
            scopes,
            caller: "TenantSwitch",
            protocol: "oidc",
            resources: null,
            additionalData: null,
            cancellationToken);

        // The token is for the target or it is not issued: a token naming another
        // tenant, or none, would leave the caller holding the wrong tenant's roles.
        var targetTenantId = claims.TryGetValue("tenant_id", out var claimedTenant) ? claimedTenant?.ToString() : null;
        if (!string.Equals(targetTenantId, tenant.Id, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Tenant switch for user {UserId} to {TenantId} resolved claims for {TokenTenantId}; no token issued",
                UserId, tenant.Id, targetTenantId ?? "(none)");
            return StatusCode(500, new
            {
                error = "tenant_switch_failed",
                error_description = "Your access to this organization could not be resolved."
            });
        }

        var permissions = _profileService is IExtendedProfileService extended
            ? (await extended.GetUserPermissionsAsync(UserId, tenant.Id, cancellationToken)).ToList()
            : new List<string>();

        // The new token is bound to whatever the presented one is bound to. Token
        // validation has already checked this request's DPoP proof against that key
        // and the client certificate against that thumbprint, so the caller holds
        // them; a switch must not turn a sender-constrained session into a bearer one.
        var confirmation = TokenConfirmation.Parse(User.FindFirst("cnf")?.Value);

        var accessToken = await _tokenService.CreateAccessTokenAsync(new TokenCreationRequest
        {
            SubjectId = UserId,
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            Scopes = scopes,
            Claims = claims,
            Permissions = permissions,
            Lifetime = client.AccessTokenLifetime,
            SessionId = SessionId,
            IncludeJwtId = client.IncludeJwtId,
            PairWiseSubjectSalt = client.PairWiseSubjectSalt,
            DPoPKeyThumbprint = confirmation.Jkt,
            MtlsCertificateThumbprint = confirmation.CertificateThumbprint
        }, cancellationToken);

        var branding = await _settingsProvider.GetBrandingSettingsAsync(tenant.Id, cancellationToken);

        _logger.LogInformation(
            "User {UserId} switched to tenant {TenantId} via client {ClientId}",
            UserId, tenant.Id, client.ClientId);

        return Ok(new TenantSwitchDto
        {
            AccessToken = accessToken,
            TokenType = confirmation.Jkt != null ? "DPoP" : "Bearer",
            ExpiresIn = client.AccessTokenLifetime,
            Tenant = new UserTenantDto
            {
                Id = tenant.Id,
                Identifier = tenant.Identifier,
                Name = tenant.Name,
                DisplayName = tenant.DisplayName ?? tenant.Name,
                LogoUrl = branding.LogoUrl,
                IsCurrent = true
            }
        });
    }
}

