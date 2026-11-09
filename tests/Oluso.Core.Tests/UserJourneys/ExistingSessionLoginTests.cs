using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Core.Tests.UserJourneys;

/// <summary>
/// A policy requested by a signed-in user runs its steps: the login step is met by the
/// session and the journey moves on, without authenticating again (so no new session is
/// issued at completion). Without a session user the step behaves as before.
/// </summary>
public class ExistingSessionLoginTests
{
    private static (StepExecutionContext Context, Mock<IOlusoUserService> Users) Rig(
        string? sessionUserId, int? reauthenticateAfter = null, DateTimeOffset? signedInAt = null)
    {
        var users = new Mock<IOlusoUserService>();
        var services = new ServiceCollection()
            .AddSingleton(users.Object)
            .BuildServiceProvider();
        var data = new Dictionary<string, object>();
        if (sessionUserId != null) data[JourneyStartContext.SessionUserIdDataKey] = sessionUserId;
        if (signedInAt != null) data[JourneyStartContext.SessionAuthTimeDataKey] = signedInAt.Value.ToUnixTimeSeconds().ToString();
        var settings = new Dictionary<string, object>();
        if (reauthenticateAfter != null) settings[ExistingSessionLogin.ReauthenticateAfterSetting] = reauthenticateAfter.Value;
        var context = new StepExecutionContext
        {
            JourneyId = "business-journey",
            StepId = "login",
            Configuration = new JourneyStepConfiguration { Id = "login", Type = "local_login", Settings = settings },
            ServiceProvider = services,
            UserId = sessionUserId,
            JourneyData = data,
        };
        return (context, users);
    }

    [Fact]
    public async Task Without_a_session_user_the_step_runs_normally()
    {
        var (context, _) = Rig(null);

        (await ExistingSessionLogin.TryCompleteAsync(context, default)).Should().BeNull();
    }

    [Fact]
    public async Task A_session_user_meets_the_login_step_without_authenticating_again()
    {
        var (context, users) = Rig("u1");
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@acme.test", FirstName = "Jo" });

        var result = await ExistingSessionLogin.TryCompleteAsync(context, default);

        result!.Outcome.Should().Be(StepOutcome.Continue);
        context.UserId.Should().Be("u1");
        context.IsAuthenticated.Should().BeFalse();
        result.OutputData!["sub"].Should().Be("u1");
        result.OutputData["given_name"].Should().Be("Jo");
        users.Verify(u => u.RecordLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_inactive_session_user_fails_the_step()
    {
        var (context, users) = Rig("u1");
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", IsActive = false });

        var result = await ExistingSessionLogin.TryCompleteAsync(context, default);

        result!.Outcome.Should().Be(StepOutcome.Failed);
    }

    private static OlusoUserInfo Jo() => new() { Id = "u1", Username = "jo", Email = "jo@acme.test" };

    [Fact]
    public async Task A_session_that_signed_in_within_reauthenticateAfter_meets_the_step()
    {
        var (context, users) = Rig("u1", reauthenticateAfter: 300, signedInAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(Jo());

        var result = await ExistingSessionLogin.TryCompleteAsync(context, default);

        result!.Outcome.Should().Be(StepOutcome.Continue);
    }

    [Fact]
    public async Task An_older_session_runs_the_step_pre_filled_for_the_session_user()
    {
        var (context, users) = Rig("u1", reauthenticateAfter: 300, signedInAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(Jo());

        var result = await ExistingSessionLogin.TryCompleteAsync(context, default);

        result.Should().BeNull();
        context.GetDataString("login_hint").Should().Be("jo@acme.test");
    }

    [Fact]
    public async Task A_session_with_no_sign_in_time_runs_the_step_when_a_limit_is_set()
    {
        var (context, users) = Rig("u1", reauthenticateAfter: 300);
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(Jo());

        (await ExistingSessionLogin.TryCompleteAsync(context, default)).Should().BeNull();
    }

    [Fact]
    public async Task Without_a_limit_any_live_session_meets_the_step()
    {
        var (context, users) = Rig("u1", signedInAt: DateTimeOffset.UtcNow.AddDays(-20));
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(Jo());

        (await ExistingSessionLogin.TryCompleteAsync(context, default))!.Outcome.Should().Be(StepOutcome.Continue);
    }
}
