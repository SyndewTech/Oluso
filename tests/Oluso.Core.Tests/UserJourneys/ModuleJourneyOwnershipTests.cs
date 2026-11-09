using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Core.Tests.UserJourneys;

/// <summary>A module's journey hands its answers only to that module; the platform keeps when and which, never what.</summary>
public class ModuleJourneyOwnershipTests
{
    private readonly InMemoryJourneySubmissionStore _submissions = new();
    private readonly List<OlusoEvent> _raised = new();
    private JourneyState _state = null!;

    private sealed class RecordingSink(string module, bool fail = false) : IJourneyResultSink
    {
        public string Module => module;
        public List<JourneyAnswers> Received { get; } = new();

        public Task DeliverAsync(JourneyAnswers answers, CancellationToken cancellationToken = default)
        {
            if (fail) throw new InvalidOperationException("module unavailable");
            Received.Add(answers);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_module_journeys_answers_go_to_the_module_and_the_platform_keeps_only_the_run()
    {
        var emr = new RecordingSink("emr");
        var crm = new RecordingSink("crm");

        var result = await CompleteAsync(Policy(owner: "emr"), emr, crm);

        result.Status.Should().Be(JourneyStatus.Completed);
        emr.Received.Should().ContainSingle().Which.Answers.Should().Contain("diagnosis", "Type 2 diabetes");
        crm.Received.Should().BeEmpty();

        var record = (await _submissions.GetByPolicyAsync("intake")).Should().ContainSingle().Subject;
        record.Data.Should().BeEmpty();
        record.JourneyId.Should().Be("journey1");
        emr.Received[0].SubmissionId.Should().Be(record.Id);

        var evt = _raised.OfType<JourneySubmissionCreatedEvent>().Should().ContainSingle().Subject;
        evt.Fields.Should().BeNull();
        evt.FieldNames.Should().Contain("diagnosis");
        evt.Module.Should().Be("emr");
    }

    [Fact]
    public async Task Without_a_module_to_take_the_answers_the_journey_fails_and_nothing_is_kept()
    {
        var result = await CompleteAsync(Policy(owner: "emr"), new RecordingSink("crm"));

        result.Status.Should().Be(JourneyStatus.Failed);
        result.Error.Should().Be("answers_not_delivered");
        _state.Status.Should().NotBe(JourneyStatus.Completed, "the person can submit again");
        (await _submissions.CountByPolicyAsync("intake")).Should().Be(0);
        _raised.Should().BeEmpty();
    }

    [Fact]
    public async Task A_module_that_refuses_the_answers_fails_the_journey()
    {
        var result = await CompleteAsync(Policy(owner: "emr"), new RecordingSink("emr", fail: true));

        result.Error.Should().Be("answers_not_delivered");
        (await _submissions.CountByPolicyAsync("intake")).Should().Be(0);
    }

    [Fact]
    public async Task A_shared_journey_answers_the_module_that_started_it_and_a_private_one_its_owner()
    {
        var emr = new RecordingSink("emr");
        var hr = new RecordingSink("hr");
        var launchedByHr = new Dictionary<string, object> { [JourneyRunOwnership.LaunchingModuleDataKey] = "hr" };

        await CompleteAsync(Policy(owner: "emr", sharing: JourneySharing.Shared), launchedByHr, emr, hr);
        hr.Received.Should().ContainSingle();
        emr.Received.Should().BeEmpty();

        await CompleteAsync(Policy(owner: "emr"), launchedByHr, emr, hr);
        emr.Received.Should().ContainSingle("a private journey's runs always belong to its owner");
        hr.Received.Should().ContainSingle();
    }

    [Fact]
    public async Task A_platform_journey_still_keeps_its_answers_in_data_collection()
    {
        var emr = new RecordingSink("emr");

        var result = await CompleteAsync(Policy(owner: null), emr);

        result.Status.Should().Be(JourneyStatus.Completed);
        emr.Received.Should().BeEmpty();
        (await _submissions.GetByPolicyAsync("intake")).Should().ContainSingle()
            .Which.Data.Should().Contain("diagnosis", "Type 2 diabetes");
        _raised.OfType<JourneySubmissionCreatedEvent>().Single().Fields.Should().ContainKey("diagnosis");
    }

    [Fact]
    public async Task A_module_journey_starts_only_with_a_launch_from_a_module_that_may_use_it_whatever_page_starts_it()
    {
        var states = new Mock<IJourneyStateStore>();
        var orchestrator = new DefaultJourneyOrchestrator(new ServiceCollection().BuildServiceProvider(), Mock.Of<IJourneyPolicyStore>(), states.Object,
            Mock.Of<IStepHandlerRegistry>(), Mock.Of<IConditionEvaluator>(), NullLogger<DefaultJourneyOrchestrator>.Instance);
        var policy = Policy(owner: "emr");
        JourneyLaunchTicket Launch(string? module, string policyId = "intake") =>
            new("default", policyId, new Dictionary<string, string> { ["patient"] = "p1" }, null, DateTimeOffset.UtcNow.AddHours(1), module);

        await orchestrator.Invoking(o => o.StartJourneyAsync(policy, new JourneyStartContext()))
            .Should().ThrowAsync<InvalidOperationException>("the plain address, embed or any other entry point carries no launch");
        await orchestrator.Invoking(o => o.StartJourneyAsync(policy, new JourneyStartContext { Launch = Launch("hr") }))
            .Should().ThrowAsync<InvalidOperationException>("another module's private journey");
        await orchestrator.Invoking(o => o.StartJourneyAsync(policy, new JourneyStartContext { Launch = Launch("emr", "other") }))
            .Should().ThrowAsync<InvalidOperationException>("a launch made for another journey");

        var state = await orchestrator.StartJourneyAsync(policy, new JourneyStartContext { Launch = Launch("emr") });
        state.Data![JourneyRunOwnership.LaunchingModuleDataKey].Should().Be("emr");
        JourneyLaunchData.ReadContext(state.Data).Should().Contain("patient", "p1");
    }

    [Theory]
    [InlineData(null, JourneySharing.Private, "crm", true)]
    [InlineData("emr", JourneySharing.Private, "emr", true)]
    [InlineData("emr", JourneySharing.Private, "crm", false)]
    [InlineData("emr", JourneySharing.Shared, "crm", true)]
    public void Modules_use_platform_journeys_their_own_and_shared_ones(string? owner, JourneySharing sharing, string module, bool usable) =>
        Policy(owner, sharing).UsableByModule(module).Should().Be(usable);

    private static JourneyPolicy Policy(string? owner, JourneySharing sharing = JourneySharing.Private) => new()
    {
        Id = "intake",
        Slug = "intake",
        Name = "Patient intake",
        Type = JourneyType.DataCollection,
        Enabled = true,
        RequiresAuthentication = false,
        PersistSubmissions = true,
        OwnerModule = owner,
        Sharing = sharing,
        Steps = new List<JourneyPolicyStep> { new() { Id = "form", Type = "dynamic_form", DisplayName = "Intake", Order = 1 } },
    };

    private Task<JourneyResult> CompleteAsync(JourneyPolicy policy, params IJourneyResultSink[] sinks) =>
        CompleteAsync(policy, new Dictionary<string, object>(), sinks);

    private async Task<JourneyResult> CompleteAsync(JourneyPolicy policy, Dictionary<string, object> data, params IJourneyResultSink[] sinks)
    {
        _state = new JourneyState
        {
            Id = "journey1",
            PolicyId = policy.Id,
            CurrentStepId = "form",
            Status = JourneyStatus.InProgress,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            TenantId = "default",
            ClientId = "forms",
            Data = new Dictionary<string, object>(data),
        };

        var policies = new Mock<IJourneyPolicyStore>();
        policies.Setup(p => p.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        var states = new Mock<IJourneyStateStore>();
        states.Setup(s => s.GetAsync("journey1", It.IsAny<CancellationToken>())).ReturnsAsync(() => _state);
        states.Setup(s => s.SaveAsync(It.IsAny<JourneyState>(), It.IsAny<CancellationToken>()))
            .Callback<JourneyState, CancellationToken>((saved, _) => _state = saved)
            .Returns(Task.CompletedTask);

        var form = new Mock<IStepHandler>();
        form.Setup(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StepExecutionContext c, CancellationToken _) =>
            {
                c.SetData("full_name", "Ada Obi");
                c.SetData("diagnosis", "Type 2 diabetes");
                return StepHandlerResult.Success();
            });
        var registry = new Mock<IStepHandlerRegistry>();
        registry.Setup(r => r.GetHandler("dynamic_form")).Returns(form.Object);

        var events = new Mock<IOlusoEventService>();
        events.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OlusoEvent, CancellationToken>((e, _) => _raised.Add(e))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        foreach (var sink in sinks) services.AddSingleton(sink);
        services.AddSingleton(events.Object);

        var orchestrator = new DefaultJourneyOrchestrator(services.BuildServiceProvider(), policies.Object, states.Object, registry.Object,
            Mock.Of<IConditionEvaluator>(), NullLogger<DefaultJourneyOrchestrator>.Instance, _submissions);

        return await orchestrator.ContinueJourneyAsync("journey1", new JourneyStepInput { StepId = "form" });
    }
}
