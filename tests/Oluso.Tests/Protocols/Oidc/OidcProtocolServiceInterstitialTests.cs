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
using Oluso.Protocols.Oidc;
using Xunit;

namespace Oluso.Tests.Protocols.Oidc;

/// <summary>
/// Verifies the terminal hop of the authorization-code flow: native clients
/// (custom-scheme redirect URIs) get an HTML interstitial that fires the deep
/// link, while web clients keep the plain 302 redirect.
/// </summary>
public class OidcProtocolServiceInterstitialTests
{
    private readonly Mock<IAuthorizationCodeStore> _codeStore = new();
    private readonly OidcProtocolService _service;

    public OidcProtocolServiceInterstitialTests()
    {
        _codeStore
            .Setup(x => x.StoreAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new OidcProtocolService(
            Options.Create(new OidcEndpointConfiguration()),
            Mock.Of<IProtocolStateStore>(),
            Mock.Of<IAuthorizeRequestValidator>(),
            _codeStore.Object,
            Mock.Of<IConsentStore>(),
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

    private static ProtocolContext CodeFlowContext(string redirectUri) => new()
    {
        HttpContext = new DefaultHttpContext(),
        ProtocolName = "oidc",
        EndpointType = EndpointType.Authorize,
        Request = new OidcAuthorizeRequest
        {
            ClientId = "mobile-client",
            RedirectUri = redirectUri,
            State = "abc123",
            RequestedResponseTypes = new List<string> { ResponseTypes.Code }
        }
    };

    private static AuthenticationResult Authenticated() => new()
    {
        Succeeded = true,
        UserId = "user-1",
        SessionId = "session-1",
        Scopes = new[] { "openid", "profile" }
    };

    [Fact]
    public async Task CustomSchemeRedirect_RendersInterstitialHtml_NotA302()
    {
        var result = await _service.BuildAuthenticatedResponseAsync(
            CodeFlowContext("oluso.auth://callback"), Authenticated());

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Contain("text/html");
        content.StatusCode.Should().Be(200);
        // Fires the deep link (history-replacing) and offers a manual fallback.
        content.Content.Should().Contain("window.location.replace");
        content.Content.Should().Contain("Return to app");
        content.Content.Should().Contain("oluso.auth://callback?code=");
    }

    [Fact]
    public async Task HttpsRedirect_KeepsPlain302()
    {
        var result = await _service.BuildAuthenticatedResponseAsync(
            CodeFlowContext("https://app.example.com/callback"), Authenticated());

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://app.example.com/callback?code=");
    }
}
