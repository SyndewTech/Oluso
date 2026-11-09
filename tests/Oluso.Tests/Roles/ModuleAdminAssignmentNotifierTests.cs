using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Controllers.Service;
using Oluso.Admin.Dto.Users;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Notifications;
using Oluso.Core.Realtime;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Oluso.EntityFramework.Stores;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// When a role carrying <c>hr.administration</c> is granted in a tenant, the
/// users who already hold exactly that permission in that tenant are told —
/// once, through the event (webhook + email workflow) and in-app — and nobody
/// else: not admins of another tenant, not <c>*</c> / <c>hr.*</c> holders, not
/// the new admin or the admin who granted it.
/// </summary>
public class ModuleAdminAssignmentNotifierTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string Permission = "hr.administration";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly RoleStore _store;
    private readonly IdentityUserService _userService;
    private readonly OrganizationMembershipStore _orgMemberships;
    private readonly List<ModuleAdminPermissionAssignedEvent> _raised = new();
    private readonly List<ModuleAdminPermissionRemovedEvent> _removed = new();
    private readonly Mock<IRealtimeBroadcaster> _realtime = new();
    private readonly Mock<IPushSender> _push = new();
    private readonly ModuleAdminAssignmentNotifier _notifier;
    private readonly RoleGrantService _grants;

    private OlusoRole _hrAdmin = null!;
    private OlusoRole _superAdmin = null!;
    private OlusoRole _hrWildcard = null!;
    private OlusoRole _orgAdmin = null!;
    private OlusoUser _adminA = null!;
    private OlusoUser _adminB = null!;
    private OlusoUser _super = null!;
    private OlusoUser _wildcard = null!;
    private OlusoUser _actor = null!;
    private OlusoUser _target = null!;
    private OlusoUser _orgOwner = null!;

    public ModuleAdminAssignmentNotifierTests()
    {
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _roleManager = IdentityTestFactory.CreateRoleManager(_context);
        var userManager = IdentityTestFactory.CreateUserManager(_context, _tenant);
        _store = new RoleStore(_roleManager, userManager, _context, _tenant);
        _orgMemberships = new OrganizationMembershipStore(_context);

        var signInManager = new SignInManager<OlusoUser>(
            userManager,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<OlusoUser>>(),
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<OlusoUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<OlusoUser>>());
        _userService = new IdentityUserService(
            userManager, signInManager, _tenant, Mock.Of<ITenantSettingsProvider>(), _context,
            NullLogger<IdentityUserService>.Instance);

        var events = new Mock<IOlusoEventService>();
        events.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .Callback((OlusoEvent e, CancellationToken _) =>
            {
                if (e is ModuleAdminPermissionAssignedEvent m) _raised.Add(m);
                if (e is ModuleAdminPermissionRemovedEvent r) _removed.Add(r);
            })
            .Returns(Task.CompletedTask);

        _notifier = new ModuleAdminAssignmentNotifier(
            _store, new TenantStore(_context), _userService, events.Object,
            NullLogger<ModuleAdminAssignmentNotifier>.Instance,
            _orgMemberships, _realtime.Object, _push.Object);
        _grants = new RoleGrantService(_store, _notifier, _userService, _tenant, events.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task SeedAsync()
    {
        _tenant.TenantId = null;
        _context.Organizations.AddRange(
            new Organization { Id = "org-a", Name = "Org A", Slug = "org-a" },
            new Organization { Id = "org-b", Name = "Org B", Slug = "org-b" });
        _context.Tenants.AddRange(
            new Tenant { Id = TenantA, Name = TenantA, Identifier = TenantA, OrganizationId = "org-a" },
            new Tenant { Id = TenantB, Name = TenantB, Identifier = TenantB, OrganizationId = "org-b" });
        await _context.SaveChangesAsync();

        _hrAdmin = await RoleAsync("HRAdmin", null, Permission, "hr.employees.read");
        _superAdmin = await RoleAsync(GlobalRoles.SuperAdmin, null, "*");
        _hrWildcard = await RoleAsync("HRAll", TenantA, "hr.*");
        _orgAdmin = await RoleAsync(EffectiveRoleResolver.OrgAdminRoleName, null, "organizations.*");

        _adminA = await UserAsync(TenantA, "admin-a", "HR Admin A");
        _adminB = await UserAsync(TenantB, "admin-b", "HR Admin B");
        _super = await UserAsync(TenantA, "root", "Root");
        _wildcard = await UserAsync(TenantA, "wild", "Wild Card");
        _actor = await UserAsync(TenantA, "actor", "Acting Admin");
        _target = await UserAsync(TenantA, "target", "New Admin");
        _orgOwner = await UserAsync(TenantA, "owner", "Org Owner");

        await _store.AssignToUserInTenantAsync(_hrAdmin.Id, _adminA.Id, TenantA, "seed");
        await _store.AssignToUserInTenantAsync(_hrAdmin.Id, _adminB.Id, TenantB, "seed");
        await _store.AssignToUserInTenantAsync(_hrAdmin.Id, _actor.Id, TenantA, "seed");
        await _store.AssignToUserInTenantAsync(_superAdmin.Id, _super.Id, TenantA, "seed");
        await _store.AssignToUserInTenantAsync(_hrWildcard.Id, _wildcard.Id, TenantA, "seed");
        await _orgMemberships.CreateAsync(new OrganizationMembership
        {
            Id = Guid.NewGuid().ToString(), OrganizationId = "org-a", UserId = _orgOwner.Id, Role = OrganizationRole.Owner,
        });

        _context.ChangeTracker.Clear();
        _tenant.TenantId = TenantA;
    }

    private async Task<OlusoRole> RoleAsync(string name, string? tenantId, params string[] permissions)
    {
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, name, tenantId, isSystemRole: tenantId == null);
        foreach (var p in permissions) await _store.AddRoleClaimAsync(role.Id, OlusoRole.PermissionClaimType, p);
        return role;
    }

    private Task<OlusoUser> UserAsync(string tenantId, string id, string name) =>
        IdentityTestFactory.CreateUserAsync(_context, tenantId, $"{id}@x.test", displayName: name);

    private RoleGrantActor Actor => new(_actor.Id, "Acting Admin");

    private void BroadcastWasSentTo(string userId, Times times) =>
        _realtime.Verify(r => r.BroadcastToUserAsync(
            TenantA, userId, ModuleAdminAssignmentNotifier.ActionCenterChannel, "security.module_admin_assigned",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task ExistingHrAdminsInTheTenant_AreNotified_AndNobodyElse()
    {
        await SeedAsync();

        (await _grants.GrantAsync(_hrAdmin.Id, _target.Id, TenantA, Actor)).Should().BeTrue();

        var evt = _raised.Should().ContainSingle().Subject;
        evt.TenantId.Should().Be(TenantA);
        evt.ModuleAdminPermission.Should().Be(Permission);
        evt.TargetUserId.Should().Be(_target.Id);
        evt.TargetUserName.Should().Be("New Admin");
        evt.TargetUserEmail.Should().Be("target@x.test");
        evt.AdminUserId.Should().Be(_actor.Id);
        evt.AdminUserName.Should().Be("Acting Admin");
        evt.RoleName.Should().Be("HRAdmin");
        // Not B's admin, not SuperAdmin (*), not hr.* holder, not target, not actor.
        evt.ExistingModuleAdminIds.Should().Equal(_adminA.Id);
        evt.ExistingModuleAdminEmails.Should().Equal("admin-a@x.test");

        BroadcastWasSentTo(_adminA.Id, Times.Once());
        _realtime.Verify(r => r.BroadcastToUserAsync(
            It.IsAny<string>(), It.Is<string>(u => u != _adminA.Id), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
        _push.Verify(p => p.SendAsync(_adminA.Id, It.IsAny<string>(), It.IsAny<string>(),
            "security.module_admin_assigned", It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminsOfAnotherTenant_AreNotNotified()
    {
        await SeedAsync();
        _tenant.TenantId = TenantB;

        await _grants.GrantAsync(_hrAdmin.Id, _target.Id, TenantB, Actor);

        _raised.Should().ContainSingle().Which.ExistingModuleAdminIds.Should().Equal(_adminB.Id);
    }

    [Fact]
    public async Task WildcardHolders_CountOnlyWhenTheyHoldTheExactPermission()
    {
        await SeedAsync();
        (await _notifier.FindModuleAdminIdsAsync(TenantA, Permission))
            .Should().BeEquivalentTo(new[] { _adminA.Id, _actor.Id });

        await _store.AddRoleClaimAsync(_superAdmin.Id, OlusoRole.PermissionClaimType, Permission);
        (await _notifier.FindModuleAdminIdsAsync(TenantA, Permission)).Should().Contain(_super.Id);
    }

    [Fact]
    public async Task OrganizationAdmins_CountOnlyWhenTheOrgAdminRoleCarriesThePermission()
    {
        await SeedAsync();
        (await _notifier.FindModuleAdminIdsAsync(TenantA, Permission)).Should().NotContain(_orgOwner.Id);

        await _store.AddRoleClaimAsync(_orgAdmin.Id, OlusoRole.PermissionClaimType, Permission);
        (await _notifier.FindModuleAdminIdsAsync(TenantA, Permission)).Should().Contain(_orgOwner.Id);
        (await _notifier.FindModuleAdminIdsAsync(TenantB, Permission)).Should().NotContain(_orgOwner.Id);
    }

    [Fact]
    public async Task GrantingAgain_OrARoleWithoutAdministration_RaisesNothingMore()
    {
        await SeedAsync();
        var viewer = await RoleAsync("HRViewer", TenantA, "hr.employees.read");

        await _grants.GrantAsync(_hrAdmin.Id, _target.Id, TenantA, Actor);
        (await _grants.GrantAsync(_hrAdmin.Id, _target.Id, TenantA, Actor)).Should().BeFalse();
        await _grants.GrantAsync(viewer.Id, _target.Id, TenantA, Actor);

        _raised.Should().ContainSingle();
    }

    [Fact]
    public async Task DirectStoreWrites_AsSeedingDoes_RaiseNothing()
    {
        await SeedAsync();

        await _store.AssignToUserInTenantAsync(_hrAdmin.Id, _target.Id, TenantA, "seed");

        _raised.Should().BeEmpty();
        _realtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdminUsersApi_SetRoles_RaisesOnce_ForTheNewGrantOnly()
    {
        await SeedAsync();
        var users = new UsersController(
            _tenant, _userService, Mock.Of<IOlusoEventService>(), _store, new TenantMembershipStore(_context),
            new TenantStore(_context), _notifier, NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", _actor.Id), new Claim("name", "Acting Admin"),
                        new Claim("tenant_id", TenantA), new Claim("super_admin", "true"),
                    }, "test")),
                },
            },
        };

        (await users.SetUserRoles(_target.Id, new SetUserRolesRequest { RoleIds = { _hrAdmin.Id } }, default))
            .Should().BeOfType<OkObjectResult>();
        (await users.SetUserRoles(_target.Id, new SetUserRolesRequest { RoleIds = { _hrAdmin.Id } }, default))
            .Should().BeOfType<OkObjectResult>();

        _raised.Should().ContainSingle().Which.ExistingModuleAdminIds.Should().Equal(_adminA.Id);
    }

    [Fact]
    public async Task ServiceApi_SetAndAddRole_RaiseOnce()
    {
        await SeedAsync();
        var service = new ServiceUsersController(
            _tenant, _userService, _store, Mock.Of<IOlusoEventService>(), _grants, _notifier,
            NullLogger<ServiceUsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("client_id", "svc-a"), new Claim("tenant_id", TenantA),
                        new Claim("scope", $"{ServiceScopes.UsersManageRoles} {ServiceScopes.UsersRead}"),
                    }, "test")),
                },
            },
        };

        (await service.SetUserRoles(_target.Id, new ServiceSetUserRolesRequest { RoleIds = { _hrAdmin.Id } }, default))
            .Should().BeOfType<OkObjectResult>();
        await service.AddUserRole(_target.Id, _hrAdmin.Id, default);

        var evt = _raised.Should().ContainSingle().Subject;
        evt.AdminUserId.Should().Be("svc-a");
        // A service client is the actor here, so every existing admin is told.
        evt.ExistingModuleAdminIds.Should().BeEquivalentTo(new[] { _adminA.Id, _actor.Id });
    }

    // ------------------------------------------------------------- Removal

    private async Task GiveTargetHrAdminAsync()
    {
        await _store.AssignToUserInTenantAsync(_hrAdmin.Id, _target.Id, TenantA, "seed");
    }

    [Fact]
    public async Task Removal_NotifiesTheRemainingHrAdminsInTheTenant_AndNobodyElse()
    {
        await SeedAsync();
        await GiveTargetHrAdminAsync();

        (await _grants.RevokeAsync(_hrAdmin.Id, _target.Id, TenantA, Actor)).Should().BeTrue();

        var evt = _removed.Should().ContainSingle().Subject;
        evt.WebhookEventType.Should().Be("security.module_admin_removed");
        evt.ModuleAdminPermission.Should().Be(Permission);
        evt.TargetUserId.Should().Be(_target.Id);
        evt.AdminUserId.Should().Be(_actor.Id);
        // Remaining admins after the removal: not the removed user, not the
        // actor, not B's admin, not */hr.* holders.
        evt.ExistingModuleAdminIds.Should().Equal(_adminA.Id);
        evt.ExistingModuleAdminEmails.Should().Equal("admin-a@x.test");
        _raised.Should().BeEmpty();
        _realtime.Verify(r => r.BroadcastToUserAsync(
            TenantA, _adminA.Id, ModuleAdminAssignmentNotifier.ActionCenterChannel, "security.module_admin_removed",
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Removal_IsSilent_WhileAnotherRoleStillCarriesThePermission()
    {
        await SeedAsync();
        await GiveTargetHrAdminAsync();
        var second = await RoleAsync("HRLead", TenantA, Permission);
        await _store.AssignToUserInTenantAsync(second.Id, _target.Id, TenantA, "seed");

        await _grants.RevokeAsync(_hrAdmin.Id, _target.Id, TenantA, Actor);
        _removed.Should().BeEmpty();

        await _grants.RevokeAsync(second.Id, _target.Id, TenantA, Actor);
        _removed.Should().ContainSingle().Which.RoleName.Should().Be("HRLead");
    }

    [Fact]
    public async Task RevokingARoleNotHeld_OrDirectStoreRemoval_RaisesNothing()
    {
        await SeedAsync();

        (await _grants.RevokeAsync(_hrAdmin.Id, _target.Id, TenantA, Actor)).Should().BeFalse();
        await GiveTargetHrAdminAsync();
        await _store.RemoveFromUserInTenantAsync(_hrAdmin.Id, _target.Id, TenantA);

        _removed.Should().BeEmpty();
    }

    [Fact]
    public async Task AdminUsersApi_SetRoles_NotifiesRemovalOnce()
    {
        await SeedAsync();
        await GiveTargetHrAdminAsync();
        var users = new UsersController(
            _tenant, _userService, Mock.Of<IOlusoEventService>(), _store, new TenantMembershipStore(_context),
            new TenantStore(_context), _notifier, NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", _actor.Id), new Claim("name", "Acting Admin"),
                        new Claim("tenant_id", TenantA), new Claim("super_admin", "true"),
                    }, "test")),
                },
            },
        };

        (await users.SetUserRoles(_target.Id, new SetUserRolesRequest(), default)).Should().BeOfType<OkObjectResult>();
        (await users.SetUserRoles(_target.Id, new SetUserRolesRequest(), default)).Should().BeOfType<OkObjectResult>();

        _removed.Should().ContainSingle().Which.ExistingModuleAdminIds.Should().Equal(_adminA.Id);
    }

    [Fact]
    public async Task ServiceApi_RemoveAndSetRoles_NotifyRemovalOnce()
    {
        await SeedAsync();
        await GiveTargetHrAdminAsync();
        var service = new ServiceUsersController(
            _tenant, _userService, _store, Mock.Of<IOlusoEventService>(), _grants, _notifier,
            NullLogger<ServiceUsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("client_id", "svc-a"), new Claim("tenant_id", TenantA),
                        new Claim("scope", $"{ServiceScopes.UsersManageRoles} {ServiceScopes.UsersRead}"),
                    }, "test")),
                },
            },
        };

        (await service.RemoveUserRole(_target.Id, _hrAdmin.Id, default)).Should().BeOfType<NoContentResult>();
        (await service.SetUserRoles(_target.Id, new ServiceSetUserRolesRequest(), default)).Should().BeOfType<OkObjectResult>();

        _removed.Should().ContainSingle().Which.AdminUserId.Should().Be("svc-a");
    }

    [Fact]
    public async Task OrganizationAdminChange_NotifiesInTheOrganizationsTenants_WhenOrgAdminCarriesThePermission()
    {
        await SeedAsync();

        // OrgAdmin carries no administration permission: membership changes are silent.
        await _notifier.NotifyOrganizationAdminChangedAsync("org-a", _orgOwner.Id, removed: true, Actor);
        _removed.Should().BeEmpty();

        await _store.AddRoleClaimAsync(_orgAdmin.Id, OlusoRole.PermissionClaimType, Permission);
        var membership = await _orgMemberships.GetByUserAndOrganizationAsync(_orgOwner.Id, "org-a");
        membership!.Role = OrganizationRole.Member;
        await _orgMemberships.UpdateAsync(membership);

        await _notifier.NotifyOrganizationAdminChangedAsync("org-a", _orgOwner.Id, removed: true, Actor);

        var evt = _removed.Should().ContainSingle().Subject;
        evt.TenantId.Should().Be(TenantA);
        evt.TargetUserId.Should().Be(_orgOwner.Id);
        evt.ExistingModuleAdminIds.Should().Equal(_adminA.Id);
    }
}
