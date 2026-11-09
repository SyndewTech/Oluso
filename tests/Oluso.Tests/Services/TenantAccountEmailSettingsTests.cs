using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Dto.Settings;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// Account email lifetimes are tenant settings: a tenant's own section wins, then the
/// host's Oluso:AccountEmailSettings, then the product defaults. Reset and confirmation
/// links are validated against the tenant's lifetime.
/// </summary>
public class TenantAccountEmailSettingsTests
{
    public sealed class User
    {
        public string Id { get; set; } = "u1";
    }

    private static Mock<UserManager<User>> Users()
    {
        var manager = new Mock<UserManager<User>>(Mock.Of<IUserStore<User>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        manager.Setup(m => m.GetUserIdAsync(It.IsAny<User>())).ReturnsAsync("u1");
        return manager;
    }

    private static TenantAccountLinkTokenProvider<User> Provider(IDataProtectionProvider protection, int lifetimeMinutes)
    {
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetAccountEmailSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountEmailSettings { AccountLinkLifetimeMinutes = lifetimeMinutes });
        return new TenantAccountLinkTokenProvider<User>(protection, Options.Create(new DataProtectionTokenProviderOptions()),
            NullLogger<DataProtectorTokenProvider<User>>.Instance, settings.Object);
    }

    [Fact]
    public async Task Links_are_validated_against_the_tenants_lifetime()
    {
        var protection = new EphemeralDataProtectionProvider();
        var users = Users();
        var token = await Provider(protection, 60).GenerateAsync("ResetPassword", users.Object, new User());

        (await Provider(protection, 60).ValidateAsync("ResetPassword", token, users.Object, new User())).Should().BeTrue();
        (await Provider(protection, 0).ValidateAsync("ResetPassword", token, users.Object, new User())).Should().BeFalse(
            "a tenant lifetime the token has outlived rejects it");
        (await Provider(protection, 60).ValidateAsync("EmailConfirmation", token, users.Object, new User())).Should().BeFalse(
            "the purpose still has to match");
    }

    private static DefaultTenantSettingsProvider SettingsProvider(TenantAccountEmailSettings? tenantSection, Dictionary<string, string?>? hostConfig = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.HasTenant).Returns(true);
        tenant.SetupGet(t => t.TenantId).Returns("t1");
        var config = new Mock<ITenantConfigurationService>();
        config.Setup(c => c.GetSectionAsync<TenantAccountEmailSettings>("t1", TenantAccountEmailSettings.SectionKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantSection);
        return new DefaultTenantSettingsProvider(tenant.Object,
            new ConfigurationBuilder().AddInMemoryCollection(hostConfig ?? new()).Build(), config.Object,
            new Mock<ITenantStore>().Object);
    }

    [Fact]
    public async Task The_tenants_section_wins_then_host_config_then_defaults()
    {
        (await SettingsProvider(new TenantAccountEmailSettings { MfaCodeLifetimeMinutes = 3 }).GetAccountEmailSettingsAsync())
            .MfaCodeLifetimeMinutes.Should().Be(3);

        var host = new Dictionary<string, string?> { ["Oluso:AccountEmailSettings:InvitationLifetimeDays"] = "14" };
        (await SettingsProvider(null, host).GetAccountEmailSettingsAsync()).InvitationLifetimeDays.Should().Be(14);
        (await SettingsProvider(null, host).GetAccountEmailSettingsAsync("t1")).InvitationLifetimeDays.Should().Be(14);

        var defaults = await SettingsProvider(null).GetAccountEmailSettingsAsync();
        (defaults.AccountLinkLifetimeMinutes, defaults.MfaCodeLifetimeMinutes, defaults.InvitationLifetimeDays).Should().Be((1440, 10, 7));
    }

    [Theory]
    [InlineData(1440, 10, 7, null)]
    [InlineData(4, 10, 7, "Account link")]
    [InlineData(1440, 61, 7, "MFA code")]
    [InlineData(1440, 10, 0, "Invitation")]
    public void Admin_settings_reject_out_of_range_lifetimes(int link, int mfa, int invite, string? error)
    {
        var dto = new AccountEmailSettingsDto { AccountLinkLifetimeMinutes = link, MfaCodeLifetimeMinutes = mfa, InvitationLifetimeDays = invite };

        if (error == null) dto.Validate().Should().BeNull();
        else dto.Validate().Should().StartWith(error);
    }

    [Theory]
    [InlineData(10, "10 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(1440, "24 hours")]
    [InlineData(10080, "7 days")]
    public void Lifetimes_read_naturally(int minutes, string text) =>
        Durations.Describe(TimeSpan.FromMinutes(minutes)).Should().Be(text);
}
