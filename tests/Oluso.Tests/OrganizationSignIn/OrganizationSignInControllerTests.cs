using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Controllers;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.EntityFramework;
using Oluso.Protocols;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.OrganizationSignIn;

/// <summary>
/// The browser hops keep the two sides' UI modes independent: the tenant host
/// runs the tenant's own sign-in (journey or standalone, by the tenant's own
/// precedence), and the platform resumes the way its login started (a standalone
/// login gets a session and returns to the authorize callback; a journey login
/// returns to its journey without a session, which the journey issues later).
/// </summary>
public class OrganizationSignInControllerTests
{
    private const string RequestId = "req-1";

    private sealed class Rig
    {
        public readonly FakeTenantContext TenantContext = new();
        public readonly Mock<IOrganizationSignInService> SignIn = new();
        public readonly Mock<IJourneyOrchestrator> Orchestrator = new();
        public readonly Mock<IJourneyPolicyStore> Policies = new();
        public readonly InMemoryJourneyStateStore JourneyStates = new();
        public readonly Mock<IOlusoUserService> Users = new();
        public readonly Mock<IAuthenticationService> Auth = new();
        public readonly Mock<IUserClaimsPrincipalFactory<OlusoUser>> ClaimsFactory = new();
        public AuthenticateResult ExistingSession = AuthenticateResult.NoResult();
        public readonly OlusoDbContext Db = IdentityTestFactory.CreateContext();

        public Rig()
        {
            SignIn.Setup(s => s.BuildCompleteUrl(It.IsAny<OrganizationHandoffRequest>(), It.IsAny<string?>()))
                .Returns((OrganizationHandoffRequest r, string? j) =>
                    $"{OrganizationSignInDefaults.CompletePath}?correlation_id={r.Id}" + (j == null ? "" : $"&journey={j}"));
            Policies.Setup(p => p.GetByTypeAsync(JourneyType.SignIn, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JourneyPolicy { Id = "tenant-signin", Slug = "tenant-signin", Name = "Sign in", Type = JourneyType.SignIn });
            Orchestrator.Setup(o => o.StartJourneyAsync(It.IsAny<JourneyPolicy>(), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((JourneyPolicy p, JourneyStartContext c, CancellationToken _) => new JourneyState
                {
                    Id = "tenant-journey",
                    TenantId = (string)c.Properties["TenantId"],
                    ClientId = "",
                    PolicyId = p.Id,
                    CurrentStepId = "login",
                    Status = JourneyStatus.InProgress,
                    CorrelationId = c.CorrelationId,
                    CallbackUrl = c.CallbackUrl,
                });
            Auth.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string?>()))
                .ReturnsAsync(() => ExistingSession);
            ClaimsFactory.Setup(f => f.CreateAsync(It.IsAny<OlusoUser>()))
                .ReturnsAsync((OlusoUser u) => new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, u.Id) }, "Oluso")));
        }

        public OrganizationSignInController Controller(string host)
        {
            var services = new ServiceCollection()
                .AddSingleton(Auth.Object)
                .AddSingleton(IdentityTestFactory.CreateUserManager(Db))
                .AddSingleton(ClaimsFactory.Object)
                .BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = services };
            http.Request.Scheme = "https";
            http.Request.Host = new HostString(host);

            var coordinator = new AuthenticationCoordinator(
                Mock.Of<IJourneyOrchestrator>(), Mock.Of<IJourneyPolicyStore>(), JourneyStates,
                Mock.Of<IProtocolStateStore>(), Mock.Of<IHttpContextAccessor>(), Users.Object,
                Options.Create(new OidcEndpointConfiguration()), NullLogger<AuthenticationCoordinator>.Instance);

            var controller = new OrganizationSignInController(
                coordinator, Orchestrator.Object, Policies.Object, JourneyStates, TenantContext, Users.Object,
                NullLogger<OrganizationSignInController>.Instance, SignIn.Object);
            var actionContext = new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
            controller.ControllerContext = new ControllerContext(actionContext);
            controller.Url = new UrlHelper(actionContext);
            return controller;
        }
    }

    private static Tenant TenantWith(bool useJourneyFlow, string? ssoUiMode = null) => new()
    {
        Id = "tenant-acme",
        Identifier = "acme",
        Name = "Acme",
        OrganizationId = "org-1",
        UseJourneyFlow = useJourneyFlow,
        DefaultSsoUiMode = ssoUiMode,
        DefaultIdpScheme = "AcmeEntra",
    };

    private static OrganizationHandoffRequest Pending(string? journeyId = null, string? prompt = null) => new()
    {
        Id = RequestId,
        TenantId = "tenant-acme",
        ReturnUrl = journeyId == null ? "/connect/authorize/callback?correlation_id=c1" : $"/journey/{journeyId}",
        JourneyId = journeyId,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        ClientId = "mail-server",
        Prompt = prompt,
        LoginHint = "jo@acme.test",
    };

    private static void TenantStartReturns(Rig rig, OrganizationHandoffRequest request) =>
        rig.SignIn.Setup(s => s.GetRequestForTenantHostAsync(It.IsAny<HttpContext>(), RequestId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationHandoffLookup(request, null));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Each_side_keeps_its_own_ui_mode(bool platformJourney, bool tenantJourney)
    {
        var rig = new Rig();
        rig.TenantContext.Tenant = TenantWith(useJourneyFlow: tenantJourney);
        await IdentityTestFactory.CreateUserAsync(rig.Db, "tenant-acme", "u1@acme.test");
        var user = rig.Db.Users.Single();
        var request = Pending(journeyId: platformJourney ? "platform-journey" : null);
        TenantStartReturns(rig, request);

        var start = (RedirectResult)await rig.Controller("acme.auth.example.com").Start(RequestId, default);

        if (tenantJourney)
        {
            start.Url.Should().Be("/journey/tenant-journey");
            var saved = await rig.JourneyStates.GetAsync("tenant-journey");
            saved!.CallbackUrl.Should().Be($"{OrganizationSignInDefaults.CompletePath}?correlation_id={RequestId}&journey=tenant-journey");
            saved.CorrelationId.Should().Be(RequestId);
        }
        else
        {
            start.Url.Should().StartWith("/account/login?returnUrl=" + Uri.EscapeDataString($"{OrganizationSignInDefaults.CompletePath}?correlation_id={RequestId}"));
            start.Url.Should().Contain("loginHint=jo%40acme.test").And.Contain("domain_hint=AcmeEntra");
            rig.Orchestrator.Verify(o => o.StartJourneyAsync(It.IsAny<JourneyPolicy>(), It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        rig.SignIn.Setup(s => s.RedeemAsync(It.IsAny<HttpContext>(), "code-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationSignInRedeemResult
            {
                Succeeded = true, Request = request, UserId = user.Id, IdentityProvider = "AcmeEntra", AuthenticationMethods = new[] { "mfa" },
            });
        rig.TenantContext.Tenant = null;

        var callback = (RedirectResult)await rig.Controller("auth.example.com").Callback("code-1", default);

        rig.SignIn.Verify(s => s.FinishAsync(It.IsAny<HttpContext>(), It.IsAny<OrganizationSignInRedeemResult>(), It.IsAny<CancellationToken>()), Times.Once);
        if (platformJourney)
        {
            callback.Url.Should().Be("/journey/platform-journey");
            rig.Auth.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()), Times.Never);
        }
        else
        {
            callback.Url.Should().Be("/connect/authorize/callback?correlation_id=c1");
            rig.Auth.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme,
                It.Is<ClaimsPrincipal>(p =>
                    p.FindFirst("idp")!.Value == "AcmeEntra"
                    && p.FindAll("amr").Select(c => c.Value).SequenceEqual(new[] { "external", "mfa" })
                    && p.FindFirst("tenant_id")!.Value == "tenant-acme"),
                It.IsAny<AuthenticationProperties?>()), Times.Once);
        }
    }

    [Theory]
    [InlineData(true, "Standalone", false)]
    [InlineData(true, "Journey", true)]
    [InlineData(true, null, true)]
    [InlineData(false, "Journey", false)]
    public async Task The_tenant_side_follows_the_tenants_own_ui_mode_precedence(bool useJourneyFlow, string? ssoUiMode, bool expectJourney)
    {
        var rig = new Rig();
        rig.TenantContext.Tenant = TenantWith(useJourneyFlow, ssoUiMode);
        TenantStartReturns(rig, Pending());

        var start = (RedirectResult)await rig.Controller("acme.auth.example.com").Start(RequestId, default);

        start.Url.Should().StartWith(expectJourney ? "/journey/" : "/account/login?");
    }

    [Theory]
    [InlineData(true, "/account/login?")]
    [InlineData(false, "/error?error=server_error")]
    public async Task A_journey_tenant_with_no_sign_in_policy_follows_its_fallback_setting(bool fallback, string expected)
    {
        var rig = new Rig();
        rig.TenantContext.Tenant = TenantWith(true, null);
        rig.TenantContext.Tenant.JourneyFallbackToStandard = fallback;
        rig.Policies.Setup(p => p.GetByTypeAsync(JourneyType.SignIn, It.IsAny<CancellationToken>())).ReturnsAsync((JourneyPolicy?)null);
        TenantStartReturns(rig, Pending());

        var start = (RedirectResult)await rig.Controller("acme.auth.example.com").Start(RequestId, default);

        start.Url.Should().StartWith(expected);
    }

    [Fact]
    public async Task An_existing_tenant_session_is_reused_unless_prompt_login_forces_a_fresh_one()
    {
        var reuse = new Rig();
        reuse.TenantContext.Tenant = TenantWith(useJourneyFlow: false);
        reuse.ExistingSession = AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "u1") }, "Oluso")), IdentityConstants.ApplicationScheme));
        TenantStartReturns(reuse, Pending());

        var reused = (RedirectResult)await reuse.Controller("acme.auth.example.com").Start(RequestId, default);
        reused.Url.Should().Be($"{OrganizationSignInDefaults.CompletePath}?correlation_id={RequestId}");

        var fresh = new Rig();
        fresh.TenantContext.Tenant = TenantWith(useJourneyFlow: false);
        fresh.ExistingSession = reuse.ExistingSession;
        TenantStartReturns(fresh, Pending(prompt: "login"));

        var forced = (RedirectResult)await fresh.Controller("acme.auth.example.com").Start(RequestId, default);
        forced.Url.Should().StartWith("/account/login?");
        fresh.Auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme, It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task An_expired_pending_request_sends_the_user_to_the_took_too_long_page()
    {
        var rig = new Rig();
        rig.TenantContext.Tenant = TenantWith(useJourneyFlow: false);
        rig.SignIn.Setup(s => s.GetRequestForTenantHostAsync(It.IsAny<HttpContext>(), RequestId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationHandoffLookup(Pending(), OrganizationSignInErrors.RequestExpired));
        rig.SignIn.Setup(s => s.BuildExpiredUrl(RequestId)).Returns("https://auth.example.com/account/org-handoff/expired?request=req-1");

        var complete = (RedirectResult)await rig.Controller("acme.auth.example.com").Complete(RequestId, null, default);

        complete.Url.Should().Be("https://auth.example.com/account/org-handoff/expired?request=req-1");
        rig.SignIn.Verify(s => s.IssueHandoffAsync(It.IsAny<HttpContext>(), It.IsAny<OrganizationHandoffRequest>(), It.IsAny<OrganizationAuthentication>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Completion_without_a_tenant_session_mints_nothing()
    {
        var rig = new Rig();
        rig.TenantContext.Tenant = TenantWith(useJourneyFlow: false);
        TenantStartReturns(rig, Pending());

        var complete = (RedirectResult)await rig.Controller("acme.auth.example.com").Complete(RequestId, null, default);

        complete.Url.Should().StartWith("/error?");
        rig.SignIn.Verify(s => s.RecordCompleteFailureAsync(It.IsAny<HttpContext>(), It.IsAny<OrganizationHandoffRequest>(), OrganizationSignInErrors.NotAuthenticated, It.IsAny<CancellationToken>()), Times.Once);
        rig.SignIn.Verify(s => s.IssueHandoffAsync(It.IsAny<HttpContext>(), It.IsAny<OrganizationHandoffRequest>(), It.IsAny<OrganizationAuthentication>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Completion_reports_how_the_tenant_journey_authenticated_the_user()
    {
        var rig = new Rig();
        rig.TenantContext.Tenant = TenantWith(useJourneyFlow: true);
        TenantStartReturns(rig, Pending());
        rig.ExistingSession = AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "u1") }, "Oluso")), IdentityConstants.ApplicationScheme));
        await rig.JourneyStates.SaveAsync(new JourneyState
        {
            Id = "tenant-journey",
            TenantId = "tenant-acme",
            ClientId = "",
            PolicyId = "tenant-signin",
            CurrentStepId = "done",
            Status = JourneyStatus.Completed,
            CorrelationId = RequestId,
            UserId = "u1",
            Data = new Dictionary<string, object> { ["idp"] = "AcmeEntra", ["amr"] = "mfa" },
        });
        OrganizationAuthentication? reported = null;
        rig.SignIn.Setup(s => s.IssueHandoffAsync(It.IsAny<HttpContext>(), It.IsAny<OrganizationHandoffRequest>(), It.IsAny<OrganizationAuthentication>(), It.IsAny<CancellationToken>()))
            .Callback((HttpContext _, OrganizationHandoffRequest _, OrganizationAuthentication a, CancellationToken _) => reported = a)
            .ReturnsAsync("https://auth.example.com/account/org-handoff/callback?code=x");

        var complete = (RedirectResult)await rig.Controller("acme.auth.example.com").Complete(RequestId, "tenant-journey", default);

        complete.Url.Should().Be("https://auth.example.com/account/org-handoff/callback?code=x");
        reported!.UserId.Should().Be("u1");
        reported.IdentityProvider.Should().Be("AcmeEntra");
        reported.AuthenticationMethods.Should().Equal("mfa");
    }
}
