using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Dynamic client registration as a Matrix client does it (Matrix spec, OAuth 2.0 API; MSC2966): no scope (it gets
/// the tenant's registration scopes, the Matrix ones), client_uri, application_type, the URI rules, the device code
/// grant when the tenant allows it,.
/// </summary>
public class MatrixDynamicRegistrationTests : IntegrationTestBase
{
    private const string Tenant = "default";
    private const string ApiScope = "urn:matrix:client:api:*";
    private const string DevicePrefix = "urn:matrix:client:device:";
    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    public MatrixDynamicRegistrationTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", Tenant);
    }

    public override async Task InitializeAsync()
    {
        await WithDbContextAsync(async db =>
        {
            var tenant = await db.Tenants.FindAsync(Tenant);
            tenant!.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenant.Id };
            tenant.ProtocolConfiguration.EnableDynamicClientRegistration = true;
            tenant.ProtocolConfiguration.AllowOpenDynamicRegistration = true;
            tenant.ProtocolConfiguration.DynamicRegistrationAllowedScopesJson = JsonSerializer.Serialize(new[] { "openid", ApiScope, DevicePrefix });
            tenant.ProtocolConfiguration.DynamicRegistrationAllowedGrantTypesJson = JsonSerializer.Serialize(new[] { "authorization_code", "refresh_token", DeviceGrant });

            using var _ = db.BeginSeedMode();
            if (!await db.ApiScopes.IgnoreQueryFilters().AnyAsync(s => s.Name == ApiScope && s.TenantId == Tenant))
            {
                db.ApiScopes.Add(new ApiScope { Name = ApiScope, DisplayName = "Matrix account", TenantId = Tenant, Enabled = true });
                db.ApiScopes.Add(new ApiScope
                {
                    Name = DevicePrefix, DisplayName = "Matrix device", TenantId = Tenant, Enabled = true,
                    Properties = new List<ApiScopeProperty>
                    {
                        new() { Key = ApiScopePrefixes.MatchProperty, Value = ApiScopePrefixes.PrefixMatch },
                        new() { Key = ApiScopePrefixes.SuffixPatternProperty, Value = "[A-Za-z0-9._~-]+" },
                    },
                });
            }
            await db.SaveChangesAsync();
        });
    }

    private static Dictionary<string, object?> ElementLike(string applicationType, params string[] redirects) => new()
    {
        ["client_name"] = "Example Client",
        ["client_uri"] = "https://client.example",
        ["logo_uri"] = "https://client.example/logo.png",
        ["policy_uri"] = "https://client.example/privacy",
        ["tos_uri"] = "https://legal.client.example/terms",
        ["application_type"] = applicationType,
        ["token_endpoint_auth_method"] = "none",
        ["grant_types"] = new[] { "authorization_code", "refresh_token", DeviceGrant },
        ["response_types"] = new[] { "code" },
        ["redirect_uris"] = redirects,
    };

    private async Task<(HttpStatusCode Status, JsonElement Body)> RegisterAsync(object metadata)
    {
        var response = await Client.PostAsJsonAsync("/connect/register", metadata);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    [Fact]
    public async Task A_native_matrix_client_registers_with_reversed_dns_and_loopback_redirects()
    {
        var (status, body) = await RegisterAsync(ElementLike("native", "example.client:/callback", "http://[::1]/callback"));

        status.Should().Be(HttpStatusCode.Created, body.ToString());
        body.GetProperty("application_type").GetString().Should().Be("native");
        body.GetProperty("scope").GetString()!.Split(' ').Should().Contain(new[] { ApiScope, DevicePrefix });
        body.GetProperty("grant_types").EnumerateArray().Select(g => g.GetString()).Should().Contain(DeviceGrant);
        body.GetProperty("policy_uri").GetString().Should().Be("https://client.example/privacy");

        var clientId = body.GetProperty("client_id").GetString();
        await WithDbContextAsync(async db =>
        {
            var client = await db.Clients.IgnoreQueryFilters().Include(c => c.Properties).SingleAsync(c => c.ClientId == clientId);
            client.RequirePkce.Should().BeTrue();
            client.Properties.Should().Contain(p => p.Key == "application_type" && p.Value == "native");
        });
    }

    [Fact]
    public async Task A_web_matrix_client_registers_with_https_redirects_on_its_host()
    {
        var (status, body) = await RegisterAsync(ElementLike("web", "https://app.client.example/callback"));

        status.Should().Be(HttpStatusCode.Created, body.ToString());
        body.GetProperty("application_type").GetString().Should().Be("web");
    }

    [Theory]
    [InlineData("native", "client:/callback", "invalid_redirect_uri")]
    [InlineData("native", "example.client://callback", "invalid_redirect_uri")]
    [InlineData("native", "http://localhost:1234/callback", "invalid_redirect_uri")]
    [InlineData("web", "https://elsewhere.example/callback", "invalid_redirect_uri")]
    [InlineData("tv", "https://client.example/callback", "invalid_client_metadata")]
    public async Task A_matrix_client_breaking_the_uri_rules_is_refused(string type, string redirect, string error)
    {
        var (status, body) = await RegisterAsync(ElementLike(type, redirect));

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be(error);
    }

    [Fact]
    public async Task A_matrix_client_needs_an_https_client_uri_and_links_on_its_host()
    {
        var noClientUri = ElementLike("web", "https://client.example/callback");
        noClientUri.Remove("client_uri");
        noClientUri.Remove("logo_uri");
        noClientUri.Remove("policy_uri");
        noClientUri.Remove("tos_uri");
        (await RegisterAsync(noClientUri)).Body.GetProperty("error").GetString().Should().Be("invalid_client_metadata");

        var foreignLogo = ElementLike("web", "https://client.example/callback");
        foreignLogo["logo_uri"] = "https://tracker.example/logo.png";
        (await RegisterAsync(foreignLogo)).Body.GetProperty("error").GetString().Should().Be("invalid_client_metadata");
    }

    [Fact]
    public async Task A_device_only_client_needs_no_redirect_uris()
    {
        var metadata = ElementLike("native");
        metadata["grant_types"] = new[] { DeviceGrant, "refresh_token" };

        var (status, body) = await RegisterAsync(metadata);

        status.Should().Be(HttpStatusCode.Created, body.ToString());
    }
}
