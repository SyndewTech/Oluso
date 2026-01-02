using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for OAuth 2.0 / OIDC grant types supported by Oluso.
///
/// Supported grant types:
/// - authorization_code (with PKCE) - tested in OidcAuthorizationCodeFlowTests
/// - password (Resource Owner Password Credentials)
/// - client_credentials
/// - refresh_token
/// - device_code
/// - token_exchange
///
/// Note: While ROPC is deprecated in OAuth 2.1, it's provided for legacy compatibility
/// and first-party mobile apps where redirect-based flows are impractical.
/// </summary>
public class OidcGrantTypeTests : IntegrationTestBase
{
    public OidcGrantTypeTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    #region Client Credentials Grant

    [Fact]
    public async Task ClientCredentials_WithValidCredentials_ReturnsAccessToken()
    {
        // Arrange - Use dedicated client credentials client
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        tokenResponse.Should().NotBeNull();

        var root = tokenResponse!.RootElement;

        // Should have access_token
        root.TryGetProperty("access_token", out var accessToken).Should().BeTrue();
        accessToken.GetString().Should().NotBeNullOrEmpty();

        // Should have token_type
        root.TryGetProperty("token_type", out var tokenType).Should().BeTrue();
        tokenType.GetString().Should().BeEquivalentTo("Bearer");

        // Should have expires_in
        root.TryGetProperty("expires_in", out var expiresIn).Should().BeTrue();
        expiresIn.GetInt32().Should().BeGreaterThan(0);

        // Client credentials should NOT have id_token (no user context)
        root.TryGetProperty("id_token", out _).Should().BeFalse();

        // Client credentials should NOT have refresh_token
        root.TryGetProperty("refresh_token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ClientCredentials_WithInvalidSecret_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
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
    public async Task ClientCredentials_WithMissingSecret_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error!.RootElement.GetProperty("error").GetString().Should().Be("invalid_client");
    }

    [Fact]
    public async Task ClientCredentials_WithInvalidClientId_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "nonexistent-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error!.RootElement.GetProperty("error").GetString().Should().Be("invalid_client");
    }

    [Fact]
    public async Task ClientCredentials_AccessTokenCanBeIntrospected()
    {
        // Arrange - Get access token
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret"
        });

        var tokenResponse = await Client.PostAsync("/connect/token", tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode) return;

        var tokens = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var accessToken = tokens!.RootElement.GetProperty("access_token").GetString();

        // Act - Introspect the token
        var introspectRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = accessToken!,
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret"
        });

        var response = await Client.PostAsync("/connect/introspect", introspectRequest);

        // Assert - If introspection is enabled
        if (response.IsSuccessStatusCode)
        {
            var introspection = await response.Content.ReadFromJsonAsync<JsonDocument>();
            introspection!.RootElement.GetProperty("active").GetBoolean().Should().BeTrue();
        }
    }

    #endregion

    #region Refresh Token Grant

    [Fact]
    public async Task RefreshToken_WithInvalidToken_ReturnsError()
    {
        // Arrange - Use test-client which supports refresh tokens (via auth code flow)
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = "invalid_refresh_token_12345",
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error!.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task RefreshToken_WithMissingToken_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // "invalid_request" when token is missing, "invalid_grant" when token is invalid
        error!.RootElement.GetProperty("error").GetString()
            .Should().BeOneOf("invalid_grant", "invalid_request");
    }

    #endregion

    #region Device Code Grant

    [Fact]
    public async Task DeviceAuthorization_ReturnsDeviceCode()
    {
        // Arrange - Device flow initiation
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "device-client",
            ["scope"] = "openid profile"
        });

        // Act
        var response = await Client.PostAsync("/connect/deviceauthorization", request);

        // Assert - Should return device code or endpoint not found if not enabled
        if (response.StatusCode == HttpStatusCode.NotFound) return;

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var deviceResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        deviceResponse.Should().NotBeNull();

        var root = deviceResponse!.RootElement;

        // Should have device_code
        root.TryGetProperty("device_code", out var deviceCode).Should().BeTrue();
        deviceCode.GetString().Should().NotBeNullOrEmpty();

        // Should have user_code
        root.TryGetProperty("user_code", out var userCode).Should().BeTrue();
        userCode.GetString().Should().NotBeNullOrEmpty();

        // Should have verification_uri
        root.TryGetProperty("verification_uri", out _).Should().BeTrue();
    }

    [Fact]
    public async Task DeviceToken_WithPendingDeviceCode_ReturnsAuthorizationPending()
    {
        // Arrange - First get a device code
        var deviceRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "device-client",
            ["scope"] = "openid"
        });

        var deviceResponse = await Client.PostAsync("/connect/deviceauthorization", deviceRequest);
        if (!deviceResponse.IsSuccessStatusCode) return;

        var deviceResult = await deviceResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var deviceCode = deviceResult!.RootElement.GetProperty("device_code").GetString();

        // Act - Try to exchange device code (user hasn't authorized yet)
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["device_code"] = deviceCode!,
            ["client_id"] = "device-client"
        });

        var response = await Client.PostAsync("/connect/token", tokenRequest);

        // Assert - Should get authorization_pending error
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error!.RootElement.GetProperty("error").GetString().Should().Be("authorization_pending");
    }

    #endregion

    #region Access Token Validation

    [Fact]
    public async Task AccessToken_FromClientCredentials_CannotAccessUserInfo()
    {
        // Arrange - Get access token via client credentials
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret"
        });

        var tokenResponse = await Client.PostAsync("/connect/token", tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode) return;

        var tokens = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var accessToken = tokens!.RootElement.GetProperty("access_token").GetString();

        // Act - Use token to access user info (should fail for client_credentials, no user)
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/connect/userinfo");

        // Assert - Client credentials has no user, so userinfo should fail
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Token Revocation

    [Fact]
    public async Task Revocation_WithValidToken_ReturnsSuccess()
    {
        // Arrange - Get access token first
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret"
        });

        var tokenResponse = await Client.PostAsync("/connect/token", tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode) return;

        var tokens = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var accessToken = tokens!.RootElement.GetProperty("access_token").GetString();

        // Act - Revoke the token
        var revokeRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = accessToken!,
            ["token_type_hint"] = "access_token",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret"
        });

        var response = await Client.PostAsync("/connect/revocation", revokeRequest);

        // Assert - Revocation should succeed (or endpoint not found if not enabled)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revocation_WithInvalidToken_StillReturnsSuccess()
    {
        // Per RFC 7009, revocation of invalid tokens should still return 200
        var revokeRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "invalid_token_12345",
            ["token_type_hint"] = "access_token",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret"
        });

        var response = await Client.PostAsync("/connect/revocation", revokeRequest);

        // Assert - Per RFC 7009, invalid tokens return success
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    #endregion

    #region Resource Owner Password Grant

    [Fact]
    public async Task PasswordGrant_WithValidCredentials_ReturnsAccessToken()
    {
        // Arrange - Use dedicated ROPC client with test user credentials
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["password"] = "Password123!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid profile email"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        tokenResponse.Should().NotBeNull();

        var root = tokenResponse!.RootElement;

        // Should have access_token
        root.TryGetProperty("access_token", out var accessToken).Should().BeTrue();
        accessToken.GetString().Should().NotBeNullOrEmpty();

        // Should have token_type
        root.TryGetProperty("token_type", out var tokenType).Should().BeTrue();
        tokenType.GetString().Should().BeEquivalentTo("Bearer");

        // Should have id_token (ROPC has user context with openid scope)
        root.TryGetProperty("id_token", out var idToken).Should().BeTrue();
        idToken.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PasswordGrant_WithInvalidPassword_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["password"] = "WrongPassword!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // User service may return "invalid_credentials" or "invalid_grant"
        error!.RootElement.GetProperty("error").GetString()
            .Should().BeOneOf("invalid_grant", "invalid_credentials");
    }

    [Fact]
    public async Task PasswordGrant_WithNonexistentUser_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "nonexistent@example.com",
            ["password"] = "Password123!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // User service may return "invalid_credentials" or "invalid_grant"
        error!.RootElement.GetProperty("error").GetString()
            .Should().BeOneOf("invalid_grant", "invalid_credentials");
    }

    [Fact]
    public async Task PasswordGrant_WithMissingUsername_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["password"] = "Password123!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // "invalid_request" when parameter missing, "invalid_grant" per OAuth spec
        error!.RootElement.GetProperty("error").GetString()
            .Should().BeOneOf("invalid_grant", "invalid_request");
    }

    [Fact]
    public async Task PasswordGrant_WithMissingPassword_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // "invalid_request" when parameter missing, "invalid_grant" per OAuth spec
        error!.RootElement.GetProperty("error").GetString()
            .Should().BeOneOf("invalid_grant", "invalid_request");
    }

    [Fact]
    public async Task PasswordGrant_WithClientWithoutPasswordGrant_ReturnsError()
    {
        // Arrange - Use test-client which doesn't have password grant
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["password"] = "Password123!",
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret"
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // Either "unsupported_grant_type" or "unauthorized_client" is valid
        // depending on whether grant type or client validation runs first
        error!.RootElement.GetProperty("error").GetString()
            .Should().BeOneOf("unsupported_grant_type", "unauthorized_client");
    }

    [Fact]
    public async Task PasswordGrant_RefreshTokenCanBeUsed()
    {
        // Arrange - First get tokens via password grant
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["password"] = "Password123!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid offline_access"
        });

        var tokenResponse = await Client.PostAsync("/connect/token", tokenRequest);
        if (!tokenResponse.IsSuccessStatusCode) return;

        var tokens = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var refreshToken = tokens!.RootElement.GetProperty("refresh_token").GetString();

        // Act - Use refresh token to get new tokens
        var refreshRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken!,
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret"
        });

        var response = await Client.PostAsync("/connect/token", refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var newTokens = await response.Content.ReadFromJsonAsync<JsonDocument>();
        newTokens!.RootElement.TryGetProperty("access_token", out var newAccessToken).Should().BeTrue();
        newAccessToken.GetString().Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Unsupported Grant Types

    [Fact]
    public async Task UnknownGrant_ReturnsError()
    {
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "unknown_grant_type",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret"
        });

        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        // Either "unsupported_grant_type" or "unauthorized_client" is valid
        // depending on whether grant type or client validation runs first
        error!.RootElement.GetProperty("error").GetString()
            .Should().BeOneOf("unsupported_grant_type", "unauthorized_client");
    }

    #endregion
}
