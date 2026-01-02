using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for SCIM 2.0 endpoints.
/// </summary>
public class ScimEndpointTests : IntegrationTestBase
{
    public ScimEndpointTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ServiceProviderConfig_ReturnsValidDocument()
    {
        // Act
        var response = await Client.GetAsync("/scim/v2/ServiceProviderConfig");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var config = await response.Content.ReadFromJsonAsync<JsonDocument>();
        config.Should().NotBeNull();

        var root = config!.RootElement;
        root.TryGetProperty("schemas", out var schemas).Should().BeTrue();
        schemas.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ResourceTypes_ReturnsUserAndGroup()
    {
        // Act
        var response = await Client.GetAsync("/scim/v2/ResourceTypes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonDocument>();
        result.Should().NotBeNull();

        var root = result!.RootElement;
        root.TryGetProperty("Resources", out var resources).Should().BeTrue();

        var resourceNames = resources.EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .ToList();

        resourceNames.Should().Contain("User");
    }

    [Fact]
    public async Task Schemas_ReturnsScimSchemas()
    {
        // Act
        var response = await Client.GetAsync("/scim/v2/Schemas");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonDocument>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Users_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await Client.GetAsync("/scim/v2/Users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUser_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var userRequest = new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = "testuser@example.com",
            name = new
            {
                givenName = "Test",
                familyName = "User"
            },
            emails = new[]
            {
                new { value = "testuser@example.com", primary = true }
            }
        };

        // Act
        var response = await Client.PostAsJsonAsync("/scim/v2/Users", userRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
