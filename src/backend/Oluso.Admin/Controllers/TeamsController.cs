using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Admin.Dto.Teams;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for tenant Teams — the platform-level "addressable group of users"
/// primitive. Used by approval workflows (team-as-approver), billing
/// (team-as-subscriber), notifications, and any module that needs to target a
/// group rather than a single user.
///
/// <para>All endpoints are tenant-scoped: callers see / mutate only teams in
/// their resolved <see cref="AdminBaseController.TenantId"/>. SuperAdmin
/// cross-tenant access goes through the standard tenant-impersonation header.</para>
/// </summary>
[Route("api/admin/teams")]
public class TeamsController : AdminBaseController
{
    private readonly ITeamStore _teamStore;
    private readonly ITeamService _teamService;
    private readonly ILogger<TeamsController> _logger;

    public TeamsController(
        ITeamStore teamStore,
        ITeamService teamService,
        ILogger<TeamsController> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _teamStore = teamStore;
        _teamService = teamService;
        _logger = logger;
    }

    // ── Team CRUD ────────────────────────────────────────────────────────

    /// <summary>
    /// Paginated team list with optional filters. Filters compose: passing
    /// <c>type=Functional&amp;search=audit</c> narrows to functional teams whose
    /// code/name/description contains "audit".
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.TeamsRead)]
    public async Task<ActionResult<PagedResult<TeamDto>>> Query(
        [FromQuery] TeamType? type = null,
        [FromQuery] string? parentTeamId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _teamStore.QueryAsync(
            TenantId,
            new TeamQuery
            {
                Type = type,
                ParentTeamId = parentTeamId,
                IsActive = isActive,
                Search = search,
                Skip = (page - 1) * pageSize,
                Take = pageSize,
            },
            cancellationToken);

        return Ok(new PagedResult<TeamDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 1,
        });
    }

    /// <summary>
    /// Lightweight picker payload. Every team-picker dropdown across the
    /// platform hits this endpoint — kept small so it returns in one round-trip
    /// even for tenants with thousands of teams.
    /// </summary>
    [HttpGet("picker")]
    [RequirePermission(AdminPermissions.TeamsRead)]
    public async Task<ActionResult<IEnumerable<TeamSummaryDto>>> Picker(
        [FromQuery] TeamType? type = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var summaries = await _teamStore.GetPickerOptionsAsync(TenantId, type, activeOnly, cancellationToken);
        return Ok(summaries.Select(s => new TeamSummaryDto(s.Id, s.Code, s.Name, s.Type, s.ParentTeamId, s.IsActive)));
    }

    [HttpGet("{id}")]
    [RequirePermission(AdminPermissions.TeamsRead)]
    public async Task<ActionResult<TeamDto>> GetById(string id, CancellationToken cancellationToken = default)
    {
        var team = await _teamStore.GetByIdAsync(TenantId, id, cancellationToken);
        if (team == null) return NotFound();
        return Ok(MapToDto(team));
    }

    [HttpPost]
    [RequirePermission(AdminPermissions.TeamsWrite)]
    public async Task<ActionResult<TeamDto>> Create(
        [FromBody] CreateTeamRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(request.ParentTeamId)
            && await _teamStore.GetByIdAsync(TenantId, request.ParentTeamId, cancellationToken) is { IsManaged: true } managedParent)
        {
            return ManagedConflict(managedParent);
        }

        try
        {
            var team = await _teamService.CreateAsync(
                TenantId,
                new Team
                {
                    Code = request.Code,
                    Name = request.Name,
                    Description = request.Description,
                    Type = request.Type,
                    ParentTeamId = request.ParentTeamId,
                    OwnerUserId = request.OwnerUserId,
                    Visibility = request.Visibility,
                    IsActive = request.IsActive,
                    DisplayOrder = request.DisplayOrder,
                },
                createdByUserId: UserId,
                cancellationToken);

            _logger.LogInformation(
                "Team {TeamId} ({Code}) created in tenant {TenantId} by {UserId}",
                team.Id, team.Code, TenantId, UserId);

            return CreatedAtAction(nameof(GetById), new { id = team.Id }, MapToDto(team));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [RequirePermission(AdminPermissions.TeamsWrite)]
    public async Task<ActionResult<TeamDto>> Update(
        string id,
        [FromBody] UpdateTeamRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await _teamStore.GetByIdAsync(TenantId, id, cancellationToken);
        if (existing == null) return NotFound();
        if (existing.IsManaged) return ManagedConflict(existing);
        if (!string.IsNullOrEmpty(request.ParentTeamId) && request.ParentTeamId != existing.ParentTeamId
            && await _teamStore.GetByIdAsync(TenantId, request.ParentTeamId, cancellationToken) is { IsManaged: true } managedParent)
        {
            return ManagedConflict(managedParent);
        }

        try
        {
            existing.Code = request.Code ?? existing.Code;
            existing.Name = request.Name ?? existing.Name;
            existing.Description = request.Description ?? existing.Description;
            if (request.Type.HasValue) existing.Type = request.Type.Value;
            existing.ParentTeamId = request.ParentTeamId; // null clears parent
            existing.OwnerUserId = request.OwnerUserId;
            if (request.Visibility.HasValue) existing.Visibility = request.Visibility.Value;
            if (request.DisplayOrder.HasValue) existing.DisplayOrder = request.DisplayOrder.Value;

            var updated = await _teamService.UpdateAsync(TenantId, existing, UserId, cancellationToken);
            return Ok(MapToDto(updated));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Soft-archive a team. Optional <c>cascadeChildren</c> archives the whole
    /// subtree in one call — useful when retiring a department + all its
    /// sub-teams together.
    /// </summary>
    [HttpPost("{id}/archive")]
    [RequirePermission(AdminPermissions.TeamsWrite)]
    public async Task<ActionResult> Archive(
        string id,
        [FromQuery] bool cascadeChildren = false,
        CancellationToken cancellationToken = default)
    {
        if (await FindManagedAsync(id, cancellationToken) is { } managed) return ManagedConflict(managed);
        await _teamService.ArchiveAsync(TenantId, id, cascadeChildren, UserId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id}/restore")]
    [RequirePermission(AdminPermissions.TeamsWrite)]
    public async Task<ActionResult> Restore(string id, CancellationToken cancellationToken = default)
    {
        if (await FindManagedAsync(id, cancellationToken) is { } managed) return ManagedConflict(managed);
        await _teamService.RestoreAsync(TenantId, id, UserId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [RequirePermission(AdminPermissions.TeamsDelete)]
    public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        if (await FindManagedAsync(id, cancellationToken) is { } managed) return ManagedConflict(managed);
        try
        {
            await _teamService.DeleteAsync(TenantId, id, UserId, cancellationToken);
            _logger.LogInformation("Team {TeamId} hard-deleted by {UserId} in tenant {TenantId}",
                id, UserId, TenantId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { Message = ex.Message });
        }
    }

    // ── Hierarchy ────────────────────────────────────────────────────────

    [HttpGet("{id}/children")]
    [RequirePermission(AdminPermissions.TeamsRead)]
    public async Task<ActionResult<IEnumerable<TeamDto>>> GetChildren(
        string id,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var children = await _teamStore.GetChildTeamsAsync(TenantId, id, activeOnly, cancellationToken);
        return Ok(children.Select(MapToDto));
    }

    // ── Membership ───────────────────────────────────────────────────────

    [HttpGet("{id}/members")]
    [RequirePermission(AdminPermissions.TeamsRead)]
    public async Task<ActionResult<IEnumerable<TeamMembershipDto>>> GetMembers(
        string id,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var members = await _teamStore.GetMembersAsync(TenantId, id, activeOnly, cancellationToken);
        return Ok(members.Select(MapMembershipToDto));
    }

    [HttpPost("{id}/members")]
    [RequirePermission(AdminPermissions.TeamsManageMembers)]
    public async Task<ActionResult<TeamMembershipDto>> AddMember(
        string id,
        [FromBody] AddMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await FindManagedAsync(id, cancellationToken) is { } managed) return ManagedConflict(managed);
        try
        {
            var membership = await _teamService.AddMemberAsync(
                TenantId, id, request.UserId, request.Role, addedByUserId: UserId, cancellationToken);
            return Ok(MapMembershipToDto(membership));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPut("{id}/members/{userId}/role")]
    [RequirePermission(AdminPermissions.TeamsManageMembers)]
    public async Task<ActionResult<TeamMembershipDto>> ChangeRole(
        string id,
        string userId,
        [FromBody] ChangeMemberRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await FindManagedAsync(id, cancellationToken) is { } managed) return ManagedConflict(managed);
        try
        {
            var membership = await _teamService.ChangeMemberRoleAsync(
                TenantId, id, userId, request.Role, changedByUserId: UserId, cancellationToken);
            return Ok(MapMembershipToDto(membership));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
    }

    [HttpDelete("{id}/members/{userId}")]
    [RequirePermission(AdminPermissions.TeamsManageMembers)]
    public async Task<ActionResult> RemoveMember(
        string id,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (await FindManagedAsync(id, cancellationToken) is { } managed) return ManagedConflict(managed);
        await _teamService.RemoveMemberAsync(TenantId, id, userId, removedByUserId: UserId, cancellationToken);
        return NoContent();
    }

    // ── Managed teams ────────────────────────────────────────────────────

    /// <summary>The team when a module maintains it, else null (also null when it doesn't exist).</summary>
    private async Task<Team?> FindManagedAsync(string id, CancellationToken cancellationToken)
    {
        var team = await _teamStore.GetByIdAsync(TenantId, id, cancellationToken);
        return team is { IsManaged: true } ? team : null;
    }

    /// <summary>
    /// A module generates this team from its own records, so a hand edit here would be
    /// overwritten on the module's next sync. Changes go through the module instead.
    /// </summary>
    private ConflictObjectResult ManagedConflict(Team team)
    {
        var name = ManagedByName(team.ManagedBy) ?? team.ManagedBy;
        return Conflict(new
        {
            Message = $"This team is managed by {name}. Make changes in {name} instead.",
            ManagedBy = team.ManagedBy,
        });
    }

    private static string? ManagedByName(string? featureKey)
    {
        if (string.IsNullOrEmpty(featureKey)) return null;
        var module = ModuleRegistry.Instance.GetModule(featureKey);
        return ModuleBrandingRegistry.GetDisplayName(featureKey, module?.DisplayName ?? featureKey);
    }

    // ── Mapping ──────────────────────────────────────────────────────────

    private static TeamDto MapToDto(Team t) => new()
    {
        Id = t.Id,
        Code = t.Code,
        Name = t.Name,
        Description = t.Description,
        Type = t.Type,
        ParentTeamId = t.ParentTeamId,
        OwnerUserId = t.OwnerUserId,
        Visibility = t.Visibility,
        IsActive = t.IsActive,
        DisplayOrder = t.DisplayOrder,
        MemberCount = t.Members?.Count(m => m.IsActive) ?? 0,
        ManagedBy = t.ManagedBy,
        ManagedByName = ManagedByName(t.ManagedBy),
        CreatedAt = t.CreatedAt,
        CreatedBy = t.CreatedBy,
        UpdatedAt = t.UpdatedAt,
        UpdatedBy = t.UpdatedBy,
    };

    private static TeamMembershipDto MapMembershipToDto(TeamMembership m) => new()
    {
        Id = m.Id,
        TeamId = m.TeamId,
        UserId = m.UserId,
        Role = m.Role,
        IsActive = m.IsActive,
        JoinedAt = m.JoinedAt,
        LeftAt = m.LeftAt,
    };
}
