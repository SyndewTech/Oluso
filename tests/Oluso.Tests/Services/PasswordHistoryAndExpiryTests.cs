using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Authentication;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Identity;
using Oluso.EntityFramework.Services;
using Oluso.EntityFramework.Stores;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// The tenant's password history refuses the last N passwords, keeps only N hashes, and every
/// password set stamps when it changed. With an expiry, a password older than the policy allows
/// still proves who the user is but signs them in to a forced change, never a session.
/// </summary>
public sealed class PasswordHistoryAndExpiryTests : IDisposable
{
    private const string Tenant = "acme";

    private readonly SqliteConnection _connection = IdentityTestFactory.OpenSqliteConnection();
    private readonly TestTenantContext _tenantContext = new() { TenantId = Tenant };
    private readonly OlusoDbContext _db;
    private TenantPasswordSettings _settings = new()
    {
        MinimumLength = 1,
        RequireDigit = false,
        RequireLowercase = false,
        RequireUppercase = false,
        RequireNonAlphanumeric = false,
        RequiredUniqueChars = 0,
        BlockCommonPasswords = false
    };

    public PasswordHistoryAndExpiryTests()
    {
        _db = IdentityTestFactory.CreateSqliteContext(_connection, _tenantContext);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private ITenantSettingsProvider Settings()
    {
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetPasswordSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        settings.Setup(s => s.GetPasswordSettingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        return settings.Object;
    }

    private OlusoUserManager Users()
    {
        var history = new PasswordHistoryStore(_db);
        var settings = Settings();
        var validator = new TenantPasswordValidator(
            settings, NullLogger<TenantPasswordValidator>.Instance, Mock.Of<IHttpClientFactory>(), history);

        return new OlusoUserManager(
            new OlusoUserStore<OlusoDbContext>(_db, _tenantContext),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<OlusoUser>(),
            Array.Empty<IUserValidator<OlusoUser>>(),
            new IPasswordValidator<OlusoUser>[] { validator },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            NullLogger<UserManager<OlusoUser>>.Instance,
            Mock.Of<ITenantStore>(),
            settings,
            history);
    }

    private async Task<OlusoUser> CreateUserAsync(OlusoUserManager users, string password)
    {
        var email = $"{Guid.NewGuid():N}@acme.test";
        var user = new OlusoUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            TenantId = Tenant,
            IsActive = true
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return user;
    }

    private Task<int> HistoryRows(string userId) => _db.PasswordHistory.CountAsync(e => e.UserId == userId);

    [Fact]
    public async Task A_password_from_the_history_is_rejected_as_reused()
    {
        _settings.PasswordHistoryCount = 3;
        var users = Users();
        var user = await CreateUserAsync(users, "first-password");
        (await users.ChangePasswordAsync(user, "first-password", "second-password")).Succeeded.Should().BeTrue();

        var reuse = await users.ChangePasswordAsync(user, "second-password", "first-password");

        reuse.Succeeded.Should().BeFalse();
        reuse.Errors.Should().ContainSingle(e => e.Code == "PasswordReused")
            .Which.Description.Should().Contain("3");
        (await users.CheckPasswordAsync(user, "second-password")).Should().BeTrue();
    }

    [Fact]
    public async Task Only_the_configured_number_of_passwords_is_kept_so_older_ones_become_usable()
    {
        _settings.PasswordHistoryCount = 2;
        var users = Users();
        var user = await CreateUserAsync(users, "password-a");
        (await users.ChangePasswordAsync(user, "password-a", "password-b")).Succeeded.Should().BeTrue();
        (await users.ChangePasswordAsync(user, "password-b", "password-c")).Succeeded.Should().BeTrue();

        (await HistoryRows(user.Id)).Should().Be(2);
        (await users.ChangePasswordAsync(user, "password-c", "password-b")).Succeeded.Should().BeFalse();
        (await users.ChangePasswordAsync(user, "password-c", "password-a")).Succeeded.Should().BeTrue();
        (await HistoryRows(user.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Every_password_set_stamps_when_it_changed()
    {
        var users = Users();
        var user = await CreateUserAsync(users, "password-a");
        user.PasswordChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        user.PasswordChangedAt = DateTime.UtcNow.AddDays(-200);
        await users.UpdateAsync(user);
        (await users.ChangePasswordAsync(user, "password-a", "password-b")).Succeeded.Should().BeTrue();

        (await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).PasswordChangedAt
            .Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Without_a_history_policy_no_hashes_are_kept()
    {
        var users = Users();
        var user = await CreateUserAsync(users, "password-a");
        (await users.ChangePasswordAsync(user, "password-a", "password-b")).Succeeded.Should().BeTrue();

        (await HistoryRows(user.Id)).Should().Be(0);
    }

    [Fact]
    public async Task With_an_expiry_the_current_password_cannot_simply_be_set_again()
    {
        _settings.PasswordExpirationDays = 90;
        var users = Users();
        var user = await CreateUserAsync(users, "password-a");

        var again = await users.ChangePasswordAsync(user, "password-a", "password-a");

        again.Errors.Should().ContainSingle(e => e.Code == "PasswordReused");
    }

    private IdentityUserService UserService(OlusoUserManager users)
    {
        var signIn = new SignInManager<OlusoUser>(
            users,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<OlusoUser>>().Object,
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<OlusoUser>>.Instance,
            new Mock<IAuthenticationSchemeProvider>().Object,
            new Mock<IUserConfirmation<OlusoUser>>().Object);

        return new IdentityUserService(users, signIn, _tenantContext, Settings(), _db, NullLogger<IdentityUserService>.Instance);
    }

    private async Task<OlusoUser> UserWithPasswordChangedDaysAgo(OlusoUserManager users, int days)
    {
        var user = await CreateUserAsync(users, "password-a");
        user.PasswordChangedAt = DateTime.UtcNow.AddDays(-days);
        await users.UpdateAsync(user);
        return user;
    }

    [Fact]
    public async Task An_expired_password_proves_identity_but_requires_a_change_instead_of_signing_in()
    {
        _settings.PasswordExpirationDays = 90;
        var users = Users();
        var user = await UserWithPasswordChangedDaysAgo(users, 91);

        var result = await UserService(users).ValidateCredentialsAsync(user.Email!, "password-a", Tenant);

        result.Succeeded.Should().BeFalse();
        result.PasswordExpired.Should().BeTrue();
        result.CredentialsVerified.Should().BeTrue();
        result.Error.Should().Be("password_expired");
        result.User!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task A_password_within_its_lifetime_signs_in()
    {
        _settings.PasswordExpirationDays = 90;
        var users = Users();
        var user = await UserWithPasswordChangedDaysAgo(users, 10);

        var result = await UserService(users).ValidateCredentialsAsync(user.Email!, "password-a", Tenant);

        result.Succeeded.Should().BeTrue();
        result.PasswordExpired.Should().BeFalse();
    }

    [Fact]
    public async Task A_wrong_password_on_an_expired_account_is_just_wrong()
    {
        _settings.PasswordExpirationDays = 90;
        var users = Users();
        var user = await UserWithPasswordChangedDaysAgo(users, 365);

        var result = await UserService(users).ValidateCredentialsAsync(user.Email!, "not-the-password", Tenant);

        result.PasswordExpired.Should().BeFalse();
        result.CredentialsVerified.Should().BeFalse();
    }

    [Fact]
    public void A_password_with_no_recorded_change_is_as_old_as_the_account()
    {
        var settings = new TenantPasswordSettings { PasswordExpirationDays = 30 };
        var now = DateTime.UtcNow;

        settings.IsPasswordExpired(null, now.AddDays(-31), now).Should().BeTrue();
        settings.IsPasswordExpired(null, now.AddDays(-5), now).Should().BeFalse();
        new TenantPasswordSettings().IsPasswordExpired(now.AddYears(-5), now.AddYears(-5), now).Should().BeFalse();
    }
}
