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
using Oluso.Core;
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
/// Permanent user deletion: the account and every row keyed to it go, audit
/// history stays, module participants run, and the deleted events are raised
/// only once the delete has happened. Platform identities, the last owner of an
/// organization, members whose home is another tenant and the acting admin
/// themselves are refused.
/// </summary>
public class UserDeletionTests : IDisposable
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
    private readonly Mock<IUserDeletionParticipant> _participant = new();
    private readonly CapturedEvents _captured = new();
    private List<OlusoEvent> _events => _captured.Raised;

    public UserDeletionTests()
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
            NullLogger<IdentityUserService>.Instance, new[] { _participant.Object }, _captured.Services);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private UsersController AdminUsers(string actorId)
    {
        _tenant.TenantId = TenantA;
        return new(
            _tenant, _userService, _captured.Service, _store, new TenantMembershipStore(_context),
            new TenantStore(_context), Mock.Of<IModuleAdminAssignmentNotifier>(), NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", actorId), new Claim("tenant_id", TenantA),
                    }, "test")),
                },
            },
        };
    }

    private ServiceUsersController ServiceUsers()
    {
        _tenant.TenantId = TenantA;
        return new(
            _tenant, _userService, _store, _captured.Service,
            new RoleGrantService(_store, Mock.Of<IModuleAdminAssignmentNotifier>(), _userService, _tenant, _captured.Service),
            Mock.Of<IModuleAdminAssignmentNotifier>(), NullLogger<ServiceUsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("client_id", "svc"), new Claim("tenant_id", TenantA),
                        new Claim("scope", $"{ServiceScopes.ServiceApi} {ServiceScopes.UsersDelete}"),
                    }, "test")),
                },
            },
        };
    }

    /// <summary>A user of tenant A with a row in every table deletion is responsible for.</summary>
    private async Task<OlusoUser> UserWithEverythingAsync()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, $"doomed-{Guid.NewGuid():N}@a.test");
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, $"Editor-{Guid.NewGuid():N}", TenantA);
        var team = new Team { Code = $"OPS-{Guid.NewGuid():N}", Name = "Ops", TenantId = TenantA, OwnerUserId = user.Id };
        _context.Teams.Add(team);

        _context.UserRoles.Add(new OlusoUserRole { UserId = user.Id, RoleId = role.Id, TenantId = TenantA });
        _context.UserClaims.Add(new OlusoUserClaim { UserId = user.Id, ClaimType = "department", ClaimValue = "ops" });
        _context.UserLogins.Add(new OlusoUserLogin { UserId = user.Id, LoginProvider = "Google", ProviderKey = Guid.NewGuid().ToString() });
        _context.UserTokens.Add(new OlusoUserToken { UserId = user.Id, LoginProvider = "[AspNetUserStore]", Name = "AuthenticatorKey", Value = "k" });
        _context.PasswordHistory.Add(new PasswordHistoryEntry { UserId = user.Id, PasswordHash = "h" });
        _context.TenantMemberships.Add(new TenantMembership { TenantId = TenantB, UserId = user.Id });
        _context.OrganizationMemberships.Add(new OrganizationMembership { Id = Guid.NewGuid().ToString(), OrganizationId = "org-1", UserId = user.Id, Role = OrganizationRole.Member });
        _context.TeamMemberships.Add(new TeamMembership { TeamId = team.Id, UserId = user.Id, TenantId = TenantA });
        _context.UserModuleAccess.Add(new UserModuleAccess { TenantId = TenantA, UserId = user.Id, FeatureKey = "hr" });
        _context.UserNotificationProfiles.Add(new UserNotificationProfile { TenantId = TenantA, UserId = user.Id });
        _context.PersistedGrants.Add(new PersistedGrant { Key = Guid.NewGuid().ToString(), Type = "refresh_token", SubjectId = user.Id, ClientId = "app", Data = "{}", CreationTime = DateTime.UtcNow });
        _context.ServerSideSessions.Add(new ServerSideSession { Key = Guid.NewGuid().ToString(), Scheme = "cookie", SubjectId = user.Id, Data = "{}" });
        _context.Consents.Add(new Consent { SubjectId = user.Id, ClientId = "app", Scopes = "openid" });
        _context.ActionTokens.Add(new ActionToken { TokenHash = Guid.NewGuid().ToString(), UserId = user.Id, Action = "approve", SubjectId = "task-1" });
        _context.AuditLogs.Add(new AuditLog { TenantId = TenantA, Timestamp = DateTime.UtcNow, EventType = "UserSignedIn", Category = "Authentication", SubjectId = user.Id });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return user;
    }

    private async Task<Dictionary<string, int>> RowsKeyedToAsync(string userId) => new()
    {
        ["users"] = await _context.Users.IgnoreQueryFilters().CountAsync(x => x.Id == userId),
        ["roles"] = await _context.UserRoles.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["claims"] = await _context.UserClaims.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["logins"] = await _context.UserLogins.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["tokens"] = await _context.UserTokens.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["passwordHistory"] = await _context.PasswordHistory.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["tenantMemberships"] = await _context.TenantMemberships.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["orgMemberships"] = await _context.OrganizationMemberships.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["teamMemberships"] = await _context.TeamMemberships.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["ownedTeams"] = await _context.Teams.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == userId),
        ["seats"] = await _context.UserModuleAccess.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["notificationProfiles"] = await _context.UserNotificationProfiles.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
        ["grants"] = await _context.PersistedGrants.IgnoreQueryFilters().CountAsync(x => x.SubjectId == userId),
        ["sessions"] = await _context.ServerSideSessions.IgnoreQueryFilters().CountAsync(x => x.SubjectId == userId),
        ["consents"] = await _context.Consents.IgnoreQueryFilters().CountAsync(x => x.SubjectId == userId),
        ["actionTokens"] = await _context.ActionTokens.IgnoreQueryFilters().CountAsync(x => x.UserId == userId),
    };

    [Fact]
    public async Task Deleting_removes_the_account_and_every_row_keyed_to_it_but_keeps_audit_history()
    {
        var user = await UserWithEverythingAsync();
        (await RowsKeyedToAsync(user.Id)).Values.Should().OnlyContain(n => n > 0);

        var result = await _userService.DeleteUserAsync(user.Id);

        result.Succeeded.Should().BeTrue();
        (await RowsKeyedToAsync(user.Id)).Should().OnlyContain(kv => kv.Value == 0);
        (await _context.Teams.IgnoreQueryFilters().CountAsync()).Should().Be(1, "the team outlives its owner");
        (await _context.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.SubjectId == user.Id)).Should().Be(1);
        _participant.Verify(p => p.DeleteUserDataAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleting_leaves_other_users_alone()
    {
        var doomed = await UserWithEverythingAsync();
        var bystander = await UserWithEverythingAsync();

        await _userService.DeleteUserAsync(doomed.Id);

        (await RowsKeyedToAsync(bystander.Id)).Values.Should().OnlyContain(n => n > 0);
    }

    [Fact]
    public async Task A_platform_tenant_user_is_refused()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, PlatformConstants.TenantId, "ops@platform.test");

        var result = await _userService.DeleteUserAsync(user.Id);

        result.Error.Should().Be(UserDeletionErrors.ProtectedIdentity);
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == user.Id)).Should().BeTrue();
        _participant.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_superadmin_is_refused_whatever_their_home_tenant()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "root@a.test");
        var superAdmin = await IdentityTestFactory.CreateRoleAsync(_roleManager, "SuperAdmin", null, isSystemRole: true);
        _context.UserRoles.Add(new OlusoUserRole { UserId = user.Id, RoleId = superAdmin.Id, TenantId = TenantA });
        await _context.SaveChangesAsync();

        var result = await _userService.DeleteUserAsync(user.Id);

        result.Error.Should().Be(UserDeletionErrors.ProtectedIdentity);
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == user.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task The_last_owner_of_an_organization_is_refused_until_another_owner_exists()
    {
        var owner = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "owner@a.test");
        _context.OrganizationMemberships.Add(new OrganizationMembership { Id = Guid.NewGuid().ToString(), OrganizationId = "org-1", UserId = owner.Id, Role = OrganizationRole.Owner });
        await _context.SaveChangesAsync();

        (await _userService.DeleteUserAsync(owner.Id)).Error.Should().Be(UserDeletionErrors.LastOrganizationOwner);

        var coOwner = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "co-owner@a.test");
        _context.OrganizationMemberships.Add(new OrganizationMembership { Id = Guid.NewGuid().ToString(), OrganizationId = "org-1", UserId = coOwner.Id, Role = OrganizationRole.Owner });
        await _context.SaveChangesAsync();

        (await _userService.DeleteUserAsync(owner.Id)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_failing_participant_doesnt_undo_or_fail_the_delete()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "x@a.test");
        _participant
            .Setup(p => p.DeleteUserDataAsync(user.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store down"));

        var result = await _userService.DeleteUserAsync(user.Id);

        result.Succeeded.Should().BeTrue();
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == user.Id)).Should().BeFalse();
    }

    // ---------------------------------------------------------------- admin API

    [Fact]
    public async Task Admin_delete_removes_the_user_and_then_raises_the_event()
    {
        var actor = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "admin@a.test");
        var user = await UserWithEverythingAsync();

        var result = await AdminUsers(actor.Id).DeleteUser(user.Id, default);

        result.Should().BeOfType<NoContentResult>();
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == user.Id)).Should().BeFalse();
        _events.Should().ContainSingle().Which.Should().BeOfType<UserDeletedEvent>()
            .Which.SubjectId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Admin_cannot_delete_themselves()
    {
        var actor = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "admin@a.test");

        var result = await AdminUsers(actor.Id).DeleteUser(actor.Id, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == actor.Id)).Should().BeTrue();
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_cannot_delete_a_member_whose_home_is_another_tenant()
    {
        var actor = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "admin@a.test");
        var member = await IdentityTestFactory.CreateUserAsync(_context, TenantB, "member@b.test");
        _context.TenantMemberships.Add(new TenantMembership { TenantId = TenantA, UserId = member.Id });
        await _context.SaveChangesAsync();

        var result = await AdminUsers(actor.Id).DeleteUser(member.Id, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == member.Id)).Should().BeTrue();
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_cannot_delete_a_platform_administrator_and_no_event_is_raised()
    {
        var actor = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "admin@a.test");
        var root = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "root@a.test");
        var superAdmin = await IdentityTestFactory.CreateRoleAsync(_roleManager, "SuperAdmin", null, isSystemRole: true);
        _context.UserRoles.Add(new OlusoUserRole { UserId = root.Id, RoleId = superAdmin.Id, TenantId = TenantA });
        await _context.SaveChangesAsync();

        var result = await AdminUsers(actor.Id).DeleteUser(root.Id, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_delete_of_a_user_outside_the_tenant_is_not_found()
    {
        var actor = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "admin@a.test");
        var stranger = await IdentityTestFactory.CreateUserAsync(_context, TenantB, "stranger@b.test");

        var result = await AdminUsers(actor.Id).DeleteUser(stranger.Id, default);

        result.Should().BeOfType<NotFoundResult>();
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == stranger.Id)).Should().BeTrue();
    }

    // ---------------------------------------------------------------- service API

    [Fact]
    public async Task Service_delete_removes_the_user_and_then_raises_the_event()
    {
        var user = await UserWithEverythingAsync();

        var result = await ServiceUsers().DeleteUser(user.Id, default);

        result.Should().BeOfType<NoContentResult>();
        (await RowsKeyedToAsync(user.Id)).Should().OnlyContain(kv => kv.Value == 0);
        var deleted = _events.Should().ContainSingle().Which.Should().BeOfType<UserDeletedEvent>().Subject;
        deleted.SubjectId.Should().Be(user.Id);
        deleted.Origin.Should().Be(EventOrigin.ServiceClient("svc"));
    }

    [Fact]
    public async Task Service_delete_of_another_tenants_user_is_not_found()
    {
        var stranger = await IdentityTestFactory.CreateUserAsync(_context, TenantB, "stranger@b.test");

        var result = await ServiceUsers().DeleteUser(stranger.Id, default);

        result.Should().BeOfType<NotFoundResult>();
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == stranger.Id)).Should().BeTrue();
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task Service_delete_of_a_member_from_another_tenant_is_forbidden()
    {
        var member = await IdentityTestFactory.CreateUserAsync(_context, TenantB, "member@b.test");
        _context.TenantMemberships.Add(new TenantMembership { TenantId = TenantA, UserId = member.Id });
        await _context.SaveChangesAsync();

        var result = await ServiceUsers().DeleteUser(member.Id, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == member.Id)).Should().BeTrue();
        _events.Should().BeEmpty();
    }
}
