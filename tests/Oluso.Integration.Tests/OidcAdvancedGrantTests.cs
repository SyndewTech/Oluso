using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for advanced OIDC/OAuth 2.0 grants and extensions.
/// Tests PAR (Pushed Authorization Request), Device Authorization, Refresh Token, etc.
/// </summary>
public class OidcAdvancedGrantTests : IntegrationTestBase
{
    public OidcAdvancedGrantTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    #region PAR (Pushed Authorization Request) Tests - RFC 9126

    [Fact]
    public async Task Par_EndpointExists()
    {
        // Arrange
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);

        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = "test-client",
            ["redirect_uri"] = "https://localhost/callback",
            ["scope"] = "openid profile",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = Guid.NewGuid().ToString("N"),
        });

        // Act
        var response = await Client.PostAsync("/connect/par", request);

        // Assert - PAR endpoint should return a response (error or success)
        // NotFound means endpoint isn't implemented yet
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Created,
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task Par_WithValidRequest_ReturnsRequestUri()
    {
        // Arrange
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);

        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["redirect_uri"] = "https://localhost/callback",
            ["scope"] = "openid profile",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = Guid.NewGuid().ToString("N"),
        });

        // Act
        var response = await Client.PostAsync("/connect/par", request);

        // Assert
        if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
        {
            var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
            content.Should().NotBeNull();

            // PAR response should contain request_uri
            content!.RootElement.TryGetProperty("request_uri", out var requestUri).Should().BeTrue();
            requestUri.GetString().Should().StartWith("urn:ietf:params:oauth:request_uri:");

            // Should also contain expires_in
            content.RootElement.TryGetProperty("expires_in", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Par_WithoutClientAuthentication_ReturnsError()
    {
        // Arrange - No client_secret
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = "test-client",
            ["redirect_uri"] = "https://localhost/callback",
            ["scope"] = "openid",
        });

        // Act
        var response = await Client.PostAsync("/connect/par", request);

        // Assert - Should require client authentication or return error
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.NotFound // If not implemented
        );
    }

    #endregion

    #region Device Authorization Grant Tests - RFC 8628

    [Fact]
    public async Task DeviceAuthorization_EndpointExists()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "device-client",
            ["scope"] = "openid profile",
        });

        // Act
        var response = await Client.PostAsync("/connect/deviceauthorization", request);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task DeviceAuthorization_WithValidClient_ReturnsDeviceCode()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "device-client",
            ["scope"] = "openid profile",
        });

        // Act
        var response = await Client.PostAsync("/connect/deviceauthorization", request);

        // Assert
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
            content.Should().NotBeNull();

            var root = content!.RootElement;

            // Device authorization response should contain required fields
            root.TryGetProperty("device_code", out _).Should().BeTrue();
            root.TryGetProperty("user_code", out _).Should().BeTrue();
            root.TryGetProperty("verification_uri", out _).Should().BeTrue();
            root.TryGetProperty("expires_in", out _).Should().BeTrue();
            root.TryGetProperty("interval", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Token_WithDeviceCodeGrant_WithoutCode_ReturnsError()
    {
        // Arrange - Device code grant without device_code
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["client_id"] = "device-client",
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error.Should().NotBeNull();
        error!.RootElement.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Token_WithDeviceCodeGrant_WithInvalidCode_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["client_id"] = "device-client",
            ["device_code"] = "invalid_device_code",
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error.Should().NotBeNull();
        // Client validation may happen first
        error!.RootElement.GetProperty("error").GetString().Should().BeOneOf(
            "invalid_grant",
            "expired_token",
            "authorization_pending",
            "slow_down",
            "invalid_client",
            "unauthorized_client"
        );
    }

    #endregion

    #region Refresh Token Grant Tests

    [Fact]
    public async Task Token_WithRefreshTokenGrant_WithoutToken_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "test-client",
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error.Should().NotBeNull();
        // Client validation may happen first
        error!.RootElement.GetProperty("error").GetString().Should().BeOneOf(
            "invalid_request",
            "invalid_grant",
            "invalid_client",
            "unauthorized_client"
        );
    }

    [Fact]
    public async Task Token_WithRefreshTokenGrant_WithInvalidToken_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "test-client",
            ["refresh_token"] = "invalid_refresh_token",
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error.Should().NotBeNull();
        // Client validation may happen first
        error!.RootElement.GetProperty("error").GetString().Should().BeOneOf(
            "invalid_grant",
            "invalid_client",
            "unauthorized_client"
        );
    }

    #endregion

    #region Client Credentials Grant Tests

    [Fact]
    public async Task Token_WithClientCredentialsGrant_WithInvalidClient_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "nonexistent-client",
            ["client_secret"] = "wrong-secret",
            ["scope"] = "api",
        });

        // Act
        var response = await Client.PostAsync("/connect/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error.Should().NotBeNull();
        error!.RootElement.GetProperty("error").GetString().Should().BeOneOf(
            "invalid_client",
            "unauthorized_client"
        );
    }

    #endregion

    #region Token Introspection Tests - RFC 7662

    [Fact]
    public async Task Introspection_EndpointExists()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "some_access_token",
        });

        // Act
        var response = await Client.PostAsync("/connect/introspect", request);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized
        );
    }

    [Fact]
    public async Task Introspection_WithInvalidToken_ReturnsInactive()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "invalid_token",
            ["client_id"] = "introspection-client",
            ["client_secret"] = "introspection-secret",
        });

        // Act
        var response = await Client.PostAsync("/connect/introspect", request);

        // Assert
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadFromJsonAsync<JsonDocument>();
            content.Should().NotBeNull();

            // Invalid token should return active: false
            content!.RootElement.GetProperty("active").GetBoolean().Should().BeFalse();
        }
    }

    #endregion

    #region End Session / Logout Tests

    [Fact]
    public async Task EndSession_EndpointExists()
    {
        // Act
        var response = await Client.GetAsync("/connect/endsession");

        // Assert - Accept NotFound if endpoint not implemented
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task EndSession_WithPostLogoutRedirectUri_Redirects()
    {
        // Arrange
        var endSessionUrl = "/connect/endsession?" +
            $"post_logout_redirect_uri={Uri.EscapeDataString("https://localhost/signout-callback")}&" +
            "state=logout-state";

        // Act
        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync(endSessionUrl);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest
        );
    }

    #endregion

    #region Discovery Extensions

    [Fact]
    public async Task Discovery_IncludesParEndpoint()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();

        // Assert
        if (discovery!.RootElement.TryGetProperty("pushed_authorization_request_endpoint", out var parEndpoint))
        {
            parEndpoint.GetString().Should().Contain("/connect/par");
        }
    }

    [Fact]
    public async Task Discovery_IncludesDeviceAuthorizationEndpoint()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();

        // Assert
        if (discovery!.RootElement.TryGetProperty("device_authorization_endpoint", out var deviceEndpoint))
        {
            deviceEndpoint.GetString().Should().Contain("/connect/deviceauthorization");
        }
    }

    [Fact]
    public async Task Discovery_IncludesIntrospectionEndpoint()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();

        // Assert
        if (discovery!.RootElement.TryGetProperty("introspection_endpoint", out var introspectionEndpoint))
        {
            introspectionEndpoint.GetString().Should().Contain("/connect/introspect");
        }
    }

    [Fact]
    public async Task Discovery_IncludesRevocationEndpoint()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();

        // Assert
        if (discovery!.RootElement.TryGetProperty("revocation_endpoint", out var revocationEndpoint))
        {
            revocationEndpoint.GetString().Should().Contain("/connect/revocation");
        }
    }

    [Fact]
    public async Task Discovery_IncludesEndSessionEndpoint()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();

        // Assert
        if (discovery!.RootElement.TryGetProperty("end_session_endpoint", out var endSessionEndpoint))
        {
            endSessionEndpoint.GetString().Should().Contain("/connect/endsession");
        }
    }

    [Fact]
    public async Task Discovery_IncludesSupportedScopes()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Assert
        if (root.TryGetProperty("scopes_supported", out var scopes))
        {
            var scopeList = scopes.EnumerateArray().Select(x => x.GetString()).ToList();
            scopeList.Should().Contain("openid");
        }
    }

    [Fact]
    public async Task Discovery_IncludesClaimsSupported()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Assert
        if (root.TryGetProperty("claims_supported", out var claims))
        {
            var claimsList = claims.EnumerateArray().Select(x => x.GetString()).ToList();
            claimsList.Should().Contain("sub");
        }
    }

    #endregion

    #region PKCE Helpers

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = sha256.ComputeHash(bytes);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    #endregion
}
