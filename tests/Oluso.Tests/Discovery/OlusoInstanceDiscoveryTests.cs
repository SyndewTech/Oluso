using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Oluso.Controllers;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Tests.Discovery;

/// <summary>
/// <c>/.well-known/oluso-instance</c>: the platform's branding comes from <c>Oluso:Branding</c> (else the older
/// <c>Oluso:Platform</c>), and <c>platformName</c> stays the platform's product name on a tenant host, where the
/// instance and display names are the tenant's.
/// </summary>
public class OlusoInstanceDiscoveryTests
{
    private static async Task<JsonElement> GetAsync(Dictionary<string, string?> config, Tenant? tenant = null)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(t => t.Tenant).Returns(tenant);
        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync("https://acme.example.com");
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetBrandingSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TenantBrandingSettings());

        var controller = new OlusoInstanceDiscoveryController(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(),
            issuer.Object, tenantContext.Object, settings.Object, Mock.Of<IJourneyPolicyStore>());
        var result = await controller.Get(CancellationToken.None);
        var value = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        return JsonSerializer.SerializeToElement(value);
    }

    [Fact]
    public async Task Branding_config_names_the_platform()
    {
        var json = await GetAsync(new()
        {
            ["Oluso:Branding:ProductName"] = "Acme Suite",
            ["Oluso:Branding:CompanyName"] = "Syndew Technology Ltd",
            ["Oluso:Platform:ProductName"] = "Older name",
        });

        json.GetProperty("platformName").GetString().Should().Be("Acme Suite");
        json.GetProperty("displayName").GetString().Should().Be("Acme Suite");
        json.GetProperty("instanceName").GetString().Should().Be("Syndew Technology Ltd");
    }

    [Fact]
    public async Task The_older_platform_keys_still_apply()
    {
        var json = await GetAsync(new() { ["Oluso:Platform:ProductName"] = "Acme ID" });

        json.GetProperty("platformName").GetString().Should().Be("Acme ID");
        json.GetProperty("displayName").GetString().Should().Be("Acme ID");
    }

    [Fact]
    public async Task On_a_tenant_host_the_platform_name_is_never_the_tenants()
    {
        var json = await GetAsync(new() { ["Oluso:Branding:ProductName"] = "Acme Suite" },
            new Tenant { Id = "t1", Name = "Tim Hortons", Identifier = "tim", Enabled = true });

        json.GetProperty("platformName").GetString().Should().Be("Acme Suite");
        json.GetProperty("displayName").GetString().Should().Be("Tim Hortons");
        json.GetProperty("instanceName").GetString().Should().Be("Tim Hortons");
    }

    [Fact]
    public async Task Without_a_product_name_the_platform_name_is_null()
    {
        var json = await GetAsync(new());

        json.GetProperty("platformName").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("instanceName").GetString().Should().Be("Oluso");
    }
}
