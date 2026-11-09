using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Core.Tests.UserJourneys;

public class DefaultJourneyOrchestratorTests
{
    private readonly Mock<IJourneyPolicyStore> _policyStoreMock;
    private readonly Mock<IJourneyStateStore> _stateStoreMock;
    private readonly Mock<IStepHandlerRegistry> _stepRegistryMock;
    private readonly Mock<IConditionEvaluator> _conditionEvaluatorMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<DefaultJourneyOrchestrator>> _loggerMock;

    public DefaultJourneyOrchestratorTests()
    {
        _policyStoreMock = new Mock<IJourneyPolicyStore>();
        _stateStoreMock = new Mock<IJourneyStateStore>();
        _stepRegistryMock = new Mock<IStepHandlerRegistry>();
        _conditionEvaluatorMock = new Mock<IConditionEvaluator>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<DefaultJourneyOrchestrator>>();
    }

    [Fact]
    public async Task StartJourneyAsync_WithValidPolicy_CreatesJourneyState()
    {
        // Arrange
        var policy = CreateTestPolicy("signin", JourneyType.SignIn);

        _stateStoreMock
            .Setup(x => x.SaveAsync(It.IsAny<JourneyState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator();
        var startContext = new JourneyStartContext
        {
            CorrelationId = Guid.NewGuid().ToString(),
            Properties = new Dictionary<string, object>
            {
                ["TenantId"] = "default",
                ["ClientId"] = "test-client"
            }
        };

        // Act
        var state = await orchestrator.StartJourneyAsync(policy, startContext);

        // Assert
        state.Should().NotBeNull();
        state.PolicyId.Should().Be("signin");
        state.CurrentStepId.Should().Be("step1");
        state.Status.Should().Be(JourneyStatus.InProgress);
    }

    [Fact]
    public async Task StartJourneyAsync_WithSessionUser_CarriesUserIntoJourney()
    {
        var policy = CreateTestPolicy("business-signin", JourneyType.SignIn);
        var orchestrator = CreateOrchestrator();

        var state = await orchestrator.StartJourneyAsync(policy, new JourneyStartContext
        {
            CorrelationId = Guid.NewGuid().ToString(),
            UserId = "u1"
        });

        state.UserId.Should().Be("u1");
        state.Data![JourneyStartContext.SessionUserIdDataKey].Should().Be("u1");
        state.Data.Should().NotContainKey("authenticated_at");
    }

    [Fact]
    public async Task StartJourneyAsync_WithoutSessionUser_LeavesJourneyAnonymous()
    {
        var policy = CreateTestPolicy("signin", JourneyType.SignIn);
        var orchestrator = CreateOrchestrator();

        var state = await orchestrator.StartJourneyAsync(policy, new JourneyStartContext
        {
            CorrelationId = Guid.NewGuid().ToString()
        });

        state.UserId.Should().BeNull();
        state.Data.Should().NotContainKey(JourneyStartContext.SessionUserIdDataKey);
    }

    [Fact]
    public async Task StartJourneyAsync_WithEmptyPolicy_ThrowsException()
    {
        // Arrange
        var policy = new JourneyPolicy
        {
            Id = "empty",
            Slug = "empty",
            Name = "Empty Policy",
            Type = JourneyType.SignIn,
            Enabled = true,
            Steps = new List<JourneyPolicyStep>() // No steps
        };

        var orchestrator = CreateOrchestrator();
        var startContext = new JourneyStartContext
        {
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.StartJourneyAsync(policy, startContext));
    }

    [Fact]
    public async Task StartJourneyAsync_WithJourneyContext_FindsMatchingPolicy()
    {
        // Arrange
        var policy = CreateTestPolicy("signin", JourneyType.SignIn);

        _policyStoreMock
            .Setup(x => x.FindMatchingAsync(It.IsAny<JourneyPolicyMatchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        _stateStoreMock
            .Setup(x => x.SaveAsync(It.IsAny<JourneyState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _stateStoreMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new JourneyState
            {
                Id = id,
                PolicyId = "signin",
                CurrentStepId = "step1",
                Status = JourneyStatus.InProgress,
                TenantId = "default",
                ClientId = "test-client"
            });

        // Set up step handler for first step
        var mockHandler = new Mock<IStepHandler>();
        mockHandler
            .Setup(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepHandlerResult
            {
                Outcome = StepOutcome.RequireInput,
                StepResult = new JourneyStepResult
                {
                    StepId = "step1",
                    StepType = "local_login",
                    ViewName = "_LocalLogin"
                }
            });

        _stepRegistryMock
            .Setup(x => x.GetHandler("local_login"))
            .Returns(mockHandler.Object);

        var orchestrator = CreateOrchestrator();
        var context = new JourneyContext
        {
            TenantId = "default",
            ClientId = "test-client",
            Type = JourneyType.SignIn
        };

        // Act
        var result = await orchestrator.StartJourneyAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(JourneyStatus.InProgress);
        result.CurrentStep.Should().NotBeNull();
    }

    [Fact]
    public async Task StartJourneyAsync_WithNoMatchingPolicy_ReturnsFailed()
    {
        // Arrange
        _policyStoreMock
            .Setup(x => x.FindMatchingAsync(It.IsAny<JourneyPolicyMatchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JourneyPolicy?)null);

        var orchestrator = CreateOrchestrator();
        var context = new JourneyContext
        {
            TenantId = "default",
            ClientId = "test-client",
            Type = JourneyType.SignIn
        };

        // Act
        var result = await orchestrator.StartJourneyAsync(context);

        // Assert
        result.Status.Should().Be(JourneyStatus.Failed);
        result.Error.Should().Be("no_policy");
    }

    [Fact]
    public async Task ContinueJourneyAsync_WithExpiredJourney_ReturnsExpired()
    {
        // Arrange
        var expiredState = new JourneyState
        {
            Id = "journey123",
            PolicyId = "signin",
            CurrentStepId = "step1",
            Status = JourneyStatus.InProgress,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5), // Expired 5 minutes ago
            TenantId = "default",
            ClientId = "test-client"
        };

        _stateStoreMock
            .Setup(x => x.GetAsync("journey123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredState);

        _stateStoreMock
            .Setup(x => x.SaveAsync(It.IsAny<JourneyState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.ContinueJourneyAsync("journey123", new JourneyStepInput { StepId = "step1" });

        // Assert
        result.Status.Should().Be(JourneyStatus.Expired);
        result.Error.Should().Be("journey_expired");
    }

    [Fact]
    public async Task ContinueJourneyAsync_WithNonExistentJourney_ReturnsNotFound()
    {
        // Arrange
        _stateStoreMock
            .Setup(x => x.GetAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JourneyState?)null);

        var orchestrator = CreateOrchestrator();

        // Act
        var result = await orchestrator.ContinueJourneyAsync("nonexistent", new JourneyStepInput { StepId = "step1" });

        // Assert
        result.Status.Should().Be(JourneyStatus.Failed);
        result.Error.Should().Be("journey_not_found");
    }

    [Fact]
    public async Task ContinueJourneyAsync_SessionUser_MeetsSignInStepAndRunsTheNextStep()
    {
        var login = new Mock<IPrimaryAuthenticationStep> { CallBase = true };
        var form = FormStep();
        var users = UserService(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@acme.test" });
        RigJourney(new Dictionary<string, object> { [JourneyStartContext.SessionUserIdDataKey] = "u1" }, "u1", login.Object, form.Object, users);

        var result = await CreateOrchestrator().ContinueJourneyAsync("journey1", new JourneyStepInput { StepId = "step1" });

        result.Status.Should().Be(JourneyStatus.InProgress);
        result.CurrentStep!.ViewName.Should().Be("Journey/_DynamicForm");
        login.Verify(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()), Times.Never);
        form.Verify(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartJourneyAsync_WithSessionAuthTime_RecordsWhenTheSessionSignedIn()
    {
        var signedInAt = DateTimeOffset.UtcNow.AddMinutes(-3);

        var state = await CreateOrchestrator().StartJourneyAsync(CreateTestPolicy("register-business", JourneyType.SignUp), new JourneyStartContext
        {
            CorrelationId = Guid.NewGuid().ToString(),
            UserId = "u1",
            SessionAuthTime = signedInAt
        });

        state.Data![JourneyStartContext.SessionAuthTimeDataKey].Should().Be(signedInAt.ToUnixTimeSeconds().ToString());
    }

    private static Dictionary<string, object> StaleSession() => new()
    {
        [JourneyStartContext.SessionUserIdDataKey] = "u1",
        [JourneyStartContext.SessionAuthTimeDataKey] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString()
    };

    private static Mock<IPrimaryAuthenticationStep> LoginSigningIn(string userId)
    {
        var login = new Mock<IPrimaryAuthenticationStep> { CallBase = true };
        login.Setup(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepExecutionContext c, CancellationToken _) =>
            {
                c.UserId = userId;
                c.SetData("authenticated_at", DateTime.UtcNow);
                return StepHandlerResult.Success();
            });
        return login;
    }

    [Fact]
    public async Task ContinueJourneyAsync_StaleSession_AsksTheSessionUserToSignInAgain()
    {
        var login = LoginSigningIn("u1");
        var form = FormStep();
        RigJourney(StaleSession(), "u1", login.Object, form.Object, UserService(new OlusoUserInfo { Id = "u1", Username = "jo" }),
            loginSettings: new Dictionary<string, object> { [ExistingSessionLogin.ReauthenticateAfterSetting] = 300 });

        var result = await CreateOrchestrator().ContinueJourneyAsync("journey1", new JourneyStepInput { StepId = "step1" });

        login.Verify(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Status.Should().Be(JourneyStatus.InProgress);
        result.CurrentStep!.ViewName.Should().Be("Journey/_DynamicForm");
    }

    [Fact]
    public async Task ContinueJourneyAsync_StaleSession_SignedInAsSomeoneElse_FailsTheStep()
    {
        var form = FormStep();
        RigJourney(StaleSession(), "u1", LoginSigningIn("u2").Object, form.Object, UserService(new OlusoUserInfo { Id = "u1", Username = "jo" }),
            loginSettings: new Dictionary<string, object> { [ExistingSessionLogin.ReauthenticateAfterSetting] = 300 });
        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.ContinueJourneyAsync("journey1", new JourneyStepInput { StepId = "step1" });

        result.Status.Should().Be(JourneyStatus.Failed);
        result.Error.Should().Be("account_mismatch");
        var state = await orchestrator.GetStateAsync("journey1");
        state!.UserId.Should().Be("u1");
        state.Data.Should().NotContainKey("authenticated_at");
        form.Verify(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContinueJourneyAsync_WithoutSessionUser_RunsTheSignInStep()
    {
        var login = new Mock<IPrimaryAuthenticationStep> { CallBase = true };
        login.Setup(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StepHandlerResult.ShowUi("Journey/_Login", new object()));
        RigJourney(new Dictionary<string, object>(), null, login.Object, FormStep().Object, UserService(null));

        var result = await CreateOrchestrator().ContinueJourneyAsync("journey1", new JourneyStepInput { StepId = "step1" });

        result.CurrentStep!.ViewName.Should().Be("Journey/_Login");
        login.Verify(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ContinueJourneyAsync_UserCondition_ReadsTheStoredProfile()
    {
        var users = UserService(new OlusoUserInfo
        {
            Id = "u1",
            Username = "jo",
            CustomProperties = new Dictionary<string, string> { ["company_name"] = "Acme" }
        });
        ConditionEvaluationContext? evaluated = null;
        _conditionEvaluatorMock
            .Setup(e => e.EvaluateConditionsAsync(It.IsAny<IList<StepCondition>>(), It.IsAny<ConditionEvaluationContext>(), It.IsAny<CancellationToken>()))
            .Callback<IList<StepCondition>, ConditionEvaluationContext, CancellationToken>((_, c, _) => evaluated = c)
            .ReturnsAsync(true);
        var form = FormStep();
        RigJourney(new Dictionary<string, object>(), "u1", new Mock<IPrimaryAuthenticationStep> { CallBase = true }.Object, form.Object, users,
            currentStep: "step2",
            formConditions: new List<StepCondition> { new() { Type = "user", Field = "company_name", Operator = "not_exists" } });

        await CreateOrchestrator().ContinueJourneyAsync("journey1", new JourneyStepInput { StepId = "step2" });

        evaluated!.User!.Id.Should().Be("u1");
        UserConditionFields.GetValue(evaluated.User, "company_name").Should().Be("Acme");
        UserConditionFields.GetValue(evaluated.User, "lockout_end").Should().BeNull();
    }

    [Fact]
    public async Task Events_raised_by_a_step_have_the_journey_as_origin()
    {
        var raised = new List<OlusoEvent>();
        var events = new OlusoEventService(
            new IOlusoEventSink[] { new CallbackEventSink((e, _) => { raised.Add(e); return Task.CompletedTask; }) },
            NullLogger<OlusoEventService>.Instance);
        var form = new Mock<IStepHandler>();
        form.Setup(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await events.RaiseAsync(new UserUpdatedEvent { TenantId = "default", SubjectId = "u1" });
                return StepHandlerResult.ShowUi("Journey/_DynamicForm", new object());
            });
        var users = UserService(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@acme.test" });
        RigJourney(new Dictionary<string, object>(), "u1", new Mock<IPrimaryAuthenticationStep> { CallBase = true }.Object, form.Object, users, currentStep: "step2");

        await CreateOrchestrator().ContinueJourneyAsync("journey1", new JourneyStepInput { StepId = "step2" });

        raised.Should().ContainSingle().Which.Origin.Should().Be(EventOrigin.Journey("business-signin", "Business sign-in"));
        EventOriginScope.Current.Should().BeNull();
    }

    private static Mock<IStepHandler> FormStep()
    {
        var form = new Mock<IStepHandler>();
        form.Setup(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StepHandlerResult.ShowUi("Journey/_DynamicForm", new object()));
        return form;
    }

    private static Mock<IOlusoUserService> UserService(OlusoUserInfo? user)
    {
        var users = new Mock<IOlusoUserService>();
        if (user != null)
        {
            users.Setup(u => u.FindByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        }
        return users;
    }

    private void RigJourney(
        Dictionary<string, object> data,
        string? userId,
        IStepHandler login,
        IStepHandler form,
        Mock<IOlusoUserService> users,
        string currentStep = "step1",
        List<StepCondition>? formConditions = null,
        IDictionary<string, object>? loginSettings = null)
    {
        var policy = new JourneyPolicy
        {
            Id = "business-signin",
            Slug = "business-signin",
            Name = "Business sign-in",
            Type = JourneyType.SignIn,
            Enabled = true,
            Steps = new List<JourneyPolicyStep>
            {
                new() { Id = "step1", Type = "local_login", DisplayName = "Login", Order = 1, Configuration = loginSettings },
                new() { Id = "step2", Type = "dynamic_form", DisplayName = "Business details", Order = 2, Conditions = formConditions }
            }
        };
        var state = new JourneyState
        {
            Id = "journey1",
            PolicyId = policy.Id,
            CurrentStepId = currentStep,
            Status = JourneyStatus.InProgress,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            TenantId = "default",
            ClientId = "test-client",
            UserId = userId,
            Data = data
        };

        _policyStoreMock.Setup(x => x.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        _stateStoreMock.Setup(x => x.GetAsync("journey1", It.IsAny<CancellationToken>())).ReturnsAsync(() => state);
        _stateStoreMock.Setup(x => x.SaveAsync(It.IsAny<JourneyState>(), It.IsAny<CancellationToken>()))
            .Callback<JourneyState, CancellationToken>((saved, _) => state = saved)
            .Returns(Task.CompletedTask);
        _stepRegistryMock.Setup(r => r.GetHandler("local_login")).Returns(login);
        _stepRegistryMock.Setup(r => r.GetHandler("dynamic_form")).Returns(form);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IOlusoUserService))).Returns(users.Object);
    }

    [Fact]
    public async Task StartJourneyAsync_WithDisabledPolicyId_ReturnsFailed()
    {
        var policy = CreateTestPolicy("business-signin", JourneyType.SignIn);
        policy.Enabled = false;
        _policyStoreMock
            .Setup(x => x.GetByIdAsync("business-signin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var result = await CreateOrchestrator().StartJourneyAsync(new JourneyContext
        {
            TenantId = "default",
            ClientId = "test-client",
            PolicyId = "business-signin"
        });

        result.Status.Should().Be(JourneyStatus.Failed);
        result.Error.Should().Be("policy_disabled");
        _stateStoreMock.Verify(x => x.SaveAsync(It.IsAny<JourneyState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private DefaultJourneyOrchestrator CreateOrchestrator()
    {
        return new DefaultJourneyOrchestrator(
            _serviceProviderMock.Object,
            _policyStoreMock.Object,
            _stateStoreMock.Object,
            _stepRegistryMock.Object,
            _conditionEvaluatorMock.Object,
            _loggerMock.Object);
    }

    private static JourneyPolicy CreateTestPolicy(string id, JourneyType type)
    {
        return new JourneyPolicy
        {
            Id = id,
            Slug = id,
            Name = $"Test {type} Policy",
            Type = type,
            Enabled = true,
            Priority = 100,
            Steps = new List<JourneyPolicyStep>
            {
                new() { Id = "step1", Type = "local_login", DisplayName = "Login", Order = 1 },
                new() { Id = "step2", Type = "consent", DisplayName = "Consent", Order = 2 }
            }
        };
    }
}
