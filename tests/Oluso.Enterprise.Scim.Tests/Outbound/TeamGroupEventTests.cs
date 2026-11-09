using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.EntityFramework.Services;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Outbound;
using Oluso.Enterprise.Scim.Stores;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests.Outbound;

/// <summary>
/// What a team group carries beyond its members (leads, parent) reaches providers: a lead change and a move to another parent
/// raise events, and the outbound sink queues the groups whose payload they change.
/// </summary>
public class TeamGroupEventTests
{
    private const string Tenant = "t1";

    // ---- TeamService raises them ----------------------------------------------------------------------------------------------

    private readonly Mock<ITeamStore> _store = new();
    private readonly List<OlusoEvent> _raised = new();

    private TeamService Service()
    {
        var events = new Mock<IOlusoEventService>();
        events.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .Callback((OlusoEvent e, CancellationToken _) => _raised.Add(e)).Returns(Task.CompletedTask);
        var services = new ServiceCollection().AddSingleton(events.Object).BuildServiceProvider();
        _store.Setup(s => s.GetByIdAsync(Tenant, "staff", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Team { Id = "staff", TenantId = Tenant, Code = "STAFF", Name = "Staff", ParentTeamId = "old-parent" });
        _store.Setup(s => s.UpdateMembershipAsync(It.IsAny<TeamMembership>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamMembership m, CancellationToken _) => m);
        _store.Setup(s => s.UpdateAsync(It.IsAny<Team>(), It.IsAny<CancellationToken>())).ReturnsAsync((Team t, CancellationToken _) => t);
        return new TeamService(_store.Object, NullLogger<TeamService>.Instance, services);
    }

    private void Member(TeamMemberRole role, bool active = true)
        => _store.Setup(s => s.GetMembershipAsync(Tenant, "staff", "u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamMembership { TeamId = "staff", UserId = "u1", Role = role, IsActive = active });

    [Fact]
    public async Task Changing_a_members_role_raises_the_role_change()
    {
        var service = Service();
        Member(TeamMemberRole.Member);

        await service.ChangeMemberRoleAsync(Tenant, "staff", "u1", TeamMemberRole.Lead, "admin-1");

        var changed = _raised.Should().ContainSingle().Which.Should().BeOfType<TeamMemberRoleChangedEvent>().Subject;
        changed.TeamId.Should().Be("staff");
        changed.UserId.Should().Be("u1");
        changed.MemberRole.Should().Be("Lead");
        changed.PreviousRole.Should().Be("Member");
        changed.WebhookEventType.Should().Be("team.member_role_changed");
    }

    [Fact]
    public async Task Adding_an_existing_member_with_another_role_raises_the_role_change_and_the_same_role_raises_nothing()
    {
        var service = Service();
        Member(TeamMemberRole.Member);
        await service.AddMemberAsync(Tenant, "staff", "u1", TeamMemberRole.Member, null);
        _raised.Should().BeEmpty();

        await service.AddMemberAsync(Tenant, "staff", "u1", TeamMemberRole.Lead, null);

        _raised.Should().ContainSingle().Which.Should().BeOfType<TeamMemberRoleChangedEvent>();
    }

    [Fact]
    public async Task Rejoining_raises_member_added_not_a_role_change()
    {
        var service = Service();
        Member(TeamMemberRole.Member, active: false);

        await service.AddMemberAsync(Tenant, "staff", "u1", TeamMemberRole.Lead, null);

        _raised.Should().ContainSingle().Which.Should().BeOfType<TeamMemberAddedEvent>();
    }

    [Fact]
    public async Task A_move_to_another_parent_names_the_old_one_and_a_rename_names_none()
    {
        var service = Service();
        _store.Setup(s => s.GetByIdAsync(Tenant, "new-parent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = "new-parent", TenantId = Tenant, Code = "NEW", Name = "New" });

        await service.UpdateAsync(Tenant, new Team { Id = "staff", Code = "STAFF", Name = "Staff", ParentTeamId = "new-parent" }, null);
        var moved = _raised.OfType<TeamUpdatedEvent>().Single();
        moved.ParentChanged.Should().BeTrue();
        moved.PreviousParentTeamId.Should().Be("old-parent");

        _raised.Clear();
        await service.UpdateAsync(Tenant, new Team { Id = "staff", Code = "STAFF", Name = "Teachers", ParentTeamId = "old-parent" }, null);
        var renamed = _raised.OfType<TeamUpdatedEvent>().Single();
        renamed.ParentChanged.Should().BeFalse();
        renamed.PreviousParentTeamId.Should().BeNull();
    }

    // ---- the outbound sink queues the groups --------------------------------------------------------------------------------

    private static (ScimOutboundEventSink Sink, List<ScimProviderOutboxItem> Queued) Sink(Mock<ITeamStore> teams)
    {
        var queued = new List<ScimProviderOutboxItem>();
        var providers = new Mock<IScimProviderStore>();
        providers.Setup(p => p.GetEnabledByTenantAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ScimProvider>
        {
            new() { Id = "chat", TenantId = Tenant, Name = "Chat", BaseUrl = "https://x", SyncUsers = true, SyncGroups = true,
                Flavor = ScimOutboundProvisioningServiceTests.TeamServerFlavor, GroupSource = ScimProviderGroupSources.Teams },
        });
        var outbox = new Mock<IScimProviderOutboxStore>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<ScimProviderOutboxItem>(), It.IsAny<CancellationToken>()))
            .Callback<ScimProviderOutboxItem, CancellationToken>((i, _) => queued.Add(i))
            .ReturnsAsync((ScimProviderOutboxItem i, CancellationToken _) => i);
        var enqueuer = new ScimProviderEnqueuer(providers.Object, outbox.Object, NullLogger<ScimProviderEnqueuer>.Instance);
        return (new ScimOutboundEventSink(enqueuer, Mock.Of<IRoleStore>(), teams.Object, Mock.Of<IOlusoUserService>(),
            NullLogger<ScimOutboundEventSink>.Instance), queued);
    }

    private static void Seed(Mock<ITeamStore> teams, string id, string? parent)
        => teams.Setup(s => s.GetByIdAsync(Tenant, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = id, TenantId = Tenant, Code = id.ToUpperInvariant(), Name = id, ParentTeamId = parent });

    [Fact]
    public async Task A_role_change_queues_only_the_teams_own_group()
    {
        var teams = new Mock<ITeamStore>();
        Seed(teams, "staff", "intake");
        var (sink, queued) = Sink(teams);

        await sink.HandleAsync(new TeamMemberRoleChangedEvent
        {
            TenantId = Tenant, TeamId = "staff", TeamName = "Staff", UserId = "u1", MemberRole = "Lead",
        });

        queued.Select(i => (i.ResourceType, i.InternalId, i.Operation)).Should().Equal(
            (ScimResourceTypes.Group, "team:staff", ScimOutboxOperations.Upsert));
    }

    [Fact]
    public async Task A_move_queues_the_team_its_new_ancestors_and_its_old_ones()
    {
        var teams = new Mock<ITeamStore>();
        Seed(teams, "staff", "new-parent");
        Seed(teams, "new-parent", "root");
        Seed(teams, "root", null);
        Seed(teams, "old-parent", "old-root");
        Seed(teams, "old-root", null);
        var (sink, queued) = Sink(teams);

        await sink.HandleAsync(new TeamUpdatedEvent
        {
            TenantId = Tenant, TeamId = "staff", TeamName = "Staff", ParentChanged = true, PreviousParentTeamId = "old-parent",
        });

        queued.Select(i => i.InternalId).Should().Equal("team:staff", "team:new-parent", "team:root", "team:old-parent", "team:old-root");
        queued.Should().AllSatisfy(i => i.Operation.Should().Be(ScimOutboxOperations.Upsert));
    }
}
