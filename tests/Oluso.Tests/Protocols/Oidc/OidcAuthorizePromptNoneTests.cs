using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
using Oluso.Protocols.Oidc;
using Xunit;

namespace Oluso.Tests.Protocols.Oidc;

/// <summary>
/// prompt=none (OIDC Core §3.1.2.1) for silent incremental authorization: no UI, an unmet requirement goes
/// back to the client as login_required / consent_required; and dpop_jkt (RFC 9449 §10) binds the code.
/// </summary>
public class OidcAuthorizePromptNoneTests
{
    private const string Jkt = "0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I";
    private readonly Mock<IConsentStore> _consents = new();
    private readonly Mock<IAuthorizationCodeStore> _codes = new();
    private readonly Client _client = new() { ClientId = "native-app", RequireConsent = true };
    private readonly OidcProtocolService _service;

    public OidcAuthorizePromptNoneTests()
    {
        var validator = new Mock<IAuthorizeRequestValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<AuthorizeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizeRequest r, CancellationToken _) => AuthorizeValidationResult.Success(r, _client, r.RequestedScopes.ToList()));
        _codes.Setup(c => c.StoreAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _service = new OidcProtocolService(
            Options.Create(new OidcEndpointConfiguration()),
            Mock.Of<IProtocolStateStore>(),
            validator.Object,
            _codes.Object,
            _consents.Object,
            Mock.Of<ITokenRequestValidator>(),
            Mock.Of<IGrantHandlerRegistry>(),
            Mock.Of<ITokenService>(),
            Mock.Of<IProfileService>(),
            Mock.Of<IPushedAuthorizationStore>(),
            Mock.Of<ITenantContext>(),
            Mock.Of<IIssuerResolver>(),
            Mock.Of<Oluso.Protocols.Services.IAudienceResolver>(),
            Mock.Of<Oluso.Protocols.Services.IClientUsageRecorder>(),
            ClientUserAccessKit.Open(),
            NullLogger<OidcProtocolService>.Instance);
    }

    private static ProtocolContext Context(string? prompt, bool signedIn, string? dpopJkt = null)
    {
        var http = new DefaultHttpContext();
        if (signedIn)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "user-1") }, "Identity.Application"));
        var raw = new Dictionary<string, string> { ["client_id"] = "native-app", ["redirect_uri"] = "nativeapp://oluso-callback" };
        if (dpopJkt != null) raw["dpop_jkt"] = dpopJkt;
        return new ProtocolContext
        {
            HttpContext = http,
            ProtocolName = "oidc",
            EndpointType = EndpointType.Authorize,
            Request = new OidcAuthorizeRequest
            {
                ClientId = "native-app",
                RedirectUri = "nativeapp://oluso-callback",
                ResponseType = "code",
                Scope = "openid offline_access suite.pulse",
                Prompt = prompt,
                RequestedScopes = new List<string> { "openid", "offline_access", "suite.pulse" },
                RequestedResponseTypes = new List<string> { ResponseTypes.Code },
                Raw = raw,
            },
        };
    }

    [Fact]
    public async Task Not_signed_in_is_login_required_on_the_redirect()
    {
        var result = await _service.ProcessAuthorizeAsync(Context("none", signedIn: false));
        result.Type.Should().Be(ProtocolResultType.Failed);
        result.Error!.Code.Should().Be(OidcConstants.Errors.LoginRequired);
    }

    [Fact]
    public async Task Missing_consent_for_a_new_scope_is_consent_required()
    {
        _consents.Setup(c => c.HasConsentAsync("user-1", "native-app", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var result = await _service.ProcessAuthorizeAsync(Context("none", signedIn: true));
        result.Error!.Code.Should().Be(OidcConstants.Errors.ConsentRequired);
    }

    [Fact]
    public async Task Signed_in_and_consented_succeeds_silently()
    {
        _consents.Setup(c => c.HasConsentAsync("user-1", "native-app", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var result = await _service.ProcessAuthorizeAsync(Context("none", signedIn: true));
        result.Type.Should().Be(ProtocolResultType.Success);
    }

    [Fact]
    public async Task None_combined_with_another_prompt_is_invalid_request()
    {
        var result = await _service.ProcessAuthorizeAsync(Context("none login", signedIn: true));
        result.Error!.Code.Should().Be(OidcConstants.Errors.InvalidRequest);
    }

    [Fact]
    public async Task Without_prompt_none_an_anonymous_user_is_sent_to_sign_in()
    {
        var result = await _service.ProcessAuthorizeAsync(Context(null, signedIn: false));
        result.Type.Should().Be(ProtocolResultType.RequiresAuthentication);
    }

    [Fact]
    public async Task A_malformed_dpop_jkt_is_invalid_request()
    {
        var result = await _service.ProcessAuthorizeAsync(Context(null, signedIn: true, dpopJkt: "not-a-thumbprint"));
        result.Error!.Code.Should().Be(OidcConstants.Errors.InvalidRequest);
    }

    [Fact]
    public async Task The_issued_code_carries_the_dpop_jkt()
    {
        AuthorizationCode? stored = null;
        _codes.Setup(c => c.StoreAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .Callback((AuthorizationCode code, CancellationToken _) => stored = code).Returns(Task.CompletedTask);
        var context = Context(null, signedIn: true, dpopJkt: Jkt);

        await _service.BuildAuthenticatedResponseAsync(context, new AuthenticationResult
        {
            Succeeded = true, UserId = "user-1", SessionId = "s-1", Scopes = new[] { "openid" },
        });

        stored!.Properties.Should().ContainKey(OidcProtocolService.AuthorizationCodeDPoPJktProperty)
            .WhoseValue.Should().Be(Jkt);
    }
}
