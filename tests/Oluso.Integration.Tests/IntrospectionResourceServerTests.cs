using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// RFC 7662: the resource server a token is for may introspect it. A client lists the resources it serves
/// (<see cref="Client.IntrospectionResources"/>) and may introspect access tokens, JWT or reference, whose audience
/// includes one of them. Other clients see only their own tokens, and refresh tokens are only ever their client's.
/// </summary>
public class IntrospectionResourceServerTests : IntegrationTestBase
{
    private const string Tenant = "default";
    private const string ResourceUri = "https://mail.introspection.test/jmap";
    private const string Scope = "introspection-test.mail";
    private const string JwtAppId = "introspection-app-jwt";
    private const string ReferenceAppId = "introspection-app-reference";
    private const string ResourceServerId = "introspection-resource-server";
    private const string OtherClientId = "introspection-other-client";
    private const string Secret = "introspection-test-secret";

    private readonly HttpClient _http;

    public IntrospectionResourceServerTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        _http = factory.CreateClient();
        _http.DefaultRequestHeaders.Add("X-Tenant-Id", Tenant);
    }

    public override async Task InitializeAsync()
    {
        await WithDbContextAsync(async db =>
        {
            if (await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == ResourceServerId))
                return;

            using var _ = db.BeginSeedMode();
            db.ApiScopes.Add(new ApiScope { Name = Scope, DisplayName = "Mail", TenantId = Tenant, Enabled = true });
            db.Resources.Add(new Resource
            {
                Uri = ResourceUri,
                DisplayName = "Mail",
                TenantId = Tenant,
                Enabled = true,
                AllowedScopes = new List<ResourceScope> { new() { Scope = Scope } },
            });

            db.Clients.Add(NewClient(JwtAppId, AccessTokenType.Jwt));
            db.Clients.Add(NewClient(ReferenceAppId, AccessTokenType.Reference));
            var resourceServer = NewClient(ResourceServerId, AccessTokenType.Jwt);
            resourceServer.IntrospectionResources = new List<string> { ResourceUri };
            db.Clients.Add(resourceServer);
            db.Clients.Add(NewClient(OtherClientId, AccessTokenType.Jwt));

            await db.SaveChangesAsync();
        });
    }

    [Theory]
    [InlineData(JwtAppId)]
    [InlineData(ReferenceAppId)]
    public async Task The_resource_server_for_the_tokens_audience_may_introspect_it(string appId)
    {
        var accessToken = await AccessTokenAsync(appId);

        var result = await IntrospectAsync(accessToken, ResourceServerId);

        result.GetProperty("active").GetBoolean().Should().BeTrue();
        result.GetProperty("client_id").GetString().Should().Be(appId);
        result.GetProperty("aud").GetString().Should().Be(ResourceUri);
    }

    [Theory]
    [InlineData(JwtAppId)]
    [InlineData(ReferenceAppId)]
    public async Task A_client_that_is_not_a_resource_server_for_the_audience_sees_it_inactive(string appId)
    {
        var accessToken = await AccessTokenAsync(appId);

        var result = await IntrospectAsync(accessToken, OtherClientId);

        result.GetProperty("active").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData(JwtAppId)]
    [InlineData(ReferenceAppId)]
    public async Task The_client_a_token_was_issued_to_may_introspect_it(string appId)
    {
        var accessToken = await AccessTokenAsync(appId);

        var result = await IntrospectAsync(accessToken, appId);

        result.GetProperty("active").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_refresh_token_is_only_ever_its_clients_even_for_the_resource_server()
    {
        var handle = $"introspection-refresh-{Guid.NewGuid():N}";
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPersistedGrantStore>().StoreAsync(new PersistedGrant
            {
                Key = handle,
                Type = "refresh_token",
                ClientId = JwtAppId,
                SubjectId = "introspection-subject",
                CreationTime = DateTime.UtcNow,
                Expiration = DateTime.UtcNow.AddHours(1),
                Data = JsonSerializer.Serialize(new { Scopes = new[] { Scope }, Audiences = new[] { ResourceUri } }),
                TenantId = Tenant,
            });
        }

        (await IntrospectAsync(handle, ResourceServerId)).GetProperty("active").GetBoolean().Should().BeFalse();
        (await IntrospectAsync(handle, JwtAppId)).GetProperty("active").GetBoolean().Should().BeTrue();
    }

    private static Client NewClient(string clientId, AccessTokenType tokenType)
    {
        var client = new Client
        {
            ClientId = clientId, ClientName = clientId, TenantId = Tenant, Enabled = true,
            RequireClientSecret = true, RequirePkce = false, AccessTokenType = (int)tokenType,
        };
        client.ClientSecrets.Add(new ClientSecret { Value = HashSecret(Secret), Type = "SharedSecret" });
        client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "client_credentials" });
        client.AllowedScopes.Add(new ClientScope { Scope = Scope });
        return client;
    }

    private async Task<string> AccessTokenAsync(string clientId)
    {
        var response = await _http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = Secret,
            ["scope"] = Scope,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<JsonElement> IntrospectAsync(string token, string callerId)
    {
        var response = await _http.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["client_id"] = callerId,
            ["client_secret"] = Secret,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string HashSecret(string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
