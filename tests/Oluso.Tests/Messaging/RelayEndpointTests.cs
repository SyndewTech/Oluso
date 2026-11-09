using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Controllers;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Messaging;

/// <summary>
/// The relay endpoints standalone apps call are closed unless the platform has a relay
/// key and the caller sends it. The email relay sends through the tenant's own sender.
/// </summary>
public class RelayEndpointTests
{
    private static IConfiguration Config(string? key) => new ConfigurationBuilder()
        .AddInMemoryCollection(key == null ? new Dictionary<string, string?>() : new() { ["EventRelay:ApiKey"] = key })
        .Build();

    private static T WithRequest<T>(T controller, string? providedKey) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        if (providedKey != null) controller.HttpContext.Request.Headers["X-Event-Relay-Key"] = providedKey;
        return controller;
    }

    private static EventRelayController EventRelay(string? configured, string? provided, Mock<IOlusoEventService> events) =>
        WithRequest(new EventRelayController(events.Object, new EventTypeRegistry(Array.Empty<IEventPropertySource>()),
            Config(configured), NullLogger<EventRelayController>.Instance), provided);

    private static EventEnvelope Envelope() => new() { EventType = "x.y", Payload = JsonDocument.Parse("{}").RootElement };

    [Theory]
    [InlineData(null, null, 503)]
    [InlineData(null, "anything", 503)]
    [InlineData("secret", null, 401)]
    [InlineData("secret", "wrong", 401)]
    public async Task Event_relay_refuses_without_the_configured_key(string? configured, string? provided, int status)
    {
        var events = new Mock<IOlusoEventService>();

        var result = await EventRelay(configured, provided, events).Relay(Envelope(), CancellationToken.None);

        (result is UnauthorizedResult ? 401 : ((ObjectResult)result).StatusCode).Should().Be(status);
        events.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Event_relay_with_the_key_reaches_event_handling()
    {
        var result = await EventRelay("secret", "secret", new Mock<IOlusoEventService>()).Relay(Envelope(), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>("the key passed and the unknown event type is what's rejected");
    }

    [Fact]
    public async Task Relayed_events_keep_the_senders_origin_and_are_system_without_one()
    {
        var source = new Mock<IEventPropertySource>();
        source.Setup(s => s.ResolveEventType(nameof(UserUpdatedEvent))).Returns(typeof(UserUpdatedEvent));
        var raised = new List<OlusoEvent>();
        var events = new Mock<IOlusoEventService>();
        events.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .Callback((OlusoEvent e, CancellationToken _) => raised.Add(e))
            .Returns(Task.CompletedTask);
        var relay = WithRequest(new EventRelayController(events.Object, new EventTypeRegistry(new[] { source.Object }),
            Config("secret"), NullLogger<EventRelayController>.Instance), "secret");

        EventEnvelope Relayed(UserUpdatedEvent evt) => new()
        {
            EventType = nameof(UserUpdatedEvent),
            Payload = JsonSerializer.SerializeToElement(evt),
        };

        await relay.Relay(Relayed(new UserUpdatedEvent { SubjectId = "u1", Origin = EventOrigin.ServiceClient("pay1") }), CancellationToken.None);
        await relay.Relay(Relayed(new UserUpdatedEvent { SubjectId = "u1" }), CancellationToken.None);

        raised.Select(e => e.Origin).Should().Equal(
            EventOrigin.ServiceClient("pay1"),
            EventOrigin.System(EventRelayController.RelaySource));
    }

    private readonly Mock<IEmailSender> _sender = new();
    private readonly Mock<IPlatformEmailTemplateService> _templates = new();
    private IDictionary<string, object>? _renderedWith;

    public RelayEndpointTests()
    {
        _templates.Setup(t => t.RenderForTenantAsync("t1", "recruiting.candidate.sign_in", It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, IDictionary<string, object>? scope, CancellationToken _) => _renderedWith = scope)
            .ReturnsAsync(new RenderedEmail { Subject = "Sign in", HtmlBody = "<html>Sign in</html>", TextBody = "Sign in" });
    }

    private EmailRelayController EmailRelay(string? configured, string? provided)
    {
        var tenants = new Mock<ITenantStore>();
        tenants.Setup(t => t.GetByIdAsync("t1", It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant { Id = "t1", Name = "acme", Identifier = "acme" });
        return WithRequest(new EmailRelayController(_sender.Object, tenants.Object, Config(configured), NullLogger<EmailRelayController>.Instance, _templates.Object), provided);
    }

    private static PlatformEmailRelayRequest Email(string tenant = "t1", string to = "ada@x.test", string template = "recruiting.candidate.sign_in") =>
        new() { TenantId = tenant, To = to, TemplateName = template, Values = new() { ["magic_link"] = "https://careers.test/candidate/auth/t" } };

    [Theory]
    [InlineData(null, "secret", 503)]
    [InlineData("secret", null, 401)]
    [InlineData("secret", "wrong", 401)]
    public async Task Email_relay_refuses_without_the_configured_key(string? configured, string? provided, int status)
    {
        var result = await EmailRelay(configured, provided).Send(Email(), CancellationToken.None);

        (result is UnauthorizedResult ? 401 : ((ObjectResult)result).StatusCode).Should().Be(status);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Email_relay_renders_the_tenants_template_and_sends_through_the_tenants_sender()
    {
        EmailMessage? sent = null;
        _sender.Setup(s => s.SendAsync("t1", It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback((string _, EmailMessage m, CancellationToken _) => sent = m)
            .ReturnsAsync(EmailResult.Succeeded("m-1"));

        var result = await EmailRelay("secret", "secret").Send(Email(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _renderedWith!["magic_link"].Should().Be("https://careers.test/candidate/auth/t");
        sent!.To.Should().Equal("ada@x.test");
        (sent.Subject, sent.HtmlBody, sent.TextBody).Should().Be(("Sign in", "<html>Sign in</html>", "Sign in"));
    }

    [Fact]
    public async Task Email_relay_rejects_unknown_tenants_addresses_and_templates_and_reports_send_failures()
    {
        (await EmailRelay("secret", "secret").Send(Email(tenant: "nope"), CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        (await EmailRelay("secret", "secret").Send(Email(to: "not-an-email"), CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        (await EmailRelay("secret", "secret").Send(Email(template: "no.such.template"), CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();

        _sender.Setup(s => s.SendAsync("t1", It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailResult.Failed("no sender profile"));
        var failed = await EmailRelay("secret", "secret").Send(Email(), CancellationToken.None);
        ((ObjectResult)failed).StatusCode.Should().Be(502);
    }
}
