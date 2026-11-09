using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Protocols.FedCM;
using Oluso.Protocols.Grants;
using Oluso.Protocols.Validation;
using Oluso.Protocols.WebAuthn;
using Xunit;

namespace Oluso.Tests.Protocols.Security;

/// <summary>
/// Sign-ins that return a code to a page (SDK passkey, embedded/headless journeys, FedCM) complete through
/// <see cref="IAuthorizationCodeIssuer"/>: the authorize endpoint's client rules decide whether a code is
/// issued at all, and the code it issues redeems at the token endpoint like any other, with the session and
/// methods of the sign-in and no claims the profile service did not grant.
/// </summary>
public class DirectAuthorizationCodeTests
{
    private readonly CodeIssuerKit _kit = new();
    private readonly Mock<IFido2Service> _fido2 = new();

    public DirectAuthorizationCodeTests()
    {
        _fido2.Setup(f => f.CreateAssertionOptionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fido2AssertionOptions { AssertionId = "a1", Challenge = "c1" });
        _fido2.Setup(f => f.VerifyAssertionAsync("a1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Fido2AssertionResult { Succeeded = true, UserId = "u1", CredentialId = "cred-1" });
    }

    private WebAuthnApiController Passkeys(string? origin = CodeIssuerKit.AppOrigin, Tenant? tenant = null)
    {
        var http = new DefaultHttpContext();
        if (origin != null) http.Request.Headers.Origin = origin;
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.Tenant).Returns(tenant);
        return new WebAuthnApiController(_fido2.Object, _kit.Users.Object, _kit.Issuer, tenantContext.Object,
            NullLogger<WebAuthnApiController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static AssertionVerifyRequest Assertion(string? scope = "openid profile", string? challenge = CodeIssuerKit.Challenge, string? method = "S256") => new()
    {
        ClientId = "spa",
        AssertionId = "a1",
        AssertionResponse = "{}",
        CodeChallenge = challenge,
        CodeChallengeMethod = method,
        Scope = scope
    };

    private static string? ErrorOf(IActionResult result) =>
        ((ObjectResult)result).Value!.GetType().GetProperty("error")!.GetValue(((ObjectResult)result).Value) as string;

    [Fact]
    public async Task A_passkey_sign_in_yields_a_code_that_redeems_with_the_session_methods_and_granted_scopes()
    {
        var result = await Passkeys().VerifyAssertion(Assertion(), default);

        var code = (string)((OkObjectResult)result).Value!.GetType().GetProperty("code")!.GetValue(((OkObjectResult)result).Value)!;
        var stored = _kit.Codes.Single();
        stored.Code.Should().Be(code);
        stored.RedirectUri.Should().Be(DirectAuthorizationRequest.PostMessage);
        stored.Scopes.Should().BeEquivalentTo("openid", "profile");
        stored.SessionId.Should().NotBeNullOrEmpty();
        stored.TenantId.Should().Be("acme");
        stored.Claims.Should().NotContainKeys("email", "name", "auth_method", "credential_id", "sub");
        stored.Claims.Should().ContainKey("auth_time");
        AuthorizationCodes.ReadAmr(stored).Should().Equal("fido2", "hwk");
        _kit.SignIns.Should().ContainSingle(s => s.SubjectId == "u1" && s.AuthenticationMethod == "fido2"
                                                  && s.ClientId == "spa" && s.SessionId == stored.SessionId);

        var grant = await Redeem(code, CodeIssuerKit.Verifier, DirectAuthorizationRequest.PostMessage);

        grant.IsValid.Should().BeTrue(grant.ErrorDescription);
        grant.SubjectId.Should().Be("u1");
        grant.SessionId.Should().Be(stored.SessionId);
        grant.Scopes.Should().BeEquivalentTo("openid", "profile");
        grant.Amr.Should().Equal("fido2", "hwk");
        grant.Claims.Should().NotContainKey("email", "the email scope was not granted");
    }

    [Fact]
    public async Task A_scope_the_client_is_not_allowed_is_left_out_of_the_code()
    {
        var result = await Passkeys().VerifyAssertion(Assertion(scope: "openid permissions no.such.scope"), default);

        result.Should().BeOfType<OkObjectResult>();
        _kit.Codes.Single().Scopes.Should().Equal("openid");
    }

    [Fact]
    public async Task Asking_only_for_scopes_the_client_is_not_allowed_issues_no_code()
    {
        var result = await Passkeys().VerifyAssertion(Assertion(scope: "permissions"), default);

        result.Should().BeOfType<BadRequestObjectResult>();
        ErrorOf(result).Should().Be(OidcConstants.Errors.InvalidScope);
        _kit.Codes.Should().BeEmpty();
        _fido2.Verify(f => f.VerifyAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the request is refused before the assertion is spent");
    }

    [Fact]
    public async Task Openid_the_client_is_not_allowed_issues_no_code()
    {
        _kit.Spa.AllowedScopes = new List<ClientScope> { new() { Scope = "profile" } };

        var result = await Passkeys().VerifyAssertion(Assertion(scope: "openid profile"), default);

        ErrorOf(result).Should().Be(OidcConstants.Errors.InvalidScope);
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_strict_client_asking_for_a_scope_it_is_not_allowed_issues_no_code()
    {
        _kit.Spa.Properties = new List<ClientProperty> { new() { Key = "strict_scopes", Value = "true" } };

        var result = await Passkeys().VerifyAssertion(Assertion(scope: "openid permissions"), default);

        ErrorOf(result).Should().Be(OidcConstants.Errors.InvalidScope);
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_scope_the_default_set_is_trimmed_to_what_the_client_allows()
    {
        _kit.Spa.AllowedScopes = new List<ClientScope> { new() { Scope = "openid" } };

        await Passkeys().VerifyAssertion(Assertion(scope: null), default);

        _kit.Codes.Single().Scopes.Should().Equal("openid");
    }

    [Fact]
    public async Task A_disabled_client_gets_no_code()
    {
        _kit.Spa.Enabled = false;

        var result = await Passkeys().VerifyAssertion(Assertion(), default);

        ErrorOf(result).Should().Be(OidcConstants.Errors.InvalidClient);
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_client_without_the_authorization_code_grant_gets_no_code()
    {
        _kit.Spa.AllowedGrantTypes = new List<ClientGrantType> { new() { GrantType = "client_credentials" } };

        var result = await Passkeys().VerifyAssertion(Assertion(), default);

        ErrorOf(result).Should().Be(OidcConstants.Errors.UnauthorizedClient);
        _kit.Codes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "S256")]
    [InlineData(CodeIssuerKit.Challenge, "plain")]
    [InlineData("too-short", "S256")]
    public async Task Pkce_s256_is_required(string? challenge, string method)
    {
        var result = await Passkeys().VerifyAssertion(Assertion(challenge: challenge, method: method), default);

        result.Should().BeOfType<BadRequestObjectResult>();
        ErrorOf(result).Should().Be(OidcConstants.Errors.InvalidRequest);
        _kit.Codes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://evil.test")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task A_page_on_an_origin_the_client_did_not_register_gets_no_code(string? origin)
    {
        var result = await Passkeys(origin).VerifyAssertion(Assertion(), default);

        ((ObjectResult)result).StatusCode.Should().Be(403);
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task The_origin_of_a_registered_redirect_uri_may_receive_codes()
    {
        _kit.Spa.AllowedCorsOrigins = new List<ClientCorsOrigin>();
        _kit.Spa.RedirectUris = new List<ClientRedirectUri> { new() { RedirectUri = "https://APP.acme.test/callback" } };

        var result = await Passkeys().VerifyAssertion(Assertion(), default);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Assertion_options_are_refused_to_an_unregistered_origin_before_the_passkey_prompt()
    {
        var result = await Passkeys("https://evil.test").GetAssertionOptions(new AssertionOptionsRequest { ClientId = "spa" }, default);

        ((ObjectResult)result).StatusCode.Should().Be(403);
        _fido2.Verify(f => f.CreateAssertionOptionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_tenant_with_passkeys_off_refuses_passkey_sign_in()
    {
        var result = await Passkeys(tenant: new Tenant { Id = "acme", EnablePasskey = false }).VerifyAssertion(Assertion(), default);

        result.Should().BeOfType<BadRequestObjectResult>();
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task An_inactive_user_gets_no_code()
    {
        _kit.Users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "u1", IsActive = false });

        var result = await Passkeys().VerifyAssertion(Assertion(), default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_client_that_requires_consent_needs_it_remembered()
    {
        _kit.Spa.RequireConsent = true;

        var refused = await Passkeys().VerifyAssertion(Assertion(), default);
        ErrorOf(refused).Should().Be(OidcConstants.Errors.ConsentRequired);

        _kit.Consents.Setup(c => c.HasConsentAsync("u1", "spa", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        (await Passkeys().VerifyAssertion(Assertion(), default)).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task A_client_bound_to_pushed_requests_signs_in_only_through_the_authorize_endpoint()
    {
        _kit.Spa.RequirePushedAuthorization = true;

        var result = await Passkeys().VerifyAssertion(Assertion(), default);

        ErrorOf(result).Should().Be(OidcConstants.Errors.InvalidRequest);
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task The_code_redeems_only_with_the_postmessage_redirect_and_the_verifier()
    {
        await Passkeys().VerifyAssertion(Assertion(), default);
        var code = _kit.Codes.Single().Code;

        (await Redeem(code, CodeIssuerKit.Verifier, "https://app.acme.test/callback")).Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        (await Redeem(code, "wrong-verifier-wrong-verifier-wrong-verifier-x", DirectAuthorizationRequest.PostMessage)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_journey_completes_with_its_methods_session_and_consent()
    {
        var journey = new JourneyState
        {
            Id = "j1",
            TenantId = "acme",
            ClientId = "spa",
            PolicyId = "p1",
            CurrentStepId = "done",
            Data = new Dictionary<string, object>
            {
                ["auth_method"] = "pwd",
                ["amr"] = new[] { "mfa" },
                ["authenticated_at"] = DateTime.UtcNow,
                ["consented_scopes"] = "profile"
            }
        };

        var issued = await _kit.Issuer.IssueForJourneyAsync(new DirectAuthorizationRequest
        {
            ClientId = "spa",
            Scope = "openid profile email",
            CodeChallenge = CodeIssuerKit.Challenge,
            Origin = CodeIssuerKit.AppOrigin
        }, journey, new JourneyCompletionResult
        {
            UserId = "u1",
            Claims = new Dictionary<string, object> { ["sub"] = "u1", ["email"] = "u1@acme.test", ["department"] = "radiology" }
        });

        issued.Succeeded.Should().BeTrue(issued.ErrorDescription);
        var code = _kit.Codes.Single();
        code.SessionId.Should().Be("j1");
        code.Scopes.Should().BeEquivalentTo("openid", "profile");
        AuthorizationCodes.ReadAmr(code).Should().Equal("pwd", "mfa");
        code.Claims.Should().ContainKey("department").And.NotContainKey("sub");
        _kit.SignIns.Should().ContainSingle(s => s.AuthenticationMethod == "pwd" && s.SessionId == "j1");
    }

    private async Task<Core.Protocols.GrantResult> Redeem(string code, string verifier, string redirectUri)
    {
        var profile = new Mock<IProfileService>();
        profile.Setup(p => p.IsActiveAsync(It.IsAny<IsActiveContext>())).Returns(Task.CompletedTask);
        profile.Setup(p => p.GetProfileDataAsync(It.IsAny<ProfileDataRequestContext>())).Returns(Task.CompletedTask);
        var handler = new AuthorizationCodeGrantHandler(
            _kit.CodeStore.Object, new PkceValidator(), profile.Object, Mock.Of<IPersistedGrantStore>(), _kit.Clients.Object,
            ClientUserAccessKit.For(profile.Object), NullLogger<AuthorizationCodeGrantHandler>.Instance);

        return await handler.HandleAsync(new TokenRequest
        {
            GrantType = OidcConstants.GrantTypes.AuthorizationCode,
            Code = code,
            CodeVerifier = verifier,
            RedirectUri = redirectUri,
            Client = new ValidatedClient { ClientId = "spa", RequirePkce = true }
        });
    }
}

/// <summary>FedCM's ID assertion is a code issued through the same issuer, for the RP origin the browser names.</summary>
public class FedCmAssertionCodeTests
{
    private readonly CodeIssuerKit _kit = new();

    private FedCmController FedCm(string? origin, string? paramsJson, string account = "u1")
    {
        var session = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "u1"), new Claim("sid", "cookie-session"), new Claim("amr", "pwd"), new Claim("amr", "mfa"),
            new Claim("auth_time", "1700000000")
        }, "cookie"));
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(session, IdentityConstants.ApplicationScheme)));

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider()
        };
        http.Request.Headers["Sec-Fetch-Dest"] = "webidentity";
        if (origin != null) http.Request.Headers.Origin = origin;
        var form = new Dictionary<string, StringValues> { ["client_id"] = "spa", ["account_id"] = account, ["nonce"] = "n-1" };
        if (paramsJson != null) form["params"] = paramsJson;
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(form);

        return new FedCmController(_kit.Users.Object, _kit.Issuer, Mock.Of<ITenantSettingsProvider>(),
            Mock.Of<IIssuerResolver>(), NullLogger<FedCmController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private const string Pkce = "{\"code_challenge\":\"" + CodeIssuerKit.Challenge + "\",\"code_challenge_method\":\"S256\",\"scope\":\"openid email\"}";

    [Fact]
    public async Task The_assertion_token_is_a_code_bound_to_the_rp_origin_pkce_and_the_existing_session()
    {
        var result = await FedCm(CodeIssuerKit.AppOrigin, Pkce).IdAssertion(default);

        result.Should().BeOfType<OkObjectResult>();
        var code = _kit.Codes.Single();
        code.RedirectUri.Should().Be(DirectAuthorizationRequest.FedCm);
        code.CodeChallenge.Should().Be(CodeIssuerKit.Challenge);
        code.Scopes.Should().BeEquivalentTo("openid", "email");
        code.Nonce.Should().Be("n-1");
        code.SessionId.Should().Be("cookie-session");
        AuthorizationCodes.ReadAmr(code).Should().Equal("fedcm", "pwd", "mfa");
        code.Claims!["auth_time"].Should().Be("1700000000");
        _kit.SignIns.Should().BeEmpty("the session was audited when it signed in");
    }

    [Fact]
    public async Task Without_a_pkce_challenge_there_is_no_code()
    {
        var result = await FedCm(CodeIssuerKit.AppOrigin, paramsJson: null).IdAssertion(default);

        result.Should().BeOfType<BadRequestObjectResult>();
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task An_rp_origin_the_client_did_not_register_gets_no_code()
    {
        var result = await FedCm("https://evil.test", Pkce).IdAssertion(default);

        ((ObjectResult)result).StatusCode.Should().Be(403);
        _kit.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task Client_metadata_answers_only_for_clients_that_can_use_fedcm()
    {
        (await FedCm(CodeIssuerKit.AppOrigin, Pkce).GetClientMetadata("spa", default)).Should().BeOfType<OkObjectResult>();
        (await FedCm(origin: null, Pkce).GetClientMetadata("spa", default)).Should().BeOfType<OkObjectResult>();

        (await FedCm(CodeIssuerKit.AppOrigin, Pkce).GetClientMetadata("unknown", default)).Should().BeOfType<NotFoundObjectResult>();
        (await FedCm("https://evil.test", Pkce).GetClientMetadata("spa", default)).Should().BeOfType<NotFoundObjectResult>();

        _kit.Spa.AllowedCorsOrigins = new List<ClientCorsOrigin>();
        (await FedCm(origin: null, Pkce).GetClientMetadata("spa", default)).Should().BeOfType<NotFoundObjectResult>("no page origin registered");

        _kit.Spa = CodeIssuerKit.NewSpa();
        _kit.Spa.Enabled = false;
        (await FedCm(CodeIssuerKit.AppOrigin, Pkce).GetClientMetadata("spa", default)).Should().BeOfType<NotFoundObjectResult>();

        _kit.Spa = CodeIssuerKit.NewSpa();
        _kit.Spa.AllowedGrantTypes = new List<ClientGrantType> { new() { GrantType = "client_credentials" } };
        (await FedCm(CodeIssuerKit.AppOrigin, Pkce).GetClientMetadata("spa", default)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void The_id_assertion_endpoint_is_rate_limited_like_sign_in()
    {
        var limit = typeof(FedCmController).GetMethod(nameof(FedCmController.IdAssertion))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
            .SingleOrDefault();

        limit.Should().NotBeNull();
        limit!.PolicyName.Should().Be(Oluso.Core.Security.SignInRateLimitPolicies.SignIn);
    }

    [Fact]
    public async Task Another_account_than_the_session_gets_no_code()
    {
        var result = await FedCm(CodeIssuerKit.AppOrigin, Pkce, account: "someone-else").IdAssertion(default);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _kit.Codes.Should().BeEmpty();
    }
}
