using Microsoft.AspNetCore.Mvc;
using Oluso.Account.Dto.Workspace;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.Services;

namespace Oluso.Account.Controllers;

/// <summary>
/// Workspace profile endpoint - returns the current user's identity info.
/// This is a generic workspace endpoint (not module-specific) that any workspace module can use.
/// Plugins can enrich the response via IProfileEnricher.
/// </summary>
[Route("api/workspace/me")]
public class WorkspaceMeController : WorkspaceBaseController
{
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoUserService _userService;
    private readonly IOrganizationStore _organizationStore;
    private readonly IEnumerable<IProfileEnricher> _enrichers;
    private readonly IFeatureGate _featureGate;
    private readonly IProfileService _profileService;
    private readonly IUserModuleAccessStore _moduleAccessStore;

    public WorkspaceMeController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        IOrganizationStore organizationStore,
        IEnumerable<IProfileEnricher> enrichers,
        IFeatureGate featureGate,
        IProfileService profileService,
        IUserModuleAccessStore moduleAccessStore) : base(tenantContext)
    {
        _tenantContext = tenantContext;
        _userService = userService;
        _organizationStore = organizationStore;
        _enrichers = enrichers;
        _featureGate = featureGate;
        _profileService = profileService;
        _moduleAccessStore = moduleAccessStore;
    }

    /// <summary>
    /// Get current user's workspace profile
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<WorkspaceProfileDto>> GetMe(CancellationToken cancellationToken)
    {
        // The signed-in user, whatever their home tenant: TenantResolutionMiddleware
        // has already admitted them to this tenant (member, organization owner or
        // admin, SuperAdmin), and the tenant-filtered lookup only sees home users
        // and members, so an organization owner got "not found" here.
        var user = (await _userService.FindByIdsAcrossTenantsAsync(new[] { UserId }, cancellationToken))
            .GetValueOrDefault(UserId);
        if (user == null)
            return NotFound(new { message = "User not found" });

        var roles = user.Roles?.ToList() ?? new List<string>();
        // Resolve permissions from the DB (roles → role claims) rather than the
        // JWT's `permissions` claim. Mirrors what the admin
        // PermissionsController.GetMyPermissions / DefaultProfileService.GetUserPermissionsAsync
        // do, and avoids the stale-token problem where a user granted a role
        // after login wouldn't see their new permissions until re-issuing the JWT.
        var raw = await _profileService.GetUserPermissionsAsync(UserId, TenantId, cancellationToken);
        var permissions = PermissionExpander.Expand(raw);

        // Resolve enabled features by checking the feature gate for installed modules
        var features = new List<string>();
        foreach (var module in ModuleRegistry.Instance.GetRegisteredModules())
        {
            var result = await _featureGate.CheckFeatureAsync(module.FeatureKey, UserId, cancellationToken);
            if (result.IsAllowed)
                features.Add(module.FeatureKey);
        }

        // Module seats — per-user access rows. Drives the workspace app
        // switcher's "requiresSeat" filter on the frontend. Tenant feature
        // is a coarse "module licensed for this tenant" gate; the seat is
        // the per-user "this user is allowed to use it" gate.
        var seatRows = await _moduleAccessStore.GetUserModulesAsync(TenantId, UserId, cancellationToken);
        var moduleSeats = seatRows
            .Select(s => s.FeatureKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tenant = _tenantContext.Tenant;
        string? orgName = null;
        if (tenant != null && !string.IsNullOrEmpty(tenant.OrganizationId))
        {
            var org = await _organizationStore.GetByIdAsync(tenant.OrganizationId, cancellationToken);
            orgName = org?.Name;
        }

        var dto = new WorkspaceProfileDto
        {
            Id = user.Id,
            UserName = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = ResolveDisplayName(user.DisplayName, user.FirstName, user.LastName, user.Username, user.Email),
            ProfilePictureUrl = user.Picture,
            TenantId = TenantId,
            TenantName = tenant?.DisplayName ?? tenant?.Name,
            OrganizationName = orgName,
            Roles = roles,
            Permissions = permissions,
            Features = features,
            ModuleSeats = moduleSeats,
        };

        // Run profile enrichers (e.g., a module adds extra profile fields)
        var context = new ProfileEnricherContext
        {
            UserId = UserId,
            TenantId = TenantId,
            Email = user.Email,
            Extensions = dto.Extensions,
        };

        foreach (var enricher in _enrichers.OrderBy(e => e.Priority))
        {
            // Module-bound enrichers only run for tenants that have the module.
            if (enricher.FeatureKey != null && !features.Contains(enricher.FeatureKey))
                continue;

            await enricher.EnrichAsync(context, cancellationToken);
        }

        return Ok(dto);
    }

    // Never an empty string: clients render this verbatim in greetings and headers.
    private static string ResolveDisplayName(string? displayName, string? firstName, string? lastName, string? userName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        var full = $"{firstName} {lastName}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) return full;
        if (!string.IsNullOrWhiteSpace(userName)) return userName;
        return email ?? string.Empty;
    }
}
