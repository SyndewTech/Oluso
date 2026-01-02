using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for the token endpoint.
/// </summary>
public class TokenEndpointTests : IntegrationTestBase
{
    public TokenEndpointTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Token_WithInvalidGrant_ReturnsError()
    {
        // Arrange - use an invalid grant type with a non-existent client
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "invalid_grant",
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert - server validates client first, so we get unauthorized_client
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // Client validation happens before grant type validation
        error!.RootElement.GetProperty("error").GetString().Should().BeOneOf(
            "unauthorized_client", "unsupported_grant_type");
    }

    [Fact]
    public async Task Token_WithInvalidClientCredentials_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "nonexistent-client",
            ["client_secret"] = "wrong-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error!.RootElement.GetProperty("error").GetString().Should().Be("invalid_client");
    }

    [Fact]
    public async Task Token_WithMissingClientId_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Token_WithMissingGrantType_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error!.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }
}
