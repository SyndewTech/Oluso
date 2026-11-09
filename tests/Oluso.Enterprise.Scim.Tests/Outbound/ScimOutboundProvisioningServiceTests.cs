using System.Text.Json.Nodes;
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

public class ScimOutboundProvisioningServiceTests
{
    private readonly FakeScimHttp _http = new();
    private readonly InMemoryMappingStore _mappings = new();
    private readonly Mock<IScimProviderAttributeMappingStore> _attributeMappings = new();
    private readonly Mock<IScimProviderLogStore> _logs = new();
    private readonly Mock<IScimProviderStore> _providers = new();
    private readonly Mock<IOlusoUserService> _users = new();
    private readonly Mock<IRoleStore> _roles = new();
    private readonly Mock<ITeamStore> _teams = new();
    private readonly Mock<ITeamService> _teamService = new();
    private readonly Mock<IScimProviderEnqueuer> _enqueuer = new();
    private readonly List<ScimProviderLog> _written = new();

    private readonly OlusoUser _user = new()
    {
        Id = "u1", TenantId = "t1", UserName = "jdoe", Email = "jane@example.org", FirstName = "Jane", LastName = "Doe", IsActive = true,
    };

    public ScimOutboundProvisioningServiceTests()
    {
        _attributeMappings.Setup(s => s.GetByProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScimProviderAttributeMapping>());
        _logs.Setup(s => s.CreateAsync(It.IsAny<ScimProviderLog>(), It.IsAny<CancellationToken>()))
            .Callback<ScimProviderLog, CancellationToken>((l, _) => _written.Add(l))
            .ReturnsAsync((ScimProviderLog l, CancellationToken _) => l);
        _users.Setup(s => s.GetByIdAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _roles.Setup(s => s.GetUserRolesInTenantAsync("u1", "t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OlusoRole>());
        _teams.Setup(s => s.GetUserTeamsAsync("t1", "u1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Team>());
    }

    // A flavor that sends the team extension, as one a module registers for its own server.
    internal const string TeamServerFlavor = "team-server";

    internal static readonly ScimProviderFlavorProfile TeamServer = new()
    {
        Key = TeamServerFlavor,
        DisplayName = "Team server",
        Description = "A SCIM server that understands the team extension.",
        DefaultUserNameSource = ScimProviderUserNameSources.UserName,
        SendsTeamExtension = true,
        UserLookupAttributes = new[] { "externalId", "userName" },
        GroupLookupAttributes = new[] { "externalId", "displayName" },
        DefaultUserMappings = ScimProviderFlavorProfiles.StandardUserMappings,
    };

    internal static ScimProviderFlavorRegistry Flavors() =>
        new(new[] { ScimProviderFlavorProfiles.Generic, ScimProviderFlavorProfiles.Stalwart, TeamServer });

    private ScimOutboundProvisioningService CreateService(params IScimGroupFilter[] filters)
    {
        var registry = new ScimGroupSourceRegistry(new IScimGroupSource[]
        {
            new RoleScimGroupSource(_roles.Object),
            new TeamScimGroupSource(_teams.Object, _teamService.Object),
        }, filters);

        return new ScimOutboundProvisioningService(
            _mappings,
            _attributeMappings.Object,
            _http,
            new ScimOutboundMapper(new ScimExpressionEngine()),
            _logs.Object,
            _providers.Object,
            _users.Object,
            _roles.Object,
            _teams.Object,
            registry,
            Flavors(),
            _enqueuer.Object,
            new ScimOptions(),
            NullLogger<ScimOutboundProvisioningService>.Instance);
    }

    private static ScimProvider Provider(string flavor = ScimProviderFlavors.Generic) => new()
    {
        Id = "p1", TenantId = "t1", Name = "Test", Flavor = flavor, BaseUrl = "https://scim.example.org/scim/v2",
        UserNameSource = flavor == ScimProviderFlavors.Stalwart ? ScimProviderUserNameSources.Email : ScimProviderUserNameSources.UserName,
        SecretUpdatedAt = DateTime.UtcNow,
    };

    private static ScimProviderOutboxItem Item(string operation = ScimOutboxOperations.Upsert, string reason = "event:test") => new()
    {
        TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.User, InternalId = "u1", Operation = operation, Reason = reason,
    };

    private static JsonObject EmptyList() => new() { ["totalResults"] = 0, ["Resources"] = new JsonArray() };

    private static JsonObject ListWith(string id) => new()
    {
        ["totalResults"] = 1,
        ["Resources"] = new JsonArray(new JsonObject { ["id"] = id, ["userName"] = "jdoe" }),
    };

    [Fact]
    public async Task Creates_remote_user_when_lookups_find_nothing()
    {
        _http.On(HttpMethod.Get, "Users?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Users", 201, new JsonObject { ["id"] = "r1", ["meta"] = new JsonObject { ["version"] = "W/\"1\"" } });

        var result = await CreateService().ProcessAsync(Item(), Provider());

        result.Success.Should().BeTrue(result.Error);
        _http.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Get, HttpMethod.Get, HttpMethod.Post);
        _http.Requests[0].Path.Should().Contain(Uri.EscapeDataString("externalId eq \"u1\""));
        _http.Requests[1].Path.Should().Contain(Uri.EscapeDataString("userName eq \"jdoe\""));

        var mapping = _mappings.Single();
        mapping.RemoteId.Should().Be("r1");
        mapping.RemoteVersion.Should().Be("W/\"1\"");
        mapping.PayloadHash.Should().NotBeNullOrEmpty();
        _written.Should().HaveCount(3);
        _providers.Verify(p => p.RecordOutcomeAsync("p1", true, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Adopts_existing_remote_user_on_conflict()
    {
        _http.On(HttpMethod.Get, "Users?filter=", 200, EmptyList(), once: true);
        _http.On(HttpMethod.Get, "Users?filter=", 200, EmptyList(), once: true);
        _http.On(HttpMethod.Post, "Users", 409, new JsonObject { ["detail"] = "exists", ["scimType"] = "uniqueness" });
        _http.On(HttpMethod.Get, "Users?filter=", 200, ListWith("x1"));
        _http.On(HttpMethod.Put, "Users/x1", 200, new JsonObject { ["id"] = "x1" });

        var result = await CreateService().ProcessAsync(Item(), Provider());

        result.Success.Should().BeTrue(result.Error);
        _mappings.Single().RemoteId.Should().Be("x1");
        _http.Requests.Last().Method.Should().Be(HttpMethod.Put);
    }

    [Fact]
    public async Task Updates_known_remote_user_and_skips_when_unchanged()
    {
        var service = CreateService();
        _http.On(HttpMethod.Put, "Users/r1", 200, new JsonObject { ["id"] = "r1" });
        await _mappings.CreateAsync(new ScimProviderResourceMapping
        {
            TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.User, InternalId = "u1", RemoteId = "r1", PayloadHash = "stale",
        });

        var first = await service.ProcessAsync(Item(), Provider());
        first.Success.Should().BeTrue(first.Error);
        _http.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Put);

        var second = await service.ProcessAsync(Item(), Provider());
        second.Success.Should().BeTrue();
        second.Note.Should().Contain("Unchanged");
        _http.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Put, "an unchanged payload is not re-sent");

        var reconcile = await service.ProcessAsync(Item(reason: "reconcile"), Provider());
        reconcile.Success.Should().BeTrue();
        _http.Requests.Count(r => r.Method == HttpMethod.Put).Should().Be(2, "a reconcile pushes regardless of the hash");
    }

    [Fact]
    public async Task Falls_back_to_patch_when_put_is_not_supported()
    {
        _http.On(HttpMethod.Put, "Users/r1", 405);
        _http.On(HttpMethod.Patch, "Users/r1", 200, new JsonObject { ["id"] = "r1" });
        await _mappings.CreateAsync(new ScimProviderResourceMapping
        {
            TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.User, InternalId = "u1", RemoteId = "r1",
        });

        var result = await CreateService().ProcessAsync(Item(), Provider());

        result.Success.Should().BeTrue(result.Error);
        var patch = _http.Requests.Single(r => r.Method == HttpMethod.Patch).Body!.AsObject();
        patch["Operations"]!.AsArray().Should().Contain(o => o!["path"]!.GetValue<string>() == "userName");
    }

    [Fact]
    public async Task Remove_deactivates_by_default_and_deletes_in_delete_mode()
    {
        await _mappings.CreateAsync(new ScimProviderResourceMapping
        {
            TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.User, InternalId = "u1", RemoteId = "r1", PayloadHash = "h",
        });
        _http.On(HttpMethod.Patch, "Users/r1", 200, new JsonObject { ["id"] = "r1", ["active"] = false });

        var deactivate = await CreateService().ProcessAsync(Item(ScimOutboxOperations.Remove), Provider());

        deactivate.Success.Should().BeTrue(deactivate.Error);
        var patch = _http.Requests.Single(r => r.Method == HttpMethod.Patch).Body!.AsObject();
        patch["Operations"]![0]!["path"]!.GetValue<string>().Should().Be("active");
        patch["Operations"]![0]!["value"]!.GetValue<bool>().Should().BeFalse();
        _mappings.Single().IsDeprovisioned.Should().BeTrue();
        _mappings.Single().PayloadHash.Should().BeNull();

        _http.Reset();
        _http.On(HttpMethod.Delete, "Users/r1", 204);
        var provider = Provider();
        provider.DeprovisionMode = ScimProviderDeprovisionModes.Delete;

        var delete = await CreateService().ProcessAsync(Item(ScimOutboxOperations.Remove), provider);

        delete.Success.Should().BeTrue(delete.Error);
        _mappings.Should().BeEmpty("delete mode forgets the remote id");
    }

    [Fact]
    public async Task User_outside_scope_is_deprovisioned_when_previously_pushed()
    {
        var provider = Provider();
        provider.ScopeRoleIds = "role-managers";
        _roles.Setup(s => s.GetUserRolesInTenantAsync("u1", "t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OlusoRole> { new() { Id = "role-staff", TenantId = "t1", Name = "Staff" } });
        await _mappings.CreateAsync(new ScimProviderResourceMapping
        {
            TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.User, InternalId = "u1", RemoteId = "r1",
        });
        _http.On(HttpMethod.Patch, "Users/r1", 200, new JsonObject { ["id"] = "r1" });

        var result = await CreateService().ProcessAsync(Item(), provider);

        result.Success.Should().BeTrue(result.Error);
        result.Note.Should().Contain("left the provisioning scope");
        _mappings.Single().IsDeprovisioned.Should().BeTrue();
    }

    [Fact]
    public async Task Stalwart_user_without_email_is_a_dead_letter_not_a_retry()
    {
        _user.Email = null;

        var result = await CreateService().ProcessAsync(Item(), Provider(ScimProviderFlavors.Stalwart));

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeFalse();
        result.Error.Should().Contain("userName");
        _http.Requests.Should().BeEmpty();
        _providers.Verify(p => p.RecordOutcomeAsync("p1", false, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Server_errors_are_retryable_and_validation_errors_are_not()
    {
        _http.On(HttpMethod.Get, "Users?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Users", 503);
        var transient = await CreateService().ProcessAsync(Item(), Provider());
        transient.Success.Should().BeFalse();
        transient.Retryable.Should().BeTrue();

        _http.Reset();
        _http.On(HttpMethod.Get, "Users?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Users", 400, new JsonObject { ["detail"] = "bad", ["scimType"] = "invalidValue" });
        var rejected = await CreateService().ProcessAsync(Item(), Provider());
        rejected.Success.Should().BeFalse();
        rejected.Retryable.Should().BeFalse();
        rejected.Error.Should().Contain("bad");
    }

    [Fact]
    public async Task Group_upsert_pushes_role_members_with_remote_ids_and_queues_missing_ones()
    {
        var provider = Provider();
        provider.SyncGroups = true;
        provider.GroupSource = ScimProviderGroupSources.Roles;
        _roles.Setup(s => s.GetByIdAsync("r-fin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoRole { Id = "r-fin", TenantId = "t1", Name = "Finance", DisplayName = "Finance Team" });
        _roles.Setup(s => s.GetUsersByRoleInTenantAsync("r-fin", "t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoleUserInfo>
            {
                new() { Id = "u1", UserName = "jdoe", Email = "jane@example.org", TenantId = "t1" },
                new() { Id = "u2", UserName = "bob", Email = "bob@example.org", TenantId = "t1" },
            });
        await _mappings.CreateAsync(new ScimProviderResourceMapping
        {
            TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.User, InternalId = "u1", RemoteId = "ru1",
        });
        _http.On(HttpMethod.Get, "Groups?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Groups", 201, new JsonObject { ["id"] = "g1" });

        var item = new ScimProviderOutboxItem
        {
            TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.Group, InternalId = "role:r-fin", Operation = ScimOutboxOperations.Upsert,
        };
        var result = await CreateService().ProcessAsync(item, provider);

        result.Success.Should().BeTrue(result.Error);
        var body = _http.Requests.Single(r => r.Method == HttpMethod.Post).Body!.AsObject();
        body["externalId"]!.GetValue<string>().Should().Be("role:r-fin");
        body["displayName"]!.GetValue<string>().Should().Be("Finance Team");
        body["members"]!.AsArray().Select(m => m!["value"]!.GetValue<string>()).Should().Equal("ru1");
        _enqueuer.Verify(e => e.EnqueueForProviderAsync(provider, ScimResourceTypes.User, "u2", ScimOutboxOperations.Upsert, "group-member", It.IsAny<CancellationToken>()), Times.Once);
        _mappings.Should().Contain(m => m.ResourceType == ScimResourceTypes.Group && m.InternalId == "role:r-fin" && m.RemoteId == "g1");
    }

    [Fact]
    public async Task Preview_builds_payload_without_calling_the_provider()
    {
        var preview = await CreateService().PreviewUserAsync(Provider(), "u1");

        preview.Error.Should().BeNull();
        preview.InScope.Should().BeTrue();
        preview.UserName.Should().Be("jdoe");
        preview.Payload!["externalId"]!.GetValue<string>().Should().Be("u1");
        _http.Requests.Should().BeEmpty();
    }

    // ---- team groups and the team extension (a flavor that sends it) ---------------------------------------------------

    private const string TeamExtension = "urn:syndew:params:scim:schemas:extension:team:2.0:Group";

    /// <summary>A managed sub-team with a lead, a member and a former lead, all three users provisioned.</summary>
    private Team StaffTeam(string? parentId = "t-root")
    {
        var team = new Team
        {
            Id = "t-staff", TenantId = "t1", Code = "COHORT2026-STAFF", Name = "JSS1 · 2026 entry · Staff", ParentTeamId = parentId,
            ManagedBy = "education",
            Members =
            {
                new TeamMembership { TeamId = "t-staff", UserId = "u-tutor", Role = TeamMemberRole.Lead, IsActive = true },
                new TeamMembership { TeamId = "t-staff", UserId = "u-teacher", Role = TeamMemberRole.Member, IsActive = true },
                new TeamMembership { TeamId = "t-staff", UserId = "u-gone", Role = TeamMemberRole.Lead, IsActive = false },
            },
        };
        _teams.Setup(s => s.GetByIdAsync("t1", "t-staff", It.IsAny<CancellationToken>())).ReturnsAsync(team);
        _teamService.Setup(s => s.GetEffectiveMemberUserIdsAsync("t1", "t-staff", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "u-tutor", "u-teacher" });
        foreach (var (user, remote) in new[] { ("u-tutor", "@tutor:school.example"), ("u-teacher", "@teacher:school.example") })
        {
            if (!_mappings.Any(m => m.InternalId == user))
                _mappings.Add(new ScimProviderResourceMapping { TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.User, InternalId = user, RemoteId = remote });
        }
        return team;
    }

    private static ScimProvider TeamProvider(string flavor)
    {
        var provider = Provider(flavor);
        provider.SyncGroups = true;
        provider.GroupSource = ScimProviderGroupSources.Teams;
        return provider;
    }

    private static ScimProviderOutboxItem TeamItem(string reason = "event:team.updated") => new()
    {
        TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.Group, InternalId = "team:t-staff",
        Operation = ScimOutboxOperations.Upsert, Reason = reason,
    };

    [Fact]
    public async Task A_team_extension_flavor_pushes_team_groups_with_code_parent_leads_and_managing_module()
    {
        StaffTeam();
        _http.On(HttpMethod.Get, "Groups?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Groups", 201, new JsonObject { ["id"] = "g-staff" });

        var result = await CreateService().ProcessAsync(TeamItem(), TeamProvider(TeamServerFlavor));

        result.Success.Should().BeTrue(result.Error);
        var body = _http.Requests.Single(r => r.Method == HttpMethod.Post).Body!.AsObject();
        body.ToJsonString().Should().Be(
            "{\"schemas\":[\"urn:ietf:params:scim:schemas:core:2.0:Group\",\"" + TeamExtension + "\"]," +
            "\"externalId\":\"team:t-staff\",\"displayName\":\"JSS1 \\u00B7 2026 entry \\u00B7 Staff\"," +
            "\"members\":[{\"value\":\"@teacher:school.example\",\"type\":\"User\"},{\"value\":\"@tutor:school.example\",\"type\":\"User\"}]," +
            "\"" + TeamExtension + "\":{\"code\":\"COHORT2026-STAFF\",\"parent\":\"team:t-root\",\"leads\":[\"@tutor:school.example\"],\"managedBy\":\"education\"}}");
    }

    [Fact]
    public async Task A_top_level_hand_made_team_has_no_parent_and_no_managing_module()
    {
        var team = StaffTeam(parentId: null);
        team.ManagedBy = null;
        _http.On(HttpMethod.Get, "Groups?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Groups", 201, new JsonObject { ["id"] = "g-staff" });

        await CreateService().ProcessAsync(TeamItem(), TeamProvider(TeamServerFlavor));

        var extension = _http.Requests.Single(r => r.Method == HttpMethod.Post).Body![TeamExtension]!.AsObject();
        extension["parent"].Should().BeNull();
        extension.ContainsKey("managedBy").Should().BeFalse();
    }

    [Theory]
    [InlineData(ScimProviderFlavors.Generic)]
    [InlineData(ScimProviderFlavors.Stalwart)]
    public async Task Other_flavors_push_the_plain_group_as_before(string flavor)
    {
        StaffTeam();
        _http.On(HttpMethod.Get, "Groups?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Groups", 201, new JsonObject { ["id"] = "g-staff" });

        await CreateService().ProcessAsync(TeamItem(), TeamProvider(flavor));

        var body = _http.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        var plain = new ScimOutboundMapper(new ScimExpressionEngine())
            .BuildGroup("team:t-staff", "JSS1 · 2026 entry · Staff", new[] { "@tutor:school.example", "@teacher:school.example" });
        body.ToJsonString().Should().Be(plain.ToJsonString(), "byte for byte what was pushed before the extension existed");
        body.ToJsonString().Should().NotContain("extension:team");
    }

    [Fact]
    public async Task A_lead_change_changes_the_payload_hash_so_the_group_is_pushed_again()
    {
        var team = StaffTeam();
        _http.On(HttpMethod.Get, "Groups?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Groups", 201, new JsonObject { ["id"] = "g-staff" });
        _http.On(HttpMethod.Put, "Groups/g-staff", 200, new JsonObject { ["id"] = "g-staff" });
        var service = CreateService();
        var provider = TeamProvider(TeamServerFlavor);
        await service.ProcessAsync(TeamItem(), provider);
        var firstHash = _mappings.Single(m => m.InternalId == "team:t-staff").PayloadHash;

        (await service.ProcessAsync(TeamItem(), provider)).Note.Should().Be("Unchanged since the last push");

        team.Members.Single(m => m.UserId == "u-teacher").Role = TeamMemberRole.Lead;
        var again = await service.ProcessAsync(TeamItem(), provider);

        again.Success.Should().BeTrue(again.Error);
        _http.Requests.Last().Method.Should().Be(HttpMethod.Put);
        _http.Requests.Last().Body![TeamExtension]!["leads"]!.AsArray().Select(l => l!.GetValue<string>())
            .Should().Equal("@teacher:school.example", "@tutor:school.example");
        _mappings.Single(m => m.InternalId == "team:t-staff").PayloadHash.Should().NotBe(firstHash);
    }

    [Fact]
    public async Task A_lead_not_provisioned_yet_is_left_out_and_queued()
    {
        var team = StaffTeam();
        team.Members.Add(new TeamMembership { TeamId = "t-staff", UserId = "u-new", Role = TeamMemberRole.Lead, IsActive = true });
        _teamService.Setup(s => s.GetEffectiveMemberUserIdsAsync("t1", "t-staff", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "u-tutor", "u-teacher", "u-new" });
        _http.On(HttpMethod.Get, "Groups?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Groups", 201, new JsonObject { ["id"] = "g-staff" });
        var provider = TeamProvider(TeamServerFlavor);

        var result = await CreateService().ProcessAsync(TeamItem(), provider);

        result.Note.Should().Contain("1 member(s) not yet provisioned and queued");
        _http.Requests.Single(r => r.Method == HttpMethod.Post).Body![TeamExtension]!["leads"]!.AsArray()
            .Select(l => l!.GetValue<string>()).Should().Equal("@tutor:school.example");
        _enqueuer.Verify(e => e.EnqueueForProviderAsync(provider, ScimResourceTypes.User, "u-new", ScimOutboxOperations.Upsert, "group-member",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Registered_flavors_are_known_and_only_the_registered_team_flavor_sends_the_extension()
    {
        var flavors = Flavors();
        var teamServer = flavors.Get(TeamServerFlavor);
        teamServer.Should().BeSameAs(TeamServer);
        flavors.IsKnown("TEAM-SERVER").Should().BeTrue();
        flavors.Get(ScimProviderFlavors.Generic).SendsTeamExtension.Should().BeFalse();
        flavors.Get(ScimProviderFlavors.Stalwart).SendsTeamExtension.Should().BeFalse();
    }

    [Fact]
    public void An_unregistered_flavor_is_unknown_and_reads_as_generic()
    {
        var flavors = new ScimProviderFlavorRegistry(new[] { ScimProviderFlavorProfiles.Generic, ScimProviderFlavorProfiles.Stalwart });
        flavors.IsKnown(TeamServerFlavor).Should().BeFalse();
        flavors.Get(TeamServerFlavor).Should().BeSameAs(ScimProviderFlavorProfiles.Generic);
        flavors.All.Select(f => f.Key).Should().Equal(ScimProviderFlavors.Generic, ScimProviderFlavors.Stalwart);
    }

    // ---- group filters (a module that owns a provider narrows which groups it pushes) ------------------------------------

    /// <summary>A filter that allows <paramref name="allowed"/> teams for the provider named <paramref name="provider"/>, and has no say elsewhere.</summary>
    internal sealed class StubFilter(string provider, params string[] allowed) : IScimGroupFilter
    {
        public HashSet<string> Allowed { get; } = new(allowed, StringComparer.Ordinal);

        public Task<IReadOnlySet<string>?> AllowedAsync(ScimProvider p, string kind, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>?>(p.Name == provider && kind == ScimGroupKinds.Team ? Allowed : null);
    }

    [Fact]
    public async Task A_team_the_filter_doesnt_allow_is_never_pushed()
    {
        StaffTeam();

        var result = await CreateService(new StubFilter("Test", "t-other")).ProcessAsync(TeamItem(), TeamProvider(TeamServerFlavor));

        result.Success.Should().BeTrue(result.Error);
        result.Note.Should().Be("Group is outside the provisioning scope");
        _http.Requests.Should().BeEmpty("nothing about the team leaves Oluso");
        _mappings.Should().NotContain(m => m.ResourceType == ScimResourceTypes.Group);
    }

    [Fact]
    public async Task A_team_the_filter_allows_is_pushed_with_its_members()
    {
        StaffTeam();
        _http.On(HttpMethod.Get, "Groups?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Groups", 201, new JsonObject { ["id"] = "g-staff" });

        var result = await CreateService(new StubFilter("Test", "t-staff")).ProcessAsync(TeamItem(), TeamProvider(TeamServerFlavor));

        result.Success.Should().BeTrue(result.Error);
        _http.Requests.Single(r => r.Method == HttpMethod.Post).Body!["members"]!.AsArray().Should().HaveCount(2);
        _mappings.Should().ContainSingle(m => m.InternalId == "team:t-staff");
    }

    [Fact]
    public async Task A_team_the_filter_no_longer_allows_has_its_group_deleted()
    {
        StaffTeam();
        _mappings.Add(new ScimProviderResourceMapping
        {
            TenantId = "t1", ScimProviderId = "p1", ResourceType = ScimResourceTypes.Group, InternalId = "team:t-staff", RemoteId = "g-staff",
        });
        _http.On(HttpMethod.Delete, "Groups/g-staff", 204);
        var filter = new StubFilter("Test");

        var result = await CreateService(filter).ProcessAsync(TeamItem("comms:links"), TeamProvider(TeamServerFlavor));

        result.Success.Should().BeTrue(result.Error);
        _http.Requests.Should().ContainSingle().Which.Should().Match<(HttpMethod Method, string Path, JsonNode? Body)>(r =>
            r.Method == HttpMethod.Delete && r.Path == "Groups/g-staff");
        _mappings.Should().NotContain(m => m.InternalId == "team:t-staff");
    }

    [Fact]
    public async Task Filters_intersect_and_one_with_no_say_leaves_the_scope_as_it_is()
    {
        StaffTeam();
        _http.On(HttpMethod.Get, "Groups?filter=", 200, EmptyList());
        _http.On(HttpMethod.Post, "Groups", 201, new JsonObject { ["id"] = "g-staff" });
        var provider = TeamProvider(TeamServerFlavor);

        (await CreateService(new StubFilter("Test", "t-staff"), new StubFilter("Test", "t-other")).ProcessAsync(TeamItem(), provider))
            .Note.Should().Be("Group is outside the provisioning scope");
        (await CreateService(new StubFilter("Another provider")).ProcessAsync(TeamItem(), provider)).Success.Should().BeTrue();
        _http.Requests.Should().Contain(r => r.Method == HttpMethod.Post, "a filter for another provider has no say here");
    }

    /// <summary>Scripted SCIM endpoint: rules are matched in order, one-shot rules are consumed.</summary>
    private sealed class FakeScimHttp : IScimProviderHttpClient
    {
        private readonly List<Rule> _rules = new();
        public List<(HttpMethod Method, string Path, JsonNode? Body)> Requests { get; } = new();

        public void On(HttpMethod method, string pathPrefix, int status, JsonNode? body = null, bool once = false) =>
            _rules.Add(new Rule(method, pathPrefix, status, body, once));

        public void Reset()
        {
            _rules.Clear();
            Requests.Clear();
        }

        public Task<ScimRemoteResponse> SendAsync(ScimProvider provider, HttpMethod method, string relativePath, JsonNode? body = null, bool anonymous = false, CancellationToken cancellationToken = default)
        {
            Requests.Add((method, relativePath, body?.DeepClone()));
            var rule = _rules.FirstOrDefault(r => r.Method == method && relativePath.StartsWith(r.PathPrefix, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No scripted response for {method} {relativePath}");
            if (rule.Once)
                _rules.Remove(rule);

            var responseBody = rule.Body?.DeepClone();
            return Task.FromResult(new ScimRemoteResponse
            {
                Method = method.Method,
                Path = relativePath,
                StatusCode = rule.Status,
                Body = responseBody,
                RawBody = responseBody?.ToJsonString(),
                ErrorDetail = rule.Status >= 400 ? (responseBody as JsonObject)?["detail"]?.ToString() ?? $"HTTP {rule.Status}" : null,
                ScimType = (responseBody as JsonObject)?["scimType"]?.ToString(),
                DurationMs = 1,
            });
        }

        private sealed record Rule(HttpMethod Method, string PathPrefix, int Status, JsonNode? Body, bool Once);
    }

    internal sealed class InMemoryMappingStore : List<ScimProviderResourceMapping>, IScimProviderResourceMappingStore
    {
        public Task<ScimProviderResourceMapping?> GetByInternalIdAsync(string scimProviderId, string resourceType, string internalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.FirstOrDefault(m => m.ScimProviderId == scimProviderId && m.ResourceType == resourceType && m.InternalId == internalId));

        public Task<IReadOnlyDictionary<string, ScimProviderResourceMapping>> GetByInternalIdsAsync(string scimProviderId, string resourceType, IReadOnlyCollection<string> internalIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ScimProviderResourceMapping>>(
                this.Where(m => m.ScimProviderId == scimProviderId && m.ResourceType == resourceType && internalIds.Contains(m.InternalId)).ToDictionary(m => m.InternalId));

        public Task<ScimProviderResourceMapping?> GetByRemoteIdAsync(string scimProviderId, string resourceType, string remoteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.FirstOrDefault(m => m.ScimProviderId == scimProviderId && m.ResourceType == resourceType && m.RemoteId == remoteId));

        public Task<IReadOnlyList<ScimProviderResourceMapping>> GetByProviderAsync(string scimProviderId, string? resourceType, int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScimProviderResourceMapping>>(this.Where(m => m.ScimProviderId == scimProviderId).Skip(skip).Take(take).ToList());

        public Task<int> GetCountByProviderAsync(string scimProviderId, string? resourceType, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.Count(m => m.ScimProviderId == scimProviderId && (resourceType == null || m.ResourceType == resourceType)));

        public Task<IReadOnlyList<ScimProviderResourceMapping>> GetAllByProviderAsync(string scimProviderId, string resourceType, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScimProviderResourceMapping>>(this.Where(m => m.ScimProviderId == scimProviderId && m.ResourceType == resourceType).ToList());

        public Task<ScimProviderResourceMapping> CreateAsync(ScimProviderResourceMapping mapping, CancellationToken cancellationToken = default)
        {
            Add(mapping);
            return Task.FromResult(mapping);
        }

        public Task UpdateAsync(ScimProviderResourceMapping mapping, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            RemoveAll(m => m.Id == id);
            return Task.CompletedTask;
        }
    }
}
