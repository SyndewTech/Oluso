using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// Role membership is keyed by role id. A tenant row and a platform-wide row
/// may share a name (module admin roles did for a long time), and none of the
/// membership queries may resolve that name back to a single row.
/// </summary>
public class RoleStoreTests : IDisposable
{
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly Oluso.EntityFramework.Stores.RoleStore _store;

    public RoleStoreTests()
    {
        _context = IdentityTestFactory.CreateContext();
        _roleManager = IdentityTestFactory.CreateRoleManager(_context);
        var userManager = IdentityTestFactory.CreateUserManager(_context);
        _store = new Oluso.EntityFramework.Stores.RoleStore(_roleManager, userManager, _context, new TestTenantContext());
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetUsersInRole_CountsByRoleId_WhenATenantRowSharesItsNameWithAPlatformRow()
    {
        var platformRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "HRAdmin", tenantId: null, isSystemRole: true);
        var tenantRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "HRAdmin", tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");

        await _store.AssignToUserAsync(tenantRow.Id, user.Id, "admin-1");

        (await _store.GetUsersInRoleAsync(tenantRow.Id)).Should().Be(1);
        (await _store.GetUsersInRoleAsync(platformRow.Id)).Should().Be(0);

        var holders = (await _store.GetUsersByRoleAsync(tenantRow.Id)).ToList();
        holders.Should().ContainSingle(h => h.Id == user.Id && h.TenantId == "t1" && h.IsActive);
        (await _store.GetUsersByRoleAsync(platformRow.Id)).Should().BeEmpty();

        var row = await _context.UserRoles.SingleAsync(ur => ur.UserId == user.Id);
        row.RoleId.Should().Be(tenantRow.Id);
        row.AssignedBy.Should().Be("admin-1");
    }

    [Fact]
    public async Task AssignToUser_IsIdempotent()
    {
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");

        await _store.AssignToUserAsync(role.Id, user.Id, "admin-1");
        await _store.AssignToUserAsync(role.Id, user.Id, "admin-2");

        (await _context.UserRoles.CountAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id)).Should().Be(1);
        (await _context.UserRoles.SingleAsync(ur => ur.UserId == user.Id)).AssignedBy.Should().Be("admin-1");
    }

    [Fact]
    public async Task RemoveFromUser_RemovesOnlyThatRow()
    {
        var platformRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "HRAdmin", tenantId: null, isSystemRole: true);
        var tenantRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "HRAdmin", tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");
        await _store.AssignToUserAsync(platformRow.Id, user.Id);
        await _store.AssignToUserAsync(tenantRow.Id, user.Id);

        await _store.RemoveFromUserAsync(tenantRow.Id, user.Id);

        var remaining = await _context.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync();
        remaining.Should().Equal(platformRow.Id);
    }

    [Fact]
    public async Task GetUsersByRole_FallsBackToFullName_WhenDisplayNameIsMissing()
    {
        var role = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", "t1");
        var named = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test", firstName: "Ada", lastName: "Lovelace");
        var anonymous = await IdentityTestFactory.CreateUserAsync(_context, "t1", "nobody@t1.test");
        await _store.AssignToUserAsync(role.Id, named.Id);
        await _store.AssignToUserAsync(role.Id, anonymous.Id);

        var holders = (await _store.GetUsersByRoleAsync(role.Id)).ToDictionary(h => h.Id);

        holders[named.Id].DisplayName.Should().Be("Ada Lovelace");
        holders[anonymous.Id].DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task GetByIds_ReturnsRowsAcrossTenants_ForExplicitIds()
    {
        var t1Row = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", tenantId: "t1");
        var t2Row = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", tenantId: "t2");
        var platformRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Auditor", tenantId: null, isSystemRole: true);

        var rows = await _store.GetByIdsAsync(new[] { t1Row.Id, t2Row.Id, platformRow.Id, "missing", " " });

        rows.Select(r => r.Id).Should().BeEquivalentTo(new[] { t1Row.Id, t2Row.Id, platformRow.Id });
        (await _store.GetByIdsAsync(Array.Empty<string>())).Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_NoLongerRefusesPlatformRows_PolicyLivesInTheControllers()
    {
        var platformRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "LegacyAdmin", tenantId: null, isSystemRole: true);

        await _store.DeleteAsync(platformRow.Id);

        (await _store.GetByIdAsync(platformRow.Id)).Should().BeNull();
    }
}
