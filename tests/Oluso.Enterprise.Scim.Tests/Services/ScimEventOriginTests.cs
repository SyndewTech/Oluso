using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Outbound;
using Oluso.Enterprise.Scim.Services;
using Oluso.Enterprise.Scim.Stores;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests.Services;

/// <summary>
/// Inbound SCIM raises the core user and role events with the SCIM client as
/// origin, and outbound provisioning doesn't push those changes back to a
/// provider linked to that client.
/// </summary>
public class ScimEventOriginTests
{
    private const string Tenant = "t1";

    private readonly List<OlusoEvent> _raised = new();
    private readonly OlusoEventService _events;
    private readonly Mock<UserManager<OlusoUser>> _userManager;
    private readonly Mock<RoleManager<OlusoRole>> _roleManager;
    private readonly Mock<IScimUserMapper> _mapper = new();
    private readonly Mock<IScimResourceMappingStore> _mappings = new();
    private readonly Mock<IScimAttributeMappingStore> _attributeMappings = new();
    private readonly Mock<IRoleStore> _roles = new();
    private readonly ScimClient _client = new() { Id = "sc1", TenantId = Tenant, Name = "Payroll" };

    public ScimEventOriginTests()
    {
        _events = new OlusoEventService(
            new IOlusoEventSink[] { new CallbackEventSink((e, _) => { _raised.Add(e); return Task.CompletedTask; }) },
            NullLogger<OlusoEventService>.Instance);

        _userManager = new Mock<UserManager<OlusoUser>>(Mock.Of<IUserStore<OlusoUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _userManager.Setup(m => m.CreateAsync(It.IsAny<OlusoUser>())).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.UpdateAsync(It.IsAny<OlusoUser>())).ReturnsAsync(IdentityResult.Success);
        _roleManager = new Mock<RoleManager<OlusoRole>>(Mock.Of<IRoleStore<OlusoRole>>(), null!, null!, null!, null!);

        _attributeMappings.Setup(s => s.GetByClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScimAttributeMapping>());
        _mappings.Setup(s => s.CreateAsync(It.IsAny<ScimResourceMapping>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScimResourceMapping m, CancellationToken _) => m);
        _mapper.Setup(m => m.ToScimUser(It.IsAny<OlusoUser>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((OlusoUser u, string _, string? _) => new ScimUser { Id = u.Id, UserName = u.UserName! });
    }

    private ScimProvisioningService Service()
    {
        var notifier = Mock.Of<IModuleAdminAssignmentNotifier>();
        var tenant = Mock.Of<ITenantContext>(t => t.TenantId == Tenant);
        var grants = new RoleGrantService(_roles.Object, notifier, Mock.Of<IOlusoUserService>(), tenant, _events);
        return new ScimProvisioningService(
            _userManager.Object, _roleManager.Object, _mapper.Object, _mappings.Object, _attributeMappings.Object,
            _roles.Object, grants, notifier, _events, Mock.Of<ITenantEmailDomainStore>(),
            NullLogger<ScimProvisioningService>.Instance);
    }

    private static readonly EventOrigin ScimOrigin = EventOrigin.Scim("sc1", "Payroll");

    [Fact]
    public async Task Creating_a_user_raises_user_registered_with_the_scim_client_as_origin()
    {
        var user = new OlusoUser { Id = "u1", TenantId = Tenant, UserName = "jdoe", Email = "jdoe@example.org" };
        _mapper.Setup(m => m.CreateFromScimUser(It.IsAny<ScimUser>(), Tenant, _client, It.IsAny<IReadOnlyList<ScimAttributeMapping>>()))
            .Returns(user);

        var result = await Service().CreateUserAsync(_client, new ScimUser { UserName = "jdoe" }, "https://idp/scim/v2");

        result.IsSuccess.Should().BeTrue();
        var registered = _raised.Should().ContainSingle().Which.Should().BeOfType<UserRegisteredEvent>().Subject;
        registered.SubjectId.Should().Be("u1");
        registered.TenantId.Should().Be(Tenant);
        registered.Email.Should().Be("jdoe@example.org");
        registered.Origin.Should().Be(ScimOrigin);
        EventOriginScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task Patching_raises_user_updated_and_deleting_raises_user_deactivated_with_the_scim_client_as_origin()
    {
        var user = new OlusoUser { Id = "u1", TenantId = Tenant, UserName = "jdoe", Email = "jdoe@example.org", IsActive = true };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);

        await Service().PatchUserAsync(_client, "u1", new ScimPatchRequest(), "https://idp/scim/v2");
        await Service().DeleteUserAsync(_client, "u1");

        _raised.Select(e => e.GetType()).Should().Equal(typeof(UserUpdatedEvent), typeof(UserDeactivatedEvent));
        _raised.Should().AllSatisfy(e => e.Origin.Should().Be(ScimOrigin));
        var deactivated = (UserDeactivatedEvent)_raised[1];
        deactivated.SubjectId.Should().Be("u1");
        deactivated.TenantId.Should().Be(Tenant);
        deactivated.Email.Should().Be("jdoe@example.org");
        user.IsActive.Should().BeFalse("SCIM delete deactivates rather than removes");
    }

    [Fact]
    public async Task Deleting_an_already_inactive_user_raises_nothing()
    {
        var user = new OlusoUser { Id = "u1", TenantId = Tenant, UserName = "jdoe", IsActive = false };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);

        (await Service().DeleteUserAsync(_client, "u1")).IsSuccess.Should().BeTrue();

        _raised.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false, typeof(UserDeactivatedEvent))]
    [InlineData(false, true, typeof(UserReactivatedEvent))]
    public async Task Patching_active_raises_the_matching_lifecycle_event_after_user_updated(
        bool wasActive, bool becomesActive, Type expected)
    {
        var user = new OlusoUser { Id = "u1", TenantId = Tenant, UserName = "jdoe", IsActive = wasActive };
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        _mapper.Setup(m => m.ApplyPatch(user, It.IsAny<ScimPatchRequest>(), _client, It.IsAny<IReadOnlyList<ScimAttributeMapping>>()))
            .Callback(() => user.IsActive = becomesActive);

        await Service().PatchUserAsync(_client, "u1", new ScimPatchRequest(), "https://idp/scim/v2");

        _raised.Select(e => e.GetType()).Should().Equal(typeof(UserUpdatedEvent), expected);
        _raised.Should().AllSatisfy(e => e.Origin.Should().Be(ScimOrigin));
    }

    [Fact]
    public async Task Adding_a_group_member_raises_user_role_assigned_with_the_scim_client_as_origin()
    {
        var role = new OlusoRole { Id = "r1", Name = "Staff", TenantId = Tenant };
        var member = new OlusoUser { Id = "u1", TenantId = Tenant, UserName = "jdoe" };
        _roleManager.Setup(m => m.FindByIdAsync("r1")).ReturnsAsync(role);
        _roleManager.Setup(m => m.UpdateAsync(role)).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(member);
        _roles.Setup(s => s.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OlusoRole> { role });
        _roles.Setup(s => s.GetUserRolesInTenantAsync("u1", Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OlusoRole>());
        _roles.Setup(s => s.GetUsersByRoleInTenantAsync("r1", Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoleUserInfo>());

        var patch = new ScimPatchRequest
        {
            Operations =
            {
                new ScimPatchOperation
                {
                    Op = "add",
                    Path = "members",
                    Value = JsonSerializer.SerializeToElement(new[] { new { value = "u1" } }),
                },
            },
        };
        await Service().PatchGroupAsync(_client, "r1", patch, "https://idp/scim/v2");

        var assigned = _raised.Should().ContainSingle().Which.Should().BeOfType<UserRoleAssignedEvent>().Subject;
        assigned.SubjectId.Should().Be("u1");
        assigned.RoleId.Should().Be("r1");
        assigned.Origin.Should().Be(ScimOrigin);
    }

    // ------------------------------------------------------------- outbound

    private static ScimProvider Provider(string id, string? linkedScimClientId) => new()
    {
        Id = id, TenantId = Tenant, Name = id, BaseUrl = "https://scim.example.org", SyncUsers = true, SyncGroups = true,
        LinkedScimClientId = linkedScimClientId,
    };

    private (ScimOutboundEventSink Sink, List<ScimProviderOutboxItem> Queued) Outbound()
    {
        var queued = new List<ScimProviderOutboxItem>();
        var providers = new Mock<IScimProviderStore>();
        providers.Setup(p => p.GetEnabledByTenantAsync(Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScimProvider> { Provider("linked", "sc1"), Provider("other", null) });
        var outbox = new Mock<IScimProviderOutboxStore>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<ScimProviderOutboxItem>(), It.IsAny<CancellationToken>()))
            .Callback<ScimProviderOutboxItem, CancellationToken>((i, _) => queued.Add(i))
            .ReturnsAsync((ScimProviderOutboxItem i, CancellationToken _) => i);
        var enqueuer = new ScimProviderEnqueuer(providers.Object, outbox.Object, NullLogger<ScimProviderEnqueuer>.Instance);
        var sink = new ScimOutboundEventSink(enqueuer, _roles.Object, Mock.Of<ITeamStore>(), Mock.Of<IOlusoUserService>(),
            NullLogger<ScimOutboundEventSink>.Instance);
        return (sink, queued);
    }

    [Fact]
    public async Task Outbound_skips_the_provider_linked_to_the_inbound_client_the_change_came_from()
    {
        var (sink, queued) = Outbound();

        await sink.HandleAsync(new UserUpdatedEvent { TenantId = Tenant, SubjectId = "u1", Origin = ScimOrigin });

        queued.Should().ContainSingle().Which.ScimProviderId.Should().Be("other");
    }

    [Fact]
    public async Task Outbound_pushes_to_the_linked_provider_when_another_inbound_client_or_actor_made_the_change()
    {
        var (sink, queued) = Outbound();

        await sink.HandleAsync(new UserUpdatedEvent { TenantId = Tenant, SubjectId = "u1", Origin = EventOrigin.Scim("sc2") });
        await sink.HandleAsync(new UserUpdatedEvent { TenantId = Tenant, SubjectId = "u1", Origin = EventOrigin.Admin("admin-1") });

        queued.Select(i => i.ScimProviderId).Should().Equal("linked", "other", "linked", "other");
    }

    [Fact]
    public async Task Outbound_queues_the_user_and_the_role_group_for_a_core_role_event()
    {
        _roles.Setup(s => s.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OlusoRole> { new() { Id = "r1", Name = "Staff", TenantId = Tenant } });
        var (sink, queued) = Outbound();

        await sink.HandleAsync(new UserRoleAssignedEvent
        {
            TenantId = Tenant, SubjectId = "u1", RoleId = "r1", RoleName = "Staff", Origin = ScimOrigin,
        });

        queued.Select(i => (i.ScimProviderId, i.ResourceType, i.InternalId)).Should().Equal(
            ("other", ScimResourceTypes.User, "u1"),
            ("other", ScimResourceTypes.Group, ScimGroupKinds.Key(ScimGroupKinds.Role, "r1")));
    }

    [Fact]
    public async Task Outbound_syncs_admin_changes_to_every_provider_from_the_core_events()
    {
        _roles.Setup(s => s.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OlusoRole> { new() { Id = "r1", Name = "Staff", TenantId = Tenant } });
        var (sink, queued) = Outbound();
        var admin = EventOrigin.Admin("admin-1", "Ada");

        await sink.HandleAsync(new UserRegisteredEvent { TenantId = Tenant, SubjectId = "u1", Email = "u1@example.org", Origin = admin });
        await sink.HandleAsync(new UserRoleRemovedEvent { TenantId = Tenant, SubjectId = "u1", RoleId = "r1", RoleName = "Staff", Origin = admin });
        await sink.HandleAsync(new UserDeletedEvent { TenantId = Tenant, SubjectId = "u1", Origin = admin });

        queued.Select(i => (i.ScimProviderId, i.ResourceType, i.InternalId, i.Operation)).Should().Equal(
            ("linked", ScimResourceTypes.User, "u1", ScimOutboxOperations.Upsert),
            ("other", ScimResourceTypes.User, "u1", ScimOutboxOperations.Upsert),
            ("linked", ScimResourceTypes.User, "u1", ScimOutboxOperations.Upsert),
            ("other", ScimResourceTypes.User, "u1", ScimOutboxOperations.Upsert),
            ("linked", ScimResourceTypes.Group, ScimGroupKinds.Key(ScimGroupKinds.Role, "r1"), ScimOutboxOperations.Upsert),
            ("other", ScimResourceTypes.Group, ScimGroupKinds.Key(ScimGroupKinds.Role, "r1"), ScimOutboxOperations.Upsert),
            ("linked", ScimResourceTypes.User, "u1", ScimOutboxOperations.Remove),
            ("other", ScimResourceTypes.User, "u1", ScimOutboxOperations.Remove));
        queued.Should().AllSatisfy(i => i.Reason.Should().StartWith("event:user."));
    }

    [Fact]
    public async Task Outbound_pushes_deactivation_and_reactivation_as_upserts_not_removals()
    {
        var (sink, queued) = Outbound();
        var admin = EventOrigin.Admin("admin-1");

        await sink.HandleAsync(new UserDeactivatedEvent { TenantId = Tenant, SubjectId = "u1", Origin = admin });
        await sink.HandleAsync(new UserReactivatedEvent { TenantId = Tenant, SubjectId = "u1", Origin = admin });

        queued.Should().HaveCount(4).And.AllSatisfy(i =>
        {
            i.ResourceType.Should().Be(ScimResourceTypes.User);
            i.Operation.Should().Be(ScimOutboxOperations.Upsert);
        });
    }
}
