using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Authorization;
using Oluso.Core;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Oluso.EntityFramework.Stores;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// A role assignment applies in the tenant it is stamped with. A platform-wide
/// role such as HRAdmin given in tenant A is not held in tenant B, even by a
/// user who belongs to both — on the token path (DefaultProfileService), the
/// permission handler's database path, and UserManager. SuperAdmin and
/// SystemAdmin are the exception: they apply in every tenant.
/// </summary>
public class TenantScopedRoleAssignmentTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly UserManager<OlusoUser> _userManager;
    private readonly RoleStore _store;
    private readonly DefaultProfileService _profile;
    private readonly PermissionAuthorizationHandler _permissions;
    private readonly Mock<ITenantMembershipStore> _memberships = new();

    public TenantScopedRoleAssignmentTests()
    {
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _roleManager = IdentityTestFactory.CreateRoleManager(_context);
        _userManager = IdentityTestFactory.CreateUserManager(_context, _tenant);
        _store = new RoleStore(_roleManager, _userManager, _context, _tenant);

        var signInManager = new SignInManager<OlusoUser>(
            _userManager,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<OlusoUser>>(),
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<OlusoUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<OlusoUser>>());
        var userService = new IdentityUserService(
            _userManager, signInManager, _tenant, Mock.Of<ITenantSettingsProvider>(), _context,
            NullLogger<IdentityUserService>.Instance);

        // Every tenant a user holds a membership in is a tenant a token may be for.
        _memberships
            .Setup(m => m.GetByTenantAndUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, string userId, CancellationToken _) =>
                new TenantMembership { TenantId = tenantId, UserId = userId });
        _memberships
            .Setup(m => m.GetByUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantMembership>());

        var claimsRegistry = new Mock<IClaimsProviderRegistry>();
        claimsRegistry
            .Setup(x => x.GetAllClaimsAsync(It.IsAny<ClaimsProviderContext>()))
            .ReturnsAsync(new Dictionary<string, object>());
        var resources = new Mock<IResourceStore>();
        resources
            .Setup(x => x.FindIdentityResourcesByScopeNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdentityResource>());
        resources
            .Setup(x => x.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ApiScope>());
        resources
            .Setup(x => x.FindResourcesByUrisAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Resource>());

        _profile = new DefaultProfileService(
            userService, _store, claimsRegistry.Object, _tenant, Mock.Of<ITenantStore>(), resources.Object,
            NullLogger<DefaultProfileService>.Instance, tenantMembershipStore: _memberships.Object);

        _permissions = new PermissionAuthorizationHandler(
            new EffectiveRoleResolver(_store, Mock.Of<ITenantStore>()), _tenant, NullLogger<PermissionAuthorizationHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<OlusoRole> RoleAsync(string name, string? tenantId, params string[] permissions)
    {
        _tenant.TenantId = null;
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, name, tenantId, isSystemRole: tenantId == null);
        foreach (var permission in permissions)
        {
            await _store.AddRoleClaimAsync(role.Id, OlusoRole.PermissionClaimType, permission);
        }
        return role;
    }

    private async Task<OlusoUser> UserAsync(string homeTenantId, string email)
    {
        _tenant.TenantId = null;
        return await IdentityTestFactory.CreateUserAsync(_context, homeTenantId, email);
    }

    private async Task<ICollection<string>> TokenPermissionsAsync(string userId, string tenantId)
    {
        _tenant.TenantId = tenantId;
        return await _profile.GetUserPermissionsAsync(userId, tenantId);
    }

    private async Task<ICollection<string>> TokenRolesAsync(string userId, string tenantId)
    {
        _tenant.TenantId = tenantId;
        return await _profile.GetUserRolesAsync(userId);
    }

    private async Task<List<Claim>> TokenClaimsAsync(string userId, string tenantId, params string[] scopes)
    {
        _tenant.TenantId = tenantId;
        var request = new ProfileDataRequestContext
        {
            SubjectId = userId,
            Client = new Client { ClientId = "c1" },
            RequestedScopes = scopes.Length > 0 ? scopes : new[] { "openid" },
        };
        await _profile.GetProfileDataAsync(request);
        return request.IssuedClaims;
    }

    /// <summary>
    /// A token without a permissions claim, so the handler reads the database
    /// for the tenant the request landed in.
    /// </summary>
    private async Task<bool> PermittedFromDatabaseAsync(string userId, string tenantId, string permission)
    {
        _tenant.TenantId = tenantId;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", userId),
            new Claim("tenant_id", tenantId),
        }, "test"));
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);
        await _permissions.HandleAsync(context);
        return context.HasSucceeded;
    }

    private Task<List<string>> StampsAsync(string userId, string roleId) =>
        _context.UserRoles.Where(ur => ur.UserId == userId && ur.RoleId == roleId)
            .Select(ur => ur.TenantId).OrderBy(t => t).ToListAsync();

    [Fact]
    public async Task PlatformRole_AssignedInA_IsNotHeldInB_ForAUserWhoBelongsToBoth()
    {
        var hrAdmin = await RoleAsync("HRAdmin", null, "hr.*");
        var user = await UserAsync(TenantA, "ada@a.test");

        await _store.AssignToUserInTenantAsync(hrAdmin.Id, user.Id, TenantA, "admin");

        (await _store.GetUserRolesInTenantAsync(user.Id, TenantA)).Select(r => r.Id).Should().Equal(hrAdmin.Id);
        (await _store.GetUserRolesInTenantAsync(user.Id, TenantB)).Should().BeEmpty();

        (await TokenPermissionsAsync(user.Id, TenantA)).Should().Contain("hr.*");
        (await TokenPermissionsAsync(user.Id, TenantB)).Should().BeEmpty();
        (await TokenRolesAsync(user.Id, TenantA)).Should().Contain("HRAdmin");
        (await TokenRolesAsync(user.Id, TenantB)).Should().NotContain("HRAdmin");
        (await TokenClaimsAsync(user.Id, TenantB)).Should().NotContain(c => c.Type == "role" && c.Value == "HRAdmin");

        (await PermittedFromDatabaseAsync(user.Id, TenantA, "hr.employees.read")).Should().BeTrue();
        (await PermittedFromDatabaseAsync(user.Id, TenantB, "hr.employees.read")).Should().BeFalse();

        _tenant.TenantId = TenantB;
        (await _userManager.IsInRoleAsync(user, "HRADMIN")).Should().BeFalse();
        (await _store.GetUsersByRoleAsync(hrAdmin.Id)).Should().BeEmpty();
        _tenant.TenantId = TenantA;
        (await _userManager.IsInRoleAsync(user, "HRADMIN")).Should().BeTrue();
        (await _store.GetUsersByRoleAsync(hrAdmin.Id)).Select(u => u.Id).Should().Equal(user.Id);
    }

    [Fact]
    public async Task AssignedInBoth_RemovedInB_IsStillHeldInA()
    {
        var hrAdmin = await RoleAsync("HRAdmin", null, "hr.*");
        var user = await UserAsync(TenantA, "ada@a.test");
        await _store.AssignToUserInTenantAsync(hrAdmin.Id, user.Id, TenantA, "admin");
        await _store.AssignToUserInTenantAsync(hrAdmin.Id, user.Id, TenantB, "admin");
        (await StampsAsync(user.Id, hrAdmin.Id)).Should().Equal(TenantA, TenantB);

        _tenant.TenantId = TenantB;
        await _store.RemoveFromUserAsync(hrAdmin.Id, user.Id);

        (await StampsAsync(user.Id, hrAdmin.Id)).Should().Equal(TenantA);
        (await PermittedFromDatabaseAsync(user.Id, TenantA, "hr.employees.read")).Should().BeTrue();
        (await PermittedFromDatabaseAsync(user.Id, TenantB, "hr.employees.read")).Should().BeFalse();
    }

    [Fact]
    public async Task UserManager_AssignsAndRemovesInTheRequestTenantOnly()
    {
        var hrAdmin = await RoleAsync("HRAdmin", null, "hr.*");
        var user = await UserAsync(TenantA, "ada@a.test");
        await _store.AssignToUserInTenantAsync(hrAdmin.Id, user.Id, TenantA, "admin");

        _tenant.TenantId = TenantB;
        (await _userManager.AddToRoleAsync(user, "HRAdmin")).Succeeded.Should().BeTrue();
        (await StampsAsync(user.Id, hrAdmin.Id)).Should().Equal(TenantA, TenantB);
        (await _userManager.GetRolesAsync(user)).Should().Equal("HRAdmin");

        (await _userManager.RemoveFromRoleAsync(user, "HRAdmin")).Succeeded.Should().BeTrue();
        (await StampsAsync(user.Id, hrAdmin.Id)).Should().Equal(TenantA);
        (await _userManager.GetRolesAsync(user)).Should().BeEmpty();
    }

    [Fact]
    public async Task SuperAdmin_HomedInOneTenant_IsSuperAdminInAnother()
    {
        var superAdmin = await RoleAsync(GlobalRoles.SuperAdmin, null, "*");
        var user = await UserAsync(TenantA, "root@a.test");
        await _store.AssignToUserInTenantAsync(superAdmin.Id, user.Id, TenantA, "system");

        (await _store.GetUserRolesInTenantAsync(user.Id, TenantB)).Select(r => r.Id).Should().Equal(superAdmin.Id);
        (await TokenPermissionsAsync(user.Id, TenantB)).Should().Contain("*");

        var claims = await TokenClaimsAsync(user.Id, TenantB);
        claims.Should().Contain(c => c.Type == "role" && c.Value == GlobalRoles.SuperAdmin);
        claims.Should().Contain(c => c.Type == "super_admin" && c.Value == "true");
        claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == TenantB);

        (await PermittedFromDatabaseAsync(user.Id, TenantB, "hr.employees.read")).Should().BeTrue();

        _tenant.TenantId = TenantB;
        (await _userManager.IsInRoleAsync(user, GlobalRoles.SuperAdminNormalized)).Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdmin_IsRemovedEverywhere_WhateverTheStamp()
    {
        var superAdmin = await RoleAsync(GlobalRoles.SuperAdmin, null, "*");
        var user = await UserAsync(TenantA, "root@a.test");
        await _store.AssignToUserInTenantAsync(superAdmin.Id, user.Id, TenantA, "system");

        // Already held: a second grant elsewhere adds nothing.
        await _store.AssignToUserInTenantAsync(superAdmin.Id, user.Id, TenantB, "system");
        (await StampsAsync(user.Id, superAdmin.Id)).Should().Equal(TenantA);

        await _store.RemoveFromUserInTenantAsync(superAdmin.Id, user.Id, TenantB);
        (await StampsAsync(user.Id, superAdmin.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task PlatformOwnedRole_AppliesOnlyInThePlatformTenant()
    {
        var ops = await RoleAsync("PlatformOps", PlatformConstants.TenantId, "platform.ops");
        var user = await UserAsync(PlatformConstants.TenantId, "ops@platform.test");
        await _store.AssignToUserInTenantAsync(ops.Id, user.Id, PlatformConstants.TenantId, "system");

        (await _store.GetUserRolesInTenantAsync(user.Id, PlatformConstants.TenantId)).Select(r => r.Id).Should().Equal(ops.Id);
        (await _store.GetUserRolesInTenantAsync(user.Id, TenantB)).Should().BeEmpty();
        (await PermittedFromDatabaseAsync(user.Id, TenantB, "platform.ops")).Should().BeFalse();

        var assignElsewhere = () => _store.AssignToUserInTenantAsync(ops.Id, user.Id, TenantB, "admin");
        await assignElsewhere.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TenantOwnedRole_CannotBeAssignedUnderAnotherTenant()
    {
        var viewer = await RoleAsync("Viewer", TenantB);
        var user = await UserAsync(TenantA, "ada@a.test");

        var explicitElsewhere = () => _store.AssignToUserInTenantAsync(viewer.Id, user.Id, TenantA, "admin");
        await explicitElsewhere.Should().ThrowAsync<InvalidOperationException>();

        _tenant.TenantId = TenantA;
        var ambientElsewhere = () => _store.AssignToUserAsync(viewer.Id, user.Id, "admin");
        await ambientElsewhere.Should().ThrowAsync<InvalidOperationException>();

        (await _context.UserRoles.CountAsync()).Should().Be(0);

        _tenant.TenantId = TenantB;
        await _store.AssignToUserAsync(viewer.Id, user.Id, "admin");
        (await StampsAsync(user.Id, viewer.Id)).Should().Equal(TenantB);
    }

    [Fact]
    public async Task AtPlatformScope_APlatformRoleIsAssignedInTheUsersHomeTenant()
    {
        var hrAdmin = await RoleAsync("HRAdmin", null, "hr.*");
        var user = await UserAsync(TenantA, "ada@a.test");

        _tenant.TenantId = PlatformConstants.TenantId;
        await _store.AssignToUserAsync(hrAdmin.Id, user.Id, "superadmin");

        (await StampsAsync(user.Id, hrAdmin.Id)).Should().Equal(TenantA);
    }

    [Fact]
    public async Task The_profile_scope_releases_the_username_as_preferred_username()
    {
        var user = await UserAsync("t-home", "ada@example.com");

        var claims = await TokenClaimsAsync(user.Id, "t-home", "openid", "profile");

        claims.Should().ContainSingle(c => c.Type == "preferred_username").Which.Value.Should().Be(user.UserName);
        (await TokenClaimsAsync(user.Id, "t-home", "openid")).Should().NotContain(c => c.Type == "preferred_username");
    }
}
