using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Teams;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Xunit;

namespace Oluso.Tests.Teams;

/// <summary>
/// A team a module maintains (<see cref="Team.ManagedBy"/>) is read-only in the Teams admin: the
/// module would overwrite a hand edit on its next sync, so the API refuses it and says where to
/// make the change instead. Hand-made teams behave as before.
/// </summary>
public class ManagedTeamsAdminTests
{
    private const string TenantId = "t1";

    private readonly Mock<ITeamStore> _store = new();
    private readonly Mock<ITeamService> _service = new();

    private readonly Team _managed = new() { Id = "managed", TenantId = TenantId, Code = "COHORT2026", Name = "JSS1 · 2026 entry", ManagedBy = "education" };
    private readonly Team _manual = new() { Id = "manual", TenantId = TenantId, Code = "FINANCE", Name = "Finance" };

    private TeamsController Controller()
    {
        _store.Setup(s => s.GetByIdAsync(TenantId, "managed", It.IsAny<CancellationToken>())).ReturnsAsync(_managed);
        _store.Setup(s => s.GetByIdAsync(TenantId, "manual", It.IsAny<CancellationToken>())).ReturnsAsync(_manual);
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        tenant.SetupGet(t => t.HasTenant).Returns(true);
        return new TeamsController(_store.Object, _service.Object, NullLogger<TeamsController>.Instance, tenant.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "admin-1") }, "test")),
                },
            },
        };
    }

    [Fact]
    public async Task Every_edit_of_a_managed_team_is_refused()
    {
        var c = Controller();

        (await c.Update("managed", new UpdateTeamRequest { Name = "x" }, default)).Result.Should().BeOfType<ConflictObjectResult>();
        (await c.Archive("managed", false, default)).Should().BeOfType<ConflictObjectResult>();
        (await c.Restore("managed", default)).Should().BeOfType<ConflictObjectResult>();
        (await c.Delete("managed", default)).Should().BeOfType<ConflictObjectResult>();
        (await c.AddMember("managed", new AddMemberRequest { UserId = "u1" }, default)).Result.Should().BeOfType<ConflictObjectResult>();
        (await c.ChangeRole("managed", "u1", new ChangeMemberRoleRequest { Role = TeamMemberRole.Lead }, default)).Result.Should().BeOfType<ConflictObjectResult>();
        (await c.RemoveMember("managed", "u1", default)).Should().BeOfType<ConflictObjectResult>();
        (await c.Create(new CreateTeamRequest { Code = "X", Name = "X", ParentTeamId = "managed" }, default)).Result.Should().BeOfType<ConflictObjectResult>();

        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_refusal_names_the_module_and_the_dto_carries_it()
    {
        var c = Controller();

        var conflict = (ConflictObjectResult)(await c.Archive("managed", false, default));
        conflict.Value!.ToString().Should().Contain("managed by");

        var dto = (TeamDto)((OkObjectResult)(await c.GetById("managed", default)).Result!).Value!;
        dto.ManagedBy.Should().Be("education");
        dto.ManagedByName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Hand_made_teams_are_still_editable()
    {
        var c = Controller();

        (await c.Archive("manual", false, default)).Should().BeOfType<NoContentResult>();
        (await c.RemoveMember("manual", "u1", default)).Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.ArchiveAsync(TenantId, "manual", false, "admin-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
