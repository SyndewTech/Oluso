using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Oluso.Enterprise.Ldap.Server;
using Oluso.EntityFramework;
using Oluso.Enterprise.Scim.EntityFramework;

namespace Oluso.E2E.Tests;

/// <summary>
/// Custom WebApplicationFactory for E2E tests that uses in-memory database.
/// </summary>
public class E2EWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"OlusoE2EDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove LDAP server hosted service to avoid port conflicts in tests
            var ldapHostedService = services.SingleOrDefault(
                d => d.ImplementationType == typeof(LdapServerHostedService));
            if (ldapHostedService != null)
            {
                services.Remove(ldapHostedService);
            }

            // Remove existing OlusoDbContext registrations
            RemoveDbContextRegistrations<OlusoDbContext>(services);
            RemoveDbContextRegistrations<ScimDbContext>(services);

            // Add in-memory database
            services.AddDbContext<OlusoDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            services.AddDbContext<ScimDbContext>(options =>
            {
                options.UseInMemoryDatabase($"{_databaseName}_Scim");
            });

            // Build service provider and ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
            db.Database.EnsureCreated();

            var scimDb = scope.ServiceProvider.GetService<ScimDbContext>();
            scimDb?.Database.EnsureCreated();
        });
    }

    private static void RemoveDbContextRegistrations<TContext>(IServiceCollection services) where TContext : DbContext
    {
        var optionsDescriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(DbContextOptions<TContext>));
        if (optionsDescriptor != null)
        {
            services.Remove(optionsDescriptor);
        }

        var contextDescriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(TContext));
        if (contextDescriptor != null)
        {
            services.Remove(contextDescriptor);
        }
    }

    /// <summary>
    /// Creates an HttpClient that doesn't follow redirects.
    /// </summary>
    public HttpClient CreateClientWithNoRedirects()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }
}

/// <summary>
/// End-to-end tests for OIDC flows.
/// These tests verify the complete OAuth/OIDC endpoints and flows.
/// </summary>
[TestFixture]
public class OidcFlowTests
{
    private E2EWebApplicationFactory _factory = null!;
    private HttpClient _httpClient = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new E2EWebApplicationFactory();
        _httpClient = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        _httpClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task Discovery_Endpoint_ReturnsValidDocument()
    {
        var response = await _httpClient.GetAsync("/.well-known/openid-configuration");

        Assert.That(response.IsSuccessStatusCode, Is.True, "Discovery endpoint should return 200 OK");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Verify required OIDC discovery properties
        Assert.That(root.TryGetProperty("issuer", out _), Is.True, "Should have issuer");
        Assert.That(root.TryGetProperty("authorization_endpoint", out _), Is.True, "Should have authorization_endpoint");
        Assert.That(root.TryGetProperty("token_endpoint", out _), Is.True, "Should have token_endpoint");
        Assert.That(root.TryGetProperty("jwks_uri", out _), Is.True, "Should have jwks_uri");
    }

    [Test]
    public async Task JWKS_Endpoint_ReturnsValidKeys()
    {
        var response = await _httpClient.GetAsync("/.well-known/jwks");

        Assert.That(response.IsSuccessStatusCode, Is.True, "JWKS endpoint should return 200 OK");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("keys", out var keys), Is.True, "Should have keys array");
        Assert.That(keys.GetArrayLength(), Is.GreaterThan(0), "Should have at least one key");
    }

    [Test]
    public async Task Authorize_WithValidClient_RedirectsToLogin()
    {
        // Build authorization URL with PKCE
        var state = Guid.NewGuid().ToString("N");
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var authUrl = "/connect/authorize?" +
            $"client_id=test-client&" +
            $"response_type=code&" +
            $"scope=openid%20profile&" +
            $"redirect_uri=http://localhost:5050/callback&" +
            $"state={state}&" +
            $"code_challenge={codeChallenge}&" +
            $"code_challenge_method=S256";

        // Use client that doesn't follow redirects
        using var client = _factory.CreateClientWithNoRedirects();
        var response = await client.GetAsync(authUrl);

        // Should redirect to login page
        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Redirect)
            .Or.EqualTo(System.Net.HttpStatusCode.Found),
            "Should redirect for unauthenticated user");

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.That(location, Does.Contain("/account/login").IgnoreCase
            .Or.Contain("/Account/Login").IgnoreCase,
            "Should redirect to login page");
    }

    [Test]
    public async Task Authorize_WithInvalidClient_ReturnsError()
    {
        var authUrl = "/connect/authorize?" +
            $"client_id=nonexistent-client&" +
            $"response_type=code&" +
            $"scope=openid&" +
            $"redirect_uri=http://example.com/callback";

        using var client = _factory.CreateClientWithNoRedirects();
        var response = await client.GetAsync(authUrl);

        // Should return error (400/302 with error, or show error page)
        var content = await response.Content.ReadAsStringAsync();
        var isError = !response.IsSuccessStatusCode ||
                      content.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                      content.Contains("invalid", StringComparison.OrdinalIgnoreCase);

        Assert.That(isError, Is.True, "Should indicate an error for invalid client");
    }

    [Test]
    public async Task Authorize_WithInvalidRedirectUri_ReturnsError()
    {
        var authUrl = "/connect/authorize?" +
            $"client_id=test-client&" +
            $"response_type=code&" +
            $"scope=openid&" +
            $"redirect_uri=http://malicious-site.com/callback";

        using var client = _factory.CreateClientWithNoRedirects();
        var response = await client.GetAsync(authUrl);

        // Should return error for invalid redirect_uri
        var content = await response.Content.ReadAsStringAsync();
        var isError = !response.IsSuccessStatusCode ||
                      content.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                      content.Contains("redirect", StringComparison.OrdinalIgnoreCase);

        Assert.That(isError, Is.True, "Should indicate an error for invalid redirect_uri");
    }

    [Test]
    public async Task Token_Endpoint_RequiresPost()
    {
        // Try to GET the token endpoint (should fail)
        var response = await _httpClient.GetAsync("/connect/token");

        // Token endpoint should not respond to GET requests with 200
        Assert.That(response.StatusCode, Is.Not.EqualTo(System.Net.HttpStatusCode.OK),
            "Token endpoint should not respond to GET requests");
    }

    [Test]
    public async Task Token_Endpoint_ClientCredentials_ReturnsToken()
    {
        // Test client credentials grant
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid"
        });

        var response = await _httpClient.PostAsync("/connect/token", content);

        Assert.That(response.IsSuccessStatusCode, Is.True, "Client credentials grant should succeed");

        var tokenContent = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(tokenContent);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("access_token", out _), Is.True, "Should have access_token");
        Assert.That(root.TryGetProperty("token_type", out _), Is.True, "Should have token_type");
    }

    [Test]
    public async Task Token_Endpoint_PasswordGrant_ReturnsToken()
    {
        // Test password grant with test user
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["password"] = "Password123!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid profile"
        });

        var response = await _httpClient.PostAsync("/connect/token", content);

        Assert.That(response.IsSuccessStatusCode, Is.True, "Password grant should succeed with valid credentials");

        var tokenContent = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(tokenContent);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("access_token", out _), Is.True, "Should have access_token");
        Assert.That(root.TryGetProperty("id_token", out _), Is.True, "Should have id_token for openid scope");
    }

    [Test]
    public async Task UserInfo_WithValidToken_ReturnsUserClaims()
    {
        // Get a token via password grant
        var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "testuser@example.com",
            ["password"] = "Password123!",
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid profile email"
        });

        var tokenResponse = await _httpClient.PostAsync("/connect/token", tokenContent);
        Assert.That(tokenResponse.IsSuccessStatusCode, Is.True,
            $"Token request should succeed: {await tokenResponse.Content.ReadAsStringAsync()}");

        var tokens = await tokenResponse.Content.ReadAsStringAsync();
        var tokenDoc = JsonDocument.Parse(tokens);
        var root = tokenDoc.RootElement;

        Assert.That(root.TryGetProperty("access_token", out var accessTokenElement), Is.True,
            "Token response should contain access_token");

        var accessToken = accessTokenElement.GetString();

        // Call userinfo endpoint
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

        Assert.That(response.IsSuccessStatusCode, Is.True,
            $"UserInfo should return 200: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");

        var userInfo = await response.Content.ReadAsStringAsync();
        var userDoc = JsonDocument.Parse(userInfo);

        Assert.That(userDoc.RootElement.TryGetProperty("sub", out _), Is.True, "Should have sub claim");
        Assert.That(userDoc.RootElement.TryGetProperty("email", out _), Is.True, "Should have email claim");
        Assert.That(userDoc.RootElement.TryGetProperty("name", out _), Is.True, "Should have name claim");
    }

    [Test]
    public async Task LoginPage_IsAccessible()
    {
        // Test that the login page is accessible
        var response = await _httpClient.GetAsync("/account/login");

        Assert.That(response.IsSuccessStatusCode, Is.True, "Login page should be accessible");

        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain("Sign In").IgnoreCase, "Login page should have Sign In text");
        Assert.That(content, Does.Contain("username").IgnoreCase, "Login page should have username field");
        Assert.That(content, Does.Contain("password").IgnoreCase, "Login page should have password field");
    }

    #region PKCE Helpers

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
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
