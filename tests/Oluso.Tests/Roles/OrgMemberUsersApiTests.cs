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
using Oluso.Core.Api;
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
/// A user whose home tenant is A but who belongs to B through a TenantMembership
/// is one of B's users: the admin Users API and the service API acting in B
/// list them, show their details, and set and remove their roles in B (stamped
/// B, never A). A user from C with no membership in B stays a 404.
/// </summary>
public class OrgMemberUsersApiTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string TenantC = "tenant-c";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly RoleStore _store;
    private readonly IdentityUserService _userService;
    private readonly UsersController _users;
    private readonly ServiceUsersController _service;

    private OlusoUser _member = null!;
    private OlusoUser _outsider = null!;
    private OlusoRole _viewerB = null!;
    private OlusoRole _viewerA = null!;

    public OrgMemberUsersApiTests()
    {
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
            NullLogger<IdentityUserService>.Instance);

        _users = new UsersController(
            _tenant, _userService, Mock.Of<IOlusoEventService>(), _store, new TenantMembershipStore(_context),
            new TenantStore(_context), Mock.Of<IModuleAdminAssignmentNotifier>(), NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", "admin-b"),
                        new Claim("tenant_id", TenantB),
                        new Claim("role", "Admin"),
                    }, "test")),
                },
            },
        };

        _service = new ServiceUsersController(
            _tenant, _userService, _store, Mock.Of<IOlusoEventService>(),
            new RoleGrantService(_store, Mock.Of<IModuleAdminAssignmentNotifier>(), _userService, _tenant, Mock.Of<IOlusoEventService>()),
            Mock.Of<IModuleAdminAssignmentNotifier>(), NullLogger<ServiceUsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("client_id", "svc-b"),
                        new Claim("tenant_id", TenantB),
                        new Claim("scope", $"{ServiceScopes.UsersRead} {ServiceScopes.UsersWrite} {ServiceScopes.UsersManageRoles}"),
                    }, "test")),
                },
            },
        };
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task SeedAsync()
    {
        _tenant.TenantId = null;
        _viewerA = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", TenantA);
        _viewerB = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", TenantB);
        _member = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "member@a.test", displayName: "Member");
        _outsider = await IdentityTestFactory.CreateUserAsync(_context, TenantC, "outsider@c.test", displayName: "Outsider");
        _context.TenantMemberships.Add(new TenantMembership { TenantId = TenantB, UserId = _member.Id });
        await _context.SaveChangesAsync();

        // Already holds A's Viewer in A; nothing done in B may touch it.
        await _store.AssignToUserInTenantAsync(_viewerA.Id, _member.Id, TenantA, "admin-a");

        // A request starts with an empty context; FindAsync would otherwise hand
        // back the seeded entities from the change tracker, past the tenant filter.
        _context.ChangeTracker.Clear();
        _tenant.TenantId = TenantB;
    }

    private Task<List<(string RoleId, string TenantId)>> AssignmentsAsync() =>
        _context.UserRoles.Where(ur => ur.UserId == _member.Id)
            .Select(ur => new ValueTuple<string, string>(ur.RoleId, ur.TenantId))
            .ToListAsync();

    private static T Ok<T>(ActionResult<T> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<T>().Subject;

    [Fact]
    public async Task AdminUsersApi_ListsAndShowsTheOrgMember_AndSetsAndRemovesRolesInB()
    {
        await SeedAsync();

        var list = Ok(await _users.GetUsers());
        list.Items.Select(u => u.Id).Should().Equal(_member.Id);

        var detail = Ok(await _users.GetUser(_member.Id, default));
        detail.Id.Should().Be(_member.Id);
        detail.Roles.Should().BeEmpty("the A assignment does not apply in B");

        (await _users.SetUserRoles(_member.Id, new SetUserRolesRequest { RoleIds = { _viewerB.Id } }, default))
            .Should().BeOfType<OkObjectResult>();
        (await AssignmentsAsync()).Should().BeEquivalentTo(new[] { (_viewerA.Id, TenantA), (_viewerB.Id, TenantB) });

        Ok(await _users.GetUserRoles(_member.Id, default)).Select(r => r.Id).Should().Equal(_viewerB.Id);
        Ok(await _users.GetUser(_member.Id, default)).Roles.Should().Equal("Viewer");
        Ok(await _users.GetMfaStatus(_member.Id, default)).Should().NotBeNull();
        Ok(await _users.GetExternalLogins(_member.Id, default)).Should().BeEmpty();

        (await _users.SetUserRoles(_member.Id, new SetUserRolesRequest(), default))
            .Should().BeOfType<OkObjectResult>();
        (await AssignmentsAsync()).Should().BeEquivalentTo(new[] { (_viewerA.Id, TenantA) });
    }

    [Fact]
    public async Task UserService_SetsRolesOnTheOrgMember_InTheRequestTenant()
    {
        await SeedAsync();

        (await _userService.AddToRoleAsync(_member.Id, "Viewer")).Succeeded.Should().BeTrue();
        (await AssignmentsAsync()).Should().Contain((_viewerB.Id, TenantB));
        (await _userService.GetRolesAsync(_member.Id)).Should().Equal("Viewer");

        (await _userService.RemoveFromRoleAsync(_member.Id, "Viewer")).Succeeded.Should().BeTrue();
        (await AssignmentsAsync()).Should().BeEquivalentTo(new[] { (_viewerA.Id, TenantA) });
    }

    [Fact]
    public async Task ServiceApi_ShowsTheOrgMember_SetsRolesInB_AndRefusesProfileEdits()
    {
        await SeedAsync();

        var list = Ok(await _service.GetUsers());
        list.Items.Select(u => u.Id).Should().Equal(_member.Id);

        (await _service.SetUserRoles(_member.Id, new ServiceSetUserRolesRequest { RoleIds = { _viewerB.Id } }, default))
            .Should().BeOfType<OkObjectResult>();
        (await AssignmentsAsync()).Should().Contain((_viewerB.Id, TenantB));
        Ok(await _service.GetUser(_member.Id, default)).Roles.Should().Equal("Viewer");

        (await _service.RemoveUserRole(_member.Id, _viewerB.Id, default)).Should().BeOfType<NoContentResult>();
        (await AssignmentsAsync()).Should().BeEquivalentTo(new[] { (_viewerA.Id, TenantA) });

        // Their profile belongs to their home tenant.
        var update = await _service.UpdateUser(_member.Id, new ServiceUpdateUserRequest { FirstName = "X" }, default);
        update.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task UserFromAnotherTenantWithoutMembership_IsStillNotFound()
    {
        await SeedAsync();

        (await _users.GetUser(_outsider.Id, default)).Result.Should().BeOfType<NotFoundResult>();
        (await _users.GetUserRoles(_outsider.Id, default)).Result.Should().BeOfType<NotFoundResult>();
        (await _users.SetUserRoles(_outsider.Id, new SetUserRolesRequest { RoleIds = { _viewerB.Id } }, default))
            .Should().BeOfType<NotFoundResult>();
        (await _users.GetMfaStatus(_outsider.Id, default)).Result.Should().BeOfType<NotFoundResult>();
        (await _service.GetUser(_outsider.Id, default)).Result.Should().BeOfType<NotFoundResult>();
        (await _service.SetUserRoles(_outsider.Id, new ServiceSetUserRolesRequest { RoleIds = { _viewerB.Id } }, default))
            .Should().BeOfType<NotFoundResult>();

        (await _userService.AddToRoleAsync(_outsider.Id, "Viewer")).Succeeded.Should().BeFalse();
        (await _userService.GetRolesAsync(_outsider.Id)).Should().BeEmpty();
        (await _context.UserRoles.CountAsync(ur => ur.UserId == _outsider.Id)).Should().Be(0);
    }
}
