using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Sample:SeedTestData=false seeds only what a working installation needs, with the platform
/// administrator taken from configuration instead of the published test accounts.
/// </summary>
public class SampleSeedingTests
{
    private const string AdminEmail = "owner@example.org";
    private const string AdminPassword = "Kq7#vRm2!xLp9wZt";

    [Fact]
    public async Task TestDataOff_SeedsOnlyTheConfiguredAdministratorAndTheNeededClients()
    {
        await using var factory = new SeedingFactory(AdminEmail, AdminPassword);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();

        var emails = await db.Users.IgnoreQueryFilters().Select(u => u.Email).ToListAsync();
        emails.Should().BeEquivalentTo(new[] { AdminEmail });

        var admin = await db.Users.IgnoreQueryFilters().SingleAsync();
        var superAdminRoleId = await db.Roles.IgnoreQueryFilters().Where(r => r.Name == "SuperAdmin").Select(r => r.Id).SingleAsync();
        (await db.Set<OlusoUserRole>().IgnoreQueryFilters().AnyAsync(ur => ur.UserId == admin.Id && ur.RoleId == superAdminRoleId))
            .Should().BeTrue();
        new PasswordHasher<OlusoUser>().VerifyHashedPassword(admin, admin.PasswordHash!, AdminPassword)
            .Should().NotBe(PasswordVerificationResult.Failed);

        var clientIds = await db.Clients.IgnoreQueryFilters().Select(c => c.ClientId).ToListAsync();
        clientIds.Should().Contain(new[] { "admin-portal", "workspace-portal", "account-ui" });
        clientIds.Should().NotContain(new[] { "test-client", "cc-client", "ropc-client", "device-client", "interactive-test-client" });
    }

    [Fact]
    public async Task TestDataOff_WithoutAdministratorSettings_FailsToStart()
    {
        await using var factory = new SeedingFactory(email: null, password: null);

        var start = () => factory.Services;

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*Sample:SuperAdminEmail and Sample:SuperAdminPassword*");
    }

    [Fact]
    public async Task TestDataOff_WithAPasswordThePolicyRejects_FailsToStart()
    {
        await using var factory = new SeedingFactory(AdminEmail, "short");

        var start = () => factory.Services;

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*Sample:SuperAdminPassword doesn't meet the password policy*");
    }

    private sealed class SeedingFactory(string? email, string? password) : OlusoWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Sample:SeedTestData", "false");
            if (email != null) builder.UseSetting("Sample:SuperAdminEmail", email);
            if (password != null) builder.UseSetting("Sample:SuperAdminPassword", password);
        }
    }
}
