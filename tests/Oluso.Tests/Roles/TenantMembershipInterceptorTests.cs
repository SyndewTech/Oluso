using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Interceptors;
using Oluso.EntityFramework.Stores;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// TenantMembership is the single source of truth for tenant access: a new user
/// gets one for their home tenant, and a tenant role grant gives one in that
/// tenant. Platform-scoped roles (global, OrgAdmin) never create one.
/// </summary>
public class TenantMembershipInterceptorTests : IDisposable
{
    private const string Home = "tenant-home";
    private const string Other = "tenant-other";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly RoleStore _store;

    public TenantMembershipInterceptorTests()
    {
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant, new TenantMembershipInterceptor());
        _roleManager = IdentityTestFactory.CreateRoleManager(_context);
        var userManager = IdentityTestFactory.CreateUserManager(_context, _tenant);
        _store = new RoleStore(_roleManager, userManager, _context, _tenant);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private Task<List<TenantMembership>> MembershipsAsync(string userId) =>
        _context.TenantMemberships.AsNoTracking().Where(m => m.UserId == userId).ToListAsync();

    [Fact]
    public async Task A_new_user_gets_a_home_tenant_membership()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Home, "home@example.com");

        (await MembershipsAsync(user.Id)).Should().ContainSingle(m => m.TenantId == Home);
    }

    [Fact]
    public async Task A_tenant_role_grant_adds_a_membership_in_that_tenant()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Home, "grant@example.com");
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, "HRAdmin", tenantId: null);

        await _store.AssignToUserInTenantAsync(role.Id, user.Id, Other, "admin");

        (await MembershipsAsync(user.Id)).Select(m => m.TenantId).Should().BeEquivalentTo(new[] { Home, Other });
    }

    [Fact]
    public async Task Granting_again_or_with_an_existing_membership_does_not_duplicate()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Home, "dup@example.com");
        var first = await IdentityTestFactory.CreateRoleAsync(_roleManager, "HRAdmin", tenantId: null);
        var second = await IdentityTestFactory.CreateRoleAsync(_roleManager, "FinanceAdmin", tenantId: null);

        await _store.AssignToUserInTenantAsync(first.Id, user.Id, Other, "admin");
        await _store.AssignToUserInTenantAsync(second.Id, user.Id, Other, "admin");

        (await MembershipsAsync(user.Id)).Count(m => m.TenantId == Other).Should().Be(1);
    }

    [Theory]
    [InlineData(GlobalRoles.SuperAdmin)]
    [InlineData(EffectiveRoleResolver.OrgAdminRoleName)]
    public async Task Platform_roles_never_create_a_membership(string roleName)
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Home, $"{roleName}@example.com");
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, roleName, tenantId: null, isSystemRole: true);

        await _store.AssignToUserInTenantAsync(role.Id, user.Id, Other, "admin");

        (await MembershipsAsync(user.Id)).Should().NotContain(m => m.TenantId == Other);
    }
}
