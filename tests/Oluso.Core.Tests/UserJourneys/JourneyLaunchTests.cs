using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Core.Tests.UserJourneys;

public class JourneyLaunchTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualClock _clock = new();
    private readonly Mock<IJourneyPolicyStore> _policies = new();
    private readonly Mock<ITenantStore> _tenants = new();
    private readonly Mock<IIssuerResolver> _issuer = new();
    private readonly Mock<ITenantAppOrigins> _origins = new();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    public JourneyLaunchTests()
    {
        var tenant = new Tenant { Id = "t1", Identifier = "acme", Name = "Acme" };
        _tenants.Setup(t => t.GetByIdAsync("t1", It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _issuer.Setup(i => i.GetIssuerForTenantAsync(tenant, It.IsAny<CancellationToken>())).ReturnsAsync("https://acme.id.example");
        _origins.Setup(o => o.GetOriginsAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https://app.acme.example" });
    }

    private JourneyLaunchTokenService Service() =>
        new(_dataProtection, _policies.Object, _tenants.Object, _issuer.Object, _origins.Object, _clock);

    private JourneyLaunchTicket Ticket(TimeSpan? lifetime = null) => new(
        "t1", "p1", new Dictionary<string, string> { ["recordId"] = "r1" }, "/done", _clock.Now + (lifetime ?? TimeSpan.FromHours(1)));

    [Fact]
    public void A_ticket_reads_back_intact()
    {
        var service = Service();

        var read = service.Read(service.Protect(Ticket()));

        read.Status.Should().Be(JourneyLaunchReadStatus.Valid);
        read.Ticket!.TenantId.Should().Be("t1");
        read.Ticket.PolicyId.Should().Be("p1");
        read.Ticket.Context.Should().Contain("recordId", "r1");
        read.Ticket.ReturnUrl.Should().Be("/done");
    }

    [Fact]
    public void A_tampered_token_is_invalid()
    {
        var service = Service();
        var token = service.Protect(Ticket());
        var tampered = token[..^4] + (token[^4] == 'A' ? "B" : "A") + token[^3..];

        service.Read(tampered).Status.Should().Be(JourneyLaunchReadStatus.Invalid);
        service.Read("not-a-token").Status.Should().Be(JourneyLaunchReadStatus.Invalid);
        service.Read("").Status.Should().Be(JourneyLaunchReadStatus.Invalid);
    }

    [Fact]
    public void A_token_from_another_key_ring_is_invalid()
    {
        var other = new JourneyLaunchTokenService(new EphemeralDataProtectionProvider(), _policies.Object, _tenants.Object, _issuer.Object, _origins.Object, _clock);

        Service().Read(other.Protect(Ticket())).Status.Should().Be(JourneyLaunchReadStatus.Invalid);
    }

    [Fact]
    public void An_expired_token_is_reported_as_expired()
    {
        var service = Service();
        var token = service.Protect(Ticket(TimeSpan.FromMinutes(5)));

        _clock.Now += TimeSpan.FromMinutes(6);

        var read = service.Read(token);
        read.Status.Should().Be(JourneyLaunchReadStatus.Expired);
        read.Ticket.Should().BeNull();
    }

    [Theory]
    [InlineData("/workspace/forms", true)]
    [InlineData("https://app.acme.example/forms?x=1", true)]
    [InlineData("https://acme.id.example/account", true)]
    [InlineData("https://evil.example/phish", false)]
    [InlineData("https://app.acme.example:8443/forms", false)]
    [InlineData("http://app.acme.example/forms", false)]
    [InlineData("https://user@app.acme.example/forms", false)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("forms", false)]
    public async Task Only_local_paths_and_the_tenants_registered_origins_are_allowed_as_return_urls(string url, bool allowed)
    {
        (await Service().IsAllowedReturnUrlAsync("t1", url)).Should().Be(allowed);
    }

    [Fact]
    public async Task A_launch_url_points_at_the_tenant_host_and_carries_a_readable_ticket()
    {
        _policies.Setup(p => p.GetAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JourneyPolicy { Id = "p1", Slug = "intake", Name = "Intake", TenantId = "t1", Enabled = true });
        var service = Service();

        var url = await service.CreateLaunchUrlAsync("t1", "p1", new Dictionary<string, string> { ["recordId"] = "r1" },
            "https://app.acme.example/back", TimeSpan.FromMinutes(30));

        url.Should().StartWith("https://acme.id.example/journey/start/intake?launch=");
        var token = Uri.UnescapeDataString(url[(url.IndexOf("launch=", StringComparison.Ordinal) + 7)..]);
        var read = service.Read(token);
        read.Ticket!.Context.Should().Contain("recordId", "r1");
        read.Ticket.ReturnUrl.Should().Be("https://app.acme.example/back");
        read.Ticket.ExpiresAt.Should().Be(_clock.Now + TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task A_global_journey_can_be_launched_for_a_tenant()
    {
        _policies.Setup(p => p.GetAsync("g1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JourneyPolicy { Id = "g1", Slug = "survey", Name = "Survey", TenantId = null, Enabled = true });

        var url = await Service().CreateLaunchUrlAsync("t1", "g1", new Dictionary<string, string>(), null, TimeSpan.FromMinutes(5));

        url.Should().StartWith("https://acme.id.example/journey/start/survey?launch=");
    }

    [Fact]
    public async Task A_launch_url_is_refused_for_another_tenants_or_a_disabled_journey_or_a_foreign_return_url()
    {
        _policies.Setup(p => p.GetAsync("other", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JourneyPolicy { Id = "other", Slug = "other", Name = "Other", TenantId = "t2", Enabled = true });
        _policies.Setup(p => p.GetAsync("off", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JourneyPolicy { Id = "off", Slug = "off", Name = "Off", TenantId = "t1", Enabled = false });
        _policies.Setup(p => p.GetAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JourneyPolicy { Id = "p1", Slug = "intake", Name = "Intake", TenantId = "t1", Enabled = true });
        var service = Service();
        var none = new Dictionary<string, string>();

        await service.Invoking(s => s.CreateLaunchUrlAsync("t1", "other", none, null, TimeSpan.FromMinutes(5)))
            .Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(s => s.CreateLaunchUrlAsync("t1", "off", none, null, TimeSpan.FromMinutes(5)))
            .Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(s => s.CreateLaunchUrlAsync("t1", "p1", none, "https://evil.example/", TimeSpan.FromMinutes(5)))
            .Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(s => s.CreateLaunchUrlAsync("t1", "p1", none, null, TimeSpan.Zero))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_modules_journey_is_launched_only_by_a_module_that_may_use_it_and_the_link_names_that_module()
    {
        _policies.Setup(p => p.GetAsync("intake", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JourneyPolicy { Id = "intake", Slug = "intake", Name = "Intake", TenantId = "t1", Enabled = true, OwnerModule = "emr" });
        _policies.Setup(p => p.GetAsync("contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JourneyPolicy
            {
                Id = "contact", Slug = "contact", Name = "Contact details", TenantId = "t1", Enabled = true, OwnerModule = "emr", Sharing = JourneySharing.Shared,
            });
        var service = Service();
        var none = new Dictionary<string, string>();

        await service.Invoking(s => s.CreateLaunchUrlAsync("t1", "intake", none, null, TimeSpan.FromMinutes(5)))
            .Should().ThrowAsync<InvalidOperationException>("a module's journey is started by a module");
        await service.Invoking(s => s.CreateLaunchUrlAsync("t1", "intake", none, null, TimeSpan.FromMinutes(5), module: "hr"))
            .Should().ThrowAsync<InvalidOperationException>("another module's private journey is not for HR");

        var shared = await service.CreateLaunchUrlAsync("t1", "contact", none, null, TimeSpan.FromMinutes(5), module: "hr");
        var token = Uri.UnescapeDataString(shared[(shared.IndexOf("launch=", StringComparison.Ordinal) + "launch=".Length)..]);
        var ticket = service.Read(token).Ticket!;
        ticket.Module.Should().Be("hr");

        var data = new Dictionary<string, object>();
        JourneyLaunchData.Write(data, ticket);
        data[JourneyRunOwnership.LaunchingModuleDataKey].Should().Be("hr");
    }

    [Fact]
    public async Task A_completed_launched_journey_hands_its_context_and_subject_to_the_submission_event()
    {
        var raised = new List<OlusoEvent>();
        var events = new OlusoEventService(
            new IOlusoEventSink[] { new CallbackEventSink((e, _) => { raised.Add(e); return Task.CompletedTask; }) },
            NullLogger<OlusoEventService>.Instance);
        var submissions = new InMemoryJourneySubmissionStore();

        var policy = new JourneyPolicy
        {
            Id = "p1",
            Slug = "intake",
            Name = "Intake",
            Type = JourneyType.DataCollection,
            Enabled = true,
            PersistSubmissions = true,
            Steps = new List<JourneyPolicyStep> { new() { Id = "form", Type = "dynamic_form", DisplayName = "Form", Order = 1 } }
        };
        var data = new Dictionary<string, object> { ["allergies"] = "none" };
        JourneyLaunchData.Write(data, new JourneyLaunchTicket("t1", "p1", new Dictionary<string, string> { ["recordId"] = "r1" }, null, _clock.Now.AddHours(1)));
        var state = new JourneyState
        {
            Id = "j1",
            PolicyId = "p1",
            CurrentStepId = "form",
            Status = JourneyStatus.InProgress,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            TenantId = "t1",
            ClientId = "",
            UserId = "u1",
            Data = data
        };

        var policies = new Mock<IJourneyPolicyStore>();
        policies.Setup(p => p.GetByIdAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        var states = new Mock<IJourneyStateStore>();
        states.Setup(s => s.GetAsync("j1", It.IsAny<CancellationToken>())).ReturnsAsync(() => state);
        states.Setup(s => s.SaveAsync(It.IsAny<JourneyState>(), It.IsAny<CancellationToken>()))
            .Callback<JourneyState, CancellationToken>((saved, _) => state = saved)
            .Returns(Task.CompletedTask);
        var form = new Mock<IStepHandler>();
        form.Setup(h => h.ExecuteAsync(It.IsAny<StepExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StepHandlerResult.Success());
        var registry = new Mock<IStepHandlerRegistry>();
        registry.Setup(r => r.GetHandler("dynamic_form")).Returns(form.Object);
        var services = new Mock<IServiceProvider>();
        services.Setup(sp => sp.GetService(typeof(IOlusoEventService))).Returns(events);
        var conditions = new Mock<IConditionEvaluator>();
        conditions.Setup(c => c.EvaluateConditionsAsync(It.IsAny<IList<StepCondition>>(), It.IsAny<ConditionEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var orchestrator = new DefaultJourneyOrchestrator(services.Object, policies.Object, states.Object, registry.Object,
            conditions.Object, NullLogger<DefaultJourneyOrchestrator>.Instance, submissions);

        var result = await orchestrator.ContinueJourneyAsync("j1", new JourneyStepInput { StepId = "form" });

        result.Status.Should().Be(JourneyStatus.Completed);
        var created = raised.OfType<JourneySubmissionCreatedEvent>().Should().ContainSingle().Subject;
        created.SubjectUserId.Should().Be("u1");
        created.LaunchContext.Should().Contain("recordId", "r1");
        created.Fields.Should().ContainKey("allergies").And.NotContainKey(JourneyLaunchData.ContextDataKey);

        var saved = await submissions.GetAsync(created.SubmissionId);
        saved!.Data.Should().NotContainKey(JourneyLaunchData.ContextDataKey);
    }

    [Fact]
    public void Launch_context_reads_back_after_a_state_store_round_trip()
    {
        var data = new Dictionary<string, object>();
        JourneyLaunchData.Write(data, Ticket());
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        JourneyLaunchData.ReadContext(roundTripped).Should().Contain("recordId", "r1");
        JourneyLaunchData.ReadContext(new Dictionary<string, object>()).Should().BeNull();
    }
}
