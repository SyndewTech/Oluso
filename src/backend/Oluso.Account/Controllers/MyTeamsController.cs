using Microsoft.AspNetCore.Mvc;
using Oluso.Account.Dto.Teams;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Account.Controllers;

/// <summary>
/// Workspace-side teams endpoint. Lets a signed-in user discover which teams
/// they belong to, what role they hold in each, and (for teams they Lead) walk
/// down to manage members.
///
/// <para>Visibility rules: a user sees teams they're a member of <em>and</em>
/// any tenant-wide team in their tenant. Private teams they don't belong to
/// are hidden. Admin endpoints under <c>/api/admin/teams</c> bypass this.</para>
/// </summary>
[Route("api/workspace/teams")]
public class MyTeamsController : WorkspaceBaseController
{
    private readonly ITeamStore _teamStore;
    private readonly ITeamService _teamService;

    public MyTeamsController(
        ITenantContext tenantContext,
        ITeamStore teamStore,
        ITeamService teamService) : base(tenantContext)
    {
        _teamStore = teamStore;
        _teamService = teamService;
    }

    /// <summary>
    /// Teams the current user is a member of. The default workspace landing
    /// view for "where am I plugged in".
    /// </summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<MyTeamDto>>> GetMyTeams(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var teams = await _teamStore.GetUserTeamsAsync(TenantId, UserId, activeOnly, cancellationToken);

        // Per-team role for the current user requires a follow-up read since
        // GetUserTeamsAsync returns Team rows, not the membership row.
        var result = new List<MyTeamDto>(teams.Count);
        foreach (var t in teams)
        {
            var membership = await _teamStore.GetMembershipAsync(TenantId, t.Id, UserId, cancellationToken);
            result.Add(new MyTeamDto(
                t.Id, t.Code, t.Name, t.Description, t.Type.ToString(), t.ParentTeamId,
                (membership?.Role ?? TeamMemberRole.Member).ToString(),
                membership?.JoinedAt ?? DateTime.MinValue));
        }
        return Ok(result);
    }

    /// <summary>
    /// Teams the user can see in the picker — own memberships plus tenant-wide
    /// teams. Drives the "select a team" dropdown on request-submission flows
    /// (approvals, project selection, etc.).
    /// </summary>
    [HttpGet("picker")]
    public async Task<ActionResult<IEnumerable<TeamPickerOptionDto>>> GetPickerOptions(
        [FromQuery] TeamType? type = null,
        CancellationToken cancellationToken = default)
    {
        var (items, _) = await _teamStore.QueryAsync(
            TenantId,
            new TeamQuery
            {
                Type = type,
                IsActive = true,
                VisibleToUserId = UserId,
                Take = 500,
            },
            cancellationToken);

        return Ok(items.Select(t => new TeamPickerOptionDto(t.Id, t.Code, t.Name, t.Type.ToString())));
    }

    /// <summary>
    /// Detail of a team the user is a member of (or any tenant-wide team).
    /// Members of the team see member list; non-members see metadata only.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<MyTeamDetailDto>> GetById(
        string id,
        CancellationToken cancellationToken = default)
    {
        var team = await _teamStore.GetByIdAsync(TenantId, id, cancellationToken);
        if (team == null) return NotFound();

        var membership = await _teamStore.GetMembershipAsync(TenantId, id, UserId, cancellationToken);
        var isMember = membership is { IsActive: true };

        // Private teams hide their existence from non-members entirely.
        if (team.Visibility == TeamVisibility.Private && !isMember) return NotFound();

        var members = isMember
            ? (await _teamStore.GetMembersAsync(TenantId, id, activeOnly: true, cancellationToken))
                .Select(m => new MyTeamMemberDto(m.UserId, m.Role.ToString(), m.JoinedAt))
                .ToList()
            : new List<MyTeamMemberDto>();

        return Ok(new MyTeamDetailDto(
            team.Id, team.Code, team.Name, team.Description, team.Type.ToString(),
            team.ParentTeamId, team.OwnerUserId, team.Visibility.ToString(), team.IsActive,
            membership?.Role.ToString(), isMember, members));
    }
}

