using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Core.Tests.UserJourneys;

/// <summary>
/// A step that keeps rejecting its input (wrong codes or passwords) fails the journey at its
/// attempt limit (MaxRetries, or the platform default), for good: the session can't be resumed
/// to keep guessing. Re-showing a step without a failed attempt doesn't count.
/// </summary>
public class StepAttemptLimitTests
{
    private readonly InMemoryJourneyPolicyStore _policies = new();
    private readonly InMemoryJourneyStateStore _states = new();

    private async Task<(DefaultJourneyOrchestrator Orchestrator, string JourneyId)> StartAsync(int maxRetries, IStepHandler handler)
    {
        var policy = new JourneyPolicy
        {
            Id = Guid.NewGuid().ToString(),
            Slug = "verify",
            Name = "Verify",
            Type = JourneyType.SignIn,
            Steps = new List<JourneyPolicyStep> { new() { Id = "code", Type = "fake_code", Order = 1, MaxRetries = maxRetries } }
        };
        await _policies.SaveAsync(policy);

        var registry = new Mock<IStepHandlerRegistry>();
        registry.Setup(r => r.GetHandler("fake_code")).Returns(handler);
        var orchestrator = new DefaultJourneyOrchestrator(
            new ServiceCollection().AddLogging().BuildServiceProvider(), _policies, _states, registry.Object,
            new DefaultConditionEvaluator(), NullLogger<DefaultJourneyOrchestrator>.Instance);

        var state = await orchestrator.StartJourneyAsync(policy, new JourneyStartContext());
        return (orchestrator, state.Id);
    }

    private static Task<JourneyResult> Submit(DefaultJourneyOrchestrator orchestrator, string journeyId) =>
        orchestrator.ContinueJourneyAsync(journeyId, new JourneyStepInput
        {
            StepId = "code",
            Values = new Dictionary<string, object> { ["code"] = "000000" }
        });

    [Fact]
    public async Task The_journey_fails_for_good_at_the_steps_limit()
    {
        var (orchestrator, journeyId) = await StartAsync(maxRetries: 2, new WrongCode());

        (await Submit(orchestrator, journeyId)).Status.Should().Be(JourneyStatus.InProgress);
        var limited = await Submit(orchestrator, journeyId);

        limited.Status.Should().Be(JourneyStatus.Failed);
        limited.Error.Should().Be("too_many_attempts");
        (await _states.GetAsync(journeyId))!.Status.Should().Be(JourneyStatus.Failed);
        (await Submit(orchestrator, journeyId)).Status.Should().Be(JourneyStatus.Failed);
    }

    [Fact]
    public async Task Without_a_step_limit_the_platform_default_applies()
    {
        var (orchestrator, journeyId) = await StartAsync(maxRetries: 0, new WrongCode());

        for (var i = 1; i < JourneyPolicyStep.DefaultMaxFailedAttempts; i++)
        {
            (await Submit(orchestrator, journeyId)).Status.Should().Be(JourneyStatus.InProgress);
        }

        (await Submit(orchestrator, journeyId)).Error.Should().Be("too_many_attempts");
    }

    [Fact]
    public async Task Showing_a_step_again_without_a_failed_attempt_does_not_count()
    {
        var (orchestrator, journeyId) = await StartAsync(maxRetries: 1, new AsksAgain());

        for (var i = 0; i < 5; i++)
        {
            (await Submit(orchestrator, journeyId)).Status.Should().Be(JourneyStatus.InProgress);
        }
    }

    private sealed class WrongCode : IStepHandler
    {
        public string StepType => "fake_code";

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(StepHandlerResult.FailedAttemptUi("Journey/_Code", new { errorMessage = "Invalid code" }));
    }

    private sealed class AsksAgain : IStepHandler
    {
        public string StepType => "fake_code";

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(StepHandlerResult.ShowUi("Journey/_Code"));
    }
}
