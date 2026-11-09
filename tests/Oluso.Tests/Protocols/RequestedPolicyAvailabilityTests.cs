using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Protocols;
using Xunit;

namespace Oluso.Tests.Protocols;

/// <summary>
/// A policy the client names with ?policy= is the one that runs. A disabled or unknown
/// one is an error, never the default sign-in policy in its place. Without ?policy=, a
/// tenant with no policy for the journey gets the standard pages if it allows the fallback,
/// otherwise the error page.
/// </summary>
public class RequestedPolicyAvailabilityTests
{
    private static (AuthenticationCoordinator Coordinator, Mock<IJourneyOrchestrator> Orchestrator, ProtocolContext Context) Rig(
        JourneyPolicy? requested, JourneyPolicy? defaultSignIn, string? requestedSlug = "business-signin", bool fallback = false)
    {
        var policies = new Mock<IJourneyPolicyStore>();
        policies.Setup(p => p.GetBySlugAsync("business-signin", It.IsAny<CancellationToken>())).ReturnsAsync(requested);
        policies.Setup(p => p.GetByTypeAsync(JourneyType.SignIn, It.IsAny<CancellationToken>())).ReturnsAsync(defaultSignIn);

        var orchestrator = new Mock<IJourneyOrchestrator>();
        orchestrator.Setup(o => o.StartJourneyAsync(It.IsAny<JourneyPolicy>(), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JourneyPolicy p, JourneyStartContext _, CancellationToken _) => new JourneyState
            {
                Id = "j1", TenantId = "acme", ClientId = "app", PolicyId = p.Id, CurrentStepId = "login"
            });

        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider()
        };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("auth.example.test");

        var endpoints = Options.Create(new OidcEndpointConfiguration());
        var protocolState = new InMemoryProtocolStateStore(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            endpoints, NullLogger<InMemoryProtocolStateStore>.Instance);

        var coordinator = new AuthenticationCoordinator(
            orchestrator.Object, policies.Object, Mock.Of<IJourneyStateStore>(), protocolState,
            new HttpContextAccessor { HttpContext = http }, Mock.Of<IOlusoUserService>(), endpoints,
            NullLogger<AuthenticationCoordinator>.Instance);

        var context = new ProtocolContext
        {
            HttpContext = http,
            ProtocolName = "oidc",
            PolicyId = requestedSlug,
            Request = new Oluso.Protocols.Oidc.OidcAuthorizeRequest { ClientId = "app" },
        };
        context.Properties["TenantJourneyFallbackToStandard"] = fallback;
        return (coordinator, orchestrator, context);
    }

    private static JourneyPolicy Policy(string slug, bool enabled) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Slug = slug,
        Name = slug,
        Type = JourneyType.SignIn,
        Enabled = enabled,
        Steps = new List<JourneyPolicyStep> { new() { Id = "login", Type = "local_login", Order = 1 } }
    };

    private static AuthenticationRequirement Requirement() => new() { ExplicitPolicyId = "business-signin" };

    [Fact]
    public async Task An_enabled_requested_policy_runs()
    {
        var (coordinator, orchestrator, context) = Rig(Policy("business-signin", enabled: true), Policy("signin", true));

        await coordinator.StartAuthenticationAsync(context, Requirement());

        orchestrator.Verify(o => o.StartJourneyAsync(
            It.Is<JourneyPolicy>(p => p.Slug == "business-signin"), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_disabled_requested_policy_is_an_error_not_the_default()
    {
        var (coordinator, orchestrator, context) = Rig(Policy("business-signin", enabled: false), Policy("signin", true));

        var result = await coordinator.StartAuthenticationAsync(context, Requirement());

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().StartWith("/error?error=invalid_request");
        orchestrator.Verify(o => o.StartJourneyAsync(It.IsAny<JourneyPolicy>(), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unknown_requested_policy_is_an_error_not_the_default()
    {
        var (coordinator, orchestrator, context) = Rig(null, Policy("signin", true));

        var result = await coordinator.StartAuthenticationAsync(context, Requirement());

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().StartWith("/error?error=invalid_request");
        orchestrator.Verify(o => o.StartJourneyAsync(It.IsAny<JourneyPolicy>(), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Without_a_requested_policy_the_tenants_sign_in_policy_runs()
    {
        var (coordinator, orchestrator, context) = Rig(null, Policy("signin", true), requestedSlug: null);

        await coordinator.StartAuthenticationAsync(context, new AuthenticationRequirement { SuggestedPolicyType = JourneyType.SignIn });

        orchestrator.Verify(o => o.StartJourneyAsync(
            It.Is<JourneyPolicy>(p => p.Slug == "signin"), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(JourneyType.SignIn, "/account/login?returnUrl=")]
    [InlineData(JourneyType.SignUp, "/account/register?returnUrl=")]
    public async Task Without_a_requested_policy_and_no_policy_for_the_journey_the_standard_page_is_used_when_allowed(JourneyType type, string page)
    {
        var (coordinator, orchestrator, context) = Rig(null, defaultSignIn: null, requestedSlug: null, fallback: true);

        var result = await coordinator.StartAuthenticationAsync(context, new AuthenticationRequirement { SuggestedPolicyType = type });

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().StartWith(page);
        orchestrator.Verify(o => o.StartJourneyAsync(It.IsAny<JourneyPolicy>(), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Without_the_fallback_a_missing_policy_is_the_error_page_with_the_way_back_to_the_app()
    {
        var (coordinator, orchestrator, context) = Rig(null, defaultSignIn: null, requestedSlug: null, fallback: false);

        var result = await coordinator.StartAuthenticationAsync(context, new AuthenticationRequirement { SuggestedPolicyType = JourneyType.SignIn });

        result.Should().BeOfType<RedirectResult>().Which.Url.Should()
            .StartWith("/error?error=server_error").And.EndWith("&client_id=app");
        orchestrator.Verify(o => o.StartJourneyAsync(It.IsAny<JourneyPolicy>(), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_missing_requested_policy_never_falls_back_even_when_the_tenant_allows_it()
    {
        var (coordinator, orchestrator, context) = Rig(null, defaultSignIn: null, fallback: true);

        var result = await coordinator.StartAuthenticationAsync(context, Requirement());

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().StartWith("/error?error=invalid_request");
    }
}
