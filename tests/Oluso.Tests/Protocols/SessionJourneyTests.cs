using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Protocols;
using Xunit;

namespace Oluso.Tests.Protocols;

/// <summary>
/// A journey the client names runs on the person's live session: its sign-in step is met
/// by the session, so a signed-in person isn't asked for their password again. The session
/// stands in only for the person the request's hints name. prompt=login still forces a
/// fresh sign-in, and a plain sign-in with a session still answers straight away.
/// </summary>
public class SessionJourneyTests
{
    private static readonly DateTimeOffset SignedInAt = DateTimeOffset.UtcNow.AddMinutes(-5);

    private sealed class Rig
    {
        public required AuthenticationCoordinator Coordinator { get; init; }
        public required ProtocolContext Context { get; init; }
        public required Mock<IAuthenticationService> Authentication { get; init; }
        public JourneyStartContext? Started { get; set; }
    }

    private static Rig Build(string? policySlug, ISessionHintMatcher? hints)
    {
        var policies = new Mock<IJourneyPolicyStore>();
        var registerFleet = new JourneyPolicy
        {
            Id = "p1", Slug = "register-fleet", Name = "Register fleet", Type = JourneyType.SignUp, Enabled = true,
            Steps = new List<JourneyPolicyStep> { new() { Id = "login", Type = "local_login", Order = 1 } }
        };
        policies.Setup(p => p.GetBySlugAsync("register-fleet", It.IsAny<CancellationToken>())).ReturnsAsync(registerFleet);
        policies.Setup(p => p.GetByTypeAsync(It.IsAny<JourneyType>(), It.IsAny<CancellationToken>())).ReturnsAsync(registerFleet);

        var authentication = new Mock<IAuthenticationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "u1"),
            new Claim("auth_time", SignedInAt.ToUnixTimeSeconds().ToString())
        }, "Identity.Application"));
        authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider()
        };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("auth.example.test");

        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@acme.test" });

        var endpoints = Options.Create(new OidcEndpointConfiguration());
        var protocolState = new InMemoryProtocolStateStore(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            endpoints, NullLogger<InMemoryProtocolStateStore>.Instance);

        var orchestrator = new Mock<IJourneyOrchestrator>();
        var rig = new Rig
        {
            Coordinator = new AuthenticationCoordinator(
                orchestrator.Object, policies.Object, Mock.Of<IJourneyStateStore>(), protocolState,
                new HttpContextAccessor { HttpContext = http }, users.Object, endpoints,
                NullLogger<AuthenticationCoordinator>.Instance, hints),
            Context = new ProtocolContext
            {
                HttpContext = http,
                ProtocolName = "oidc",
                PolicyId = policySlug,
                Request = new Oluso.Protocols.Oidc.OidcAuthorizeRequest { ClientId = "app" },
            },
            Authentication = authentication,
        };
        orchestrator.Setup(o => o.StartJourneyAsync(It.IsAny<JourneyPolicy>(), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JourneyPolicy p, JourneyStartContext started, CancellationToken _) =>
            {
                rig.Started = started;
                return new JourneyState { Id = "j1", TenantId = "acme", ClientId = "app", PolicyId = p.Id, CurrentStepId = "login" };
            });
        return rig;
    }

    private static ISessionHintMatcher Hints(bool matches)
    {
        var hints = new Mock<ISessionHintMatcher>();
        hints.Setup(h => h.MatchesAsync("u1", "app", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matches);
        return hints.Object;
    }

    [Fact]
    public async Task A_named_journey_runs_on_the_session_without_a_hint()
    {
        var rig = Build("register-fleet", Hints(matches: false));

        var result = await rig.Coordinator.StartAuthenticationAsync(rig.Context,
            new AuthenticationRequirement { ExplicitPolicyId = "register-fleet" });

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/journey/j1");
        rig.Started!.UserId.Should().Be("u1");
        rig.Started.SessionAuthTime!.Value.ToUnixTimeSeconds().Should().Be(SignedInAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task A_hint_naming_the_session_user_reuses_the_session()
    {
        var rig = Build("register-fleet", Hints(matches: true));

        await rig.Coordinator.StartAuthenticationAsync(rig.Context,
            new AuthenticationRequirement { ExplicitPolicyId = "register-fleet", IdTokenHint = "token-for-u1" });

        rig.Started!.UserId.Should().Be("u1");
    }

    [Fact]
    public async Task A_hint_naming_someone_else_gets_the_normal_sign_in()
    {
        var rig = Build("register-fleet", Hints(matches: false));

        var result = await rig.Coordinator.StartAuthenticationAsync(rig.Context,
            new AuthenticationRequirement { ExplicitPolicyId = "register-fleet", LoginHint = "someone@else.test" });

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/journey/j1");
        rig.Started!.UserId.Should().BeNull();
        rig.Started.SessionAuthTime.Should().BeNull();
        rig.Started.LoginHint.Should().Be("someone@else.test");
    }

    [Fact]
    public async Task A_hint_that_cannot_be_checked_does_not_reuse_the_session()
    {
        var rig = Build("register-fleet", hints: null);

        await rig.Coordinator.StartAuthenticationAsync(rig.Context,
            new AuthenticationRequirement { ExplicitPolicyId = "register-fleet", IdTokenHint = "token" });

        rig.Started!.UserId.Should().BeNull();
    }

    [Fact]
    public async Task Prompt_login_still_forces_a_fresh_sign_in()
    {
        var rig = Build("register-fleet", Hints(matches: true));

        await rig.Coordinator.StartAuthenticationAsync(rig.Context,
            new AuthenticationRequirement { ExplicitPolicyId = "register-fleet", Prompt = PromptModes.Login });

        rig.Authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme, It.IsAny<AuthenticationProperties?>()), Times.Once);
        rig.Started!.UserId.Should().BeNull();
    }

    [Fact]
    public async Task A_plain_sign_in_with_a_session_answers_straight_away()
    {
        var rig = Build(policySlug: null, Hints(matches: false));

        var result = await rig.Coordinator.StartAuthenticationAsync(rig.Context,
            new AuthenticationRequirement { SuggestedPolicyType = JourneyType.SignIn, LoginHint = "someone@else.test" });

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().StartWith("https://auth.example.test/connect/authorize/callback?correlation_id=");
        rig.Started.Should().BeNull();
    }
}
