using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Interceptors;
using Oluso.EntityFramework.Services;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// Runs against a context with the reserved-auth interceptor attached, which
/// is what rejected "Admin" and "*" on the ordinary write path and broke
/// tenant provisioning.
/// </summary>
public class TenantDefaultRoleSeederTests : IDisposable
{
    private readonly OlusoDbContext _context;
    private readonly TenantDefaultRoleSeeder _seeder;

    public TenantDefaultRoleSeederTests()
    {
        var options = new DbContextOptionsBuilder<OlusoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ReservedAuthInterceptor())
            .Options;
        _context = new OlusoDbContext(options);
        _seeder = new TenantDefaultRoleSeeder(_context, NullLogger<TenantDefaultRoleSeeder>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private Task<List<string>> PermissionsOf(string roleId) =>
        _context.RoleClaims
            .Where(c => c.RoleId == roleId && c.ClaimType == OlusoRole.PermissionClaimType)
            .Select(c => c.ClaimValue!)
            .ToListAsync();

    [Fact]
    public async Task Seed_CreatesAdminAndUser_PastTheReservedGuards()
    {
        var roles = await _seeder.SeedAsync("t1");

        roles.Admin.TenantId.Should().Be("t1");
        roles.Admin.Name.Should().Be("Admin");
        roles.Admin.Category.Should().Be("tenant");
        roles.Admin.ManagedByRole.Should().Be("SuperAdmin");
        roles.Admin.IsSystemRole.Should().BeFalse();
        (await PermissionsOf(roles.Admin.Id)).Should().BeEquivalentTo(new[] { "admin.access", "*" });

        roles.User.TenantId.Should().Be("t1");
        roles.User.ManagedByRole.Should().Be("Admin");
        (await PermissionsOf(roles.User.Id)).Should().BeEquivalentTo(new[] { "dashboard.view" });
    }

    /// <summary>Makes the database refuse role inserts once, the way Postgres did in production.</summary>
    private sealed class RejectRoleInsertsInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Reject { get; set; } = true;

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Reject && eventData.Context!.ChangeTracker.Entries<OlusoRole>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("An error occurred while saving the entity changes.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task A_failed_seed_lets_go_of_its_rows_so_the_half_made_tenant_can_be_removed()
    {
        var database = new RejectRoleInsertsInterceptor();
        var options = new DbContextOptionsBuilder<OlusoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ReservedAuthInterceptor(), database)
            .Options;
        await using var context = new OlusoDbContext(options);
        var tenants = new Oluso.EntityFramework.Stores.TenantStore(context);
        var tenant = await tenants.CreateAsync(new Tenant { Id = "t-new", Name = "New", Identifier = "new", OrganizationId = "org-1" });

        var seed = () => new TenantDefaultRoleSeeder(context, NullLogger<TenantDefaultRoleSeeder>.Instance).SeedAsync(tenant.Id);
        await seed.Should().ThrowAsync<DbUpdateException>();

        // Before the fix this delete re-tried the failed "Admin" role outside seed mode and the
        // reserved-role guard threw, leaving the tenant behind.
        database.Reject = false;
        await tenants.DeleteAsync(tenant.Id);

        (await context.Tenants!.AnyAsync(t => t.Id == tenant.Id)).Should().BeFalse();
        (await context.Roles.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenant.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Seed_IsIdempotent_AndBackfillsAMissingPermission()
    {
        var first = await _seeder.SeedAsync("t1");
        var starClaim = await _context.RoleClaims.SingleAsync(c => c.RoleId == first.Admin.Id && c.ClaimValue == "*");
        _context.RoleClaims.Remove(starClaim);
        using (_context.BeginSeedMode())
        {
            await _context.SaveChangesAsync();
        }

        var second = await _seeder.SeedAsync("t1");

        second.Admin.Id.Should().Be(first.Admin.Id);
        second.User.Id.Should().Be(first.User.Id);
        (await _context.Roles.IgnoreQueryFilters().CountAsync(r => r.TenantId == "t1")).Should().Be(2);
        (await PermissionsOf(first.Admin.Id)).Should().BeEquivalentTo(new[] { "admin.access", "*" });
    }

    [Fact]
    public async Task Seed_GivesEachTenantItsOwnRows()
    {
        var t1 = await _seeder.SeedAsync("t1");
        var t2 = await _seeder.SeedAsync("t2");

        t1.Admin.Id.Should().NotBe(t2.Admin.Id);
        (await _context.Roles.IgnoreQueryFilters().CountAsync(r => r.NormalizedName == "ADMIN")).Should().Be(2);
    }

    [Fact]
    public async Task Seed_LeavesTheGuardOnAfterwards()
    {
        await _seeder.SeedAsync("t1");

        _context.Roles.Add(new OlusoRole { Id = Guid.NewGuid().ToString(), Name = "Admin", NormalizedName = "ADMIN", TenantId = "t2" });
        var act = () => _context.SaveChangesAsync();

        await act.Should().ThrowAsync<ReservedRoleException>();
    }
}
