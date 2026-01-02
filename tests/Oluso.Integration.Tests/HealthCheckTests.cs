using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for health check and info endpoints.
/// </summary>
public class HealthCheckTests : IntegrationTestBase
{
    public HealthCheckTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<JsonDocument>();
        health.Should().NotBeNull();

        var root = health!.RootElement;
        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Info_ReturnsApplicationInfo()
    {
        // Act
        var response = await Client.GetAsync("/info");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var info = await response.Content.ReadFromJsonAsync<JsonDocument>();
        info.Should().NotBeNull();

        var root = info!.RootElement;
        root.TryGetProperty("name", out _).Should().BeTrue();
        root.TryGetProperty("version", out _).Should().BeTrue();
        root.TryGetProperty("discovery", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Info_IncludesEnterpriseFeatures()
    {
        // Act
        var response = await Client.GetAsync("/info");
        var info = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = info!.RootElement;

        // Assert
        root.TryGetProperty("enterprise", out var enterprise).Should().BeTrue();

        enterprise.TryGetProperty("fido2", out _).Should().BeTrue();
        enterprise.TryGetProperty("ldap", out _).Should().BeTrue();
        enterprise.TryGetProperty("saml", out _).Should().BeTrue();
        enterprise.TryGetProperty("scim", out _).Should().BeTrue();
    }
}
