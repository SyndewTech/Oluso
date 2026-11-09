using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;
using Oluso.Tests.Protocols;
using Oluso.UI.Pages.Embed;
using Xunit;

namespace Oluso.Tests.UI;

/// <summary>
/// The embedded sign-in only runs for a parent page on an origin the client registered, with an allowed scope
/// and a PKCE S256 challenge, and never echoes an unchecked origin into its script.
/// </summary>
public class EmbedAuthRequestTests
{
    private readonly CodeIssuerKit _kit = new();
    private readonly Mock<IJourneyOrchestrator> _orchestrator = new();

    private AuthModel Embed(string origin, string scope = "openid profile", string? challenge = CodeIssuerKit.Challenge) =>
        new(_orchestrator.Object, Mock.Of<IJourneyStateStore>(), Mock.Of<IJourneyPolicyStore>(), _kit.Issuer,
            Mock.Of<ITenantSettingsProvider>(), NullLogger<AuthModel>.Instance)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            ClientId = "spa",
            ParentOrigin = origin,
            Scope = scope,
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256"
        };

    [Theory]
    [InlineData("https://evil.test")]
    [InlineData("';alert(document.domain)//")]
    public async Task A_parent_on_an_unregistered_origin_is_refused_and_not_echoed(string origin)
    {
        var page = Embed(origin);

        await page.OnGetAsync();

        page.ErrorMessage.Should().Be("Origin not allowed");
        page.ParentOrigin.Should().BeNull();
        _orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Only_scopes_beyond_the_clients_are_refused()
    {
        var page = Embed(CodeIssuerKit.AppOrigin, scope: "permissions");

        await page.OnGetAsync();

        page.ErrorMessage.Should().NotBeNull();
        _orchestrator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_pkce_challenge_is_required()
    {
        var page = Embed(CodeIssuerKit.AppOrigin, challenge: null);

        await page.OnGetAsync();

        page.ErrorMessage.Should().NotBeNull();
        _orchestrator.VerifyNoOtherCalls();
    }
}
