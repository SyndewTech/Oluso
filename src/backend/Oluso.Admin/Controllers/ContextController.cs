using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Admin.Dto.Context;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for getting current context and switching tenants.
/// Used by the admin dashboard to understand the user's access level and available tenants.
/// </summary>
[Route("api/admin/context")]
[Authorize(Policy = "AdminApi")]
public class ContextController : AdminBaseController
{
    private readonly ITenantStore _tenantStore;
    private readonly IOlusoUserService _userService;
    private readonly IOrganizationStore? _organizationStore;
    private readonly IOrganizationMembershipStore? _membershipStore;
    private readonly ILogger<ContextController> _logger;

    public ContextController(
        ITenantContext tenantContext,
        ITenantStore tenantStore,
        IOlusoUserService userService,
        ILogger<ContextController> logger,
        IOrganizationStore? organizationStore = null,
        IOrganizationMembershipStore? membershipStore = null)
        : base(tenantContext)
    {
        _tenantStore = tenantStore;
        _userService = userService;
        _organizationStore = organizationStore;
        _membershipStore = membershipStore;
        _logger = logger;
    }

    /// <summary>
    /// Get the current admin context including user info, current tenant, and available resources.
    /// This is the first call the admin dashboard should make to understand the user's access.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AdminContextDto>> GetContext(CancellationToken cancellationToken)
    {
        var context = new AdminContextDto
        {
            UserId = AdminUserId,
            Email = AdminEmail,
            IsSuperAdmin = IsSuperAdmin,
            IsOrgAdmin = IsAnyOrgAdmin,
            CurrentTenant = TenantContext.HasTenant ? new TenantSummaryDto
            {
                Id = TenantContext.TenantId!,
                Name = TenantContext.Tenant?.Name,
                Identifier = TenantContext.Tenant?.Identifier
            } : null,
            CurrentOrganization = CurrentOrganizationId != null ? new OrganizationSummaryDto
            {
                Id = CurrentOrganizationId,
                Role = CurrentOrganizationRole?.ToString().ToLowerInvariant()
            } : null
        };

        // Resolve org name
        if (context.CurrentOrganization != null && _organizationStore != null)
        {
            var org = await _organizationStore.GetByIdAsync(context.CurrentOrganization.Id, cancellationToken);
            context.CurrentOrganization.Name = org?.Name;
        }

        // Get available tenants
        context.AvailableTenants = await GetAvailableTenantsInternalAsync(cancellationToken);

        // Get available organizations
        if (_membershipStore != null && !string.IsNullOrEmpty(AdminUserId))
        {
            var memberships = await _membershipStore.GetByUserAsync(AdminUserId, cancellationToken);
            context.Organizations = memberships.Select(m => new OrganizationMembershipSummaryDto
            {
                OrganizationId = m.OrganizationId,
                OrganizationName = m.Organization?.Name,
                OrganizationSlug = m.Organization?.Slug,
                Role = m.Role.ToString().ToLowerInvariant()
            }).ToList();
        }

        return Ok(context);
    }

    /// <summary>
    /// Get list of tenants the current user can switch to.
    /// SuperAdmins see all tenants, OrgAdmins see tenants in their organizations.
    /// </summary>
    [HttpGet("tenants")]
    public async Task<ActionResult<IEnumerable<TenantSwitchDto>>> GetAvailableTenants(
        [FromQuery] string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var tenants = await GetAvailableTenantsInternalAsync(cancellationToken, organizationId);

        // The token names the tenant switched to, so the client can't tell which tenant is home; the user record can.
        if (!string.IsNullOrEmpty(AdminUserId))
        {
            var users = await _userService.FindByIdsAcrossTenantsAsync(new[] { AdminUserId }, cancellationToken);
            var homeTenantId = users.GetValueOrDefault(AdminUserId)?.TenantId;
            foreach (var tenant in tenants)
            {
                tenant.IsHome = tenant.Id == homeTenantId;
            }
        }

        return Ok(tenants);
    }

    /// <summary>
    /// Get list of organizations the current user belongs to.
    /// </summary>
    [HttpGet("organizations")]
    public async Task<ActionResult<IEnumerable<OrganizationSwitchDto>>> GetAvailableOrganizations(
        CancellationToken cancellationToken)
    {
        if (_membershipStore == null || _organizationStore == null)
        {
            return Ok(Array.Empty<OrganizationSwitchDto>());
        }

        if (IsSuperAdmin)
        {
            // SuperAdmins see all organizations
            var allOrgs = await _organizationStore.GetAllAsync(includeDisabled: false, cancellationToken);
            return Ok(allOrgs.Select(o => new OrganizationSwitchDto
            {
                Id = o.Id,
                Name = o.Name,
                Slug = o.Slug,
                Role = "superadmin",
                TenantCount = o.Tenants?.Count ?? 0,
                Enabled = o.Enabled
            }));
        }

        if (string.IsNullOrEmpty(AdminUserId))
        {
            return Ok(Array.Empty<OrganizationSwitchDto>());
        }

        var memberships = await _membershipStore.GetByUserAsync(AdminUserId, cancellationToken);
        var result = new List<OrganizationSwitchDto>();

        foreach (var membership in memberships)
        {
            var org = membership.Organization ?? await _organizationStore.GetByIdAsync(membership.OrganizationId, cancellationToken);
            if (org != null && org.Enabled)
            {
                result.Add(new OrganizationSwitchDto
                {
                    Id = org.Id,
                    Name = org.Name,
                    Slug = org.Slug,
                    Role = membership.Role.ToString().ToLowerInvariant(),
                    TenantCount = org.Tenants?.Count ?? 0,
                    Enabled = org.Enabled
                });
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// Get details about a specific tenant before switching to it.
    /// Validates that the user has access to the tenant.
    /// </summary>
    [HttpGet("tenants/{tenantId}")]
    public async Task<ActionResult<TenantDetailDto>> GetTenantDetails(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        // Validate access
        if (!IsSuperAdmin)
        {
            var hasAccess = await ValidateTenantAccessAsync(tenant, cancellationToken);
            if (!hasAccess)
            {
                return Forbid();
            }
        }

        Organization? org = null;
        if (_organizationStore != null && !string.IsNullOrEmpty(tenant.OrganizationId))
        {
            org = await _organizationStore.GetByIdAsync(tenant.OrganizationId, cancellationToken);
        }

        return Ok(new TenantDetailDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            DisplayName = tenant.DisplayName,
            Identifier = tenant.Identifier,
            Description = tenant.Description,
            Environment = tenant.Environment.ToString(),
            Enabled = tenant.Enabled,
            OrganizationId = tenant.OrganizationId,
            OrganizationName = org?.Name,
            Created = tenant.Created,
            Updated = tenant.Updated
        });
    }

    private async Task<List<TenantSwitchDto>> GetAvailableTenantsInternalAsync(
        CancellationToken cancellationToken,
        string? filterOrganizationId = null)
    {
        var result = new List<TenantSwitchDto>();

        if (IsSuperAdmin)
        {
            // SuperAdmins see all tenants
            var allTenants = await _tenantStore.GetAllAsync(cancellationToken);
            foreach (var tenant in allTenants.Where(t => t.Enabled))
            {
                if (filterOrganizationId != null && tenant.OrganizationId != filterOrganizationId)
                    continue;

                result.Add(new TenantSwitchDto
                {
                    Id = tenant.Id,
                    Name = tenant.Name,
                    DisplayName = tenant.DisplayName,
                    Identifier = tenant.Identifier,
                    Environment = tenant.Environment.ToString(),
                    OrganizationId = tenant.OrganizationId,
                    Enabled = tenant.Enabled
                });
            }
            return result;
        }

        if (string.IsNullOrEmpty(AdminUserId))
        {
            return result;
        }

        // Get tenants from user's organizations
        var memberships = _membershipStore == null
            ? Enumerable.Empty<OrganizationMembership>()
            : await _membershipStore.GetByUserAsync(AdminUserId, cancellationToken);

        foreach (var membership in memberships)
        {
            if (filterOrganizationId != null && membership.OrganizationId != filterOrganizationId)
                continue;

            var allowedTenantIds = await _membershipStore.GetAllowedTenantIdsAsync(
                AdminUserId, membership.OrganizationId, cancellationToken);

            var orgTenants = await _tenantStore.GetByOrganizationAsync(membership.OrganizationId, cancellationToken);

            foreach (var tenant in orgTenants.Where(t => t.Enabled && allowedTenantIds.Contains(t.Id)))
            {
                result.Add(new TenantSwitchDto
                {
                    Id = tenant.Id,
                    Name = tenant.Name,
                    DisplayName = tenant.DisplayName,
                    Identifier = tenant.Identifier,
                    Environment = tenant.Environment.ToString(),
                    OrganizationId = tenant.OrganizationId,
                    UserRole = membership.Role.ToString().ToLowerInvariant(),
                    Enabled = tenant.Enabled
                });
            }
        }

        // The user's own tenants (home tenant and tenant memberships, carried in
        // allowed_tenants) are switchable whether or not they belong to one of
        // the user's organizations.
        foreach (var tenantId in User.GetAllowedTenantIds())
        {
            if (result.Any(t => t.Id == tenantId))
                continue;

            var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
            if (tenant is not { Enabled: true })
                continue;
            if (filterOrganizationId != null && tenant.OrganizationId != filterOrganizationId)
                continue;

            result.Add(new TenantSwitchDto
            {
                Id = tenant.Id,
                Name = tenant.Name,
                DisplayName = tenant.DisplayName,
                Identifier = tenant.Identifier,
                Environment = tenant.Environment.ToString(),
                OrganizationId = tenant.OrganizationId,
                UserRole = "member",
                Enabled = tenant.Enabled
            });
        }

        return result;
    }

    private async Task<bool> ValidateTenantAccessAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        if (User.GetAllowedTenantIds().Contains(tenant.Id))
        {
            return true;
        }

        if (_membershipStore == null || string.IsNullOrEmpty(AdminUserId))
        {
            return false;
        }

        if (string.IsNullOrEmpty(tenant.OrganizationId))
        {
            // Legacy tenant without organization - allow for backward compatibility
            return true;
        }

        var allowedTenantIds = await _membershipStore.GetAllowedTenantIdsAsync(
            AdminUserId, tenant.OrganizationId, cancellationToken);

        return allowedTenantIds.Contains(tenant.Id);
    }
}
