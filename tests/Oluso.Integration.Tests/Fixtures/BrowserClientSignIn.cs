using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;

namespace Oluso.Integration.Tests.Fixtures;

/// <summary>
/// A public browser client in test-client's tenant (authorization code and implicit, no PKCE, no consent, the
/// standalone login page) and an interactive sign-in through it as testuser@example.com.
/// </summary>
public static class BrowserClientSignIn
{
    public const string ClientId = "response-mode-browser-client";
    public const string RedirectUri = "http://localhost:5100/signin-oidc";

    /// <summary>Adds the client (idempotent).</summary>
    public static async Task EnsureClientAsync(OlusoDbContext db)
    {
        if (await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == ClientId))
            return;

        var tenantId = await db.Clients.IgnoreQueryFilters()
            .Where(c => c.ClientId == "test-client").Select(c => c.TenantId).FirstAsync();
        var client = new Client
        {
            ClientId = ClientId,
            ClientName = "Browser client",
            TenantId = tenantId,
            Enabled = true,
            RequireClientSecret = false,
            TokenEndpointAuthMethod = "none",
            RequirePkce = false,
            RequireConsent = false,
            AllowAccessTokensViaBrowser = true,
            UseJourneyFlow = false,
        };
        client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code", TenantId = tenantId });
        client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "implicit", TenantId = tenantId });
        client.RedirectUris.Add(new ClientRedirectUri { RedirectUri = RedirectUri, TenantId = tenantId });
        client.AllowedScopes.Add(new ClientScope { Scope = "openid", TenantId = tenantId });
        client.AllowedScopes.Add(new ClientScope { Scope = "email", TenantId = tenantId });
        db.Clients.Add(client);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Authorize with <paramref name="parameters"/>, the standalone login page, then the callback it sends the
    /// browser to, whose response is returned.
    /// </summary>
    public static async Task<HttpResponseMessage> SignInAsync(OlusoWebApplicationFactory factory, string parameters)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("X-Tenant-Id", "default");

        var authorize = await http.GetAsync($"/connect/authorize?{parameters}&state=s1" +
            $"&client_id={ClientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}");
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loginLocation = authorize.Headers.Location!.ToString();
        loginLocation.Should().Contain("/account/login");

        var loginPage = await (await http.GetAsync(loginLocation)).Content.ReadAsStringAsync();
        var antiforgery = Regex.Match(loginPage, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var returnUrl = HttpUtility.ParseQueryString(new Uri(new Uri("http://localhost"), loginLocation).Query)["returnUrl"];
        var login = await http.PostAsync(loginLocation, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = "testuser@example.com",
            ["Input.Password"] = "Password123!",
            ["Input.RememberMe"] = "false",
            ["ReturnUrl"] = returnUrl!,
            ["__RequestVerificationToken"] = antiforgery,
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());

        return await http.GetAsync(login.Headers.Location!.ToString());
    }

    /// <summary>Signs in for a code (query response) and returns it.</summary>
    public static async Task<string> SignInForCodeAsync(OlusoWebApplicationFactory factory, string scope = "openid")
    {
        var callback = await SignInAsync(factory, "response_type=code&scope=" + Uri.EscapeDataString(scope));
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = callback.Headers.Location!;
        location.ToString().Should().StartWith(RedirectUri);
        var code = HttpUtility.ParseQueryString(location.Query)["code"];
        code.Should().NotBeNullOrEmpty(location.ToString());
        return code!;
    }
}
