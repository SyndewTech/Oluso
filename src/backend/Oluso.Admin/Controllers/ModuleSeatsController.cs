using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Admin.Dto.Seats;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Module seat management for tenant admins and org admins.
/// Allows managing which users have access to which ERP modules (per-seat licensing).
/// </summary>
[Route("api/admin/module-seats")]
public class ModuleSeatsController : AdminBaseController
{
    private readonly IUserModuleAccessStore _moduleAccessStore;
    private readonly ITenantFeatureStore _tenantFeatureStore;
    private readonly IOrganizationFeatureStore _orgFeatureStore;
    private readonly ITenantStore _tenantStore;
    private readonly IOlusoUserService _userService;
    private readonly ILogger<ModuleSeatsController> _logger;

    public ModuleSeatsController(
        ITenantContext tenantContext,
        IUserModuleAccessStore moduleAccessStore,
        ITenantFeatureStore tenantFeatureStore,
        IOrganizationFeatureStore orgFeatureStore,
        ITenantStore tenantStore,
        IOlusoUserService userService,
        ILogger<ModuleSeatsController> logger) : base(tenantContext)
    {
        _moduleAccessStore = moduleAccessStore;
        _tenantFeatureStore = tenantFeatureStore;
        _orgFeatureStore = orgFeatureStore;
        _tenantStore = tenantStore;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Get all module seat allocations for the current tenant.
    /// Returns each feature with its seat usage and limit.
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.ModuleSeatsRead)]
    public async Task<ActionResult> GetAllModuleSeats(CancellationToken cancellationToken)
    {
        var features = await _tenantFeatureStore.GetFeaturesAsync(TenantId, cancellationToken);
        var enabledFeatures = features.Where(f => f.Enabled).ToList();

        // Build org-level feature availability map
        var orgDisabledFeatures = await GetOrgDisabledFeaturesAsync(cancellationToken);

        var result = new List<object>();
        foreach (var feature in enabledFeatures)
        {
            if (orgDisabledFeatures.Contains(feature.FeatureKey))
                continue;

            var seatCount = await _moduleAccessStore.GetSeatCountAsync(TenantId, feature.FeatureKey, cancellationToken);
            result.Add(new
            {
                featureKey = feature.FeatureKey,
                maxSeats = feature.MaxSeats,
                usedSeats = seatCount,
                seatBased = feature.MaxSeats != null,
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Get seat details for a specific module, including assigned users.
    /// </summary>
    [HttpGet("{featureKey}")]
    [RequirePermission(AdminPermissions.ModuleSeatsRead)]
    public async Task<ActionResult> GetModuleSeats(string featureKey, CancellationToken cancellationToken)
    {
        var features = await _tenantFeatureStore.GetFeaturesAsync(TenantId, cancellationToken);
        var feature = features.FirstOrDefault(f =>
            f.FeatureKey.Equals(featureKey, StringComparison.OrdinalIgnoreCase));

        if (feature == null || !feature.Enabled)
            return NotFound(new { message = $"Feature '{featureKey}' is not enabled for this tenant" });

        var orgDisabledFeatures = await GetOrgDisabledFeaturesAsync(cancellationToken);
        if (orgDisabledFeatures.Contains(featureKey))
            return NotFound(new { message = $"Feature '{featureKey}' is not available for this tenant" });

        var seats = await _moduleAccessStore.GetAccessAsync(TenantId, featureKey, cancellationToken);

        // Resolve user display info
        var userSeats = new List<object>();
        foreach (var seat in seats)
        {
            var user = await _userService.FindByIdInTenantAsync(seat.UserId, TenantId!, cancellationToken);
            userSeats.Add(new
            {
                seat.UserId,
                userName = user?.Username,
                displayName = user?.DisplayName ?? user?.Email,
                email = user?.Email,
                seat.GrantedAt,
                seat.GrantedByUserId,
            });
        }

        return Ok(new
        {
            featureKey = feature.FeatureKey,
            maxSeats = feature.MaxSeats,
            usedSeats = seats.Count,
            seatBased = feature.MaxSeats != null,
            users = userSeats,
        });
    }

    /// <summary>
    /// Grant a user a seat in a module.
    /// </summary>
    [HttpPost("{featureKey}/users/{userId}")]
    [RequirePermission(AdminPermissions.ModuleSeatsWrite)]
    public async Task<ActionResult> GrantSeat(
        string featureKey,
        string userId,
        CancellationToken cancellationToken)
    {
        var features = await _tenantFeatureStore.GetFeaturesAsync(TenantId, cancellationToken);
        var feature = features.FirstOrDefault(f =>
            f.FeatureKey.Equals(featureKey, StringComparison.OrdinalIgnoreCase));

        if (feature == null || !feature.Enabled)
            return BadRequest(new { message = $"Feature '{featureKey}' is not enabled for this tenant" });

        var orgDisabledFeatures = await GetOrgDisabledFeaturesAsync(cancellationToken);
        if (orgDisabledFeatures.Contains(featureKey))
            return BadRequest(new { message = $"Feature '{featureKey}' is not available for this tenant" });

        // Membership-aware: the seat list is drawn from everyone in the tenant,
        // which now includes users whose home tenant is a sibling. A plain
        // FindByIdAsync resolves under the ambient filter and wouldn't see them.
        var user = await _userService.FindByIdInTenantAsync(userId, TenantId!, cancellationToken);
        if (user == null)
            return NotFound(new { message = $"User '{userId}' not found in this tenant" });

        // Check if already assigned
        var hasAccess = await _moduleAccessStore.HasAccessAsync(TenantId, userId, featureKey, cancellationToken);
        if (hasAccess)
            return Ok(new { featureKey, userId, granted = true, message = "User already has access" });

        // Enforce seat limit
        if (feature.MaxSeats != null)
        {
            var seatCount = await _moduleAccessStore.GetSeatCountAsync(TenantId, featureKey, cancellationToken);
            if (seatCount >= feature.MaxSeats.Value)
                return Conflict(new { message = $"Seat limit reached ({seatCount}/{feature.MaxSeats.Value})" });
        }

        await _moduleAccessStore.GrantAccessAsync(TenantId, userId, featureKey, RequireAdminUserId, cancellationToken);

        _logger.LogInformation("Granted {FeatureKey} seat to user {UserId} in tenant {TenantId} by {AdminUserId}",
            featureKey, userId, TenantId, AdminUserId);

        return Ok(new { featureKey, userId, granted = true });
    }

    /// <summary>
    /// Revoke a user's seat in a module.
    /// </summary>
    [HttpDelete("{featureKey}/users/{userId}")]
    [RequirePermission(AdminPermissions.ModuleSeatsWrite)]
    public async Task<ActionResult> RevokeSeat(
        string featureKey,
        string userId,
        CancellationToken cancellationToken)
    {
        await _moduleAccessStore.RevokeAccessAsync(TenantId, userId, featureKey, cancellationToken);

        _logger.LogInformation("Revoked {FeatureKey} seat from user {UserId} in tenant {TenantId} by {AdminUserId}",
            featureKey, userId, TenantId, AdminUserId);

        return Ok(new { featureKey, userId, revoked = true });
    }

    /// <summary>
    /// Bulk grant seats to multiple users for a module.
    /// </summary>
    [HttpPost("{featureKey}/users")]
    [RequirePermission(AdminPermissions.ModuleSeatsWrite)]
    public async Task<ActionResult> BulkGrantSeats(
        string featureKey,
        [FromBody] BulkSeatRequest request,
        CancellationToken cancellationToken)
    {
        var features = await _tenantFeatureStore.GetFeaturesAsync(TenantId, cancellationToken);
        var feature = features.FirstOrDefault(f =>
            f.FeatureKey.Equals(featureKey, StringComparison.OrdinalIgnoreCase));

        if (feature == null || !feature.Enabled)
            return BadRequest(new { message = $"Feature '{featureKey}' is not enabled for this tenant" });

        var orgDisabledFeatures = await GetOrgDisabledFeaturesAsync(cancellationToken);
        if (orgDisabledFeatures.Contains(featureKey))
            return BadRequest(new { message = $"Feature '{featureKey}' is not available for this tenant" });

        // Check seat availability
        if (feature.MaxSeats != null)
        {
            var currentCount = await _moduleAccessStore.GetSeatCountAsync(TenantId, featureKey, cancellationToken);
            // Count how many are new (not already assigned)
            var newCount = 0;
            foreach (var uid in request.UserIds)
            {
                var has = await _moduleAccessStore.HasAccessAsync(TenantId, uid, featureKey, cancellationToken);
                if (!has) newCount++;
            }

            if (currentCount + newCount > feature.MaxSeats.Value)
                return Conflict(new { message = $"Not enough seats. Available: {feature.MaxSeats.Value - currentCount}, requested: {newCount}" });
        }

        // The single-grant path verifies the user belongs to this tenant; without
        // the same check here a bulk call could hand seats to arbitrary user ids.
        var unknownUsers = new List<string>();
        foreach (var uid in request.UserIds.Distinct())
        {
            if (await _userService.FindByIdInTenantAsync(uid, TenantId!, cancellationToken) == null)
            {
                unknownUsers.Add(uid);
            }
        }

        if (unknownUsers.Count > 0)
        {
            return NotFound(new
            {
                message = $"{unknownUsers.Count} user(s) not found in this tenant",
                userIds = unknownUsers
            });
        }

        var granted = 0;
        foreach (var uid in request.UserIds)
        {
            var has = await _moduleAccessStore.HasAccessAsync(TenantId, uid, featureKey, cancellationToken);
            if (!has)
            {
                await _moduleAccessStore.GrantAccessAsync(TenantId, uid, featureKey, RequireAdminUserId, cancellationToken);
                granted++;
            }
        }

        _logger.LogInformation("Bulk granted {FeatureKey} seats to {Count} users in tenant {TenantId} by {AdminUserId}",
            featureKey, granted, TenantId, AdminUserId);

        return Ok(new { featureKey, granted, alreadyAssigned = request.UserIds.Count - granted });
    }

    /// <summary>
    /// Get all modules a specific user has access to.
    /// </summary>
    [HttpGet("users/{userId}")]
    [RequirePermission(AdminPermissions.ModuleSeatsRead)]
    public async Task<ActionResult> GetUserModules(string userId, CancellationToken cancellationToken)
    {
        var modules = await _moduleAccessStore.GetUserModulesAsync(TenantId, userId, cancellationToken);

        return Ok(new
        {
            userId,
            modules = modules.Select(m => new
            {
                m.FeatureKey,
                m.GrantedAt,
                m.GrantedByUserId,
            }),
        });
    }
    /// <summary>
    /// Returns the set of feature keys disabled at the org level.
    /// If the org has no features configured, returns empty (all allowed).
    /// </summary>
    private async Task<HashSet<string>> GetOrgDisabledFeaturesAsync(CancellationToken cancellationToken)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tenant = await _tenantStore.GetByIdAsync(TenantId, cancellationToken);
        if (tenant == null || string.IsNullOrEmpty(tenant.OrganizationId))
            return disabled;

        var orgHasFeatures = await _orgFeatureStore.HasAnyFeaturesConfiguredAsync(tenant.OrganizationId, cancellationToken);
        if (!orgHasFeatures)
            return disabled; // No org features configured = all allowed

        var orgFeatures = await _orgFeatureStore.GetFeaturesAsync(tenant.OrganizationId, cancellationToken);
        var orgFeatureMap = orgFeatures.ToDictionary(f => f.FeatureKey, StringComparer.OrdinalIgnoreCase);

        foreach (var def in ModuleFeatures.GetRegistered())
        {
            if (!orgFeatureMap.TryGetValue(def.Key, out var of) || !of.Enabled || (of.ExpiresAt != null && of.ExpiresAt <= DateTime.UtcNow))
            {
                disabled.Add(def.Key);
            }
        }

        return disabled;
    }
}
