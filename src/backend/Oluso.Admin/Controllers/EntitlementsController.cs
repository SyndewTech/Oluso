using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Entitlements;
using Oluso.Admin.Services;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for the tenant's own application entitlements.
///
/// These are the tenant's vocabulary, not Oluso's: they never satisfy a platform
/// permission gate. Assigning them to a role puts them in the role's
/// <c>entitlements</c> claims, which reach a token when a client is granted the
/// <c>entitlements</c> scope.
/// </summary>
[Route("api/admin/entitlements")]
public class EntitlementsController : AdminBaseController
{
    private readonly IEntitlementStore _entitlementStore;
    private readonly IRoleStore _roleStore;
    private readonly IEntitlementScopeProvisioner _scopeProvisioner;
    private readonly ILogger<EntitlementsController> _logger;

    public EntitlementsController(
        ITenantContext tenantContext,
        IEntitlementStore entitlementStore,
        IRoleStore roleStore,
        IEntitlementScopeProvisioner scopeProvisioner,
        ILogger<EntitlementsController> logger)
        : base(tenantContext)
    {
        _entitlementStore = entitlementStore;
        _roleStore = roleStore;
        _scopeProvisioner = scopeProvisioner;
        _logger = logger;
    }

    /// <summary>
    /// List the tenant's entitlement catalog
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.EntitlementsRead)]
    public async Task<ActionResult<IEnumerable<EntitlementDto>>> GetAll(CancellationToken cancellationToken)
    {
        var entitlements = await _entitlementStore.ListAsync(TenantId, cancellationToken);
        return Ok(entitlements.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Get a single entitlement
    /// </summary>
    [HttpGet("{id}")]
    [RequirePermission(AdminPermissions.EntitlementsRead)]
    public async Task<ActionResult<EntitlementDto>> GetById(string id, CancellationToken cancellationToken)
    {
        var entitlement = await _entitlementStore.GetAsync(TenantId, id, cancellationToken);
        if (entitlement == null)
        {
            return NotFound();
        }

        return Ok(MapToDto(entitlement));
    }

    /// <summary>
    /// Define a new entitlement
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.EntitlementsWrite)]
    public async Task<ActionResult<EntitlementDto>> Create(
        [FromBody] CreateEntitlementRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        var nameError = ValidateName(name);
        if (nameError != null)
        {
            return BadRequest(new { error = nameError });
        }

        var existing = await _entitlementStore.FindByNamesAsync(TenantId, new[] { name }, cancellationToken);
        if (existing.Count > 0)
        {
            return Conflict(new { error = $"An entitlement named '{name}' already exists" });
        }

        var entitlement = new Entitlement
        {
            Name = name,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? name : request.DisplayName.Trim(),
            Description = request.Description?.Trim(),
            Group = string.IsNullOrWhiteSpace(request.Group) ? null : request.Group.Trim(),
            Enabled = request.Enabled ?? true,
            TenantId = TenantId,
            CreatedBy = AdminUserId
        };

        await _entitlementStore.CreateAsync(entitlement, cancellationToken);

        // Make sure the scope that releases the claim exists before anyone can
        // assign this — otherwise the first assignment silently never reaches a
        // token, which is the trap this whole feature exists to remove.
        await _scopeProvisioner.EnsureAsync(cancellationToken);

        _logger.LogInformation("Created entitlement {Name} for tenant {TenantId}", name, TenantId);

        return CreatedAtAction(nameof(GetById), new { id = entitlement.Id }, MapToDto(entitlement));
    }

    /// <summary>
    /// Update an entitlement's presentation or enabled state
    /// </summary>
    [HttpPut("{id}")]
    [RequirePermission(AdminPermissions.EntitlementsWrite)]
    public async Task<ActionResult<EntitlementDto>> Update(
        string id,
        [FromBody] UpdateEntitlementRequest request,
        CancellationToken cancellationToken)
    {
        var entitlement = await _entitlementStore.GetAsync(TenantId, id, cancellationToken);
        if (entitlement == null)
        {
            return NotFound();
        }

        if (request.DisplayName != null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return BadRequest(new { error = "Display name cannot be empty" });
            }
            entitlement.DisplayName = request.DisplayName.Trim();
        }

        if (request.Description != null)
        {
            entitlement.Description = request.Description.Trim();
        }

        if (request.Group != null)
        {
            entitlement.Group = string.IsNullOrWhiteSpace(request.Group) ? null : request.Group.Trim();
        }

        if (request.Enabled.HasValue)
        {
            entitlement.Enabled = request.Enabled.Value;
        }

        entitlement.UpdatedBy = AdminUserId;
        await _entitlementStore.UpdateAsync(entitlement, cancellationToken);

        return Ok(MapToDto(entitlement));
    }

    /// <summary>
    /// Delete an entitlement and strip it from every role that holds it
    /// </summary>
    [HttpDelete("{id}")]
    [RequirePermission(AdminPermissions.EntitlementsDelete)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var entitlement = await _entitlementStore.GetAsync(TenantId, id, cancellationToken);
        if (entitlement == null)
        {
            return NotFound();
        }

        // Cascade rather than orphan: a role claim naming a catalog entry that
        // no longer exists is invisible in the picker and inert in tokens, so
        // leaving it behind means the role quietly misrepresents itself.
        var removedFromRoles = 0;
        var roles = await _roleStore.GetRolesAsync(TenantId, includeSystem: false, cancellationToken);

        foreach (var role in roles)
        {
            var claims = await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken);
            var holdsIt = claims.Any(c =>
                c.Type == OlusoRole.EntitlementClaimType &&
                string.Equals(c.Value, entitlement.Name, StringComparison.Ordinal));

            if (!holdsIt)
            {
                continue;
            }

            await _roleStore.RemoveRoleClaimAsync(
                role.Id, OlusoRole.EntitlementClaimType, entitlement.Name, cancellationToken);
            removedFromRoles++;
        }

        await _entitlementStore.DeleteAsync(TenantId, id, cancellationToken);

        _logger.LogInformation(
            "Deleted entitlement {Name} for tenant {TenantId}, removed from {RoleCount} role(s)",
            entitlement.Name, TenantId, removedFromRoles);

        return Ok(new { removedFromRoles });
    }

    /// <summary>
    /// Entitlement names travel as a space-delimited-free claim value and are
    /// compared verbatim by the tenant's app, so reject anything that would be
    /// ambiguous on arrival.
    /// </summary>
    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required";
        }

        if (name.Length > 200)
        {
            return "Name cannot exceed 200 characters";
        }

        if (name.Any(char.IsWhiteSpace))
        {
            return "Name cannot contain whitespace";
        }

        return null;
    }

    private static EntitlementDto MapToDto(Entitlement e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        DisplayName = e.DisplayName,
        Description = e.Description,
        Group = e.Group,
        Enabled = e.Enabled,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
