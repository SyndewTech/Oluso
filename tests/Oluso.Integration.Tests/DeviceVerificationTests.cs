using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// The device authorization grant end to end (RFC 8628), as "sign in with QR" (MSC4108) uses it for a Matrix client:
/// the device asks for a code with the Matrix API and device scopes, polls the token endpoint (pending, then
/// slow_down when too fast), the user opens the verification page (signing in first), checks the code, client and
/// Matrix device id, and approves or denies. Approval yields tokens with the device scope and a refresh token
/// (the Matrix API scope implies refresh, no offline_access); denial yields access_denied; an expired code
/// expired_token; a wrong code is reported on the page.
/// </summary>
public class DeviceVerificationTests : IntegrationTestBase
{
    private const string Tenant = "default";
    private const string ClientId = "matrix-device-test";
    private const string ClientName = "Example Client";
    private const string ApiScope = "urn:matrix:client:api:*";
    private const string DevicePrefix = "urn:matrix:client:device:";
    private const string DeviceId = "QRDEVICE1";
    private const string UserEmail = "testuser@example.com";
    private const string UserPassword = "Password123!";

    public DeviceVerificationTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override async Task InitializeAsync()
    {
        await WithDbContextAsync(async db =>
        {
            if (await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == ClientId))
                return;

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

            var client = new Client
            {
                ClientId = ClientId, ClientName = ClientName, TenantId = Tenant, Enabled = true,
                ClientUri = "https://client.example", LogoUri = "https://client.example/logo.png",
                RequireClientSecret = false, RequirePkce = true, UseJourneyFlow = false,
                AccessTokenLifetime = 300, DeviceCodeLifetime = 300,
            };
            client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "urn:ietf:params:oauth:grant-type:device_code" });
            client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token" });
            client.AllowedScopes.Add(new ClientScope { Scope = "openid" });
            client.AllowedScopes.Add(new ClientScope { Scope = ApiScope });
            client.AllowedScopes.Add(new ClientScope { Scope = DevicePrefix });
            client.Properties.Add(new ClientProperty { Key = "policy_uri", Value = "https://client.example/privacy" });
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        });
    }

    private HttpClient NewBrowser()
    {
        var http = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("X-Tenant-Id", Tenant);
        return http;
    }

    private async Task<(string DeviceCode, string UserCode, JsonElement Body)> StartAsync(HttpClient http)
    {
        var response = await http.PostAsync("/connect/deviceauthorization", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = $"openid {ApiScope} {DevicePrefix}{DeviceId}",
        }));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonDocument.Parse(body).RootElement.Clone();
        return (json.GetProperty("device_code").GetString()!, json.GetProperty("user_code").GetString()!, json);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PollAsync(HttpClient http, string deviceCode)
    {
        var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["client_id"] = ClientId,
            ["device_code"] = deviceCode,
        }));
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    /// <summary>Follows the verification URI as a signed-out browser: sign-in, then back to the page.</summary>
    private static async Task<string> SignInThroughVerificationPageAsync(HttpClient http, string verificationUri)
    {
        var start = await http.GetAsync(verificationUri);
        start.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loginLocation = start.Headers.Location!.ToString();
        loginLocation.Should().Contain("/account/login", "the device client uses the standard pages");

        var loginPage = await http.GetAsync(loginLocation);
        loginPage.StatusCode.Should().Be(HttpStatusCode.OK);
        var antiforgery = Antiforgery(await loginPage.Content.ReadAsStringAsync());
        var returnUrl = HttpUtility.ParseQueryString(new Uri(new Uri("http://localhost"), loginLocation).Query)["returnUrl"]!;

        var login = await http.PostAsync(loginLocation, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = UserEmail,
            ["Input.Password"] = UserPassword,
            ["Input.RememberMe"] = "false",
            ["ReturnUrl"] = returnUrl,
            ["__RequestVerificationToken"] = antiforgery,
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        var callback = login.Headers.Location!.ToString();
        callback.Should().Contain("/device/callback");

        var back = await http.GetAsync(callback);
        back.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var page = back.Headers.Location!.ToString();
        page.Should().StartWith("/device");

        var shown = await http.GetAsync(page);
        shown.StatusCode.Should().Be(HttpStatusCode.OK);
        return await shown.Content.ReadAsStringAsync();
    }

    private static async Task<string> AnswerAsync(HttpClient http, string pageHtml, string userCode, string button)
    {
        var response = await http.PostAsync("/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["user_code"] = userCode,
            ["button"] = button,
            ["__RequestVerificationToken"] = Antiforgery(pageHtml),
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Approving_on_the_verification_page_issues_tokens_with_the_device_scope_and_a_refresh_token()
    {
        var http = NewBrowser();
        var (deviceCode, userCode, start) = await StartAsync(http);
        start.GetProperty("verification_uri").GetString().Should().EndWith("/device");
        var complete = start.GetProperty("verification_uri_complete").GetString()!;
        complete.Should().Contain("/device?user_code=");

        var pending = await PollAsync(http, deviceCode);
        pending.Status.Should().Be(HttpStatusCode.BadRequest);
        pending.Body.GetProperty("error").GetString().Should().Be("authorization_pending");

        var tooSoon = await PollAsync(http, deviceCode);
        tooSoon.Body.GetProperty("error").GetString().Should().Be("slow_down", "the device polled again within the interval");

        // The code as a person might type it: lower case, no dash.
        var typed = userCode.Replace("-", string.Empty).ToLowerInvariant();
        var page = await SignInThroughVerificationPageAsync(http, $"/device?user_code={typed}");
        page.Should().Contain(userCode);
        page.Should().Contain(ClientName);
        page.Should().Contain($"Device ID: {DeviceId}");
        page.Should().Contain("Sign in to your chat account on a new device");
        page.Should().Contain("https://client.example/privacy");
        page.Should().Contain("not this one");

        var answered = await AnswerAsync(http, page, userCode, "approve");
        answered.Should().Contain("Device approved");

        var token = await PollAsync(http, deviceCode);
        token.Status.Should().Be(HttpStatusCode.OK, token.Body.ToString());
        var scope = token.Body.GetProperty("scope").GetString()!.Split(' ');
        scope.Should().Contain(new[] { ApiScope, DevicePrefix + DeviceId });
        scope.Should().NotContain("offline_access");
        token.Body.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty(
            "the Matrix API scope implies refresh without offline_access");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Body.GetProperty("access_token").GetString());
        jwt.Claims.Should().Contain(c => c.Type == "sid");
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == Tenant);
        jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value).Should().Contain(DevicePrefix + DeviceId);

        var again = await PollAsync(http, deviceCode);
        again.Body.GetProperty("error").GetString().Should().Be("invalid_grant", "a device code is redeemed once");
    }

    [Fact]
    public async Task Denying_on_the_verification_page_answers_access_denied()
    {
        var http = NewBrowser();
        var (deviceCode, userCode, _) = await StartAsync(http);

        var page = await SignInThroughVerificationPageAsync(http, $"/device?user_code={userCode}");
        var answered = await AnswerAsync(http, page, userCode, "deny");
        answered.Should().Contain("Sign-in denied");

        var token = await PollAsync(http, deviceCode);
        token.Status.Should().Be(HttpStatusCode.BadRequest);
        token.Body.GetProperty("error").GetString().Should().Be("access_denied");
    }

    [Fact]
    public async Task An_expired_code_is_reported_on_the_page_and_answers_expired_token()
    {
        var http = NewBrowser();
        var (deviceCode, userCode, _) = await StartAsync(http);
        await WithDbContextAsync(async db =>
        {
            var code = await db.DeviceFlowCodes.IgnoreQueryFilters().SingleAsync(d => d.DeviceCode == deviceCode);
            code.Expiration = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });

        var page = await SignInThroughVerificationPageAsync(http, $"/device?user_code={userCode}");
        page.Should().Contain("That code has expired");
        page.Should().NotContain("value=\"approve\"");

        var token = await PollAsync(http, deviceCode);
        token.Body.GetProperty("error").GetString().Should().Be("expired_token");
    }

    [Fact]
    public async Task A_wrong_code_is_reported_on_the_page_and_cannot_be_approved()
    {
        var http = NewBrowser();
        var (deviceCode, userCode, _) = await StartAsync(http);
        var signedIn = await SignInThroughVerificationPageAsync(http, $"/device?user_code={userCode}");

        var page = await (await http.GetAsync("/device?user_code=WXYZ-WXYZ")).Content.ReadAsStringAsync();
        page.Should().Contain("That code is not valid");

        // A crafted post for the wrong code (the page for it has no approve form).
        var answered = await AnswerAsync(http, signedIn, "WXYZ-WXYZ", "approve");
        answered.Should().Contain("That code is not valid");

        var token = await PollAsync(http, deviceCode);
        token.Body.GetProperty("error").GetString().Should().Be("authorization_pending");
    }

    [Fact]
    public async Task The_verification_page_without_a_code_asks_for_one()
    {
        var http = NewBrowser();
        var (_, userCode, _) = await StartAsync(http);
        await SignInThroughVerificationPageAsync(http, $"/device?user_code={userCode}");

        var page = await (await http.GetAsync("/device")).Content.ReadAsStringAsync();
        page.Should().Contain("Enter the code shown on the device");
        page.Should().Contain("name=\"user_code\"");
    }

    private async Task<(HttpClient Http, JsonElement Tokens)> ApprovedDeviceAsync()
    {
        var http = NewBrowser();
        var (deviceCode, userCode, _) = await StartAsync(http);
        var page = await SignInThroughVerificationPageAsync(http, $"/device?user_code={userCode}");
        await AnswerAsync(http, page, userCode, "approve");
        var token = await PollAsync(http, deviceCode);
        token.Status.Should().Be(HttpStatusCode.OK, token.Body.ToString());
        return (http, token.Body);
    }

    private static async Task<HttpStatusCode> RevokeAsync(HttpClient http, string token)
    {
        var response = await http.PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["token"] = token,
        }));
        return response.StatusCode;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(HttpClient http, string refreshToken)
    {
        var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
        }));
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    [Fact]
    public async Task Revoking_the_access_token_revokes_the_refresh_token_of_the_same_sign_in()
    {
        var (http, tokens) = await ApprovedDeviceAsync();
        var (_, other) = await ApprovedDeviceAsync();

        (await RevokeAsync(http, tokens.GetProperty("access_token").GetString()!)).Should().Be(HttpStatusCode.OK);

        var refresh = await RefreshAsync(http, tokens.GetProperty("refresh_token").GetString()!);
        refresh.Status.Should().Be(HttpStatusCode.BadRequest);
        refresh.Body.GetProperty("error").GetString().Should().Be("invalid_grant");

        var unaffected = await RefreshAsync(http, other.GetProperty("refresh_token").GetString()!);
        unaffected.Status.Should().Be(HttpStatusCode.OK, "another device's sign-in is its own authorization");
    }

    [Fact]
    public async Task Each_approved_device_gets_its_own_session_id()
    {
        var (_, first) = await ApprovedDeviceAsync();
        var (_, second) = await ApprovedDeviceAsync();

        string Sid(JsonElement t) => new JwtSecurityTokenHandler().ReadJwtToken(t.GetProperty("access_token").GetString())
            .Claims.Single(c => c.Type == "sid").Value;
        Sid(first).Should().NotBe(Sid(second));
    }

    [Fact]
    public async Task Revoking_a_refresh_token_revokes_its_rotated_successor_too()
    {
        var (http, tokens) = await ApprovedDeviceAsync();
        var original = tokens.GetProperty("refresh_token").GetString()!;
        var rotated = await RefreshAsync(http, original);
        rotated.Status.Should().Be(HttpStatusCode.OK, rotated.Body.ToString());
        var next = rotated.Body.TryGetProperty("refresh_token", out var n) ? n.GetString()! : original;

        (await RevokeAsync(http, next)).Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(http, next)).Status.Should().Be(HttpStatusCode.BadRequest);
        (await RefreshAsync(http, original)).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    private static string Antiforgery(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success)
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        match.Success.Should().BeTrue("the form must carry an antiforgery token");
        return match.Groups[1].Value;
    }
}
