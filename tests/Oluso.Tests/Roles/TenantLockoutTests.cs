using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Users;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Identity;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// Failed sign-ins lock an account by the user's own tenant policy (attempts and duration),
/// with the platform default for users whose tenant has none. Admins can end a lockout, or
/// lock an account, which also ends its sessions.
/// </summary>
public sealed class TenantLockoutTests : IDisposable
{
    private readonly SqliteConnection _connection = IdentityTestFactory.OpenSqliteConnection();
    private readonly OlusoDbContext _db;
    private readonly Mock<ITenantStore> _tenants = new();

    public TenantLockoutTests()
    {
        _db = IdentityTestFactory.CreateSqliteContext(_connection, new TestTenantContext());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private OlusoUserManager UserManager() => new(
        new OlusoUserStore<OlusoDbContext>(_db, new TestTenantContext()),
        Options.Create(new IdentityOptions { Lockout = { MaxFailedAccessAttempts = 5, DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15) } }),
        new PasswordHasher<OlusoUser>(),
        Array.Empty<IUserValidator<OlusoUser>>(),
        Array.Empty<IPasswordValidator<OlusoUser>>(),
        new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(),
        services: null!,
        NullLogger<UserManager<OlusoUser>>.Instance,
        _tenants.Object,
        new Mock<ITenantSettingsProvider>().Object,
        new Mock<IPasswordHistoryStore>().Object);

    private async Task<OlusoUser> UserIn(string tenantId)
    {
        var user = await IdentityTestFactory.CreateUserAsync(_db, tenantId, $"{Guid.NewGuid():N}@acme.test");
        user.LockoutEnabled = true;
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task The_users_tenant_policy_decides_attempts_and_duration()
    {
        _tenants.Setup(t => t.GetByIdAsync("acme", It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant
        {
            Id = "acme",
            Name = "Acme",
            Identifier = "acme",
            PasswordPolicy = new TenantPasswordPolicy { TenantId = "acme", MaxFailedAttempts = 3, LockoutDurationMinutes = 30 }
        });
        var users = UserManager();
        var user = await UserIn("acme");

        await users.AccessFailedAsync(user);
        await users.AccessFailedAsync(user);
        (await users.IsLockedOutAsync(user)).Should().BeFalse();

        await users.AccessFailedAsync(user);

        (await users.IsLockedOutAsync(user)).Should().BeTrue();
        user.LockoutEnd.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromMinutes(1));
        user.AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task Without_a_tenant_policy_the_platform_default_applies()
    {
        var users = UserManager();
        var user = await UserIn("no-policy");

        for (var i = 0; i < 4; i++) await users.AccessFailedAsync(user);
        (await users.IsLockedOutAsync(user)).Should().BeFalse();

        await users.AccessFailedAsync(user);

        (await users.IsLockedOutAsync(user)).Should().BeTrue();
        user.LockoutEnd.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
    }

    private static (UsersController Controller, Mock<IOlusoUserService> Users, Mock<IServerSideSessionStore> Sessions) Admin(
        string adminId = "admin-1", string userTenant = "acme")
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns("acme");
        tenant.Setup(t => t.HasTenant).Returns(true);
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", TenantId = userTenant });
        users.Setup(u => u.UnlockAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        users.Setup(u => u.LockAsync("u1", It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sessions = new Mock<IServerSideSessionStore>();

        var controller = new UsersController(
            tenant.Object, users.Object, Mock.Of<IOlusoEventService>(), Mock.Of<IRoleStore>(),
            Mock.Of<ITenantMembershipStore>(), Mock.Of<ITenantStore>(), Mock.Of<IModuleAdminAssignmentNotifier>(),
            NullLogger<UsersController>.Instance, sessionStore: sessions.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", adminId) }, "test"))
                }
            }
        };
        return (controller, users, sessions);
    }

    [Fact]
    public async Task An_admin_can_unlock_an_account()
    {
        var (controller, users, _) = Admin();

        var result = await controller.UnlockUser("u1", default);

        result.Result.Should().NotBeOfType<BadRequestObjectResult>();
        users.Verify(u => u.UnlockAsync("u1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Locking_an_account_ends_its_sessions()
    {
        var (controller, users, sessions) = Admin();
        var until = DateTimeOffset.UtcNow.AddHours(1);

        await controller.LockUser("u1", new LockUserRequest { LockoutEnd = until }, default);

        users.Verify(u => u.LockAsync("u1", until, It.IsAny<CancellationToken>()), Times.Once);
        sessions.Verify(s => s.DeleteSessionsBySubjectAsync("u1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_admin_cannot_lock_themselves_or_into_the_past()
    {
        var (self, users, _) = Admin(adminId: "u1");
        (await self.LockUser("u1", new LockUserRequest(), default)).Result.Should().BeOfType<BadRequestObjectResult>();

        var (controller, _, _) = Admin();
        (await controller.LockUser("u1", new LockUserRequest { LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-1) }, default))
            .Result.Should().BeOfType<BadRequestObjectResult>();

        users.Verify(u => u.LockAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Another_tenants_user_cannot_be_unlocked_or_locked_here()
    {
        var (controller, users, _) = Admin(userTenant: "globex");

        (await controller.UnlockUser("u1", default)).Result.Should().NotBeNull();
        (await controller.LockUser("u1", new LockUserRequest(), default)).Result.Should().NotBeNull();

        users.Verify(u => u.UnlockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        users.Verify(u => u.LockAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
