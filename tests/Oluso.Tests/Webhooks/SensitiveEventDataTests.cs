using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Webhooks;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Webhooks;
using Xunit;
using DomainEndpoint = Oluso.Core.Domain.Interfaces.WebhookEndpoint;
using EventsDelivery = Oluso.Core.Events.WebhookDelivery;
using EventsEndpoint = Oluso.Core.Events.WebhookEndpoint;
using CreateWebhookEndpoint = Oluso.Core.Domain.Interfaces.CreateWebhookEndpoint;
using UpdateWebhookEndpoint = Oluso.Core.Domain.Interfaces.UpdateWebhookEndpoint;
using WebhookDeliveryStatus = Oluso.Core.Events.WebhookDeliveryStatus;

namespace Oluso.Tests.Webhooks;

[DataClassification(DataSensitivity.Restricted, RequiredPermission = SensitiveEventDataTests.Permission, Purpose = "Case details")]
public abstract class ClassifiedCaseEventBase : OlusoEvent
{
    public override string Category => "cases";
}

[WebhookEvent("Case Opened")]
public class ClassifiedCaseOpenedEvent : ClassifiedCaseEventBase
{
    public override string? WebhookEventType => SensitiveEventDataTests.CaseOpened;

    [DataClassification(DataSensitivity.Internal)]
    public string CaseId { get; set; } = string.Empty;

    public string HolderName { get; set; } = string.Empty;

    public ClassifiedFinances? Finances { get; set; }
}

public class ClassifiedFinances
{
    public int Salary { get; set; }
}

[DataClassification(DataSensitivity.Internal)]
[WebhookEvent("Shelf Restocked")]
public class ClassifiedShelfRestockedEvent : ClassifiedCaseEventBase
{
    public override string? WebhookEventType => "cases.shelf_restocked";
    public string ItemName { get; set; } = string.Empty;
}

[WebhookEvent("Order Placed")]
public class ClassifiedOrderPlacedEvent : OlusoEvent
{
    public override string Category => "shop";
    public override string? WebhookEventType => "shop.order_placed";
    public string OrderId { get; set; } = string.Empty;

    [DataClassification(DataSensitivity.Restricted)]
    public string CardLast4 { get; set; } = string.Empty;
}

/// <summary>
/// Event data classification: how the generator discovers it, and how webhook subscriptions and
/// deliveries apply it.
/// </summary>
public class SensitiveEventDataTests
{
    public const string Permission = "cases.share";
    public const string CaseOpened = "cases.case_opened";
    private const string Tenant = "t1";

    private static readonly IWebhookEventProvider Generated = new TestsEventPropertySource();

    private static WebhookEventDefinition Definition(string eventType) =>
        Generated.GetEventDefinitions().Single(d => d.EventType == eventType);

    #region Discovery

    [Fact]
    public void A_restricted_base_class_classifies_its_events_and_withholds_every_property_not_released()
    {
        var opened = Definition(CaseOpened);

        opened.Sensitivity.Should().Be(DataSensitivity.Restricted);
        opened.RequiredPermission.Should().Be(Permission);
        opened.ClassificationPurpose.Should().Be("Case details");
        opened.Properties!.Where(p => p.Sensitive).Select(p => p.Name).Should().BeEquivalentTo("holder_name", "finances_salary");
        opened.Properties!.Single(p => p.Name == "case_id").Sensitive.Should().BeFalse();
    }

    [Fact]
    public void An_event_can_override_its_base_classification()
    {
        var shelf = Definition("cases.shelf_restocked");

        shelf.Sensitivity.Should().Be(DataSensitivity.Internal);
        shelf.RequiredPermission.Should().BeNull();
        shelf.Properties!.Should().OnlyContain(p => !p.Sensitive);
    }

    [Fact]
    public void An_unrestricted_event_marks_only_its_restricted_properties_sensitive()
    {
        var order = Definition("shop.order_placed");

        order.Sensitivity.Should().Be(DataSensitivity.Unclassified);
        order.IsRestricted().Should().BeFalse();
        order.HasSensitiveData().Should().BeTrue();
        order.Properties!.Where(p => p.Sensitive).Select(p => p.Name).Should().Equal("card_last4");
    }

    [Fact]
    public void A_restricted_event_treats_undeclared_keys_as_confidential_but_not_the_envelope()
    {
        var opened = Definition(CaseOpened);

        opened.IsConfidential("added_later", SensitiveEventData.WebhookEnvelopeKeys).Should().BeTrue();
        opened.IsConfidential("timestamp", SensitiveEventData.WebhookEnvelopeKeys).Should().BeFalse();
        opened.IsConfidential("case_id", SensitiveEventData.WebhookEnvelopeKeys).Should().BeFalse();
        Definition("shop.order_placed").IsConfidential("added_later", SensitiveEventData.WebhookEnvelopeKeys).Should().BeFalse();
    }

    #endregion

    #region Delivery

    private readonly Mock<IWebhookSubscriptionStore> _subscriptions = new();
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

    private WebhookDispatcher Dispatcher()
    {
        var registry = new WebhookEventRegistry([Generated], [(IEventPropertySource)Generated]);
        return new WebhookDispatcher(_subscriptions.Object, registry, new HttpClient(_http),
            NullLogger<WebhookDispatcher>.Instance, Options.Create(new WebhookOptions()));
    }

    private static EventsEndpoint Endpoint(string id, bool authorised) => new()
    {
        Id = id, TenantId = Tenant, Name = id, Url = $"https://{id}.example.org/hook", Enabled = true,
        EventTypes = [CaseOpened],
        SensitiveDataAuthorizedAt = authorised ? DateTime.UtcNow : null,
        SensitiveDataAuthorizedBy = authorised ? "admin-1" : null,
    };

    private JsonElement DataSentTo(string host) =>
        JsonDocument.Parse(_http.Requests.Single(r => r.Url.Host.StartsWith(host + ".")).Body).RootElement;

    [Fact]
    public async Task Unauthorised_endpoints_get_the_payload_without_its_sensitive_properties()
    {
        _subscriptions.Setup(s => s.GetSubscribedEndpointsAsync(Tenant, CaseOpened, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Endpoint("open", authorised: false), Endpoint("trusted", authorised: true)]);
        _subscriptions.Setup(s => s.RecordDeliveryAsync(It.IsAny<string>(), It.IsAny<EventsDelivery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, EventsDelivery d, CancellationToken _) => d);
        var sink = new WebhookEventSink(Dispatcher(), Array.Empty<IWebhookPayloadMapper>(),
            [(IEventPropertySource)Generated], NullLogger<WebhookEventSink>.Instance);

        await sink.HandleAsync(new ClassifiedCaseOpenedEvent
        {
            TenantId = Tenant, CaseId = "v1", HolderName = "Ada Obi", Finances = new ClassifiedFinances { Salary = 120 },
        });

        var open = DataSentTo("open");
        open.GetProperty("data").GetProperty("case_id").GetString().Should().Be("v1");
        open.GetProperty("data").TryGetProperty("holder_name", out _).Should().BeFalse();
        open.GetProperty("data").TryGetProperty("finances_salary", out _).Should().BeFalse();
        open.GetProperty("redacted_fields").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo("holder_name", "finances_salary");

        var trusted = DataSentTo("trusted");
        trusted.GetProperty("data").GetProperty("holder_name").GetString().Should().Be("Ada Obi");
        trusted.TryGetProperty("redacted_fields", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_retry_to_an_endpoint_no_longer_authorised_strips_the_stored_payload()
    {
        var stored = JsonSerializer.Serialize(new
        {
            event_type = CaseOpened,
            data = new { case_id = "v1", holder_name = "Ada Obi", timestamp = "2026-01-01" },
        });
        _subscriptions.Setup(s => s.GetDeliveryAsync("d1", It.IsAny<CancellationToken>())).ReturnsAsync(new EventsDelivery
        {
            Id = "d1", EndpointId = "open", EventType = CaseOpened, PayloadId = "p1", Payload = stored,
            Status = WebhookDeliveryStatus.Failed,
        });
        _subscriptions.Setup(s => s.GetEndpointAsync("open", It.IsAny<CancellationToken>())).ReturnsAsync(Endpoint("open", authorised: false));

        (await Dispatcher().RetryDeliveryAsync("d1")).Should().BeTrue();

        var sent = DataSentTo("open");
        sent.GetProperty("data").GetProperty("case_id").GetString().Should().Be("v1");
        sent.GetProperty("data").TryGetProperty("holder_name", out _).Should().BeFalse();
    }

    #endregion

    #region Subscriptions

    private readonly Mock<IWebhookStore> _store = new();
    private readonly Mock<IOlusoEventService> _events = new();

    private WebhooksController Controller(bool holdsPermission)
    {
        var registry = new WebhookEventRegistry([Generated], [(IEventPropertySource)Generated]);
        var dispatcher = new Mock<IWebhookDispatcher>();
        dispatcher.Setup(d => d.GetAvailableEventTypes()).Returns(registry.GetAllEventDefinitions());

        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), PermissionPolicyConstants.PolicyPrefix + Permission))
            .ReturnsAsync(holdsPermission ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Tenant);

        _store.Setup(s => s.CreateEndpointAsync(Tenant, It.IsAny<CreateWebhookEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CreateWebhookEndpoint c, CancellationToken _) => new DomainEndpoint
            {
                Id = "e1", TenantId = Tenant, Name = c.Name, Url = c.Url, EventTypes = c.EventTypes,
                SensitiveDataAuthorizedAt = c.AuthorizeSensitiveData ? DateTime.UtcNow : null,
                SensitiveDataAuthorizedBy = c.SensitiveDataAuthorizedBy,
            });
        _store.Setup(s => s.UpdateEndpointAsync("e1", It.IsAny<UpdateWebhookEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainEndpoint { Id = "e1", TenantId = Tenant, Name = "hook", Url = "https://x.example.org", EventTypes = [CaseOpened] });

        return new WebhooksController(_store.Object, dispatcher.Object, tenant.Object, _events.Object,
            Mock.Of<IFeatureGate>(), registry, Mock.Of<IClientStore>(), authorization.Object,
            NullLogger<WebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "admin-1"), new Claim("name", "Ada Admin")], "test")),
                },
            },
        };
    }

    private void Existing(bool authorised)
    {
        _store.Setup(s => s.GetEndpointAsync("e1", It.IsAny<CancellationToken>())).ReturnsAsync(new DomainEndpoint
        {
            Id = "e1", TenantId = Tenant, Name = "hook", Url = "https://x.example.org", EventTypes = [CaseOpened],
            SensitiveDataAuthorizedAt = authorised ? DateTime.UtcNow : null,
        });
    }

    private static CreateWebhookEndpointRequest NewEndpoint(bool authorise = false) => new()
    {
        Name = "hook", Url = "https://x.example.org", EventTypes = [CaseOpened], AuthorizeSensitiveData = authorise,
    };

    [Fact]
    public async Task Subscribing_to_a_restricted_event_without_its_permission_is_refused()
    {
        var result = await Controller(holdsPermission: false).CreateEndpoint(NewEndpoint(), CancellationToken.None);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        _store.Verify(s => s.CreateEndpointAsync(It.IsAny<string>(), It.IsAny<CreateWebhookEndpoint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_holder_can_subscribe_and_authorise_sensitive_data_which_is_recorded_and_audited()
    {
        var result = await Controller(holdsPermission: true).CreateEndpoint(NewEndpoint(authorise: true), CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        _store.Verify(s => s.CreateEndpointAsync(Tenant,
            It.Is<CreateWebhookEndpoint>(c => c.AuthorizeSensitiveData && c.SensitiveDataAuthorizedBy == "admin-1" && c.SensitiveDataAuthorizedByName == "Ada Admin"),
            It.IsAny<CancellationToken>()));
        _events.Verify(e => e.RaiseAsync(
            It.Is<AdminWebhookSensitiveDataChangedEvent>(ev => ev.Authorized && ev.RestrictedEventTypes.Contains(CaseOpened)),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Authorising_an_existing_endpoint_or_moving_an_authorised_one_needs_the_permission()
    {
        Existing(authorised: false);
        (await Controller(holdsPermission: false).UpdateEndpoint("e1", new UpdateWebhookEndpointRequest { AuthorizeSensitiveData = true }, CancellationToken.None))
            .Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);

        Existing(authorised: true);
        (await Controller(holdsPermission: false).UpdateEndpoint("e1", new UpdateWebhookEndpointRequest { Url = "https://elsewhere.example.org" }, CancellationToken.None))
            .Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);

        _store.Verify(s => s.UpdateEndpointAsync(It.IsAny<string>(), It.IsAny<UpdateWebhookEndpoint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoking_needs_no_permission_and_renaming_keeps_existing_subscriptions()
    {
        Existing(authorised: true);
        var controller = Controller(holdsPermission: false);

        (await controller.UpdateEndpoint("e1", new UpdateWebhookEndpointRequest { AuthorizeSensitiveData = false }, CancellationToken.None))
            .Result.Should().BeOfType<OkObjectResult>();
        _store.Verify(s => s.UpdateEndpointAsync("e1", It.Is<UpdateWebhookEndpoint>(u => u.AuthorizeSensitiveData == false), It.IsAny<CancellationToken>()));

        (await controller.UpdateEndpoint("e1", new UpdateWebhookEndpointRequest { Name = "renamed" }, CancellationToken.None))
            .Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task The_event_catalog_flags_restricted_events_and_whether_the_admin_may_subscribe()
    {
        var result = await Controller(holdsPermission: false).GetAvailableEvents(CancellationToken.None);

        var opened = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<WebhookEventsResponse>()
            .Subject.Events.Single(e => e.EventType == CaseOpened);
        opened.Restricted.Should().BeTrue();
        opened.RequiredPermission.Should().Be(Permission);
        opened.CanSubscribe.Should().BeFalse();
        opened.SensitiveProperties.Should().BeEquivalentTo("holder_name", "finances_salary");
    }

    #endregion
}
