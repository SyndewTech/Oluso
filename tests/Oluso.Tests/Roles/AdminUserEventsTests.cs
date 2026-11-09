using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Users;
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
/// The admin Users API raises the core user events, once per fact, with the
/// acting admin as origin: created, updated and deleted, one assigned event per
/// role gained and one removed event per role lost.
/// </summary>
public class AdminUserEventsTests : IDisposable
{
    private const string Tenant = "tenant-a";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly RoleStore _store;
    private readonly IdentityUserService _userService;
    private readonly CapturedEvents _captured = new();
    private List<OlusoEvent> _events => _captured.Raised;

    private static readonly EventOrigin AdminOrigin = EventOrigin.Admin("admin-1", "Ada", "203.0.113.7");

    public AdminUserEventsTests()
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
            NullLogger<IdentityUserService>.Instance, services: _captured.Services);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private UsersController Users()
    {
        _tenant.TenantId = Tenant;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", "admin-1"), new Claim("name", "Ada"),
                new Claim("tenant_id", Tenant), new Claim("role", "Admin"),
            }, "test")),
        };
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        return new UsersController(
            _tenant, _userService, _captured.Service, _store, new TenantMembershipStore(_context),
            new TenantStore(_context), Mock.Of<IModuleAdminAssignmentNotifier>(), NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private async Task<(OlusoRole Viewer, OlusoRole Editor)> RolesAsync()
    {
        _tenant.TenantId = null;
        var viewer = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", Tenant);
        var editor = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Editor", Tenant);
        return (viewer, editor);
    }

    [Fact]
    public async Task Create_raises_user_registered_and_one_assigned_event_per_role()
    {
        var (viewer, _) = await RolesAsync();

        var result = await Users().CreateUser(new Oluso.Admin.Dto.Users.CreateUserRequest
        {
            Email = "new@a.test",
            UserName = "new@a.test",
            Password = "Str0ng!Passw0rd#2026",
            RoleIds = new List<string> { viewer.Id },
        }, Mock.Of<IIssuerResolver>(), Mock.Of<ITenantSettingsProvider>(), null, null, Mock.Of<ITenantEmailDomainStore>(), default);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        _events.Select(e => e.GetType()).Should().Equal(typeof(UserRegisteredEvent), typeof(UserRoleAssignedEvent));
        _events.Should().AllSatisfy(e => e.Origin.Should().Be(AdminOrigin));

        var registered = (UserRegisteredEvent)_events[0];
        registered.Email.Should().Be("new@a.test");
        registered.TenantId.Should().Be(Tenant);
        var assigned = (UserRoleAssignedEvent)_events[1];
        assigned.SubjectId.Should().Be(registered.SubjectId);
        assigned.RoleId.Should().Be(viewer.Id);
        assigned.RoleName.Should().Be("Viewer");
    }

    [Fact]
    public async Task Creating_a_user_marked_verified_is_refused_unless_the_domain_is_the_tenants_own()
    {
        var domains = new Mock<ITenantEmailDomainStore>();
        domains.Setup(d => d.IsVerifiedForTenantAsync(Tenant, "ok@verified.test", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var refused = await Users().CreateUser(new Oluso.Admin.Dto.Users.CreateUserRequest
        {
            Email = "someone@gmail.com", Password = "Str0ng!Passw0rd#2026", EmailVerified = true,
        }, Mock.Of<IIssuerResolver>(), Mock.Of<ITenantSettingsProvider>(), null, null, domains.Object, default);
        var accepted = await Users().CreateUser(new Oluso.Admin.Dto.Users.CreateUserRequest
        {
            Email = "ok@verified.test", Password = "Str0ng!Passw0rd#2026", EmailVerified = true,
        }, Mock.Of<IIssuerResolver>(), Mock.Of<ITenantSettingsProvider>(), null, null, domains.Object, default);

        refused.Result.Should().BeOfType<BadRequestObjectResult>();
        var created = accepted.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value.Should().BeOfType<UserDto>().Subject;
        created.EmailVerified.Should().BeTrue("the response reports what was stored");
    }

    [Fact]
    public async Task Update_raises_a_single_user_updated()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, "u@a.test");

        var result = await Users().UpdateUser(user.Id, new UserDetailDto
        {
            Id = user.Id, Email = "u@a.test", FirstName = "Una", IsActive = true,
        }, Mock.Of<ITenantEmailDomainStore>(), default);

        result.Result.Should().BeOfType<OkObjectResult>();
        var updated = _events.Should().ContainSingle().Which.Should().BeOfType<UserUpdatedEvent>().Subject;
        updated.SubjectId.Should().Be(user.Id);
        updated.Email.Should().Be("u@a.test");
        updated.Origin.Should().Be(AdminOrigin);
    }

    [Fact]
    public async Task Switching_a_user_off_without_other_edits_raises_only_user_deactivated()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, "u@a.test");

        var result = await Users().UpdateUser(user.Id, new UserDetailDto
        {
            Id = user.Id, Email = "u@a.test", IsActive = false,
        }, Mock.Of<ITenantEmailDomainStore>(), default);

        result.Result.Should().BeOfType<OkObjectResult>();
        var deactivated = _events.Should().ContainSingle().Which.Should().BeOfType<UserDeactivatedEvent>().Subject;
        deactivated.SubjectId.Should().Be(user.Id);
        deactivated.TenantId.Should().Be(Tenant);
        deactivated.Origin.Should().Be(AdminOrigin);
    }

    [Fact]
    public async Task Setting_roles_raises_assigned_for_gained_and_removed_for_lost_roles_only()
    {
        var (viewer, editor) = await RolesAsync();
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, "u@a.test");
        await _store.AssignToUserInTenantAsync(viewer.Id, user.Id, Tenant, "seed");
        _context.ChangeTracker.Clear();

        var users = Users();
        (await users.SetUserRoles(user.Id, new SetUserRolesRequest { RoleIds = { editor.Id } }, default))
            .Should().BeOfType<OkObjectResult>();

        _events.Should().HaveCount(2).And.AllSatisfy(e => e.Origin.Should().Be(AdminOrigin));
        _events.OfType<UserRoleAssignedEvent>().Should().ContainSingle().Which.RoleId.Should().Be(editor.Id);
        _events.OfType<UserRoleRemovedEvent>().Should().ContainSingle().Which.RoleId.Should().Be(viewer.Id);

        _events.Clear();
        (await users.SetUserRoles(user.Id, new SetUserRolesRequest { RoleIds = { editor.Id } }, default))
            .Should().BeOfType<OkObjectResult>();
        _events.Should().BeEmpty("nothing changed");
    }

    [Fact]
    public async Task Delete_raises_a_single_user_deleted()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, "gone@a.test");

        (await Users().DeleteUser(user.Id, default)).Should().BeOfType<NoContentResult>();

        var deleted = _events.Should().ContainSingle().Which.Should().BeOfType<UserDeletedEvent>().Subject;
        deleted.SubjectId.Should().Be(user.Id);
        deleted.Email.Should().Be("gone@a.test");
        deleted.DeletedByAdmin.Should().BeTrue();
        deleted.Origin.Should().Be(AdminOrigin);
    }
}
