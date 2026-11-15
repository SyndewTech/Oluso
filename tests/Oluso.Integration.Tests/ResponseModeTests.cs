using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Authorization response modes end to end (OAuth 2.0 Multiple Response Type Encoding Practices, OAuth 2.0 Form Post
/// Response Mode): an error is delivered in the mode a success would be, a response that carries a token is never
/// query encoded, and the hybrid flow is open to a client registered for authorization_code and implicit.
/// </summary>
public class ResponseModeTests : IntegrationTestBase
{
    private const string CodeClientId = "test-client";
    private const string BrowserClientId = BrowserClientSignIn.ClientId;
    private const string RedirectUri = BrowserClientSignIn.RedirectUri;

    public ResponseModeTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override Task InitializeAsync() => WithDbContextAsync(BrowserClientSignIn.EnsureClientAsync);

    [Fact]
    public async Task FormPost_error_is_posted_to_the_redirect_uri()
    {
        var response = await AuthorizeAsync(CodeClientId, "code", "&response_mode=form_post&prompt=none");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain($"<form method=\"post\" action=\"{RedirectUri}\">");
        html.Should().Contain("name=\"error\" value=\"login_required\"");
        html.Should().Contain("name=\"state\" value=\"s1\"");
        html.Should().Contain("name=\"iss\"");
    }

    [Fact]
    public async Task FormPost_success_after_sign_in_posts_the_code_to_the_redirect_uri()
    {
        // The callback after the login page builds the response from the restored request: form_post must work there.
        var callback = await SignInAsync("response_type=code&response_mode=form_post&scope=openid");

        callback.StatusCode.Should().Be(HttpStatusCode.OK, callback.Headers.Location?.ToString());
        var html = await callback.Content.ReadAsStringAsync();
        html.Should().Contain($"<form method=\"post\" action=\"{RedirectUri}\">");
        html.Should().MatchRegex("name=\"code\" value=\"[^\"]+\"");
        html.Should().Contain("name=\"state\" value=\"s1\"");
        callback.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("http://localhost:5100");
    }

    [Fact]
    public async Task Implicit_id_token_after_sign_in_carries_the_scope_claims()
    {
        // OpenID Connect Core §5.4: with no access token issued, the scope's claims are in the ID token. They come from
        // the client loaded again for the callback after the login page.
        var callback = await SignInAsync("response_type=id_token&nonce=n1&scope=" + Uri.EscapeDataString("openid email"));

        var location = Location(callback);
        var idToken = HttpUtility.ParseQueryString(location.Fragment.TrimStart('#'))["id_token"];
        idToken.Should().NotBeNullOrEmpty(location.ToString());
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(idToken).Claims.ToList();
        claims.Should().Contain(c => c.Type == "email" && c.Value == "testuser@example.com");
        claims.Should().Contain(c => c.Type == "email_verified");
        claims.Should().Contain(c => c.Type == "tenant_id");
    }

    [Fact]
    public async Task Hybrid_id_token_leaves_the_scope_claims_to_userinfo()
    {
        // OpenID Connect Core §5.4: code id_token leads to an access token, so email is read from userinfo.
        var callback = await SignInAsync("response_type=" + Uri.EscapeDataString("code id_token") +
            "&nonce=n1&scope=" + Uri.EscapeDataString("openid email"));

        var location = Location(callback);
        var idToken = HttpUtility.ParseQueryString(location.Fragment.TrimStart('#'))["id_token"];
        idToken.Should().NotBeNullOrEmpty(location.ToString());
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(idToken).Claims.ToList();
        claims.Should().Contain(c => c.Type == "c_hash");
        claims.Should().NotContain(c => c.Type == "email");
        claims.Should().Contain(c => c.Type == "tenant_id");
    }

    private Task<HttpResponseMessage> SignInAsync(string parameters) => BrowserClientSignIn.SignInAsync(Factory, parameters);

    [Fact]
    public async Task Implicit_error_without_response_mode_is_fragment_encoded()
    {
        var response = await AuthorizeAsync(BrowserClientId, "id_token", "&nonce=n1&prompt=none");

        var location = Location(response);
        location.Query.Should().BeEmpty();
        var fragment = HttpUtility.ParseQueryString(location.Fragment.TrimStart('#'));
        fragment["error"].Should().Be("login_required");
        fragment["state"].Should().Be("s1");
    }

    [Fact]
    public async Task Hybrid_is_allowed_for_a_client_with_authorization_code_and_implicit()
    {
        var response = await AuthorizeAsync(BrowserClientId, "code id_token", "&nonce=n1&prompt=none");

        // Past response type validation: the request reaches sign-in and comes back as login_required.
        var fragment = HttpUtility.ParseQueryString(Location(response).Fragment.TrimStart('#'));
        fragment["error"].Should().Be("login_required");
    }

    [Fact]
    public async Task Hybrid_is_refused_for_a_client_without_implicit()
    {
        var response = await AuthorizeAsync(CodeClientId, "code id_token", "&nonce=n1");

        // Checked before the redirect URI is, so it is shown on the error page rather than sent to the client.
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("error=unauthorized_client");
    }

    [Fact]
    public async Task Query_mode_for_a_token_response_is_refused_in_the_fragment()
    {
        var response = await AuthorizeAsync(BrowserClientId, "id_token", "&nonce=n1&response_mode=query");

        var location = Location(response);
        location.Query.Should().BeEmpty();
        var fragment = HttpUtility.ParseQueryString(location.Fragment.TrimStart('#'));
        fragment["error"].Should().Be("invalid_request");
        fragment["state"].Should().Be("s1");
    }

    [Fact]
    public async Task Unsupported_response_mode_is_refused()
    {
        var response = await AuthorizeAsync(CodeClientId, "code", "&response_mode=query.jwt");

        var query = HttpUtility.ParseQueryString(Location(response).Query);
        query["error"].Should().Be("invalid_request");
        query["state"].Should().Be("s1");
    }

    private async Task<HttpResponseMessage> AuthorizeAsync(string clientId, string responseType, string extra)
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = "/connect/authorize?" +
            $"response_type={Uri.EscapeDataString(responseType)}&" +
            $"client_id={clientId}&" +
            $"redirect_uri={Uri.EscapeDataString(RedirectUri)}&" +
            "scope=openid&state=s1&" +
            $"code_challenge={challenge}&code_challenge_method=S256" +
            extra;

        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        return await client.GetAsync(url);
    }

    private static Uri Location(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.ToString().Should().StartWith(RedirectUri);
        return location;
    }
}
