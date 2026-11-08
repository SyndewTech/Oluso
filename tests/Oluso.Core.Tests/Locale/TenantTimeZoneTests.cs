using FluentAssertions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Locale;
using Xunit;

namespace Oluso.Core.Tests.Locale;

public class TenantTimeZoneTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    [Fact]
    public void Local_times_convert_across_daylight_saving()
    {
        London.ToUtc(new DateOnly(2026, 1, 15), new TimeOnly(9, 30)).Should().Be(new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc));
        London.ToUtc(new DateOnly(2026, 7, 15), new TimeOnly(9, 30)).Should().Be(new DateTime(2026, 7, 15, 8, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_time_skipped_by_the_clocks_going_forward_moves_past_the_gap()
    {
        // 29 March 2026: 01:00 GMT jumps to 02:00 BST, so 01:30 does not exist.
        London.ToUtc(new DateOnly(2026, 3, 29), new TimeOnly(1, 30)).Should().Be(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Today_is_the_local_calendar_day()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        tokyo.Today(new DateTime(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public async Task There_is_no_fallback_when_the_tenant_has_not_set_a_time_zone()
    {
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetLocaleSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new LocaleSettings());

        (await settings.Object.FindTimeZoneAsync()).Should().BeNull();
        await settings.Object.Invoking(s => s.GetTimeZoneAsync()).Should().ThrowAsync<TenantTimeZoneNotSetException>();
    }

    [Fact]
    public async Task A_named_tenant_time_zone_is_read_from_its_locale_section()
    {
        var config = new Mock<ITenantConfigurationService>();
        config.Setup(c => c.GetSectionAsync<LocaleSettings>("t1", "Locale", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocaleSettings { Timezone = "Europe/London" });

        (await config.Object.FindTimeZoneAsync("t1"))!.Id.Should().Be("Europe/London");
    }

    [Fact]
    public async Task An_unknown_time_zone_id_fails_loudly()
    {
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetLocaleSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new LocaleSettings { Timezone = "Mars/Olympus" });

        await settings.Object.Invoking(s => s.FindTimeZoneAsync()).Should().ThrowAsync<InvalidOperationException>();
    }
}
