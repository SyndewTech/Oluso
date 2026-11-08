using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Outbound;
using Oluso.Enterprise.Scim.Stores;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests.Outbound;

/// <summary>
/// The reconcile with a group filter: only the groups the filter allows are queued, and every group the provider holds that the
/// filter no longer allows is queued for removal (for example groups pushed before the filter existed).
/// </summary>
public class ScimProviderReconcileServiceTests
{
    private readonly ScimOutboundProvisioningServiceTests.InMemoryMappingStore _mappings = new();
    private readonly Mock<ITeamStore> _teams = new();
    private readonly Mock<IScimProviderEnqueuer> _enqueuer = new();
    private readonly List<(string ResourceType, string Key, string Operation)> _queued = new();

    public ScimProviderReconcileServiceTests()
    {
        _teams.Setup(s => s.GetPickerOptionsAsync("t1", null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TeamSummary>
            {
                new("t-intake", "INTAKE", "Intake", TeamType.Functional, null, true),
                new("t-learners", "INTAKE-LEARNERS", "Learners", TeamType.Functional, "t-intake", true),
                new("t-finance", "FINANCE", "Finance", TeamType.Functional, null, true),
            });
        _enqueuer.Setup(e => e.EnqueueForProviderAsync(It.IsAny<ScimProvider>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((ScimProvider _, string type, string key, string op, string _, CancellationToken _) => _queued.Add((type, key, op)))
            .ReturnsAsync(new ScimProviderOutboxItem());
    }

    private ScimProviderReconcileService Service(params IScimGroupFilter[] filters)
        => new(new Mock<IOlusoUserService>().Object,
            new ScimGroupSourceRegistry(new IScimGroupSource[] { new TeamScimGroupSource(_teams.Object, new Mock<ITeamService>().Object) }, filters),
            _mappings, _enqueuer.Object, new Mock<IScimProviderStore>().Object, new ScimOptions(), NullLogger<ScimProviderReconcileService>.Instance);

    private static ScimProvider Provider() => new()
    {
        Id = "p1", TenantId = "t1", Name = "Chat", Flavor = ScimOutboundProvisioningServiceTests.TeamServerFlavor, SyncUsers = false, SyncGroups = true,
        GroupSource = ScimProviderGroupSources.Teams,
    };

    private void Holds(params string[] keys)
    {
        foreach (var key in keys)
            _mappings.Add(new ScimProviderResourceMapping
            {
                TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.Group, InternalId = key, RemoteId = "g-" + key,
            });
    }

    [Fact]
    public async Task Only_allowed_team_groups_are_queued_and_groups_held_for_other_teams_are_removed()
    {
        Holds("team:t-intake", "team:t-finance", "team:t-archived");

        var summary = await Service(new ScimOutboundProvisioningServiceTests.StubFilter("Chat", "t-intake", "t-learners")).ReconcileAsync(Provider());

        _queued.Where(q => q.Operation == ScimOutboxOperations.Upsert).Select(q => q.Key).Should().BeEquivalentTo(["team:t-intake", "team:t-learners"]);
        _queued.Where(q => q.Operation == ScimOutboxOperations.Remove).Select(q => q.Key).Should().BeEquivalentTo(["team:t-finance", "team:t-archived"]);
        summary.GroupsQueued.Should().Be(2);
        summary.GroupRemovalsQueued.Should().Be(2);
    }

    [Fact]
    public async Task A_filter_that_allows_nothing_removes_every_group_the_provider_holds()
    {
        Holds("team:t-intake", "team:t-finance");

        await Service(new ScimOutboundProvisioningServiceTests.StubFilter("Chat")).ReconcileAsync(Provider());

        _queued.Should().OnlyContain(q => q.Operation == ScimOutboxOperations.Remove);
        _queued.Select(q => q.Key).Should().BeEquivalentTo(["team:t-intake", "team:t-finance"]);
    }

    [Fact]
    public async Task Without_a_filter_every_team_is_queued_as_before()
    {
        await Service().ReconcileAsync(Provider());

        _queued.Select(q => q.Key).Should().BeEquivalentTo(["team:t-intake", "team:t-learners", "team:t-finance"]);
    }
}
