using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// The journey runner hands on only the claims a policy allows, follows branches named by
/// key or step, fails clearly on targets that don't exist, and stops step loops.
/// </summary>
public class JourneyRunnerHardeningTests
{
    private sealed class Step(string type, Func<StepExecutionContext, StepHandlerResult> run) : IStepHandler
    {
        public string StepType => type;
        public int Runs;

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs++;
            return Task.FromResult(run(context));
        }
    }

    private static async Task<JourneyResult> RunAsync(JourneyPolicy policy, IEnumerable<IStepHandler> handlers, UserJourneyOptions? options = null)
    {
        var registry = new Mock<IStepHandlerRegistry>();
        foreach (var handler in handlers)
        {
            registry.Setup(r => r.GetHandler(handler.StepType)).Returns(handler);
        }
        var policies = new Mock<IJourneyPolicyStore>();
        policies.Setup(p => p.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        var conditions = new Mock<IConditionEvaluator>();
        conditions.Setup(c => c.EvaluateConditionsAsync(It.IsAny<IList<StepCondition>>(), It.IsAny<ConditionEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var services = new ServiceCollection().AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)).BuildServiceProvider();

        var runner = new DefaultJourneyOrchestrator(services, policies.Object, new InMemoryJourneyStateStore(), registry.Object,
            conditions.Object, NullLogger<DefaultJourneyOrchestrator>.Instance, options: options);

        var state = await runner.StartJourneyAsync(policy, new JourneyStartContext
        {
            HttpContext = new DefaultHttpContext(),
            Properties = new Dictionary<string, object> { ["TenantId"] = "t1" },
        });
        return await runner.ContinueJourneyAsync(state.Id, new JourneyStepInput { StepId = state.CurrentStepId });
    }

    private static JourneyPolicy Policy(params JourneyPolicyStep[] steps) =>
        new() { Id = "p1", Slug = "p1", Name = "Test", TenantId = "t1", Steps = steps.ToList() };

    private static StepHandlerResult Collect() => StepHandlerResult.Success(new Dictionary<string, object>
    {
        ["sub"] = "u1", ["email"] = "ada@x.test", ["ip_address"] = "10.0.0.1", ["reset_code"] = "123456", ["businessName"] = "Acme",
    });

    [Fact]
    public async Task Without_a_mapping_only_identity_claims_leave_the_journey()
    {
        var result = await RunAsync(Policy(new JourneyPolicyStep { Id = "s1", Type = "collect", Order = 1 }), [new Step("collect", _ => Collect())]);

        result.Status.Should().Be(JourneyStatus.Completed);
        result.Completion!.Claims!.Keys.Should().BeEquivalentTo("sub", "email");
    }

    [Fact]
    public async Task The_identity_claims_handed_on_are_configurable()
    {
        var options = new UserJourneyOptions { DefaultOutputClaims = ["sub", "businessName"] };

        var result = await RunAsync(Policy(new JourneyPolicyStep { Id = "s1", Type = "collect", Order = 1 }), [new Step("collect", _ => Collect())], options);

        result.Completion!.Claims!.Keys.Should().BeEquivalentTo("sub", "businessName");
    }

    [Fact]
    public async Task A_next_step_that_does_not_exist_fails_instead_of_finishing_early()
    {
        var later = new Step("mfa_like", _ => StepHandlerResult.Success());
        var result = await RunAsync(Policy(
                new JourneyPolicyStep { Id = "s1", Type = "collect", Order = 1, OnSuccess = "missing" },
                new JourneyPolicyStep { Id = "s2", Type = "mfa_like", Order = 2 }),
            [new Step("collect", _ => Collect()), later]);

        (result.Status, result.Error).Should().Be((JourneyStatus.Failed, "next_step_not_found"));
        later.Runs.Should().Be(0);
    }

    [Fact]
    public async Task A_branch_can_name_a_step_directly_or_by_key()
    {
        var target = new Step("target", _ => StepHandlerResult.Success());
        var byStep = await RunAsync(Policy(
                new JourneyPolicyStep { Id = "s1", Type = "branchy", Order = 1 },
                new JourneyPolicyStep { Id = "skipped", Type = "never", Order = 2 },
                new JourneyPolicyStep { Id = "s3", Type = "target", Order = 3 }),
            [new Step("branchy", _ => StepHandlerResult.Branch("s3")), new Step("never", _ => throw new InvalidOperationException()), target]);

        byStep.Status.Should().Be(JourneyStatus.Completed);
        target.Runs.Should().Be(1);

        var unknown = await RunAsync(Policy(new JourneyPolicyStep { Id = "s1", Type = "branchy", Order = 1 }),
            [new Step("branchy", _ => StepHandlerResult.Branch("nowhere"))]);
        (unknown.Status, unknown.Error).Should().Be((JourneyStatus.Failed, "branch_step_not_found"));
    }

    [Fact]
    public async Task A_step_that_keeps_failing_into_itself_stops_instead_of_recursing()
    {
        var failing = new Step("failing", _ => StepHandlerResult.Fail("nope", "still failing"));

        var result = await RunAsync(Policy(new JourneyPolicyStep { Id = "s1", Type = "failing", Order = 1, OnFailure = "s1" }), [failing],
            new UserJourneyOptions { MaxStepsPerJourney = 10 });

        (result.Status, result.Error).Should().Be((JourneyStatus.Failed, "journey_loop"));
        failing.Runs.Should().Be(10);
    }
}
