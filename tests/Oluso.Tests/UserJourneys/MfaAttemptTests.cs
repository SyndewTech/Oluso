using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// A wrong MFA code is a failed attempt at the step and a failed sign-in for the account, so
/// guessing codes across journeys still locks it. A locked account isn't checked at all.
/// </summary>
public class MfaAttemptTests
{
    private static (StepExecutionContext Context, Mock<IOlusoUserService> Users) Rig(OlusoUserInfo user)
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        users.Setup(u => u.ValidateMfaCodeAsync(user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(users.Object)
            .AddSingleton(Mock.Of<ITenantContext>())
            .BuildServiceProvider();

        var context = new StepExecutionContext
        {
            JourneyId = "journey",
            StepId = "mfa",
            Configuration = new JourneyStepConfiguration { Id = "mfa", Type = "mfa" },
            ServiceProvider = services,
            UserId = user.Id,
            Input = new JourneyStepInput
            {
                StepId = "mfa",
                Values = new Dictionary<string, object> { ["code"] = "000000", ["provider"] = "totp" }
            }
        };
        return (context, users);
    }

    [Fact]
    public async Task A_wrong_code_is_a_failed_attempt_and_a_failed_sign_in()
    {
        var (context, users) = Rig(new OlusoUserInfo { Id = "u1", Username = "jo", TwoFactorEnabled = true, LockoutEnabled = true });

        var result = await new MfaStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.RequireInput);
        result.FailedAttempt.Should().BeTrue();
        users.Verify(u => u.RecordFailedLoginAsync("u1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_locked_account_is_refused_before_any_code_is_checked()
    {
        var (context, users) = Rig(new OlusoUserInfo
        {
            Id = "u1",
            Username = "jo",
            TwoFactorEnabled = true,
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10)
        });

        var result = await new MfaStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Failed);
        result.Error.Should().Be("account_locked");
        users.Verify(u => u.ValidateMfaCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
