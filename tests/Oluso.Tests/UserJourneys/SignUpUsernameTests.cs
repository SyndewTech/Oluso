using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>The sign-up step's username: the email by default, or one the person chooses.</summary>
public class SignUpUsernameTests
{
    private readonly Mock<IOlusoUserService> _users = new();
    private readonly List<CreateUserRequest> _created = new();

    public SignUpUsernameTests()
    {
        // Creation "fails" so the step stops right after recording what it asked for.
        _users.Setup(u => u.CreateUserAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CreateUserRequest r, CancellationToken _) => _created.Add(r))
            .ReturnsAsync(UserOperationResult.Failed("stop"));
    }

    private StepExecutionContext Rig(string? usernameMode, Dictionary<string, object>? input)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns("t1");
        var settings = new Dictionary<string, object> { ["requireEmailVerification"] = false };
        if (usernameMode != null) settings["usernameMode"] = usernameMode;

        return new StepExecutionContext
        {
            JourneyId = "j1",
            StepId = "signup",
            Configuration = new JourneyStepConfiguration { Id = "signup", Type = "signup", Settings = settings },
            ServiceProvider = new ServiceCollection().AddLogging()
                .AddSingleton(_users.Object).AddSingleton(tenant.Object).BuildServiceProvider(),
            Input = input == null ? null : new JourneyStepInput { StepId = "signup", Values = input },
        };
    }

    private static Dictionary<string, object> Form(string? username = null)
    {
        var form = new Dictionary<string, object>
        {
            ["email"] = "jo@example.com",
            ["password"] = "Passw0rd!",
            ["confirmPassword"] = "Passw0rd!",
            ["firstName"] = "Jo",
        };
        if (username != null) form["username"] = username;
        return form;
    }

    private static SignUpViewModel ViewModel(StepHandlerResult result) =>
        (SignUpViewModel)result.StepResult!.ViewModel!;

    [Fact]
    public async Task By_default_the_email_is_the_username_and_no_username_field_is_shown()
    {
        ViewModel(await new SignUpStepHandler().ExecuteAsync(Rig(null, null))).AskUsername.Should().BeFalse();

        await new SignUpStepHandler().ExecuteAsync(Rig(null, Form(username: "ignored")));

        _created.Should().ContainSingle().Which.Username.Should().Be("jo@example.com");
    }

    [Fact]
    public async Task Asking_for_a_username_shows_the_field_and_uses_the_chosen_one()
    {
        ViewModel(await new SignUpStepHandler().ExecuteAsync(Rig("ask", null))).AskUsername.Should().BeTrue();

        await new SignUpStepHandler().ExecuteAsync(Rig("ask", Form(username: "  jo_d  ")));

        _created.Should().ContainSingle().Which.Username.Should().Be("jo_d");
    }

    [Fact]
    public async Task Asking_for_a_username_requires_one()
    {
        var result = await new SignUpStepHandler().ExecuteAsync(Rig("ask", Form()));

        _created.Should().BeEmpty();
        var form = ViewModel(result);
        (form.ErrorMessage, form.Email, form.FirstName).Should().Be(("Choose a username.", "jo@example.com", "Jo"));
    }
}
