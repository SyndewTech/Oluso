using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// A sign-up hands the user service the client and the journey policy it came through, so the service's
/// user.created names them and modules can tell sign-ups apart; the step raises nothing itself.
/// </summary>
public class SignUpEventTests
{
    [Fact]
    public async Task The_sign_up_passes_its_client_and_policy_to_the_user_service_and_raises_nothing_itself()
    {
        var users = new Mock<IOlusoUserService>();
        CreateUserRequest? request = null;
        users.Setup(u => u.CreateUserAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CreateUserRequest r, CancellationToken _) => request = r)
            .ReturnsAsync(UserOperationResult.Success("u1", new OlusoUserInfo { Id = "u1", Username = "jo@example.com", Email = "jo@example.com" }));
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns("t1");
        var events = new List<OlusoEvent>();
        var eventService = new Mock<IOlusoEventService>();
        eventService.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .Callback((OlusoEvent e, CancellationToken _) => events.Add(e)).Returns(Task.CompletedTask);

        var context = new StepExecutionContext
        {
            JourneyId = "run-1", StepId = "signup", TenantId = "t1", ClientId = "shop-app", PolicyId = "customer-signup",
            Configuration = new JourneyStepConfiguration { Id = "signup", Type = "signup", Settings = new Dictionary<string, object> { ["requireEmailVerification"] = false } },
            ServiceProvider = new ServiceCollection().AddLogging().AddSingleton(users.Object).AddSingleton(tenant.Object).AddSingleton(eventService.Object).BuildServiceProvider(),
            Input = new JourneyStepInput
            {
                StepId = "signup",
                Values = new Dictionary<string, object> { ["email"] = "jo@example.com", ["password"] = "Passw0rd!", ["confirmPassword"] = "Passw0rd!" },
            },
        };

        await new SignUpStepHandler().ExecuteAsync(context);

        (request!.ClientId, request.PolicyId).Should().Be(("shop-app", "customer-signup"));
        events.Should().BeEmpty();
    }
}
