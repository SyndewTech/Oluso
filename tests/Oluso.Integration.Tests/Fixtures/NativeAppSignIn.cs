using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;
using static Oluso.Integration.Tests.Fixtures.DPoPTestProofs;

namespace Oluso.Integration.Tests.Fixtures;

/// <summary>
/// A native app's sign-in against a platform client: one public client in the platform tenant (PKCE S256, DPoP-bound,
/// authorization code + one-time sliding refresh tokens, <c>nativeapp://oluso-callback</c>) used on every tenant's host.
/// </summary>
public sealed class NativeAppSignIn
{
    public const string RedirectUri = "nativeapp://oluso-callback";
    public const string Password = "Password123!";

    private readonly OlusoWebApplicationFactory _factory;
    private readonly ECDsa _key;

    public NativeAppSignIn(OlusoWebApplicationFactory factory, ECDsa key, string clientId)
    {
        _factory = factory;
        _key = key;
        ClientId = clientId;
    }

    public string ClientId { get; }

    /// <summary>Adds the platform client (idempotent).</summary>
    public static async Task EnsurePlatformClientAsync(OlusoDbContext db, string clientId, IEnumerable<string> scopes)
    {
        if (await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == clientId && c.TenantId == PlatformConstants.TenantId))
            return;

        using var _ = db.BeginSeedMode();
        const string platform = PlatformConstants.TenantId;
        var client = new Client
        {
            ClientId = clientId,
            ClientName = "Native app",
            TenantId = platform,
            Enabled = true,
            NonEditable = true,
            RequireClientSecret = false,
            TokenEndpointAuthMethod = "none",
            RequirePkce = true,
            AllowPlainTextPkce = false,
            RequireDPoP = true,
            RequireConsent = false,
            AllowOfflineAccess = true,
            RefreshTokenUsage = (int)TokenUsage.OneTimeOnly,
            RefreshTokenExpiration = (int)TokenExpiration.Sliding,
            UseJourneyFlow = false,
        };
        client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code", TenantId = platform });
        client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "refresh_token", TenantId = platform });
        client.RedirectUris.Add(new ClientRedirectUri { RedirectUri = RedirectUri, TenantId = platform });
        foreach (var scope in scopes)
            client.AllowedScopes.Add(new ClientScope { Scope = scope, TenantId = platform });
        client.Properties.Add(new ClientProperty { Key = Oluso.Core.Authorization.ScopeRestrictedClients.PropertyKey, Value = "true", TenantId = platform });
        db.Clients.Add(client);
        await db.SaveChangesAsync();
    }

    /// <summary>A client that stays on <paramref name="tenant"/>'s host (X-Tenant-Id) and keeps its cookies.</summary>
    public HttpClient HttpFor(string tenant)
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        return http;
    }

    /// <summary>The authorize request, PKCE challenge for <paramref name="verifier"/>.</summary>
    public Task<HttpResponseMessage> AuthorizeAsync(HttpClient http, string[] scopes, string verifier) =>
        http.GetAsync(
            "/connect/authorize?response_type=code" +
            $"&client_id={ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(string.Join(' ', scopes))}" +
            $"&code_challenge={Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))}&code_challenge_method=S256" +
            "&state=native");

    /// <summary>
    /// authorize → standalone login → callback → token, on <paramref name="http"/>'s tenant host. The token
    /// response must report <paramref name="granted"/> as its scope (default: all of <paramref name="scopes"/>).
    /// </summary>
    public async Task<JsonElement> SignInAsync(HttpClient http, string email, string[] scopes, string[]? granted = null)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var authorize = await AuthorizeAsync(http, scopes, verifier);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect, await authorize.Content.ReadAsStringAsync());
        var loginLocation = authorize.Headers.Location!.ToString();
        loginLocation.Should().Contain("/account/login");

        var loginPage = await http.GetAsync(loginLocation);
        var antiforgery = Antiforgery(await loginPage.Content.ReadAsStringAsync());
        var returnUrl = HttpUtility.ParseQueryString(new Uri(new Uri("http://localhost"), loginLocation).Query)["returnUrl"];

        var login = await http.PostAsync(loginLocation, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = email,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
            ["ReturnUrl"] = returnUrl!,
            ["__RequestVerificationToken"] = antiforgery!,
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());

        var callback = await http.GetAsync(login.Headers.Location!.ToString());
        var code = await CodeFromAsync(callback);

        var response = await TokenRequestAsync(http, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("scope").GetString()!.Split(' ').Should().BeEquivalentTo(granted ?? scopes);
        return body;
    }

    /// <summary>The code a callback delivers: the native-app interstitial page that fires the deep link.</summary>
    public static async Task<string> CodeFromAsync(HttpResponseMessage callback)
    {
        callback.StatusCode.Should().Be(HttpStatusCode.OK, callback.Headers.Location?.ToString());
        var page = await callback.Content.ReadAsStringAsync();
        var link = Regex.Match(page, Regex.Escape(RedirectUri) + @"\?[^""'\s<>\\]+");
        link.Success.Should().BeTrue(page);
        var code = HttpUtility.ParseQueryString(new Uri(WebUtility.HtmlDecode(link.Value)).Query)["code"];
        code.Should().NotBeNullOrEmpty(link.Value);
        return code!;
    }

    /// <summary>
    /// Redeems a code stored for <paramref name="subjectId"/> on <paramref name="tenant"/>'s host, as the authorize
    /// endpoint would have issued it after the user signed in there.
    /// </summary>
    public async Task<HttpResponseMessage> RedeemStoredCodeAsync(string tenant, string subjectId, string[] scopes)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var code = $"native_{Guid.NewGuid():N}";
        using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_factory.Services))
        {
            var store = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Oluso.Core.Domain.Interfaces.IAuthorizationCodeStore>(scope.ServiceProvider);
            await store.StoreAsync(new AuthorizationCode
            {
                Code = code,
                ClientId = ClientId,
                SubjectId = subjectId,
                RedirectUri = RedirectUri,
                Scopes = scopes.ToList(),
                CodeChallenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                CodeChallengeMethod = "S256",
                TenantId = tenant,
                CreationTime = DateTime.UtcNow,
                Expiration = DateTime.UtcNow.AddMinutes(5),
            });
        }

        return await TokenRequestAsync(HttpFor(tenant), new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        });
    }

    /// <summary>A refresh on <paramref name="tenant"/>'s host, optionally naming the scopes.</summary>
    public Task<HttpResponseMessage> RefreshAsync(string tenant, string refreshToken, string? scope = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
        };
        if (scope != null)
            form["scope"] = scope;
        return TokenRequestAsync(HttpFor(tenant), form);
    }

    /// <summary>A token request with a DPoP proof, answering the server's nonce challenge (RFC 9449 §8).</summary>
    public async Task<HttpResponseMessage> TokenRequestAsync(HttpClient http, Dictionary<string, string> form)
    {
        string? nonce = null;
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent(form) };
            request.Headers.Add("DPoP", Proof(_key, "POST", "/connect/token", accessToken: null, nonce));
            var response = await http.SendAsync(request);
            if (attempt == 0 && response.StatusCode == HttpStatusCode.BadRequest
                && response.Headers.TryGetValues("DPoP-Nonce", out var nonces))
            {
                nonce = nonces.First();
                continue;
            }
            return response;
        }
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Antiforgery(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (match.Success) return match.Groups[1].Value;
        match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        return match.Success ? match.Groups[1].Value : null;
    }
}
