using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// End-to-end coverage for the interactive standalone login flow
/// (authorize -> /account/login -> callback -> token), which the existing
/// password-grant test does NOT exercise. Guards the fix that carries the
/// validated scopes (including offline_access) through the standalone callback
/// instead of a hardcoded set — without it, no refresh token is ever issued for
/// standalone-login clients.
/// </summary>
public class StandaloneLoginRefreshTokenTests : IntegrationTestBase
{
    private const string ClientId = "interactive-test-client";
    private const string ClientSecret = "test-secret";
    private const string RedirectUri = "http://localhost:9876/callback";

    public StandaloneLoginRefreshTokenTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task StandaloneLogin_WithOfflineAccess_IssuesRefreshToken()
    {
        // One client reused throughout so the cookie container carries the
        // antiforgery + auth cookies across the redirect chain. AllowAutoRedirect
        // is off so we can inspect each hop and capture the authorization code.
        var http = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("X-Tenant-Id", "default");

        // 1. /authorize -> 302 to the standalone login page
        var authorizeUrl =
            "/connect/authorize?response_type=code" +
            $"&client_id={ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString("openid profile email offline_access")}" +
            "&state=test-state";

        var authorizeResp = await http.GetAsync(authorizeUrl);
        authorizeResp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loginLocation = authorizeResp.Headers.Location!.ToString();
        loginLocation.Should().Contain("/account/login", "the client is standalone (UseJourneyFlow=false)");

        // 2. GET the login page -> scrape the antiforgery token + returnUrl
        var loginPageResp = await http.GetAsync(loginLocation);
        loginPageResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginHtml = await loginPageResp.Content.ReadAsStringAsync();
        var antiforgery = ExtractAntiforgeryToken(loginHtml);
        antiforgery.Should().NotBeNullOrEmpty("the login form must render an antiforgery token");

        var loginUri = new Uri(new Uri("http://localhost"), loginLocation);
        var returnUrl = HttpUtility.ParseQueryString(loginUri.Query)["returnUrl"];
        returnUrl.Should().NotBeNullOrEmpty();

        // 3. POST credentials -> 302 back to the authorize callback
        var loginPost = await http.PostAsync(loginLocation, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = "testuser@example.com",
            ["Input.Password"] = "Password123!",
            ["Input.RememberMe"] = "false",
            ["ReturnUrl"] = returnUrl!,
            ["__RequestVerificationToken"] = antiforgery!
        }));

        loginPost.StatusCode.Should().Be(HttpStatusCode.Redirect,
            $"login should succeed and redirect to the callback; body: {await loginPost.Content.ReadAsStringAsync()}");
        var callbackLocation = loginPost.Headers.Location!.ToString();
        callbackLocation.Should().Contain("/connect/authorize/callback");

        // 4. GET the callback -> 302 to redirect_uri with the authorization code
        var callbackResp = await http.GetAsync(callbackLocation);
        callbackResp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var finalLocation = callbackResp.Headers.Location!.ToString();
        finalLocation.Should().StartWith(RedirectUri);

        var code = HttpUtility.ParseQueryString(new Uri(finalLocation).Query)["code"];
        code.Should().NotBeNullOrEmpty("the callback must return an authorization code");

        // 5. Exchange the code -> assert a refresh token comes back (the whole point)
        var tokenResp = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret
        }));

        var tokenBody = await tokenResp.Content.ReadAsStringAsync();
        tokenResp.StatusCode.Should().Be(HttpStatusCode.OK, $"token exchange failed: {tokenBody}");

        var token = JsonDocument.Parse(tokenBody).RootElement;
        token.TryGetProperty("access_token", out var at).Should().BeTrue();
        at.GetString().Should().NotBeNullOrEmpty();

        token.TryGetProperty("refresh_token", out var rt).Should().BeTrue(
            "offline_access was requested and allowed, so the standalone login must yield a refresh token");
        rt.GetString().Should().NotBeNullOrEmpty();

        // The granted scope must retain offline_access (the bug dropped it).
        if (token.TryGetProperty("scope", out var scope))
        {
            scope.GetString().Should().Contain("offline_access");
        }
    }

    private static string? ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (match.Success) return match.Groups[1].Value;

        // Attribute order can vary; try value-before-name too.
        match = Regex.Match(html,
            "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        return match.Success ? match.Groups[1].Value : null;
    }
}
