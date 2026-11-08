using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// A policy run for a signed-in user may accept the MFA its session already passed, but
/// only when the step opts in, only within the window, and only for that same user.
/// </summary>
public class MfaSessionReuseTests
{
    private static StepExecutionContext Rig(
        int reuseSeconds,
        string? sessionUserId,
        IEnumerable<string> sessionMethods,
        DateTimeOffset sessionAuthTime,
        string cookieUserId = "u1")
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", TwoFactorEnabled = false });

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, cookieUserId) }
                .Concat(SessionAuthenticationClaims.Create(sessionMethods, sessionAuthTime)),
            IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), IdentityConstants.ApplicationScheme);
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(ticket));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider()
        };

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(users.Object)
            .AddSingleton(Mock.Of<ITenantContext>())
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext })
            .BuildServiceProvider();

        var data = new Dictionary<string, object>();
        if (sessionUserId != null) data[JourneyStartContext.SessionUserIdDataKey] = sessionUserId;

        return new StepExecutionContext
        {
            JourneyId = "business-journey",
            StepId = "mfa",
            Configuration = new JourneyStepConfiguration
            {
                Id = "mfa",
                Type = "mfa",
                Settings = new Dictionary<string, object> { ["reuseSessionMfaSeconds"] = reuseSeconds }
            },
            ServiceProvider = services,
            UserId = "u1",
            JourneyData = data,
        };
    }

    private static async Task<bool> ReusedSessionMfa(StepExecutionContext context)
    {
        await new MfaStepHandler().ExecuteAsync(context);
        return context.GetDataString("mfa_provider") == "session";
    }

    [Fact]
    public async Task Recent_session_mfa_is_accepted_when_the_step_allows_it()
    {
        var context = Rig(3600, "u1", new[] { "pwd", "mfa" }, DateTimeOffset.UtcNow.AddMinutes(-5));

        (await ReusedSessionMfa(context)).Should().BeTrue();
        context.GetDataString("amr").Should().Be("mfa");
    }

    [Fact]
    public async Task Session_mfa_is_not_accepted_by_default()
    {
        var context = Rig(0, "u1", new[] { "pwd", "mfa" }, DateTimeOffset.UtcNow.AddMinutes(-5));

        (await ReusedSessionMfa(context)).Should().BeFalse();
    }

    [Fact]
    public async Task Session_mfa_older_than_the_window_is_not_accepted()
    {
        var context = Rig(600, "u1", new[] { "pwd", "mfa" }, DateTimeOffset.UtcNow.AddHours(-2));

        (await ReusedSessionMfa(context)).Should().BeFalse();
    }

    [Fact]
    public async Task A_session_without_mfa_is_not_accepted()
    {
        var context = Rig(3600, "u1", new[] { "pwd" }, DateTimeOffset.UtcNow.AddMinutes(-5));

        (await ReusedSessionMfa(context)).Should().BeFalse();
    }

    [Fact]
    public async Task A_journey_not_started_from_the_session_does_not_lean_on_it()
    {
        var context = Rig(3600, null, new[] { "pwd", "mfa" }, DateTimeOffset.UtcNow.AddMinutes(-5));

        (await ReusedSessionMfa(context)).Should().BeFalse();
    }

    [Fact]
    public async Task Another_users_session_is_not_accepted()
    {
        var context = Rig(3600, "u1", new[] { "pwd", "mfa" }, DateTimeOffset.UtcNow.AddMinutes(-5), cookieUserId: "u2");

        (await ReusedSessionMfa(context)).Should().BeFalse();
    }

    [Fact]
    public void Security_stamp_refresh_keeps_how_and_when_the_session_signed_in()
    {
        var current = new ClaimsPrincipal(new ClaimsIdentity(
            SessionAuthenticationClaims.Create(new[] { "pwd", "mfa" }, DateTimeOffset.UtcNow), "cookie"));
        var rebuilt = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "u1") }, "cookie"));

        SessionAuthenticationClaims.CarryOver(current, rebuilt);

        rebuilt.FindAll("amr").Select(c => c.Value).Should().BeEquivalentTo("pwd", "mfa");
        rebuilt.FindFirst("auth_time").Should().NotBeNull();
    }
}
