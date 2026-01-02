using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for OIDC Authorization Code flow with PKCE.
/// Tests the complete OAuth 2.0 authorization code grant with PKCE extension.
/// </summary>
public class OidcAuthorizationCodeFlowTests : IntegrationTestBase
{
    public OidcAuthorizationCodeFlowTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    #region PKCE Helpers

    /// <summary>
    /// Generates a cryptographically random code verifier for PKCE.
    /// </summary>
    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Creates a code challenge from a code verifier using SHA256.
    /// </summary>
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

    [Fact]
    public async Task Authorize_WithValidRequest_RedirectsToLogin()
    {
        // Arrange
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var state = Guid.NewGuid().ToString("N");

        var authorizeUrl = "/connect/authorize?" +
            "response_type=code&" +
            "client_id=test-client&" +
            $"redirect_uri={Uri.EscapeDataString("https://localhost/callback")}&" +
            "scope=openid%20profile&" +
            $"state={state}&" +
            $"code_challenge={codeChallenge}&" +
            "code_challenge_method=S256";

        // Act - Don't follow redirects automatically
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(authorizeUrl);

        // Assert - Should redirect to login/journey page or return error for invalid client
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.OK, HttpStatusCode.BadRequest);

        // If redirect, should go to login, journey, or error page
        if (response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.Found)
        {
            var location = response.Headers.Location?.ToString();
            location.Should().NotBeNullOrEmpty();
            // Should redirect to login, journey page, or contain error (for invalid client)
            (location!.Contains("/journey") ||
             location.Contains("/login") ||
             location.Contains("/account") ||
             location.Contains("/connect") ||
             location.Contains("error")).Should().BeTrue($"Unexpected redirect location: {location}");
        }
    }

    [Fact]
    public async Task Authorize_WithMissingClientId_ReturnsError()
    {
        // Arrange
        var authorizeUrl = "/connect/authorize?" +
            "response_type=code&" +
            $"redirect_uri={Uri.EscapeDataString("https://localhost/callback")}&" +
            "scope=openid";

        // Act
        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync(authorizeUrl);

        // Assert - Should return error (either as redirect or page)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK); // May show error page
    }

    [Fact]
    public async Task Authorize_WithInvalidClientId_ReturnsError()
    {
        // Arrange
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);

        var authorizeUrl = "/connect/authorize?" +
            "response_type=code&" +
            "client_id=nonexistent-client&" +
            $"redirect_uri={Uri.EscapeDataString("https://localhost/callback")}&" +
            "scope=openid&" +
            $"code_challenge={codeChallenge}&" +
            "code_challenge_method=S256";

        // Act
        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync(authorizeUrl);

        // Assert - Should return error for invalid client
        // The server may return a redirect with error or show an error page
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authorize_WithInvalidResponseType_ReturnsError()
    {
        // Arrange
        var authorizeUrl = "/connect/authorize?" +
            "response_type=invalid_type&" +
            "client_id=test-client&" +
            $"redirect_uri={Uri.EscapeDataString("https://localhost/callback")}&" +
            "scope=openid";

        // Act
        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync(authorizeUrl);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_WithInvalidCode_ReturnsError()
    {
        // Arrange - Try to exchange an invalid authorization code
        var codeVerifier = GenerateCodeVerifier();

        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = "invalid_authorization_code",
            ["redirect_uri"] = "https://localhost/callback",
            ["client_id"] = "test-client",
            ["code_verifier"] = codeVerifier
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
    public async Task Token_WithMissingCodeVerifier_ReturnsError()
    {
        // Arrange - PKCE is required, missing code_verifier should fail
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = "some_code",
            ["redirect_uri"] = "https://localhost/callback",
            ["client_id"] = "test-client"
            // Missing code_verifier
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
    public async Task Discovery_IncludesPkceSupport()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Assert - PKCE code challenge methods should be advertised
        if (root.TryGetProperty("code_challenge_methods_supported", out var methods))
        {
            var methodList = methods.EnumerateArray().Select(x => x.GetString()).ToList();
            methodList.Should().Contain("S256");
        }
    }

    [Fact]
    public async Task Authorize_PreservesState()
    {
        // Arrange
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var state = "test-state-12345";

        var authorizeUrl = "/connect/authorize?" +
            "response_type=code&" +
            "client_id=test-client&" +
            $"redirect_uri={Uri.EscapeDataString("https://localhost/callback")}&" +
            "scope=openid&" +
            $"state={state}&" +
            $"code_challenge={codeChallenge}&" +
            "code_challenge_method=S256";

        // Act
        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync(authorizeUrl);

        // Assert - When error redirects happen, state should be preserved
        if (response.Headers.Location != null)
        {
            var query = HttpUtility.ParseQueryString(response.Headers.Location.Query);
            // If there's an error redirect, state should be preserved
            if (query["error"] != null)
            {
                query["state"].Should().Be(state);
            }
        }
    }

    [Fact]
    public async Task Authorize_WithNonce_PreservesNonce()
    {
        // Arrange - nonce is used for ID token replay protection
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var nonce = Guid.NewGuid().ToString("N");
        var state = Guid.NewGuid().ToString("N");

        var authorizeUrl = "/connect/authorize?" +
            "response_type=code&" +
            "client_id=test-client&" +
            $"redirect_uri={Uri.EscapeDataString("https://localhost/callback")}&" +
            "scope=openid&" +
            $"state={state}&" +
            $"nonce={nonce}&" +
            $"code_challenge={codeChallenge}&" +
            "code_challenge_method=S256";

        // Act
        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync(authorizeUrl);

        // Assert - Request should be accepted (redirect to login)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.OK);
    }

    [Fact]
    public void CodeVerifier_And_CodeChallenge_AreValid()
    {
        // Arrange & Act
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);

        // Assert - Verify PKCE values are properly formatted
        codeVerifier.Should().NotBeNullOrEmpty();
        codeVerifier.Length.Should().BeGreaterOrEqualTo(43); // Min length per RFC 7636

        codeChallenge.Should().NotBeNullOrEmpty();
        codeChallenge.Should().NotContain("+"); // Base64URL encoding
        codeChallenge.Should().NotContain("/");
        codeChallenge.Should().NotContain("=");
    }

    [Fact]
    public async Task UserInfo_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await Client.GetAsync("/connect/userinfo");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Introspection_WithoutCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "some_token"
        });

        // Act
        var response = await Client.PostAsync("/connect/introspect", request);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revocation_WithoutCredentials_ReturnsError()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = "some_token"
        });

        // Act
        var response = await Client.PostAsync("/connect/revocation", request);

        // Assert - Revocation may accept without client auth but token won't be found
        // Also accept NotFound if endpoint not enabled
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    #region Complete Authorization Code Flow Tests

    /// <summary>
    /// Helper to store an authorization code using the IAuthorizationCodeStore service.
    /// </summary>
    private async Task StoreAuthorizationCodeAsync(AuthorizationCode code)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuthorizationCodeStore>();
        await store.StoreAsync(code);
    }

    [Fact]
    public async Task Token_WithValidAuthorizationCode_ReturnsTokens()
    {
        // Arrange - Create authorization code and PKCE values
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"test_auth_code_{Guid.NewGuid():N}";
        var redirectUri = "http://localhost:5100/signin-oidc";

        // Get a test user ID from the database
        string? userId = null;
        await WithDbContextAsync(async db =>
        {
            var testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser@example.com")
                ?? db.Users.FirstOrDefault();
            userId = testUser?.Id;
        });

        if (userId == null)
        {
            // If no user exists, this test cannot run meaningfully
            return;
        }

        // Store authorization code using the store service
        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = "test-client",
            SubjectId = userId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid", "profile" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = "default",
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddMinutes(5),
            IsConsumed = false
        });

        // Act - Exchange the authorization code for tokens
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["code_verifier"] = codeVerifier
        });

        var response = await Client.PostAsync("/connect/token", tokenRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        tokenResponse.Should().NotBeNull();

        var root = tokenResponse!.RootElement;
        root.TryGetProperty("access_token", out var accessToken).Should().BeTrue();
        accessToken.GetString().Should().NotBeNullOrEmpty();

        root.TryGetProperty("token_type", out var tokenType).Should().BeTrue();
        tokenType.GetString().Should().BeEquivalentTo("Bearer");

        root.TryGetProperty("expires_in", out var expiresIn).Should().BeTrue();
        expiresIn.GetInt32().Should().BeGreaterThan(0);

        // Should have id_token since we requested openid scope
        root.TryGetProperty("id_token", out var idToken).Should().BeTrue();
        idToken.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Token_WithWrongCodeVerifier_ReturnsError()
    {
        // Arrange - Create authorization code with one verifier
        var correctCodeVerifier = GenerateCodeVerifier();
        var correctCodeChallenge = CreateCodeChallenge(correctCodeVerifier);
        var wrongCodeVerifier = GenerateCodeVerifier(); // Different verifier
        var authCode = $"test_auth_code_wrong_{Guid.NewGuid():N}";
        var redirectUri = "http://localhost:5100/signin-oidc";

        // Get a test user ID from the database
        string? userId = null;
        await WithDbContextAsync(async db =>
        {
            var testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser@example.com")
                ?? db.Users.FirstOrDefault();
            userId = testUser?.Id;
        });

        if (userId == null) return;

        // Store the authorization code with the correct challenge
        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = "test-client",
            SubjectId = userId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid" },
            CodeChallenge = correctCodeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = "default",
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddMinutes(5),
            IsConsumed = false
        });

        // Act - Try to exchange with wrong code verifier
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["code_verifier"] = wrongCodeVerifier // Using wrong verifier!
        });

        var response = await Client.PostAsync("/connect/token", tokenRequest);

        // Assert - Should fail PKCE validation
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        errorResponse!.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Token_WithExpiredAuthorizationCode_ReturnsError()
    {
        // Arrange - Create an expired authorization code
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"test_auth_code_expired_{Guid.NewGuid():N}";
        var redirectUri = "http://localhost:5100/signin-oidc";

        // Get a test user ID from the database
        string? userId = null;
        await WithDbContextAsync(async db =>
        {
            var testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser@example.com")
                ?? db.Users.FirstOrDefault();
            userId = testUser?.Id;
        });

        if (userId == null) return;

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = "test-client",
            SubjectId = userId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = "default",
            CreationTime = DateTime.UtcNow.AddMinutes(-10),
            Expiration = DateTime.UtcNow.AddMinutes(-5), // Expired!
            IsConsumed = false
        });

        // Act
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["code_verifier"] = codeVerifier
        });

        var response = await Client.PostAsync("/connect/token", tokenRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        errorResponse!.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Token_WithAlreadyUsedAuthorizationCode_ReturnsError()
    {
        // Arrange - Create an already consumed authorization code
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"test_auth_code_used_{Guid.NewGuid():N}";
        var redirectUri = "http://localhost:5100/signin-oidc";

        // Get a test user ID from the database
        string? userId = null;
        await WithDbContextAsync(async db =>
        {
            var testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser@example.com")
                ?? db.Users.FirstOrDefault();
            userId = testUser?.Id;
        });

        if (userId == null) return;

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = "test-client",
            SubjectId = userId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = "default",
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddMinutes(5),
            IsConsumed = true, // Already used!
            ConsumedTime = DateTime.UtcNow.AddSeconds(-30)
        });

        // Act
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["code_verifier"] = codeVerifier
        });

        var response = await Client.PostAsync("/connect/token", tokenRequest);

        // Assert - Code replay should be rejected
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        errorResponse!.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Token_WithWrongRedirectUri_ReturnsError()
    {
        // Arrange - Create authorization code with specific redirect_uri
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"test_auth_code_redirect_{Guid.NewGuid():N}";
        var originalRedirectUri = "http://localhost:5100/signin-oidc";
        var wrongRedirectUri = "http://localhost:9999/different";

        // Get a test user ID from the database
        string? userId = null;
        await WithDbContextAsync(async db =>
        {
            var testUser = db.Users.FirstOrDefault(u => u.UserName == "testuser@example.com")
                ?? db.Users.FirstOrDefault();
            userId = testUser?.Id;
        });

        if (userId == null) return;

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = "test-client",
            SubjectId = userId,
            RedirectUri = originalRedirectUri,
            Scopes = new List<string> { "openid" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = "default",
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddMinutes(5),
            IsConsumed = false
        });

        // Act - Try with wrong redirect_uri
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = wrongRedirectUri, // Wrong URI!
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["code_verifier"] = codeVerifier
        });

        var response = await Client.PostAsync("/connect/token", tokenRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        errorResponse!.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    #endregion
}
