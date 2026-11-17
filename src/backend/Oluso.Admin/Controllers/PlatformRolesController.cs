using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Roles;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Platform-wide roles (no owning tenant: SuperAdmin, SystemAdmin, each module's admin role) and who holds them in
/// every tenant. A tenant's Roles page only shows the holders in that tenant; this one shows them all, and assigns or
/// removes a holder in a named tenant with the same events and module-admin notices as the Users page.
/// </summary>
[Route("api/admin/platform/roles")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformRolesController : AdminBaseController
{
    private const int PickerPageSize = 20;

    private readonly IRoleStore _roleStore;
    private readonly ITenantStore _tenantStore;
    private readonly ITenantMembershipStore _tenantMembershipStore;
    private readonly IOlusoUserService _userService;
    private readonly IOlusoEventService _eventService;
    private readonly IModuleAdminAssignmentNotifier _moduleAdminNotifier;
    private readonly ILogger<PlatformRolesController> _logger;

    public PlatformRolesController(
        ITenantContext tenantContext,
        IRoleStore roleStore,
        ITenantStore tenantStore,
        ITenantMembershipStore tenantMembershipStore,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        IModuleAdminAssignmentNotifier moduleAdminNotifier,
        ILogger<PlatformRolesController> logger)
        : base(tenantContext)
    {
        _roleStore = roleStore;
        _tenantStore = tenantStore;
        _tenantMembershipStore = tenantMembershipStore;
        _userService = userService;
        _eventService = eventService;
        _moduleAdminNotifier = moduleAdminNotifier;
        _logger = logger;
    }

    [HttpGet]
    [RequirePermission(AdminPermissions.PlatformRolesRead)]
    public async Task<ActionResult<IEnumerable<PlatformRoleDto>>> GetRoles(CancellationToken cancellationToken)
    {
        var roles = (await _roleStore.GetRolesAsync(null, includeSystem: true, cancellationToken))
            .Where(r => r.TenantId == null)
            .ToList();

        var result = new List<PlatformRoleDto>();
        foreach (var role in roles)
        {
            var assignments = await _roleStore.GetAssignmentsAsync(role.Id, cancellationToken);
            result.Add(new PlatformRoleDto
            {
                Id = role.Id,
                Name = role.Name!,
                DisplayName = role.DisplayName ?? role.Name!,
                Description = role.Description,
                IsSystemRole = role.IsSystemRole,
                AppliesEverywhere = GlobalRoles.IsGlobal(role),
                IsDerived = IsDerived(role),
                HolderCount = assignments.Select(a => a.UserId).Distinct(StringComparer.Ordinal).Count(),
                AssignmentCount = assignments.Count,
            });
        }

        return Ok(result.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase));
    }

    [HttpGet("{roleId}/holders")]
    [RequirePermission(AdminPermissions.PlatformRolesRead)]
    public async Task<ActionResult<IEnumerable<PlatformRoleHolderDto>>> GetHolders(string roleId, CancellationToken cancellationToken)
    {
        var role = await FindPlatformRoleAsync(roleId, cancellationToken);
        if (role == null) return NotFound();

        var assignments = await _roleStore.GetAssignmentsAsync(roleId, cancellationToken);

        var tenantNames = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tenantId in assignments.SelectMany(a => new[] { a.TenantId, a.HomeTenantId }).OfType<string>().Distinct())
        {
            var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
            tenantNames[tenantId] = tenant == null ? null : tenant.DisplayName ?? tenant.Name;
        }

        var assignerNames = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var assignerId in assignments.Select(a => a.AssignedBy).OfType<string>().Where(id => id != "system").Distinct())
        {
            var assigner = await _userService.FindByIdAsync(assignerId, cancellationToken);
            assignerNames[assignerId] = assigner == null ? null : assigner.DisplayName ?? assigner.Email ?? assigner.Username;
        }

        return Ok(assignments.Select(a => new PlatformRoleHolderDto
        {
            UserId = a.UserId,
            UserName = a.UserName,
            Email = a.Email,
            DisplayName = a.DisplayName,
            IsActive = a.IsActive,
            HomeTenantId = a.HomeTenantId,
            HomeTenantName = a.HomeTenantId == null ? null : tenantNames.GetValueOrDefault(a.HomeTenantId),
            TenantId = a.TenantId,
            TenantName = tenantNames.GetValueOrDefault(a.TenantId),
            AssignedAt = a.AssignedAt,
            AssignedBy = a.AssignedBy,
            AssignedByName = a.AssignedBy == null ? null : assignerNames.GetValueOrDefault(a.AssignedBy),
        }));
    }

    [HttpPost("{roleId}/holders")]
    [RequirePermission(AdminPermissions.PlatformRolesManage)]
    public async Task<IActionResult> AssignHolder(string roleId, [FromBody] AssignPlatformRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await FindPlatformRoleAsync(roleId, cancellationToken);
        if (role == null) return NotFound();
        if (IsDerived(role)) return BadRequest(new { error = DerivedError(role) });
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.TenantId))
        {
            return BadRequest(new { error = "Choose an organization and a person." });
        }

        var tenant = await _tenantStore.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant == null) return BadRequest(new { error = "That organization no longer exists." });

        var user = await _userService.FindByIdAsync(request.UserId, cancellationToken);
        if (user == null) return BadRequest(new { error = "That person no longer exists." });

        if (user.TenantId != tenant.Id
            && await _tenantMembershipStore.GetByTenantAndUserAsync(tenant.Id, user.Id, cancellationToken) == null)
        {
            return BadRequest(new { error = $"{Label(user)} isn't a member of {tenant.DisplayName ?? tenant.Name}." });
        }

        var existing = await _roleStore.GetAssignmentsAsync(roleId, cancellationToken);
        var appliesEverywhere = GlobalRoles.IsGlobal(role);
        if (existing.Any(a => a.UserId == user.Id && (appliesEverywhere || a.TenantId == tenant.Id)))
        {
            return Conflict(new
            {
                error = appliesEverywhere
                    ? $"{Label(user)} already holds {role.DisplayName ?? role.Name}, which applies in every organization."
                    : $"{Label(user)} already holds {role.DisplayName ?? role.Name} in {tenant.DisplayName ?? tenant.Name}."
            });
        }

        await _roleStore.AssignToUserInTenantAsync(roleId, user.Id, tenant.Id, AdminUserId, cancellationToken);

        _logger.LogInformation(
            "Admin {AdminUserId} assigned platform role {Role} to user {UserId} in tenant {TenantId}",
            AdminUserId, role.Name, user.Id, tenant.Id);

        await _eventService.RaiseAsync(new UserRoleAssignedEvent
        {
            TenantId = tenant.Id,
            SubjectId = user.Id,
            RoleId = role.Id,
            RoleName = role.Name ?? role.Id,
            Origin = Origin
        }, cancellationToken);
        await _moduleAdminNotifier.NotifyAsync(tenant.Id, user.Id, new[] { role }, Actor(), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Removes the assignment in <paramref name="tenantId"/>. SuperAdmin and SystemAdmin apply everywhere, so removing
    /// either takes it away in every tenant; neither can be taken from yourself, and the last SuperAdmin keeps the role.
    /// </summary>
    [HttpDelete("{roleId}/holders/{userId}")]
    [RequirePermission(AdminPermissions.PlatformRolesManage)]
    public async Task<IActionResult> RemoveHolder(string roleId, string userId, [FromQuery] string tenantId, CancellationToken cancellationToken)
    {
        var role = await FindPlatformRoleAsync(roleId, cancellationToken);
        if (role == null) return NotFound();
        if (IsDerived(role)) return BadRequest(new { error = DerivedError(role) });

        var assignments = await _roleStore.GetAssignmentsAsync(roleId, cancellationToken);
        if (!assignments.Any(a => a.UserId == userId && a.TenantId == tenantId)) return NotFound();

        var appliesEverywhere = GlobalRoles.IsGlobal(role);
        if (appliesEverywhere && userId == AdminUserId)
        {
            return BadRequest(new { error = $"You can't remove {role.Name} from yourself." });
        }

        if (string.Equals(role.Name, GlobalRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase)
            && assignments.All(a => a.UserId == userId))
        {
            return BadRequest(new { error = "This is the last SuperAdmin. Assign SuperAdmin to someone else first." });
        }

        await _roleStore.RemoveFromUserInTenantAsync(roleId, userId, tenantId, cancellationToken);

        _logger.LogInformation(
            "Admin {AdminUserId} removed platform role {Role} from user {UserId} in tenant {TenantId}",
            AdminUserId, role.Name, userId, tenantId);

        await _eventService.RaiseAsync(new UserRoleRemovedEvent
        {
            TenantId = tenantId,
            SubjectId = userId,
            RoleId = role.Id,
            RoleName = role.Name ?? role.Id,
            Origin = Origin
        }, cancellationToken);
        await _moduleAdminNotifier.NotifyRemovedAsync(tenantId, userId, new[] { role }, Actor(), cancellationToken);

        return NoContent();
    }

    /// <summary>Tenants to assign a platform role in, matched by name or identifier.</summary>
    [HttpGet("tenant-options")]
    [RequirePermission(AdminPermissions.PlatformRolesManage)]
    public async Task<ActionResult<IEnumerable<PlatformRoleTenantOptionDto>>> GetTenantOptions(
        [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var (items, _) = await _tenantStore.SearchAsync(new TenantSearchQuery { Search = search, Take = PickerPageSize }, cancellationToken);
        return Ok(items.Select(t => new PlatformRoleTenantOptionDto(t.Id, t.DisplayName ?? t.Name, t.Identifier)));
    }

    /// <summary>People in <paramref name="tenantId"/> (home tenant or member) to assign a platform role to.</summary>
    [HttpGet("candidates")]
    [RequirePermission(AdminPermissions.PlatformRolesManage)]
    public async Task<ActionResult<IEnumerable<PlatformRoleCandidateDto>>> GetCandidates(
        [FromQuery] string tenantId, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) return BadRequest(new { error = "Choose an organization first." });

        var result = await _userService.GetUsersAsync(new UsersQuery
        {
            TenantId = tenantId,
            Search = search,
            PageSize = PickerPageSize,
        }, cancellationToken);

        return Ok(result.Users.Select(u => new PlatformRoleCandidateDto(u.Id, u.Email ?? "", u.DisplayName, u.Username)));
    }

    private async Task<OlusoRole?> FindPlatformRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);
        return role is { TenantId: null } ? role : null;
    }

    private static bool IsDerived(OlusoRole role) => ReservedClaimTypes.DerivedRoles.Contains(role.Name!);

    private static string DerivedError(OlusoRole role) =>
        $"{role.Name} follows organization membership. Make the person an Owner or Admin of the organization instead.";

    private static string Label(OlusoUserInfo user) => user.DisplayName ?? user.Email ?? user.Username;

    private RoleGrantActor Actor() => new(AdminUserId, AdminUserName, ClientIp);

    private EventOrigin Origin => EventOrigin.Admin(AdminUserId!, AdminUserName, ClientIp);
}
