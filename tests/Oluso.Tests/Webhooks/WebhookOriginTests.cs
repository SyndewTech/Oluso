using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.EntityFramework.Events;
using Oluso.Webhooks;
using Xunit;
using WebhookDelivery = Oluso.Core.Events.WebhookDelivery;
using WebhookEndpoint = Oluso.Core.Events.WebhookEndpoint;

namespace Oluso.Tests.Webhooks;

/// <summary>
/// The event origin travels in the webhook payload and the audit row, and an
/// endpoint linked to a service client isn't sent the changes that client made.
/// </summary>
public class WebhookOriginTests
{
    private const string Tenant = "t1";

    private readonly Mock<IWebhookSubscriptionStore> _store = new();
    private readonly CapturingHandler _http = new();

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(Uri Url, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    public WebhookOriginTests()
    {
        _store.Setup(s => s.RecordDeliveryAsync(It.IsAny<string>(), It.IsAny<WebhookDelivery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, WebhookDelivery d, CancellationToken _) => d);
    }

    private void Endpoints(params WebhookEndpoint[] endpoints) =>
        _store.Setup(s => s.GetSubscribedEndpointsAsync(Tenant, "user.updated", It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoints);

    private static WebhookEndpoint Endpoint(string id, string? clientId = null, bool skipOwn = true) => new()
    {
        Id = id, TenantId = Tenant, Name = id, Url = $"https://{id}.example.org/hook", Enabled = true,
        EventTypes = new[] { "user.updated" }, ClientId = clientId, SkipOwnClientEvents = skipOwn,
    };

    private WebhookEventSink Sink()
    {
        var registry = new Mock<IWebhookEventRegistry>();
        registry.Setup(r => r.IsValidEventType(It.IsAny<string>())).Returns(true);
        var dispatcher = new WebhookDispatcher(
            _store.Object, registry.Object, new HttpClient(_http), NullLogger<WebhookDispatcher>.Instance,
            Options.Create(new WebhookOptions()));
        return new WebhookEventSink(dispatcher, Array.Empty<IWebhookPayloadMapper>(),
            new IEventPropertySource[] { new CoreEventPropertySource() }, NullLogger<WebhookEventSink>.Instance);
    }

    private static UserUpdatedEvent Updated(EventOrigin origin) =>
        new() { TenantId = Tenant, SubjectId = "u1", Email = "u1@example.org", Origin = origin };

    private IEnumerable<string> DeliveredTo() => _http.Requests.Select(r => r.Url.Host.Split('.')[0]);

    [Fact]
    public async Task Payload_carries_the_origin_as_a_top_level_object()
    {
        Endpoints(Endpoint("crm"));

        await Sink().HandleAsync(Updated(EventOrigin.ServiceClient("pay1", "203.0.113.7", "Pay1")));

        using var json = JsonDocument.Parse(_http.Requests.Should().ContainSingle().Subject.Body);
        var origin = json.RootElement.GetProperty("origin");
        origin.GetProperty("kind").GetString().Should().Be("service_client");
        origin.GetProperty("id").GetString().Should().Be("pay1");
        origin.GetProperty("name").GetString().Should().Be("Pay1");
        origin.GetProperty("ip_address").GetString().Should().Be("203.0.113.7");
        json.RootElement.GetProperty("data").GetProperty("subject_id").GetString().Should().Be("u1");
    }

    [Fact]
    public async Task Endpoint_linked_to_the_originating_client_is_skipped()
    {
        Endpoints(Endpoint("pay1", clientId: "pay1"), Endpoint("crm"));

        await Sink().HandleAsync(Updated(EventOrigin.ServiceClient("pay1")));

        DeliveredTo().Should().Equal("crm");
        _store.Verify(s => s.RecordDeliveryAsync(It.IsAny<string>(), It.IsAny<WebhookDelivery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Linked_endpoint_still_gets_other_actors_changes_and_its_own_when_it_opts_in()
    {
        Endpoints(Endpoint("pay1", clientId: "pay1"), Endpoint("echo", clientId: "pay1", skipOwn: false));

        await Sink().HandleAsync(Updated(EventOrigin.ServiceClient("other")));
        await Sink().HandleAsync(Updated(EventOrigin.Admin("pay1")));
        await Sink().HandleAsync(Updated(EventOrigin.ServiceClient("pay1")));

        DeliveredTo().Should().Equal("pay1", "echo", "pay1", "echo", "echo");
    }

    [Fact]
    public async Task Audit_row_records_the_origin()
    {
        AuditLog? written = null;
        var store = new Mock<IAuditLogStore>();
        store.Setup(s => s.WriteAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);

        await new AuditEventSink(store.Object, NullLogger<AuditEventSink>.Instance)
            .HandleAsync(Updated(EventOrigin.Scim("sc1", "Payroll", "192.0.2.5")));

        written!.OriginKind.Should().Be(EventOriginKinds.Scim);
        written.OriginId.Should().Be("sc1");
        written.OriginName.Should().Be("Payroll");
        written.IpAddress.Should().Be("192.0.2.5");
    }
}
