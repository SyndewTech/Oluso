using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// The claims request parameter (OpenID Connect Core 1.0 section 5.5) on a tenant that supports it: claims asked for
/// in the ID token go in it when a granted scope releases them, an essential acr the sign-in does not meet fails it
/// (section 5.5.1.1), and a named sub is answered only for that user (section 5.5.1).
/// </summary>
public class ClaimsParameterTests : IntegrationTestBase
{
    private bool? _supportedBefore;

    public ClaimsParameterTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override async Task InitializeAsync()
    {
        await WithDbContextAsync(BrowserClientSignIn.EnsureClientAsync);
        await WithDbContextAsync(async db =>
        {
            var tenant = await db.Tenants.FindAsync("default");
            tenant!.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenant.Id };
            _supportedBefore = tenant.ProtocolConfiguration.ClaimsParameterSupported;
            tenant.ProtocolConfiguration.ClaimsParameterSupported = true;
            await db.SaveChangesAsync();
        });
    }

    public override Task DisposeAsync() => SetSupportedAsync(_supportedBefore ?? false);

    [Fact]
    public async Task A_claim_asked_for_in_the_id_token_is_in_it_beside_the_access_token()
    {
        var idToken = await IdTokenAsync("openid email", """{"id_token":{"email":null}}""");

        idToken.Claims.Should().Contain(c => c.Type == "email" && c.Value == "testuser@example.com");
        idToken.Claims.Should().NotContain(c => c.Type == ClaimsRequest.IdTokenClaimsCarrier);
    }

    [Fact]
    public async Task A_claim_no_granted_scope_releases_is_left_out()
    {
        var idToken = await IdTokenAsync("openid", """{"id_token":{"email":{"essential":true}}}""");

        idToken.Claims.Should().NotContain(c => c.Type == "email");
    }

    [Fact]
    public async Task An_essential_acr_the_sign_in_meets_is_in_the_id_token()
    {
        var claims = JsonSerializer.Serialize(new
        {
            id_token = new { acr = new { essential = true, values = new[] { AuthenticationContextClasses.SingleFactor } } }
        });

        var idToken = await IdTokenAsync("openid", claims);

        idToken.Claims.Should().Contain(c => c.Type == "acr" && c.Value == AuthenticationContextClasses.SingleFactor);
    }

    [Fact]
    public async Task An_essential_acr_the_sign_in_does_not_meet_fails_the_request()
    {
        var claims = JsonSerializer.Serialize(new
        {
            id_token = new { acr = new { essential = true, value = AuthenticationContextClasses.MultiFactor } }
        });

        var callback = await SignInAsync("openid", claims);

        var query = CallbackQuery(callback);
        query["error"].Should().Be(OidcConstants.Errors.UnmetAuthenticationRequirements);
        query["code"].Should().BeNull();
        query["state"].Should().Be("s1");
    }

    [Fact]
    public async Task A_named_sub_that_is_not_the_signed_in_user_fails_the_request()
    {
        var callback = await SignInAsync("openid", """{"id_token":{"sub":{"value":"someone-else"}}}""");

        var query = CallbackQuery(callback);
        query["error"].Should().Be(OidcConstants.Errors.LoginRequired);
        query["code"].Should().BeNull();
    }

    [Fact]
    public async Task A_malformed_claims_parameter_is_refused_on_the_redirect_uri()
    {
        var response = await AuthorizeAsync("""{"id_token":["email"]}""");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.ToString().Should().StartWith(BrowserClientSignIn.RedirectUri);
        HttpUtility.ParseQueryString(location.Query)["error"].Should().Be(OidcConstants.Errors.InvalidRequest);
    }

    [Fact]
    public async Task A_tenant_that_does_not_support_it_ignores_it()
    {
        await SetSupportedAsync(false);

        var response = await AuthorizeAsync("not json");

        // Past validation: the request goes on to sign-in.
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/account/login");
    }

    [Fact]
    public async Task The_settings_page_switch_turns_it_on_and_off_in_discovery()
    {
        var admin = await SuperAdminClient.CreateAsync(Factory, "default");
        var anonymous = Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
        var settings = JsonNode.Parse(await admin.GetStringAsync("/api/admin/settings"))!;
        settings["protocolSettings"]!["claimsParameterSupported"]!.GetValue<bool>().Should().BeTrue();

        foreach (var supported in new[] { false, true })
        {
            var body = settings.DeepClone();
            body["protocolSettings"]!["claimsParameterSupported"] = supported;
            var saved = await admin.PutAsync("/api/admin/settings",
                new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

            var discovery = JsonDocument.Parse(await anonymous.GetStringAsync("/.well-known/openid-configuration")).RootElement;
            discovery.GetProperty("claims_parameter_supported").GetBoolean().Should().Be(supported);
        }
    }

    private async Task<JwtSecurityToken> IdTokenAsync(string scope, string claims)
    {
        var callback = await SignInAsync(scope, claims);
        var query = CallbackQuery(callback);
        query["code"].Should().NotBeNullOrEmpty(callback.Headers.Location?.ToString());

        var redeemed = await Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = query["code"]!,
            ["redirect_uri"] = BrowserClientSignIn.RedirectUri,
            ["client_id"] = BrowserClientSignIn.ClientId,
        }));
        redeemed.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = JsonDocument.Parse(await redeemed.Content.ReadAsStringAsync()).RootElement;
        tokens.TryGetProperty("access_token", out _).Should().BeTrue();
        return new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("id_token").GetString());
    }

    private Task<HttpResponseMessage> SignInAsync(string scope, string claims) =>
        BrowserClientSignIn.SignInAsync(Factory,
            "response_type=code&scope=" + Uri.EscapeDataString(scope) + "&claims=" + Uri.EscapeDataString(claims));

    private async Task<HttpResponseMessage> AuthorizeAsync(string claims)
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
        return await client.GetAsync("/connect/authorize?response_type=code&scope=openid&state=s1" +
            $"&client_id={BrowserClientSignIn.ClientId}&redirect_uri={Uri.EscapeDataString(BrowserClientSignIn.RedirectUri)}" +
            "&claims=" + Uri.EscapeDataString(claims));
    }

    private static System.Collections.Specialized.NameValueCollection CallbackQuery(HttpResponseMessage callback)
    {
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = callback.Headers.Location!;
        location.ToString().Should().StartWith(BrowserClientSignIn.RedirectUri);
        return HttpUtility.ParseQueryString(location.Query);
    }

    private Task SetSupportedAsync(bool supported) => WithDbContextAsync(async db =>
    {
        var tenant = await db.Tenants.FindAsync("default");
        tenant!.ProtocolConfiguration!.ClaimsParameterSupported = supported;
        await db.SaveChangesAsync();
    });
}
