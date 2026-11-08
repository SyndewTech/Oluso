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
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers.Service;
using Oluso.Core;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Oluso.EntityFramework.Stores;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// Service API (machine-to-machine) guards: role permissions a service client
/// may write, roles reported for a user, reserved-role revocation, and email
/// verification when an email changes.
/// </summary>
public class ServiceApiSecurityTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly RoleStore _store;
    private readonly IdentityUserService _userService;
    private readonly CapturedEvents _captured = new();

    public ServiceApiSecurityTests()
    {
        PermissionRegistry.Instance.RegisterPermissions(AdminPermissions.GetCorePermissions());

        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _roleManager = IdentityTestFactory.CreateRoleManager(_context);
        var userManager = IdentityTestFactory.CreateUserManager(_context, _tenant);
        _store = new RoleStore(_roleManager, userManager, _context, _tenant);

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
            NullLogger<IdentityUserService>.Instance, services: _captured.Services);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static ControllerContext ServiceClient(string tenantId, params string[] scopes) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("client_id", "svc"),
                new Claim("tenant_id", tenantId),
                new Claim("scope", string.Join(' ', new[] { ServiceScopes.ServiceApi }.Concat(scopes))),
            }, "test")),
        },
    };

    private ServiceUsersController Users(string tenantId)
    {
        _tenant.TenantId = tenantId;
        return new ServiceUsersController(
            _tenant, _userService, _store, Mock.Of<IOlusoEventService>(),
            new RoleGrantService(_store, Mock.Of<IModuleAdminAssignmentNotifier>(), _userService, _tenant, Mock.Of<IOlusoEventService>()),
            Mock.Of<IModuleAdminAssignmentNotifier>(), NullLogger<ServiceUsersController>.Instance)
        {
            ControllerContext = ServiceClient(tenantId,
                ServiceScopes.UsersRead, ServiceScopes.UsersWrite, ServiceScopes.UsersManageRoles),
        };
    }

    private ServiceRolesController Roles(string tenantId)
    {
        _tenant.TenantId = tenantId;
        return new ServiceRolesController(
            _tenant, _store, Mock.Of<IOlusoEventService>(), NullLogger<ServiceRolesController>.Instance)
        {
            ControllerContext = ServiceClient(tenantId, ServiceScopes.RolesRead, ServiceScopes.RolesWrite),
        };
    }

    private static T Ok<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<T>().Subject;

    private Task<List<(string RoleId, string TenantId)>> AssignmentsAsync(string userId) =>
        _context.UserRoles.Where(ur => ur.UserId == userId)
            .Select(ur => new ValueTuple<string, string>(ur.RoleId, ur.TenantId))
            .ToListAsync();

    private async Task<List<string>> PermissionsOfAsync(string roleId) =>
        (await _store.GetRoleClaimsAsync(roleId))
            .Where(c => c.Type == OlusoRole.PermissionClaimType)
            .Select(c => c.Value)
            .ToList();

    // ---------------------------------------------------------------- role permissions

    [Theory]
    [InlineData(AdminPermissions.TenantsWrite)]          // RequiresSuperAdmin
    [InlineData(AdminPermissions.PlatformClientsWrite)]  // RequiresSuperAdmin
    [InlineData(AdminPermissions.OrganizationsManageMembers)] // organization level
    [InlineData(AdminPermissions.TenantsRead)]           // organization level
    public async Task CreateRole_RefusesPermissionsAboveAServiceClient(string permission)
    {
        var result = await Roles(TenantA).CreateRole(new ServiceCreateRoleRequest
        {
            Name = "Escalated",
            Permissions = new() { permission },
        }, default);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await _context.Roles.IgnoreQueryFilters().AnyAsync(r => r.Name == "Escalated")).Should().BeFalse();
    }

    [Fact]
    public async Task CreateRole_RefusesModulePermissions()
    {
        PermissionRegistry.Instance.RegisterPermissions(new[]
        {
            new PermissionDefinition("svcapitest.module.manage", "Manage", "Module", "Module permission"),
        }, featureKey: "svcapitest");

        var result = await Roles(TenantA).CreateRole(new ServiceCreateRoleRequest
        {
            Name = "ModuleRole",
            Permissions = new() { "svcapitest.module.manage" },
        }, default);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateRole_AcceptsCoreTenantPermissions()
    {
        var result = await Roles(TenantA).CreateRole(new ServiceCreateRoleRequest
        {
            Name = "Support",
            Permissions = new() { AdminPermissions.UsersRead, AdminPermissions.UsersWrite },
        }, default);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Which.Value.Should().BeOfType<ServiceRoleDto>().Subject;
        (await PermissionsOfAsync(created.Id)).Should().BeEquivalentTo(AdminPermissions.UsersRead, AdminPermissions.UsersWrite);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("super_admin")]
    [InlineData("entitlements")]
    public async Task CreateRole_RefusesProtectedClaimTypes(string claimType)
    {
        var result = await Roles(TenantA).CreateRole(new ServiceCreateRoleRequest
        {
            Name = "Sneaky",
            Claims = new() { new ServiceRoleClaimDto { Type = claimType, Value = AdminPermissions.TenantsWrite } },
        }, default);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await _context.Roles.IgnoreQueryFilters().AnyAsync(r => r.Name == "Sneaky")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRole_RefusesEscalation_AndLeavesTheRoleUntouched()
    {
        _tenant.TenantId = null;
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Support", TenantA);
        await _store.AddRoleClaimAsync(role.Id, OlusoRole.PermissionClaimType, AdminPermissions.UsersRead);
        _context.ChangeTracker.Clear();

        var result = await Roles(TenantA).UpdateRole(role.Id, new ServiceUpdateRoleRequest
        {
            Name = "Renamed",
            Permissions = new() { AdminPermissions.TenantsDelete },
        }, default);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await _context.Roles.IgnoreQueryFilters().SingleAsync(r => r.Id == role.Id)).Name.Should().Be("Support");
        (await PermissionsOfAsync(role.Id)).Should().Equal(AdminPermissions.UsersRead);
    }

    [Fact]
    public async Task UpdateRole_RefusesProtectedClaims_AndKeepsEntitlements()
    {
        _tenant.TenantId = null;
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Support", TenantA);
        await _store.AddRoleClaimAsync(role.Id, OlusoRole.EntitlementClaimType, "reports");
        _context.ChangeTracker.Clear();

        var refused = await Roles(TenantA).UpdateRole(role.Id, new ServiceUpdateRoleRequest
        {
            Claims = new() { new ServiceRoleClaimDto { Type = "permission", Value = AdminPermissions.TenantsWrite } },
        }, default);
        refused.Result.Should().BeOfType<BadRequestObjectResult>();

        var replaced = await Roles(TenantA).UpdateRole(role.Id, new ServiceUpdateRoleRequest
        {
            Claims = new() { new ServiceRoleClaimDto { Type = "department", Value = "support" } },
        }, default);
        replaced.Result.Should().BeOfType<OkObjectResult>();

        var claims = (await _store.GetRoleClaimsAsync(role.Id)).Select(c => (c.Type, c.Value)).ToList();
        claims.Should().BeEquivalentTo(new[] { (OlusoRole.EntitlementClaimType, "reports"), ("department", "support") });
    }

    // ---------------------------------------------------------------- roles in user responses

    [Fact]
    public async Task UserResponses_OnlyReportRolesHeldInTheClientsTenant()
    {
        // Home A with a role there; member of the platform tenant. A client of
        // the platform tenant must not see A's role — the ambient role lookup
        // falls back to the home tenant at platform scope.
        _tenant.TenantId = null;
        var viewerA = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", TenantA);
        var member = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "member@a.test");
        _context.TenantMemberships.Add(new TenantMembership { TenantId = PlatformConstants.TenantId, UserId = member.Id });
        await _context.SaveChangesAsync();
        await _store.AssignToUserInTenantAsync(viewerA.Id, member.Id, TenantA, "admin-a");
        _context.ChangeTracker.Clear();

        var service = Users(PlatformConstants.TenantId);

        var detail = Ok(await service.GetUser(member.Id, default));
        detail.Roles.Should().BeEmpty();
        detail.Claims.Where(c => c.Type == ClaimTypes.Role).Should().BeEmpty();

        Ok(await service.GetUsers()).Items.Single(u => u.Id == member.Id).Roles.Should().BeEmpty();

        // In A, the same user reports A's role.
        _context.ChangeTracker.Clear();
        var inA = Users(TenantA);
        Ok(await inA.GetUser(member.Id, default)).Roles.Should().Equal("Viewer");
        Ok(await inA.GetUserByEmail("member@a.test", default)).Roles.Should().Equal("Viewer");
        Ok(await inA.GetUsers()).Items.Single(u => u.Id == member.Id).Roles.Should().Equal("Viewer");
    }

    [Fact]
    public async Task UserList_ReportsRolesOfTheNamedTenant_NotTheAmbientOne()
    {
        _tenant.TenantId = null;
        var viewerA = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", TenantA);
        var editorB = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Editor", TenantB);
        var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "both@a.test");
        _context.TenantMemberships.Add(new TenantMembership { TenantId = TenantB, UserId = user.Id });
        await _context.SaveChangesAsync();
        await _store.AssignToUserInTenantAsync(viewerA.Id, user.Id, TenantA, "admin");
        await _store.AssignToUserInTenantAsync(editorB.Id, user.Id, TenantB, "admin");
        _context.ChangeTracker.Clear();

        _tenant.TenantId = TenantA;
        var result = await _userService.GetUsersAsync(new UsersQuery { TenantId = TenantB });

        result.Users.Single(u => u.Id == user.Id).Roles.Should().Equal("Editor");
    }

    // ---------------------------------------------------------------- reserved roles

    private async Task<(OlusoUser User, OlusoRole Admin, OlusoRole Viewer)> SeedReservedHolderAsync()
    {
        _tenant.TenantId = null;
        var admin = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Admin", null);
        var viewer = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", TenantA);
        var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "admin@a.test");
        await _store.AssignToUserInTenantAsync(admin.Id, user.Id, TenantA, "admin");
        await _store.AssignToUserInTenantAsync(viewer.Id, user.Id, TenantA, "admin");
        _context.ChangeTracker.Clear();
        return (user, admin, viewer);
    }

    [Fact]
    public async Task RemoveUserRole_RefusesReservedRoles()
    {
        var (user, admin, _) = await SeedReservedHolderAsync();

        var result = await Users(TenantA).RemoveUserRole(user.Id, admin.Id, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await AssignmentsAsync(user.Id)).Should().Contain((admin.Id, TenantA));
    }

    [Fact]
    public async Task RemoveUserRole_StillRemovesOrdinaryRoles()
    {
        var (user, admin, viewer) = await SeedReservedHolderAsync();

        (await Users(TenantA).RemoveUserRole(user.Id, viewer.Id, default)).Should().BeOfType<NoContentResult>();

        (await AssignmentsAsync(user.Id)).Should().BeEquivalentTo(new[] { (admin.Id, TenantA) });
    }

    [Fact]
    public async Task SetUserRoles_KeepsHeldReservedRoles()
    {
        var (user, admin, _) = await SeedReservedHolderAsync();

        (await Users(TenantA).SetUserRoles(user.Id, new ServiceSetUserRolesRequest(), default))
            .Should().BeOfType<OkObjectResult>();

        (await AssignmentsAsync(user.Id)).Should().BeEquivalentTo(new[] { (admin.Id, TenantA) },
            "the reserved role is outside a service client's reach; only Viewer is removed");
    }

    [Fact]
    public async Task SetUserRoles_AcceptsAHeldReservedRoleInTheList_ButNeverGrantsOne()
    {
        var (user, admin, viewer) = await SeedReservedHolderAsync();

        (await Users(TenantA).SetUserRoles(user.Id, new ServiceSetUserRolesRequest { RoleIds = { admin.Id, viewer.Id } }, default))
            .Should().BeOfType<OkObjectResult>();
        (await AssignmentsAsync(user.Id)).Should().BeEquivalentTo(new[] { (admin.Id, TenantA), (viewer.Id, TenantA) });

        _context.ChangeTracker.Clear();
        var other = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "plain@a.test");
        _context.ChangeTracker.Clear();
        (await Users(TenantA).SetUserRoles(other.Id, new ServiceSetUserRolesRequest { RoleIds = { admin.Id } }, default))
            .Should().BeOfType<BadRequestObjectResult>();
        (await Users(TenantA).AddUserRole(other.Id, admin.Id, default)).Should().BeOfType<BadRequestObjectResult>();
        (await AssignmentsAsync(other.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task RoleIdsOfAnotherTenant_AreNotFound()
    {
        _tenant.TenantId = null;
        var viewerB = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", TenantB);
        var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "a@a.test");
        _context.ChangeTracker.Clear();

        (await Users(TenantA).AddUserRole(user.Id, viewerB.Id, default)).Should().BeOfType<NotFoundObjectResult>();
        (await Users(TenantA).SetUserRoles(user.Id, new ServiceSetUserRolesRequest { RoleIds = { viewerB.Id } }, default))
            .Should().BeOfType<NotFoundObjectResult>();
        (await Users(TenantA).RemoveUserRole(user.Id, viewerB.Id, default)).Should().BeOfType<NotFoundObjectResult>();
    }

    // ---------------------------------------------------------------- email verification

    private async Task<OlusoUser> VerifiedUserAsync(string email)
    {
        _tenant.TenantId = null;
        var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, email);
        user.EmailConfirmed = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        _tenant.TenantId = TenantA;
        return user;
    }

    private async Task<bool> IsVerifiedAsync(string userId)
    {
        _context.ChangeTracker.Clear();
        return await _context.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.EmailConfirmed).SingleAsync();
    }

    [Fact]
    public async Task UpdateUser_ChangingTheEmail_MarksItUnverified()
    {
        var user = await VerifiedUserAsync("old@a.test");

        (await _userService.UpdateUserAsync(user.Id, new UpdateUserRequest { Email = "new@a.test" })).Succeeded.Should().BeTrue();

        (await IsVerifiedAsync(user.Id)).Should().BeFalse();
        (await _context.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id)).NormalizedEmail.Should().Be("NEW@A.TEST");
    }

    [Fact]
    public async Task UpdateUser_ChangingTheEmailAfterProvingIt_StoresItVerified()
    {
        var user = await VerifiedUserAsync("proved-old@a.test");

        (await _userService.UpdateUserAsync(user.Id, new UpdateUserRequest { Email = "proved-new@a.test", EmailVerified = true }))
            .Succeeded.Should().BeTrue();

        (await IsVerifiedAsync(user.Id)).Should().BeTrue();
        (await _context.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id)).NormalizedEmail.Should().Be("PROVED-NEW@A.TEST");
    }

    [Fact]
    public async Task UpdateUser_SameEmailInAnotherCase_StaysVerified()
    {
        var user = await VerifiedUserAsync("same@a.test");

        (await _userService.UpdateUserAsync(user.Id, new UpdateUserRequest { Email = "Same@A.test" })).Succeeded.Should().BeTrue();

        (await IsVerifiedAsync(user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateUser_WithoutAnEmail_StaysVerified()
    {
        var user = await VerifiedUserAsync("keep@a.test");

        (await _userService.UpdateUserAsync(user.Id, new UpdateUserRequest { FirstName = "Kept", Picture = "https://x/p.png" }))
            .Succeeded.Should().BeTrue();

        (await IsVerifiedAsync(user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ServiceApi_ChangingTheEmail_ReportsAndStoresItUnverified()
    {
        var user = await VerifiedUserAsync("svc-old@a.test");

        var dto = Ok(await Users(TenantA).UpdateUser(user.Id, new ServiceUpdateUserRequest { Email = "svc-new@a.test" }, default));

        dto.Email.Should().Be("svc-new@a.test");
        dto.EmailVerified.Should().BeFalse();
        (await IsVerifiedAsync(user.Id)).Should().BeFalse();
    }

    private (ServiceUsersController Users, List<OlusoEvent> Raised) UsersWithEvents(params string[] scopes)
    {
        var raised = _captured.Raised;
        var events = _captured.Service;
        _tenant.TenantId = TenantA;
        var users = new ServiceUsersController(
            _tenant, _userService, _store, events,
            new RoleGrantService(_store, Mock.Of<IModuleAdminAssignmentNotifier>(), _userService, _tenant, events),
            Mock.Of<IModuleAdminAssignmentNotifier>(), NullLogger<ServiceUsersController>.Instance)
        {
            ControllerContext = ServiceClient(TenantA, scopes),
        };
        return (users, raised);
    }

    [Fact]
    public async Task Service_api_user_writes_raise_only_the_core_account_events_with_the_client_as_origin()
    {
        var (users, raised) = UsersWithEvents(ServiceScopes.UsersWrite, ServiceScopes.UsersDelete);

        var created = Created(await users.CreateUser(new ServiceCreateUserRequest { Email = "svc-made@a.test", UserName = "svc-made@a.test" }, Mock.Of<ITenantEmailDomainStore>(), default));
        await users.UpdateUser(created.Id, new ServiceUpdateUserRequest { FirstName = "Ada" }, default);
        await users.DeleteUser(created.Id, default);

        raised.Select(e => e.GetType()).Should().Equal(typeof(UserRegisteredEvent), typeof(UserUpdatedEvent), typeof(UserDeletedEvent));
        raised.OfType<UserRegisteredEvent>().Should().ContainSingle(e => e.SubjectId == created.Id && e.TenantId == TenantA && e.ClientId == "svc");
        raised.OfType<UserUpdatedEvent>().Should().ContainSingle(e => e.SubjectId == created.Id);
        raised.OfType<UserDeletedEvent>().Should().ContainSingle(e => e.SubjectId == created.Id && e.Email == "svc-made@a.test");
        raised.Should().AllSatisfy(e => e.Origin.Should().Be(EventOrigin.ServiceClient("svc")));
    }

    [Fact]
    public async Task Service_api_role_changes_raise_the_core_role_events_with_the_client_as_origin()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "roles@a.test");
        var viewer = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", TenantA);
        var editor = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Editor", TenantA);
        var (users, raised) = UsersWithEvents(ServiceScopes.UsersManageRoles);

        await users.AddUserRole(user.Id, viewer.Id, default);
        await users.SetUserRoles(user.Id, new ServiceSetUserRolesRequest { RoleIds = { editor.Id } }, default);
        await users.RemoveUserRole(user.Id, editor.Id, default);

        raised.Select(e => e switch
        {
            UserRoleAssignedEvent a => (nameof(UserRoleAssignedEvent), a.RoleId),
            UserRoleRemovedEvent r => (nameof(UserRoleRemovedEvent), r.RoleId),
            _ => (e.GetType().Name, ""),
        }).Should().Equal(
            (nameof(UserRoleAssignedEvent), viewer.Id),
            (nameof(UserRoleAssignedEvent), editor.Id),
            (nameof(UserRoleRemovedEvent), viewer.Id),
            (nameof(UserRoleRemovedEvent), editor.Id));
        raised.Should().AllSatisfy(e =>
        {
            e.Origin.Should().Be(EventOrigin.ServiceClient("svc"));
            e.TenantId.Should().Be(TenantA);
        });
    }

    private static ServiceUserDto Created(ActionResult<ServiceUserDto> result) =>
        (ServiceUserDto)((ObjectResult)result.Result!).Value!;
}
