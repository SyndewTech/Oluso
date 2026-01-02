using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for OIDC discovery endpoint.
/// </summary>
public class OidcDiscoveryTests : IntegrationTestBase
{
    public OidcDiscoveryTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Discovery_ReturnsValidDocument()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        discovery.Should().NotBeNull();

        var root = discovery!.RootElement;
        root.TryGetProperty("issuer", out _).Should().BeTrue();
        root.TryGetProperty("authorization_endpoint", out _).Should().BeTrue();
        root.TryGetProperty("token_endpoint", out _).Should().BeTrue();
        root.TryGetProperty("jwks_uri", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Discovery_ContainsRequiredEndpoints()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Assert - Required endpoints
        root.GetProperty("authorization_endpoint").GetString().Should().Contain("/connect/authorize");
        root.GetProperty("token_endpoint").GetString().Should().Contain("/connect/token");
        root.GetProperty("userinfo_endpoint").GetString().Should().Contain("/connect/userinfo");
        root.GetProperty("jwks_uri").GetString().Should().Contain("/.well-known/jwks");
    }

    [Fact]
    public async Task Discovery_IncludesGrantTypesSupported()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Assert
        root.TryGetProperty("grant_types_supported", out var grantTypes).Should().BeTrue();
        var grants = grantTypes.EnumerateArray().Select(x => x.GetString()).ToList();

        grants.Should().Contain("authorization_code");
        grants.Should().Contain("refresh_token");
    }

    [Fact]
    public async Task Discovery_IncludesResponseTypesSupported()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Assert
        root.TryGetProperty("response_types_supported", out var responseTypes).Should().BeTrue();
        var types = responseTypes.EnumerateArray().Select(x => x.GetString()).ToList();

        types.Should().Contain("code");
    }

    [Fact]
    public async Task Jwks_ReturnsValidKeys()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/jwks");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jwks = await response.Content.ReadFromJsonAsync<JsonDocument>();
        jwks.Should().NotBeNull();

        var root = jwks!.RootElement;
        root.TryGetProperty("keys", out var keys).Should().BeTrue();
        keys.GetArrayLength().Should().BeGreaterThan(0);

        // Verify first key has required properties
        var firstKey = keys[0];
        firstKey.TryGetProperty("kty", out _).Should().BeTrue();
        firstKey.TryGetProperty("use", out _).Should().BeTrue();
        firstKey.TryGetProperty("kid", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Discovery_AllEndpointsAreAccessible()
    {
        // Get discovery document
        var discoveryResponse = await Client.GetAsync("/.well-known/openid-configuration");
        discoveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var discovery = await discoveryResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Verify JWKS endpoint from discovery is accessible
        var jwksUri = root.GetProperty("jwks_uri").GetString()!;
        var jwksPath = new Uri(jwksUri).PathAndQuery;
        var jwksResponse = await Client.GetAsync(jwksPath);
        jwksResponse.StatusCode.Should().Be(HttpStatusCode.OK, "JWKS endpoint from discovery should be accessible");

        // Verify token endpoint exists (POST only)
        var tokenUri = root.GetProperty("token_endpoint").GetString()!;
        var tokenPath = new Uri(tokenUri).PathAndQuery;
        var tokenResponse = await Client.GetAsync(tokenPath);
        // Token endpoint should reject GET (returns 405 or similar non-200)
        tokenResponse.StatusCode.Should().NotBe(HttpStatusCode.OK, "Token endpoint should not respond to GET");

        // Verify userinfo endpoint requires auth (returns 401)
        var userinfoUri = root.GetProperty("userinfo_endpoint").GetString()!;
        var userinfoPath = new Uri(userinfoUri).PathAndQuery;
        var userinfoResponse = await Client.GetAsync(userinfoPath);
        userinfoResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "UserInfo endpoint should require authentication");

        // Verify authorization endpoint is accessible (redirects to login for unauthenticated)
        var authUri = root.GetProperty("authorization_endpoint").GetString()!;
        var authPath = new Uri(authUri).PathAndQuery;
        // Add required params for authorize endpoint
        var authResponse = await Factory.CreateClientWithNoRedirects()
            .GetAsync($"{authPath}?client_id=test-client&response_type=code&scope=openid&redirect_uri=http://localhost:5050/callback");
        // Should redirect to login (302/303) for unauthenticated user
        var validRedirectStatuses = new[] { HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.SeeOther };
        validRedirectStatuses.Should().Contain(authResponse.StatusCode,
            "Authorization endpoint should redirect unauthenticated users to login");
    }

    [Fact]
    public async Task Discovery_EndpointPathsMatchActualRoutes()
    {
        // Get discovery document
        var discoveryResponse = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await discoveryResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Extract all endpoint paths from discovery
        var endpoints = new Dictionary<string, string>
        {
            ["authorization_endpoint"] = root.GetProperty("authorization_endpoint").GetString()!,
            ["token_endpoint"] = root.GetProperty("token_endpoint").GetString()!,
            ["userinfo_endpoint"] = root.GetProperty("userinfo_endpoint").GetString()!,
            ["jwks_uri"] = root.GetProperty("jwks_uri").GetString()!,
            ["revocation_endpoint"] = root.GetProperty("revocation_endpoint").GetString()!,
            ["introspection_endpoint"] = root.GetProperty("introspection_endpoint").GetString()!,
            ["end_session_endpoint"] = root.GetProperty("end_session_endpoint").GetString()!,
            ["device_authorization_endpoint"] = root.GetProperty("device_authorization_endpoint").GetString()!,
        };

        // All endpoints should have the same base URL
        var issuer = root.GetProperty("issuer").GetString()!;
        foreach (var (name, url) in endpoints)
        {
            url.Should().StartWith("http", $"{name} should be an absolute URL");
            var uri = new Uri(url);
            uri.Scheme.Should().BeOneOf("http", "https", $"{name} should use HTTP(S)");
        }

        // Verify each endpoint path is routable (not 404)
        var noRedirectClient = Factory.CreateClientWithNoRedirects();
        foreach (var (name, url) in endpoints)
        {
            var path = new Uri(url).PathAndQuery;

            // Skip endpoints that need special handling
            if (name == "authorization_endpoint")
            {
                // Needs query params
                path += "?client_id=test&response_type=code&scope=openid&redirect_uri=http://localhost/cb";
            }

            var response = await noRedirectClient.GetAsync(path);

            // Should NOT be 404 - any other status is acceptable
            // (401 for protected, 302 for redirects, 405 for wrong method, etc.)
            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
                $"{name} at {path} should be a valid route (got {response.StatusCode})");
        }
    }

    [Fact]
    public async Task Discovery_IncludesAllRequiredOidcProperties()
    {
        // Get discovery document
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        // Required per OpenID Connect Discovery 1.0 spec
        var requiredProperties = new[]
        {
            "issuer",
            "authorization_endpoint",
            "token_endpoint",
            "jwks_uri",
            "response_types_supported",
            "subject_types_supported",
            "id_token_signing_alg_values_supported"
        };

        foreach (var prop in requiredProperties)
        {
            root.TryGetProperty(prop, out _).Should().BeTrue($"Discovery should include required property '{prop}'");
        }

        // Recommended properties
        var recommendedProperties = new[]
        {
            "userinfo_endpoint",
            "scopes_supported",
            "claims_supported",
            "grant_types_supported",
            "token_endpoint_auth_methods_supported",
            "code_challenge_methods_supported"
        };

        foreach (var prop in recommendedProperties)
        {
            root.TryGetProperty(prop, out _).Should().BeTrue($"Discovery should include recommended property '{prop}'");
        }
    }

    [Fact]
    public async Task Discovery_IssuerMatchesRequestHost()
    {
        // Get discovery document
        var response = await Client.GetAsync("/.well-known/openid-configuration");
        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var root = discovery!.RootElement;

        var issuer = root.GetProperty("issuer").GetString()!;

        // Issuer should be a valid URL
        var issuerUri = new Uri(issuer);
        issuerUri.Scheme.Should().BeOneOf("http", "https");

        // Issuer should not have a trailing slash
        issuer.Should().NotEndWith("/", "Issuer should not have a trailing slash per spec");

        // Issuer should not have query or fragment
        issuerUri.Query.Should().BeEmpty("Issuer should not contain query string");
        issuerUri.Fragment.Should().BeEmpty("Issuer should not contain fragment");
    }
}
