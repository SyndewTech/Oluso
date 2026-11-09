using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.UI;
using Xunit;

namespace Oluso.Tests.UI;

/// <summary>
/// Server pages show the tenant's branding icon, or say there is none so the browser doesn't ask the host for a
/// /favicon.ico it doesn't serve.
/// </summary>
public class TenantFaviconTests
{
    private static Mock<ITenantSettingsProvider> Branding(string? favicon)
    {
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetBrandingSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantBrandingSettings { FaviconUrl = favicon });
        return settings;
    }

    [Fact]
    public async Task The_tenants_icon_is_used_when_it_has_one()
    {
        (await TenantFavicon.HrefAsync(Branding("https://cdn.example.org/clinic.ico").Object, NullLogger.Instance))
            .Should().Be("https://cdn.example.org/clinic.ico");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_tenant_without_an_icon_gets_an_explicit_empty_one(string? favicon)
    {
        (await TenantFavicon.HrefAsync(Branding(favicon).Object, NullLogger.Instance)).Should().Be(TenantFavicon.None);
    }

    [Fact]
    public async Task Branding_that_fails_to_load_leaves_the_page_without_an_icon()
    {
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetBrandingSettingsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("store down"));

        (await TenantFavicon.HrefAsync(settings.Object, NullLogger.Instance)).Should().Be(TenantFavicon.None);
    }
}
