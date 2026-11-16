using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Self-contained access tokens stop working when they are revoked: those of a session whose authorization code was
/// replayed (RFC 6749 section 4.1.2), and a token revoked at the revocation endpoint (RFC 7009), at this server's
/// resource endpoints and at introspection (RFC 7662). Also the acr of an ID token (OpenID Connect Core 1.0 section 2).
/// </summary>
public class AccessTokenRevocationTests : IntegrationTestBase
{
    public AccessTokenRevocationTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override Task InitializeAsync() => WithDbContextAsync(BrowserClientSignIn.EnsureClientAsync);

    [Fact]
    public async Task A_replayed_code_revokes_the_access_token_already_issued_from_it()
    {
        var code = await BrowserClientSignIn.SignInForCodeAsync(Factory);

        var first = await RedeemAsync(code);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var accessToken = (await ReadJsonAsync(first)).GetProperty("access_token").GetString()!;
        (await UserInfoAsync(accessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await RedeemAsync(code);
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(replay)).GetProperty("error").GetString().Should().Be("invalid_grant");

        (await UserInfoAsync(accessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoking_a_session_access_token_stops_it_and_leaves_other_sessions_alone()
    {
        var revokedToken = await SignInForAccessTokenAsync();
        var otherSessionToken = await SignInForAccessTokenAsync();

        var revoke = await Client.PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = revokedToken,
            ["token_type_hint"] = "access_token",
            ["client_id"] = BrowserClientSignIn.ClientId,
        }));
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        (await UserInfoAsync(revokedToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await UserInfoAsync(otherSessionToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_revoked_token_of_no_session_is_inactive_at_introspection()
    {
        var issued = await Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret",
        }));
        issued.StatusCode.Should().Be(HttpStatusCode.OK);
        var accessToken = (await ReadJsonAsync(issued)).GetProperty("access_token").GetString()!;
        new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.Should().NotContain(c => c.Type == "sid");
        (await IntrospectAsync(accessToken)).GetProperty("active").GetBoolean().Should().BeTrue();

        var revoke = await Client.PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = accessToken,
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret",
        }));
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        (await IntrospectAsync(accessToken)).GetProperty("active").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_session_revocation_covers_only_tokens_issued_up_to_it()
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAccessTokenRevocationStore>();
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        await store.RevokeSessionAsync("default", "some-client", sessionId, now.AddHours(1));

        (await store.IsRevokedAsync("default", "some-client", sessionId, null, now.AddSeconds(-5))).Should().BeTrue();
        (await store.IsRevokedAsync("default", "some-client", sessionId, null, now.AddSeconds(5))).Should().BeFalse();
        (await store.IsRevokedAsync("default", "other-client", sessionId, null, now.AddSeconds(-5))).Should().BeFalse();
        (await store.IsRevokedAsync("other-tenant", "some-client", sessionId, null, now.AddSeconds(-5))).Should().BeFalse();
    }

    [Fact]
    public async Task A_password_sign_in_is_single_factor_in_the_id_token_and_discovery_lists_both_values()
    {
        var code = await BrowserClientSignIn.SignInForCodeAsync(Factory);
        var tokens = await ReadJsonAsync(await RedeemAsync(code));
        var idToken = new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("id_token").GetString());
        idToken.Claims.Should().Contain(c => c.Type == "acr" && c.Value == AuthenticationContextClasses.SingleFactor);

        var discovery = await ReadJsonAsync(await Client.GetAsync("/.well-known/openid-configuration"));
        discovery.GetProperty("acr_values_supported").EnumerateArray().Select(v => v.GetString())
            .Should().BeEquivalentTo(AuthenticationContextClasses.SingleFactor, AuthenticationContextClasses.MultiFactor);
    }

    private async Task<string> SignInForAccessTokenAsync()
    {
        var response = await RedeemAsync(await BrowserClientSignIn.SignInForCodeAsync(Factory));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadJsonAsync(response)).GetProperty("access_token").GetString()!;
    }

    private Task<HttpResponseMessage> RedeemAsync(string code) =>
        Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = BrowserClientSignIn.RedirectUri,
            ["client_id"] = BrowserClientSignIn.ClientId,
        }));

    private async Task<HttpResponseMessage> UserInfoAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }

    private async Task<JsonElement> IntrospectAsync(string token) =>
        await ReadJsonAsync(await Client.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret",
        })));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
