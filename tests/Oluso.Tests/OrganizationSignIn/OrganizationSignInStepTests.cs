using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.OrganizationSignIn;

/// <summary>
/// A journey platform login resumes its journey: the login step that offered
/// "Sign in with your organisation" completes with the handed-over result, once,
/// and only for an active account. Without a result the step behaves as before.
/// </summary>
public class OrganizationSignInStepTests
{
    private static (StepExecutionContext Context, Mock<IOrganizationSignInService> SignIn, Mock<IOlusoUserService> Users) Rig()
    {
        var signIn = new Mock<IOrganizationSignInService>();
        var users = new Mock<IOlusoUserService>();
        var services = new ServiceCollection()
            .AddSingleton(signIn.Object)
            .AddSingleton(users.Object)
            .BuildServiceProvider();
        var context = new StepExecutionContext
        {
            JourneyId = "platform-journey",
            StepId = "login",
            Configuration = new JourneyStepConfiguration { Id = "login", Type = "composite_login" },
            ServiceProvider = services,
        };
        return (context, signIn, users);
    }

    [Fact]
    public async Task Without_a_handed_over_result_the_step_runs_normally()
    {
        var (context, _, _) = Rig();

        (await OrganizationSignInStep.TryCompleteAsync(context, default)).Should().BeNull();
        context.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task A_handed_over_result_authenticates_the_step()
    {
        var (context, signIn, users) = Rig();
        signIn.Setup(s => s.TakeJourneyResultAsync("platform-journey", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationAuthentication("u1", "AcmeEntra", new[] { "mfa" }));
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@acme.test", TenantId = "tenant-acme" });

        var result = await OrganizationSignInStep.TryCompleteAsync(context, default);

        result!.Outcome.Should().Be(StepOutcome.Continue);
        context.UserId.Should().Be("u1");
        context.IsAuthenticated.Should().BeTrue();
        context.JourneyData["auth_method"].Should().Be(OrganizationSignInStep.AuthenticationMethod);
        context.JourneyData["idp"].Should().Be("AcmeEntra");
        result.OutputData!["amr"].Should().BeEquivalentTo(new[] { "external", "mfa" });
        users.Verify(u => u.RecordLoginAsync("u1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_result_for_an_inactive_account_fails_the_step()
    {
        var (context, signIn, users) = Rig();
        signIn.Setup(s => s.TakeJourneyResultAsync("platform-journey", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationAuthentication("u1", null, Array.Empty<string>()));
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", IsActive = false });

        var result = await OrganizationSignInStep.TryCompleteAsync(context, default);

        result!.Outcome.Should().Be(StepOutcome.Failed);
        context.IsAuthenticated.Should().BeFalse();
    }
}
