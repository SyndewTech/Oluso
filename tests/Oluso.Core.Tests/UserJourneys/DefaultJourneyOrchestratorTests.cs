using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
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
    public async Task StartJourneyAsync_WithEmptyPolicy_ThrowsException()
    {
        // Arrange
        var policy = new JourneyPolicy
        {
            Id = "empty",
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
