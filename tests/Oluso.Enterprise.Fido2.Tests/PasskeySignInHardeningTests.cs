using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Fido2;
using Oluso.Enterprise.Fido2.Configuration;
using Oluso.Enterprise.Fido2.Controllers;
using Oluso.Enterprise.Fido2.Entities;
using Oluso.Enterprise.Fido2.Services;
using Oluso.Enterprise.Fido2.Steps;
using Oluso.Enterprise.Fido2.Stores;
using Xunit;
using CoreAssertionResult = Oluso.Core.Services.Fido2AssertionResult;
using CoreAssertionOptions = Oluso.Core.Services.Fido2AssertionOptions;
using StepAssertionResult = Oluso.Enterprise.Fido2.Services.Fido2AssertionResult;

namespace Oluso.Enterprise.Fido2.Tests;

/// <summary>The page-based passkey login: what it echoes, where it returns to, and the session it issues.</summary>
public class Fido2LoginControllerTests
{
    private readonly Mock<IFido2Service> _fido2 = new();
    private readonly Mock<IOlusoUserService> _users = new();
    private readonly Mock<ISignInAuditor> _auditor = new();
    private readonly Mock<IClientStore> _clients = new();
    private readonly List<ClaimsPrincipal> _signedIn = new();

    public Fido2LoginControllerTests()
    {
        _fido2.Setup(f => f.CreateAssertionOptionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoreAssertionOptions { AssertionId = "a1", Challenge = "c1" });
        _users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo" });
        _users.Setup(u => u.GetClaimsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Claim> { new("sub", "u1") });
        _clients.Setup(c => c.IsRegisteredRedirectUriAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string uri, CancellationToken _) => uri == "https://app.acme.test/callback");
    }

    private Fido2LoginController Controller(string? pendingAssertion = null)
    {
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme, It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string?, ClaimsPrincipal, AuthenticationProperties?>((_, _, principal, _) => _signedIn.Add(principal))
            .Returns(Task.CompletedTask);
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider()
        };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("id.acme.test");
        var session = new TestSession();
        if (pendingAssertion != null) session.SetString("fido2.assertionId", pendingAssertion);
        http.Features.Set<ISessionFeature>(new SessionFeature { Session = session });

        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.IsLocalUrl(It.IsAny<string?>()))
            .Returns((string? u) => !string.IsNullOrEmpty(u) && u.StartsWith('/') && !u.StartsWith("//") && !u.StartsWith("/\\"));

        return new Fido2LoginController(_fido2.Object, _users.Object, NullLogger<Fido2LoginController>.Instance,
            tenantContext: null, signInAuditor: _auditor.Object, clients: _clients.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            Url = url.Object
        };
    }

    private static string Page(IActionResult result) => ((ContentResult)result).Content!;

    [Fact]
    public async Task A_return_url_is_html_encoded_in_the_page()
    {
        var page = Page(await Controller().Login("/connect/authorize/callback?x=\"><script>alert(1)</script>"));

        page.Should().NotContain("\"><script>alert(1)</script>");
        page.Should().Contain("&quot;&gt;&lt;script&gt;");
    }

    [Theory]
    [InlineData("https://evil.test/steal")]
    [InlineData("//evil.test/steal")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://app.acme.test/other-page")]
    [InlineData("http://id.acme.test/connect/authorize/callback")]
    public async Task A_return_url_that_is_not_local_or_registered_goes_home(string returnUrl)
    {
        var page = Page(await Controller().Login(returnUrl));

        page.Should().Contain("name=\"returnUrl\" value=\"/\"");
        page.Should().NotContain("evil.test");
    }

    [Fact]
    public async Task A_return_url_that_is_exactly_a_registered_redirect_uri_is_kept()
    {
        var page = Page(await Controller().Login("https://app.acme.test/callback"));

        page.Should().Contain("name=\"returnUrl\" value=\"https://app.acme.test/callback\"");
    }

    [Fact]
    public async Task The_authorize_callback_on_this_host_is_kept()
    {
        // The sign-in page links here with the absolute callback the coordinator built for this host.
        var callback = "https://id.acme.test/connect/authorize/callback?correlation_id=c1";

        var page = Page(await Controller().Login(callback));

        page.Should().Contain("value=\"https://id.acme.test/connect/authorize/callback?correlation_id=c1\"");
    }

    [Fact]
    public async Task The_error_page_encodes_the_message_and_the_failure_is_audited()
    {
        _fido2.Setup(f => f.VerifyAssertionAsync("a1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoreAssertionResult { Succeeded = false, Error = "bad", ErrorDescription = "<img src=x onerror=alert(1)>" });

        var page = Page(await Controller(pendingAssertion: "a1").VerifyLogin("{}", "/"));

        page.Should().NotContain("<img src=x");
        page.Should().Contain("&lt;img src=x onerror=alert(1)&gt;");
        _auditor.Verify(a => a.RecordFailureAsync(It.Is<SignInFailureAudit>(f => f.FailureReason == "bad"), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task The_form_sign_in_records_how_and_when_it_signed_in_and_is_audited()
    {
        _fido2.Setup(f => f.VerifyAssertionAsync("a1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoreAssertionResult { Succeeded = true, UserId = "u1", CredentialId = "cred" });

        var result = await Controller(pendingAssertion: "a1").VerifyLogin("{}", "https://evil.test/");

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/");
        var principal = _signedIn.Single();
        principal.FindAll("amr").Select(c => c.Value).Should().Equal("fido2", "hwk");
        principal.FindFirst("auth_time").Should().NotBeNull();
        _auditor.Verify(a => a.RecordSuccessAsync(It.Is<SignInAudit>(s => s.SubjectId == "u1" && s.AuthenticationMethod == "fido2"), It.IsAny<CancellationToken>()));
        _users.Verify(u => u.RecordLoginAsync("u1", It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task An_assertion_is_used_once()
    {
        _fido2.Setup(f => f.VerifyAssertionAsync("a1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoreAssertionResult { Succeeded = true, UserId = "u1" });
        var controller = Controller(pendingAssertion: "a1");

        await controller.VerifyLogin("{}", "/");
        var again = Page(await controller.VerifyLogin("{}", "/"));

        again.Should().Contain("Session expired");
        _fido2.Verify(f => f.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();
        public bool IsAvailable => true;
        public string Id => "s1";
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }
}

/// <summary>Username-based assertion options don't tell whether the account exists or has passkeys.</summary>
public class AssertionOptionsProbingTests
{
    private readonly Mock<IOlusoUserService> _users = new();
    private readonly Mock<IFido2CredentialStore> _credentials = new();

    public AssertionOptionsProbingTests()
    {
        _users.Setup(u => u.FindByUsernameAsync("jo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo" });
        _users.Setup(u => u.FindByUsernameAsync("nokeys", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u2", Username = "nokeys" });
        _credentials.Setup(c => c.GetActiveByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Fido2CredentialEntity>());
        _credentials.Setup(c => c.GetActiveByUserIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new Fido2CredentialEntity { UserId = "u1", CredentialId = "real-credential-id" } });
    }

    private Fido2AuthenticationService Service()
    {
        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync("https://id.acme.test");
        return new Fido2AuthenticationService(_users.Object, _credentials.Object, Mock.Of<ITenantContext>(),
            new HttpContextAccessor(), issuer.Object,
            Options.Create(new Fido2Options { RelyingPartyId = "id.acme.test", RelyingPartyName = "Acme" }),
            NullLogger<Fido2AuthenticationService>.Instance);
    }

    [Fact]
    public async Task A_real_user_gets_their_credentials()
    {
        var options = await Service().CreateAssertionOptionsAsync("jo");

        options.AllowCredentials!.Select(c => c.Id).Should().Equal("real-credential-id");
    }

    [Theory]
    [InlineData("nobody@acme.test")]
    [InlineData("nokeys")]
    public async Task An_unknown_account_or_one_without_passkeys_gets_stable_decoys_shaped_like_credentials(string username)
    {
        var service = Service();

        var first = await service.CreateAssertionOptionsAsync(username);
        var second = await service.CreateAssertionOptionsAsync(username.ToUpperInvariant());

        first.AllowCredentials.Should().ContainSingle();
        var decoy = first.AllowCredentials![0];
        decoy.Type.Should().Be("public-key");
        decoy.Id.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        second.AllowCredentials!.Single().Id.Should().Be(decoy.Id, "the same username always gets the same answer");
        (await service.CreateAssertionOptionsAsync("someone-else")).AllowCredentials!.Single().Id.Should().NotBe(decoy.Id);
    }

    [Fact]
    public async Task Every_username_costs_the_same_lookups()
    {
        await Service().CreateAssertionOptionsAsync("nobody@acme.test");
        await Service().CreateAssertionOptionsAsync("jo");

        _users.Verify(u => u.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _users.Verify(u => u.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _credentials.Verify(c => c.GetActiveByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}

/// <summary>Journey passkey steps verify only the assertion (or challenge) they issued, once.</summary>
public class JourneyPasskeyBindingTests
{
    private readonly Mock<IFido2Service> _fido2 = new();
    private readonly Mock<IOlusoUserService> _users = new();
    private readonly Mock<IFido2AuthenticationService> _webAuthn = new();

    public JourneyPasskeyBindingTests()
    {
        _fido2.Setup(f => f.VerifyAssertionAsync("a1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoreAssertionResult { Succeeded = true, UserId = "u1" });
        _users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo" });
        _webAuthn.Setup(w => w.CreateAssertionOptionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AssertionOptions { Challenge = Guid.NewGuid().ToString("N"), RpId = "id.acme.test" });
        _webAuthn.Setup(w => w.VerifyAssertionAsync(It.IsAny<AuthenticatorAssertionResponse>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepAssertionResult { Success = true, UserId = "u1" });
    }

    private StepExecutionContext Step(string type, IDictionary<string, object> data, Dictionary<string, object> input) => new()
    {
        JourneyId = "j1",
        StepId = "passkey",
        Configuration = new JourneyStepConfiguration { Id = "passkey", Type = type },
        JourneyData = data,
        Input = new JourneyStepInput { StepId = "passkey", Values = input },
        ServiceProvider = new ServiceCollection().AddLogging()
            .AddSingleton(_fido2.Object).AddSingleton(_users.Object).AddSingleton(_webAuthn.Object)
            .AddSingleton(Mock.Of<ITenantContext>())
            .BuildServiceProvider()
    };

    [Fact]
    public async Task A_posted_assertion_id_other_than_the_journeys_is_refused()
    {
        var data = new Dictionary<string, object> { [JourneyPasskeyAssertion.DataKey] = "a1" };

        var result = await new Fido2LoginStepHandler().ExecuteAsync(
            Step("fido2_login", data, new() { ["assertionResponse"] = "{}", ["assertionId"] = "someone-elses" }));

        result.Outcome.Should().Be(StepOutcome.RequireInput);
        _fido2.Verify(f => f.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        data.Should().NotContainKey(JourneyPasskeyAssertion.DataKey, "a refused attempt spends the pending assertion");
    }

    [Fact]
    public async Task The_journeys_assertion_signs_in_once()
    {
        var data = new Dictionary<string, object> { [JourneyPasskeyAssertion.DataKey] = "a1" };
        var handler = new Fido2LoginStepHandler();

        var first = await handler.ExecuteAsync(Step("fido2_login", data, new() { ["assertionResponse"] = "{}", ["assertionId"] = "a1" }));
        var replay = await handler.ExecuteAsync(Step("fido2_login", data, new() { ["assertionResponse"] = "{}", ["assertionId"] = "a1" }));

        first.Outcome.Should().Be(StepOutcome.Continue);
        replay.Outcome.Should().Be(StepOutcome.RequireInput);
        _fido2.Verify(f => f.VerifyAssertionAsync("a1", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_webauthn_step_refuses_a_challenge_it_did_not_issue()
    {
        var data = new Dictionary<string, object>();

        var result = await new Fido2StepHandler().ExecuteAsync(
            Step("webauthn", data, new() { ["fido2_response"] = "{}", ["fido2_challenge"] = "attacker-chosen" }));

        result.Outcome.Should().Be(StepOutcome.RequireInput);
        _webAuthn.Verify(w => w.VerifyAssertionAsync(It.IsAny<AuthenticatorAssertionResponse>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_webauthn_step_verifies_against_its_own_challenge_once()
    {
        var data = new Dictionary<string, object>();
        var handler = new Fido2StepHandler();
        await handler.ExecuteAsync(Step("webauthn", data, new()));
        var issued = (string)data[Fido2StepHandler.ChallengeKey];

        var first = await handler.ExecuteAsync(Step("webauthn", data, new() { ["fido2_response"] = "{}", ["fido2_challenge"] = issued }));
        var replay = await handler.ExecuteAsync(Step("webauthn", data, new() { ["fido2_response"] = "{}", ["fido2_challenge"] = issued }));

        first.Outcome.Should().Be(StepOutcome.Continue);
        replay.Outcome.Should().Be(StepOutcome.RequireInput);
        _webAuthn.Verify(w => w.VerifyAssertionAsync(It.IsAny<AuthenticatorAssertionResponse>(), issued, It.IsAny<CancellationToken>()), Times.Once);
    }
}
