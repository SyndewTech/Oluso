using System.Security.Claims;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Protocols.Oidc;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Oidc;

/// <summary>
/// RP-Initiated Logout 1.0 at the end session endpoint: an id_token_hint must be one this server issued; a
/// signed-in user is signed out without asking only when a verified hint names them; otherwise they confirm on
/// the sign-out page, whose post must carry an anti-forgery token and the request validated before asking.
/// Nothing else signs a user out or revokes their grants.
/// </summary>
public class OidcEndSessionControllerTests
{
    private const string ClientId = "rp-1";
    private const string UserId = "user-1";
    private const string RegisteredLogoutUri = "https://rp.example/signed-out";

    private readonly Mock<IClientStore> _clients = new();
    private readonly Mock<IPersistedGrantStore> _grants = new();
    private readonly Mock<ISessionHintMatcher> _hints = new();
    private readonly Mock<IAntiforgery> _antiforgery = new();
    private readonly Mock<IAuthenticationService> _authentication = new();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    public OidcEndSessionControllerTests()
    {
        _clients.Setup(c => c.FindClientByIdAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client
        {
            ClientId = ClientId,
            ClientName = "Relying party",
            PostLogoutRedirectUris = new List<ClientPostLogoutRedirectUri> { new() { PostLogoutRedirectUri = RegisteredLogoutUri } }
        });
    }

    [Fact]
    public async Task A_hint_this_server_did_not_issue_is_refused_and_signs_nobody_out()
    {
        SignedIn(UserId);
        _hints.Setup(h => h.ReadIdTokenHintSubjectAsync(It.IsAny<string>(), ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await Get($"id_token_hint={UnsignedHint(ClientId)}&post_logout_redirect_uri={Uri.EscapeDataString(RegisteredLogoutUri)}");

        RedirectTarget(result).Should().StartWith("/error");
        NothingWasEnded();
    }

    [Fact]
    public async Task Without_a_verified_hint_naming_them_a_signed_in_user_is_asked_first()
    {
        SignedIn(UserId);

        var result = await Get($"client_id={ClientId}&post_logout_redirect_uri={Uri.EscapeDataString(RegisteredLogoutUri)}&state=s1");

        RedirectTarget(result).Should().StartWith(OidcEndSessionController.ConfirmationPage + "?logout_id=");
        NothingWasEnded();
    }

    [Fact]
    public async Task A_verified_hint_naming_someone_else_still_asks_first()
    {
        SignedIn(UserId);
        HintNames("another-user");

        var result = await Get($"id_token_hint={UnsignedHint(ClientId)}");

        RedirectTarget(result).Should().StartWith(OidcEndSessionController.ConfirmationPage);
        NothingWasEnded();
    }

    [Fact]
    public async Task A_verified_hint_naming_the_signed_in_user_signs_out_and_returns_to_the_registered_uri()
    {
        SignedIn(UserId);
        HintNames(UserId);

        var result = await Get($"id_token_hint={UnsignedHint(ClientId)}&post_logout_redirect_uri={Uri.EscapeDataString(RegisteredLogoutUri)}&state=s1");

        RedirectTarget(result).Should().Be($"{RegisteredLogoutUri}?state=s1");
        _authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme, It.IsAny<AuthenticationProperties>()), Times.Once);
    }

    [Fact]
    public async Task Signing_out_ends_the_sessions_grants_except_other_clients_offline_access()
    {
        // The initiating client loses everything of this session; another client of the session keeps only a
        // refresh token granted with offline_access (OIDC Core 11). Grants of other sessions are never read.
        SignedIn(UserId);
        HintNames(UserId);
        SessionHas(
            Grant("own-offline", ClientId, "refresh_token", "openid", "offline_access"),
            Grant("other-offline", "rp-2", "refresh_token", "openid", "offline_access"),
            Grant("other-session-bound", "rp-2", "refresh_token", "openid"),
            Grant("other-reference", "rp-2", "reference_token", "openid"));

        await Get($"id_token_hint={UnsignedHint(ClientId)}");

        _grants.Verify(g => g.RemoveAsync("own-offline", It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.RemoveAsync("other-session-bound", It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.RemoveAsync("other-reference", It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.RemoveAsync("other-offline", It.IsAny<CancellationToken>()), Times.Never);
        _grants.Verify(g => g.GetAllAsync(It.Is<PersistedGrantFilter>(f => f.SessionId == "sid-1" && f.SubjectId == UserId), It.IsAny<CancellationToken>()));
        _grants.Verify(g => g.RemoveAllBySubjectAndClientAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Clients_of_the_session_with_a_front_channel_logout_uri_are_loaded_on_the_signed_out_page()
    {
        SignedIn(UserId);
        HintNames(UserId);
        _clients.Setup(c => c.FindClientByIdAsync("rp-2", It.IsAny<CancellationToken>())).ReturnsAsync(new Client
        {
            ClientId = "rp-2",
            FrontChannelLogoutUri = "https://rp2.example/fc-logout",
            FrontChannelLogoutSessionRequired = true,
            RedirectUris = new List<ClientRedirectUri> { new() { RedirectUri = "https://rp2.example/callback" } }
        });
        _clients.Setup(c => c.FindClientByIdAsync("rp-elsewhere", It.IsAny<CancellationToken>())).ReturnsAsync(new Client
        {
            ClientId = "rp-elsewhere",
            // Not the origin of any of its redirect URIs: never framed.
            FrontChannelLogoutUri = "https://attacker.example/frame",
            RedirectUris = new List<ClientRedirectUri> { new() { RedirectUri = "https://rp3.example/callback" } }
        });
        SessionHas(Grant("g1", "rp-2", "session_client"), Grant("g2", "rp-elsewhere", "session_client"));

        var result = await Get($"id_token_hint={UnsignedHint(ClientId)}&post_logout_redirect_uri={Uri.EscapeDataString(RegisteredLogoutUri)}");

        var target = RedirectTarget(result);
        target.Should().StartWith(OidcEndSessionController.SignedOutPagePath + "?logout_id=");
        var protector = _dataProtection.CreateProtector(OidcEndSessionController.SignedOutPurpose).ToTimeLimitedDataProtector();
        var page = JsonSerializer.Deserialize<OidcEndSessionController.SignedOutPage>(
            protector.Unprotect(HttpUtility.ParseQueryString(target[(target.IndexOf('?') + 1)..])[OidcEndSessionController.LogoutIdField]!))!;
        page.Frames.Should().ContainSingle().Which.Should().Be("https://rp2.example/fc-logout?iss=https%3A%2F%2Fop.example&sid=sid-1");
        page.Continue.Should().Be(RegisteredLogoutUri);
    }

    [Fact]
    public async Task A_client_id_that_is_not_the_hints_client_is_refused()
    {
        SignedIn(UserId);
        _hints.Setup(h => h.ReadIdTokenHintSubjectAsync(It.IsAny<string>(), "someone-else", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await Get($"client_id=someone-else&id_token_hint={UnsignedHint(ClientId)}");

        RedirectTarget(result).Should().StartWith("/error");
        NothingWasEnded();
    }

    [Fact]
    public async Task An_unregistered_post_logout_redirect_uri_is_never_followed()
    {
        SignedIn(UserId);
        HintNames(UserId);

        var result = await Get($"id_token_hint={UnsignedHint(ClientId)}&post_logout_redirect_uri={Uri.EscapeDataString("https://attacker.example/")}");

        RedirectTarget(result).Should().StartWith("/error").And.NotContain("attacker.example");
        NothingWasEnded();
    }

    [Fact]
    public async Task Confirming_without_the_anti_forgery_token_signs_nobody_out()
    {
        SignedIn(UserId);
        var logoutId = await PendingLogoutIdAsync();
        _antiforgery.Setup(a => a.IsRequestValidAsync(It.IsAny<HttpContext>())).ReturnsAsync(false);

        var result = await Post(logoutId);

        RedirectTarget(result).Should().StartWith("/error");
        NothingWasEnded();
    }

    [Fact]
    public async Task Confirming_signs_out_and_follows_the_request_validated_before_asking()
    {
        SignedIn(UserId);
        var logoutId = await PendingLogoutIdAsync();
        _antiforgery.Setup(a => a.IsRequestValidAsync(It.IsAny<HttpContext>())).ReturnsAsync(true);

        var result = await Post(logoutId);

        RedirectTarget(result).Should().Be($"{RegisteredLogoutUri}?state=s1");
        _authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme, It.IsAny<AuthenticationProperties>()), Times.Once);
    }

    [Fact]
    public async Task A_confirmation_for_another_user_signs_nobody_out()
    {
        SignedIn(UserId);
        var logoutId = await PendingLogoutIdAsync();
        SignedIn("another-user");
        _antiforgery.Setup(a => a.IsRequestValidAsync(It.IsAny<HttpContext>())).ReturnsAsync(true);

        var result = await Post(logoutId);

        RedirectTarget(result).Should().StartWith("/error");
        NothingWasEnded();
    }

    [Fact]
    public async Task A_tampered_confirmation_signs_nobody_out()
    {
        SignedIn(UserId);
        _antiforgery.Setup(a => a.IsRequestValidAsync(It.IsAny<HttpContext>())).ReturnsAsync(true);

        var result = await Post("not-a-protected-request");

        RedirectTarget(result).Should().StartWith("/error");
        NothingWasEnded();
    }

    private async Task<string> PendingLogoutIdAsync()
    {
        var asked = await Get($"client_id={ClientId}&post_logout_redirect_uri={Uri.EscapeDataString(RegisteredLogoutUri)}&state=s1");
        var target = RedirectTarget(asked);
        _authentication.Invocations.Clear();
        _grants.Invocations.Clear();
        return HttpUtility.ParseQueryString(target[(target.IndexOf('?') + 1)..])[OidcEndSessionController.LogoutIdField]!;
    }

    private void SignedIn(string userId)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId), new Claim("sid", "sid-1") }, "cookie"));
        _authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
    }

    private void HintNames(string subject) =>
        _hints.Setup(h => h.ReadIdTokenHintSubjectAsync(It.IsAny<string>(), ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subject);

    private void NothingWasEnded()
    {
        _authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()), Times.Never);
        _grants.Verify(g => g.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SessionHas(params PersistedGrant[] grants) =>
        _grants.Setup(g => g.GetAllAsync(It.Is<PersistedGrantFilter>(f => f.SessionId == "sid-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grants);

    private static PersistedGrant Grant(string key, string clientId, string type, params string[] scopes) => new()
    {
        Key = key,
        ClientId = clientId,
        Type = type,
        SubjectId = UserId,
        SessionId = "sid-1",
        Data = JsonSerializer.Serialize(new { Scopes = scopes })
    };

    private Task<IActionResult> Get(string query)
    {
        var controller = Controller();
        controller.HttpContext.Request.Method = HttpMethods.Get;
        controller.HttpContext.Request.QueryString = new QueryString("?" + query);
        return controller.EndSession(CancellationToken.None);
    }

    private Task<IActionResult> Post(string logoutId)
    {
        var controller = Controller();
        controller.HttpContext.Request.Method = HttpMethods.Post;
        controller.HttpContext.Request.ContentType = "application/x-www-form-urlencoded";
        controller.HttpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            [OidcEndSessionController.LogoutIdField] = logoutId
        });
        return controller.EndSession(CancellationToken.None);
    }

    private OidcEndSessionController Controller()
    {
        var services = new ServiceCollection().AddSingleton(_authentication.Object).BuildServiceProvider();
        return new OidcEndSessionController(
            _clients.Object,
            _grants.Object,
            new RedirectUriValidator(),
            Mock.Of<ITenantContext>(),
            _hints.Object,
            Mock.Of<IIssuerResolver>(i => i.GetIssuerAsync(It.IsAny<CancellationToken>()) == Task.FromResult("https://op.example")),
            _antiforgery.Object,
            _dataProtection,
            NullLogger<OidcEndSessionController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } }
        };
    }

    private static string RedirectTarget(IActionResult result) => result.Should().BeOfType<RedirectResult>().Subject.Url;

    /// <summary>A JWT-shaped hint naming the client; whether it verifies is the matcher's (mocked) answer.</summary>
    private static string UnsignedHint(string audience)
    {
        static string Segment(object value) => Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(JsonSerializer.Serialize(value));
        return $"{Segment(new { alg = "RS256" })}.{Segment(new { aud = audience, sub = UserId })}.c2ln";
    }
}
