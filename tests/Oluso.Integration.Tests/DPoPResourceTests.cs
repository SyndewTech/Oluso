using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Oluso.Integration.Tests.Fixtures;
using static Oluso.Integration.Tests.Fixtures.DPoPTestProofs;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// DPoP where access tokens are used (RFC 9449 §7): a token bound to a key works only with a proof from that
/// key for that request, never as a bearer token; and introspection tells a resource server about the binding.
/// </summary>
public class DPoPResourceTests : IntegrationTestBase, IDisposable
{
    private const string UserInfo = "/connect/userinfo";
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public DPoPResourceTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
    }

    public void Dispose()
    {
        _key.Dispose();
        _otherKey.Dispose();
    }

    [Fact]
    public async Task A_bound_token_works_with_a_proof_from_its_key()
    {
        var token = await BoundTokenAsync();

        var response = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, Proof(_key, "GET", UserInfo, token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_bound_token_presented_as_a_bearer_token_is_refused()
    {
        var token = await BoundTokenAsync();

        var response = await CallAsync(HttpMethod.Get, UserInfo, "Bearer", token, proof: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().StartWith("DPoP").And.Contain("invalid_token");
    }

    [Fact]
    public async Task A_bound_token_presented_as_a_bearer_token_is_refused_even_with_a_valid_proof()
    {
        var token = await BoundTokenAsync();

        var response = await CallAsync(HttpMethod.Get, UserInfo, "Bearer", token, Proof(_key, "GET", UserInfo, token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_bound_token_without_a_proof_is_refused()
    {
        var token = await BoundTokenAsync();

        var response = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, proof: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("invalid_dpop_proof");
    }

    [Fact]
    public async Task A_proof_from_another_key_is_refused()
    {
        var token = await BoundTokenAsync();

        var response = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, Proof(_otherKey, "GET", UserInfo, token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_proof_for_another_request_or_token_is_refused()
    {
        var token = await BoundTokenAsync();

        var otherPath = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, Proof(_key, "GET", "/connect/token", token));
        var otherMethod = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, Proof(_key, "POST", UserInfo, token));
        var otherToken = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, Proof(_key, "GET", UserInfo, "some-other-token"));

        otherPath.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        otherMethod.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        otherToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_replayed_proof_is_refused()
    {
        var token = await BoundTokenAsync();
        var proof = Proof(_key, "GET", UserInfo, token);

        var first = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, proof);
        var replay = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, proof);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unbound_token_works_as_a_bearer_token_and_is_refused_as_dpop()
    {
        var token = await TokenAsync(proof: null);

        var bearer = await CallAsync(HttpMethod.Get, UserInfo, "Bearer", token, proof: null);
        var asDPoP = await CallAsync(HttpMethod.Get, UserInfo, "DPoP", token, Proof(_key, "GET", UserInfo, token));

        bearer.StatusCode.Should().Be(HttpStatusCode.OK);
        asDPoP.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Introspection_reports_the_key_a_token_is_bound_to()
    {
        var bound = await BoundTokenAsync();
        var unbound = await TokenAsync(proof: null);

        var boundResult = await IntrospectAsync(bound);
        var unboundResult = await IntrospectAsync(unbound);

        boundResult.GetProperty("active").GetBoolean().Should().BeTrue();
        boundResult.GetProperty("cnf").GetProperty("jkt").GetString().Should().Be(TokenJkt(bound)).And.NotBeNullOrEmpty();
        unboundResult.GetProperty("active").GetBoolean().Should().BeTrue();
        unboundResult.TryGetProperty("cnf", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_bound_refresh_token_is_redeemed_only_with_a_proof_from_its_key()
    {
        var tokens = await TokensAsync(Proof(_key, "POST", "/connect/token", accessToken: null));
        var refreshToken = tokens.GetProperty("refresh_token").GetString()!;

        var noProof = await RefreshAsync(refreshToken, proof: null);
        var otherKey = await RefreshAsync(refreshToken, Proof(_otherKey, "POST", "/connect/token", accessToken: null));
        var ownKey = await RefreshAsync(refreshToken, Proof(_key, "POST", "/connect/token", accessToken: null));

        noProof.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noProof.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_dpop_proof");
        otherKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ownKey.StatusCode.Should().Be(HttpStatusCode.OK, "a refused attempt leaves the token unspent");
        var refreshed = await ownKey.Content.ReadFromJsonAsync<JsonElement>();
        refreshed.GetProperty("token_type").GetString().Should().Be("DPoP");
        TokenJkt(refreshed.GetProperty("access_token").GetString()!).Should().Be(TokenJkt(tokens.GetProperty("access_token").GetString()!));
    }

    [Fact]
    public async Task An_unbound_refresh_token_used_with_a_proof_returns_tokens_bound_to_that_key()
    {
        var tokens = await TokensAsync(proof: null);
        var unboundRefresh = tokens.GetProperty("refresh_token").GetString()!;

        var upgraded = await RefreshAsync(unboundRefresh, Proof(_key, "POST", "/connect/token", accessToken: null));

        upgraded.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await upgraded.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token_type").GetString().Should().Be("DPoP");
        TokenJkt(body.GetProperty("access_token").GetString()!).Should().NotBeNullOrEmpty();
        var nextRefresh = body.TryGetProperty("refresh_token", out var rotated) ? rotated.GetString()! : unboundRefresh;
        (await RefreshAsync(nextRefresh, proof: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "from then on the refresh token is bound to the key");
    }

    private async Task<JsonElement> TokensAsync(string? proof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = "testuser@example.com",
                ["password"] = "Password123!",
                ["client_id"] = "ropc-client",
                ["client_secret"] = "test-secret",
                ["scope"] = "openid profile email offline_access",
            }),
        };
        if (proof != null) request.Headers.Add("DPoP", proof);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("refresh_token", out _).Should().BeTrue("ropc-client is allowed offline access");
        return body;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken, string? proof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = "ropc-client",
                ["client_secret"] = "test-secret",
            }),
        };
        if (proof != null) request.Headers.Add("DPoP", proof);
        return await Client.SendAsync(request);
    }

    private async Task<string> BoundTokenAsync()
    {
        var token = await TokenAsync(Proof(_key, "POST", "/connect/token", accessToken: null));
        TokenJkt(token).Should().NotBeNullOrEmpty("the token endpoint binds the token to the proof's key");
        return token;
    }

    private async Task<string> TokenAsync(string? proof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = "testuser@example.com",
                ["password"] = "Password123!",
                ["client_id"] = "ropc-client",
                ["client_secret"] = "test-secret",
                ["scope"] = "openid profile email",
            }),
        };
        if (proof != null) request.Headers.Add("DPoP", proof);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token_type").GetString().Should().Be(proof == null ? "Bearer" : "DPoP");
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> CallAsync(HttpMethod method, string path, string scheme, string token, string? proof)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
        if (proof != null) request.Headers.Add("DPoP", proof);
        return await Client.SendAsync(request);
    }

    private async Task<JsonElement> IntrospectAsync(string token)
    {
        var response = await Client.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["client_id"] = "ropc-client",
            ["client_secret"] = "test-secret",
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
