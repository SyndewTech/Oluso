using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Protocols.Grants;
using Oluso.Protocols.Oidc;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Oidc;

/// <summary>
/// On the redirect code flow, the user's claims come only from the profile service for the granted scopes:
/// whatever the sign-in or the session cookie knew about the user never rides the code into the tokens.
/// </summary>
public class AuthorizeCodeClaimsTests
{
    private readonly List<AuthorizationCode> _codes = new();
    private readonly Mock<IAuthorizationCodeStore> _codeStore = new();
    private readonly Mock<IProfileService> _profile = new();

    public AuthorizeCodeClaimsTests()
    {
        _codeStore.Setup(x => x.TryConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _codeStore.Setup(x => x.StoreAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationCode, CancellationToken>((c, _) => _codes.Add(c))
            .Returns(Task.CompletedTask);
        _codeStore.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => _codes.FirstOrDefault(c => c.Code == code));

        // The profile service gates by scope: name for profile, email for email.
        _profile.Setup(p => p.IsActiveAsync(It.IsAny<IsActiveContext>())).Returns(Task.CompletedTask);
        _profile.Setup(p => p.GetProfileDataAsync(It.IsAny<ProfileDataRequestContext>()))
            .Callback<ProfileDataRequestContext>(c =>
            {
                if (c.RequestedScopes.Contains("profile")) c.IssuedClaims.Add(new Claim("name", "Jo Bloggs"));
                if (c.RequestedScopes.Contains("email")) c.IssuedClaims.Add(new Claim("email", "jo@acme.test"));
            })
            .Returns(Task.CompletedTask);
    }

    private OidcProtocolService Service() => new(
        Options.Create(new OidcEndpointConfiguration()),
        Mock.Of<IProtocolStateStore>(),
        Mock.Of<IAuthorizeRequestValidator>(),
        Mock.Of<IRequestObjectValidator>(),
        _codeStore.Object,
        Mock.Of<IConsentStore>(),
        Mock.Of<ITokenRequestValidator>(),
        Mock.Of<IGrantHandlerRegistry>(),
        Mock.Of<ITokenService>(),
        _profile.Object,
        Mock.Of<IPushedAuthorizationStore>(),
        Mock.Of<IPersistedGrantStore>(),
        Mock.Of<ITenantContext>(),
        Mock.Of<IIssuerResolver>(),
        Mock.Of<Oluso.Protocols.Services.IAudienceResolver>(),
        Mock.Of<Oluso.Protocols.Services.IClientUsageRecorder>(),
        ClientUserAccessKit.For(_profile.Object),
        Mock.Of<IClientStore>(),
        NullLogger<OidcProtocolService>.Instance);

    private static ProtocolContext Context() => new()
    {
        HttpContext = new DefaultHttpContext(),
        ProtocolName = "oidc",
        EndpointType = EndpointType.Authorize,
        Request = new OidcAuthorizeRequest
        {
            ClientId = "web",
            RedirectUri = "https://app.acme.test/callback",
            RequestedResponseTypes = new List<string> { ResponseTypes.Code }
        }
    };

    private static AuthenticationResult SignedIn() => AuthenticationResult.Success(
        userId: "u1",
        sessionId: "s1",
        scopes: new[] { "openid", "profile" },
        claims: new Dictionary<string, object>
        {
            ["sub"] = "u1",
            ["name"] = "Jo Bloggs",
            ["email"] = "jo@acme.test",
            ["email_verified"] = "true",
            ["phone_number"] = "+2348000000000",
            ["role"] = "TenantAdmin",
            ["external_access_token"] = "upstream-secret",
            ["idp"] = "google",
            ["tenant_id"] = "acme",
            ["department"] = "radiology"
        },
        amr: new[] { "pwd" });

    [Fact]
    public async Task The_code_carries_only_session_and_custom_claims()
    {
        await Service().BuildAuthenticatedResponseAsync(Context(), SignedIn());

        var code = _codes.Single();
        code.Claims!.Keys.Should().BeEquivalentTo("idp", "tenant_id", "department", "auth_time");
        AuthorizationCodes.ReadAmr(code).Should().Equal("pwd");
    }

    [Fact]
    public async Task Tokens_get_the_user_claims_of_the_granted_scopes_only()
    {
        await Service().BuildAuthenticatedResponseAsync(Context(), SignedIn());
        var code = _codes.Single();
        var handler = new AuthorizationCodeGrantHandler(_codeStore.Object, new PkceValidator(), _profile.Object,
            Mock.Of<IPersistedGrantStore>(), Mock.Of<IClientStore>(), ClientUserAccessKit.For(_profile.Object),
            Mock.Of<IAccessTokenRevocationStore>(), NullLogger<AuthorizationCodeGrantHandler>.Instance);

        var grant = await handler.HandleAsync(new TokenRequest
        {
            GrantType = "authorization_code",
            Code = code.Code,
            RedirectUri = "https://app.acme.test/callback",
            Client = new ValidatedClient { ClientId = "web" }
        });

        grant.IsValid.Should().BeTrue(grant.ErrorDescription);
        grant.Claims["name"].Should().Be("Jo Bloggs");
        grant.Claims.Should().NotContainKeys("email", "email_verified", "phone_number", "external_access_token");
        grant.Claims["department"].Should().Be("radiology");
    }

    [Fact]
    public async Task Userinfo_answers_for_the_scopes_in_the_access_token()
    {
        var claims = await UserInfoAsync(new Client { ClientId = "web" }, "openid", "profile");

        claims["name"].Should().Be("Jo Bloggs");
        claims["sub"].Should().Be("u1");
        claims.Should().NotContainKey("email");
    }

    [Fact]
    public async Task Userinfo_gives_a_pairwise_client_the_sub_of_its_id_token_never_the_user_id()
    {
        // OIDC Core 5.3.2: the userinfo sub must be the ID token's sub, pairwise for a pairwise client.
        var client = new Client { ClientId = "web", PairWiseSubjectSalt = "salt-1" };

        var claims = await UserInfoAsync(client, "openid", "profile");

        claims["sub"].Should().Be(Oluso.Core.Protocols.SubjectIdentifiers.ForClient("u1", "web", "salt-1"));
        claims["sub"].Should().NotBe("u1");
    }

    [Fact]
    public async Task Userinfo_refuses_a_token_for_a_client_that_no_longer_exists()
    {
        var result = await UserInfoResultAsync(clientStoreHas: null, "openid");

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    private async Task<IDictionary<string, object>> UserInfoAsync(Client client, params string[] scopes) =>
        (IDictionary<string, object>)((OkObjectResult)await UserInfoResultAsync(client, scopes)).Value!;

    private async Task<IActionResult> UserInfoResultAsync(Client? clientStoreHas, params string[] scopes)
    {
        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.FindClientByIdAsync("web", It.IsAny<CancellationToken>())).ReturnsAsync(clientStoreHas);
        var controller = new OidcUserInfoController(_profile.Object, clients.Object, Mock.Of<ITenantContext>(), NullLogger<OidcUserInfoController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim("sub", "u1"), new Claim("client_id", "web") }
                            .Concat(scopes.Select(s => new Claim("scope", s))), "Bearer"))
                }
            }
        };

        return await controller.GetUserInfo(default);
    }
}
