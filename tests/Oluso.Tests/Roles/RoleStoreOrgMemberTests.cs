using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Stores;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// A user belongs to a tenant as their home tenant or through a TenantMembership.
/// Inside a tenant request the user query filter clamps to the home tenant, so
/// role holders who joined through a membership must still be reached.
/// </summary>
public class RoleStoreOrgMemberTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string TenantC = "tenant-c";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly RoleStore _store;

    public RoleStoreOrgMemberTests()
    {
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _roleManager = IdentityTestFactory.CreateRoleManager(_context);
        var userManager = IdentityTestFactory.CreateUserManager(_context);
        _store = new RoleStore(_roleManager, userManager, _context, _tenant);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<(OlusoRole Role, OlusoUser Home, OlusoUser Member, OlusoUser Outsider)> SeedAsync()
    {
        _tenant.TenantId = null;

        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", TenantB, category: "hr");
        var home = await IdentityTestFactory.CreateUserAsync(_context, TenantB, "home@b.test", displayName: "Home");
        var member = await IdentityTestFactory.CreateUserAsync(_context, TenantA, "member@a.test", displayName: "Member");
        var outsider = await IdentityTestFactory.CreateUserAsync(_context, TenantC, "outsider@c.test", displayName: "Outsider");

        _context.TenantMemberships.Add(new TenantMembership { TenantId = TenantB, UserId = member.Id });
        await _context.SaveChangesAsync();

        return (role, home, member, outsider);
    }

    [Fact]
    public async Task GetUsersByRole_InTenant_IncludesOrgMembers_AndExcludesHoldersElsewhere()
    {
        var (role, home, member, outsider) = await SeedAsync();
        var platformRole = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Auditor", tenantId: null, isSystemRole: true);
        await _store.AssignToUserInTenantAsync(role.Id, home.Id, TenantB, "admin");
        await _store.AssignToUserInTenantAsync(role.Id, member.Id, TenantB, "admin");
        await _store.AssignToUserInTenantAsync(platformRole.Id, member.Id, TenantB, "admin");
        // Held in C only: not a holder in B.
        await _store.AssignToUserInTenantAsync(platformRole.Id, outsider.Id, TenantC, "admin");

        _tenant.TenantId = TenantB;
        var holders = (await _store.GetUsersByRoleAsync(role.Id)).ToList();
        holders.Select(h => h.Id).Should().BeEquivalentTo(new[] { home.Id, member.Id });
        holders.Single(h => h.Id == member.Id).TenantId.Should().Be(TenantA);

        (await _store.GetUsersByRoleAsync(platformRole.Id)).Select(h => h.Id).Should().Equal(member.Id);
    }

    [Fact]
    public async Task GetUsersByRole_OnPlatformScope_ListsEveryHolder()
    {
        var (_, home, member, outsider) = await SeedAsync();
        var platformRole = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Auditor", tenantId: null, isSystemRole: true);
        await _store.AssignToUserInTenantAsync(platformRole.Id, home.Id, TenantB, "admin");
        await _store.AssignToUserInTenantAsync(platformRole.Id, member.Id, TenantA, "admin");
        await _store.AssignToUserInTenantAsync(platformRole.Id, outsider.Id, TenantC, "admin");

        _tenant.TenantId = PlatformConstants.TenantId;
        var holders = await _store.GetUsersByRoleAsync(platformRole.Id);

        holders.Select(h => h.Id).Should().BeEquivalentTo(new[] { home.Id, member.Id, outsider.Id });
    }

    [Fact]
    public async Task GetUsersInRole_InTenant_CountsOrgMembers()
    {
        var (role, _, member, _) = await SeedAsync();
        await _store.AssignToUserInTenantAsync(role.Id, member.Id, TenantB, "admin");

        _tenant.TenantId = TenantB;

        (await _store.GetUsersInRoleAsync(role.Id)).Should().Be(1);
    }
}
