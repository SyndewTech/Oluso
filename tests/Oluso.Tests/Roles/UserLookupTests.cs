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
using Oluso.Admin.Controllers.Service;
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
/// Finding a tenant's users by email reaches its members from other tenants the
/// same way lookup by id does, and the role filter on user listings is applied
/// before counting and paging.
/// </summary>
public class UserLookupTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private readonly SqliteConnection _connection;
    // Seeding runs outside a tenant so rows keep the tenant they're given; the
    // controllers then act in tenant A.
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly RoleStore _store;
    private readonly IdentityUserService _userService;

    public UserLookupTests()
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
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private ServiceUsersController ServiceUsers()
    {
        _tenant.TenantId = TenantA;
        return new(
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
                        new Claim("client_id", "svc"), new Claim("tenant_id", TenantA),
                        new Claim("scope", $"{ServiceScopes.ServiceApi} {ServiceScopes.UsersRead}"),
                    }, "test")),
                },
            },
        };
    }

    private async Task AddMembershipAsync(string userId, string tenantId)
    {
        _context.TenantMemberships.Add(new TenantMembership { TenantId = tenantId, UserId = userId });
        await _context.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- by email

    [Fact]
    public async Task By_email_finds_a_member_whose_home_is_another_tenant()
    {
        var member = await IdentityTestFactory.CreateUserAsync(_context, TenantB, "member@b.test");
        await AddMembershipAsync(member.Id, TenantA);

        var result = await ServiceUsers().GetUserByEmail("Member@B.test", default);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ServiceUserDto>().Which.Id.Should().Be(member.Id);
    }

    [Fact]
    public async Task By_email_does_not_find_a_user_who_is_not_a_member()
    {
        await IdentityTestFactory.CreateUserAsync(_context, TenantB, "stranger@b.test");

        var result = await ServiceUsers().GetUserByEmail("stranger@b.test", default);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task By_email_prefers_the_tenants_own_account_over_a_member_with_the_same_address()
    {
        var member = await IdentityTestFactory.CreateUserAsync(_context, TenantB, "shared@x.test");
        await AddMembershipAsync(member.Id, TenantA);
        var own = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "shared@x.test");

        (await _userService.FindByEmailInTenantAsync("shared@x.test", TenantA))!.Id.Should().Be(own.Id);
        (await _userService.FindByEmailInTenantAsync("shared@x.test", TenantB))!.Id.Should().Be(member.Id);
    }

    // ---------------------------------------------------------------- role filter

    private async Task<(OlusoRole Editor, List<OlusoUser> Editors)> SeedEditorsAsync()
    {
        var editor = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Editor", TenantA);
        var editorB = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Editor", TenantB);
        var editors = new List<OlusoUser>();
        for (var i = 0; i < 7; i++)
        {
            var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, $"u{i}@a.test");
            if (i % 2 == 0)
            {
                _context.UserRoles.Add(new OlusoUserRole { UserId = user.Id, RoleId = editor.Id, TenantId = TenantA });
                editors.Add(user);
            }
        }

        // Holds the same-named role, but in another tenant: not an editor of A.
        var elsewhere = await IdentityTestFactory.CreateUserAsync(_context, TenantB, "b-editor@b.test");
        await AddMembershipAsync(elsewhere.Id, TenantA);
        _context.UserRoles.Add(new OlusoUserRole { UserId = elsewhere.Id, RoleId = editorB.Id, TenantId = TenantB });

        await _context.SaveChangesAsync();
        return (editor, editors);
    }

    [Fact]
    public async Task Role_filter_counts_and_pages_only_the_role_holders()
    {
        var (_, editors) = await SeedEditorsAsync();

        var page1 = await _userService.GetUsersAsync(new UsersQuery { TenantId = TenantA, Role = "Editor", Page = 1, PageSize = 3 });
        var page2 = await _userService.GetUsersAsync(new UsersQuery { TenantId = TenantA, Role = "Editor", Page = 2, PageSize = 3 });

        page1.TotalCount.Should().Be(4);
        page1.TotalPages.Should().Be(2);
        page1.Users.Should().HaveCount(3);
        page2.Users.Should().HaveCount(1);
        page1.Users.Concat(page2.Users).Select(u => u.Id).Should().BeEquivalentTo(editors.Select(e => e.Id));
    }

    [Fact]
    public async Task Role_filter_accepts_a_role_id()
    {
        var (editor, editors) = await SeedEditorsAsync();

        var result = await _userService.GetUsersAsync(new UsersQuery { TenantId = TenantA, Role = editor.Id, PageSize = 50 });

        result.TotalCount.Should().Be(editors.Count);
        result.Users.Select(u => u.Id).Should().BeEquivalentTo(editors.Select(e => e.Id));
    }

    [Fact]
    public async Task Role_filter_with_no_holders_is_empty()
    {
        await SeedEditorsAsync();

        var result = await _userService.GetUsersAsync(new UsersQuery { TenantId = TenantA, Role = "Auditor" });

        result.TotalCount.Should().Be(0);
        result.Users.Should().BeEmpty();
    }
}
