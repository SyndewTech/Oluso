using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.Messaging;
using Oluso.Messaging.WhatsApp;
using Xunit;

namespace Oluso.Tests.Messaging.WhatsApp;

public class MetaWhatsAppSenderTests
{
    private const string Tenant = WhatsAppRig.Tenant;
    private const string SentOk = """{"messaging_product":"whatsapp","contacts":[{"wa_id":"2348031234567"}],"messages":[{"id":"wamid.ABC"}]}""";

    private readonly RecordingHandler _http = new();
    private readonly InMemoryContactStore _contacts = new();
    private readonly InMemoryTemplateStore _templates = new();
    private readonly CapturingEvents _events = new();
    private ResolvedWhatsAppSettings? _settings = WhatsAppRig.Settings();

    private MetaWhatsAppSender Sender()
    {
        var resolver = new Mock<IWhatsAppSettingsResolver>();
        resolver.Setup(r => r.ResolveAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        var contactService = WhatsAppRig.ContactService(_contacts, _events);
        return new MetaWhatsAppSender(WhatsAppRig.Graph(_http), resolver.Object, contactService, _contacts, _templates, NullLogger<MetaWhatsAppSender>.Instance);
    }

    private void SessionOpenFor(string e164, WhatsAppConsentStatus consent = WhatsAppConsentStatus.OptedIn) =>
        _contacts.Rows.Add(new WhatsAppContact { TenantId = Tenant, PhoneNumber = e164, ConsentStatus = consent, LastInboundAt = DateTime.UtcNow.AddHours(-1) });

    [Fact]
    public async Task Template_send_posts_the_meta_payload_with_per_request_bearer_auth()
    {
        _http.Enqueue(HttpStatusCode.OK, SentOk);

        var result = await Sender().SendTemplateAsync(Tenant, "0803 123 4567", "order_update", "en_US", new[] { "Ada", "#42" });

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("wamid.ABC");
        result.NormalizedPhone.Should().Be("+2348031234567");

        var req = _http.Requests.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.Url.Should().Be("https://graph.facebook.com/v21.0/PN123/messages");
        req.Authorization.Should().Be("Bearer token-123");

        using var body = JsonDocument.Parse(req.Body!);
        var root = body.RootElement;
        root.GetProperty("to").GetString().Should().Be("2348031234567", "Meta wants digits without the plus");
        root.GetProperty("type").GetString().Should().Be("template");
        var template = root.GetProperty("template");
        template.GetProperty("name").GetString().Should().Be("order_update");
        template.GetProperty("language").GetProperty("code").GetString().Should().Be("en_US");
        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        parameters.GetArrayLength().Should().Be(2);
        parameters[0].GetProperty("text").GetString().Should().Be("Ada");
        parameters[1].GetProperty("text").GetString().Should().Be("#42");

        _contacts.Rows.Should().ContainSingle(c => c.PhoneNumber == "+2348031234567" && c.LastOutboundAt != null);
    }

    [Fact]
    public async Task Text_send_is_refused_when_the_recipient_never_wrote_in()
    {
        var result = await Sender().SendTextAsync(Tenant, "+2348031234567", "hello");

        result.Success.Should().BeFalse();
        result.Error.Should().Be(WhatsAppResult.SessionClosedReason);
        _http.Requests.Should().BeEmpty("no Graph call is made for a send Meta would reject");
    }

    [Fact]
    public async Task Text_send_goes_through_inside_the_24h_window()
    {
        SessionOpenFor("+2348031234567");
        _http.Enqueue(HttpStatusCode.OK, SentOk);

        var result = await Sender().SendTextAsync(Tenant, "+2348031234567", "hello");

        result.Success.Should().BeTrue();
        using var body = JsonDocument.Parse(_http.Requests.Single().Body!);
        body.RootElement.GetProperty("type").GetString().Should().Be("text");
        body.RootElement.GetProperty("text").GetProperty("body").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task Text_send_is_refused_once_the_window_has_lapsed()
    {
        _contacts.Rows.Add(new WhatsAppContact { TenantId = Tenant, PhoneNumber = "+2348031234567", LastInboundAt = DateTime.UtcNow.AddHours(-25) });

        var result = await Sender().SendTextAsync(Tenant, "+2348031234567", "hello");

        result.Error.Should().Be(WhatsAppResult.SessionClosedReason);
    }

    [Fact]
    public async Task Opted_out_recipients_get_nothing_even_a_template()
    {
        SessionOpenFor("+2348031234567", WhatsAppConsentStatus.OptedOut);

        var result = await Sender().SendTemplateAsync(Tenant, "+2348031234567", "order_update", "en");

        result.Success.Should().BeFalse();
        result.Error.Should().Be(WhatsAppResult.OptedOutReason);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Unparseable_numbers_fail_before_any_call()
    {
        _settings = WhatsAppRig.Settings(defaultCountry: null);

        var result = await Sender().SendTemplateAsync(Tenant, "0803 123 4567", "order_update", "en");

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(WhatsAppResult.InvalidPhoneReason);
        result.Error.Should().Contain("no default country");
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Rate_limits_are_retried_and_the_retry_after_is_honoured()
    {
        _http.Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"message":"Too many","code":80007}}""", TimeSpan.FromMilliseconds(5))
             .Enqueue(HttpStatusCode.OK, SentOk);

        var result = await Sender().SendTemplateAsync(Tenant, "+2348031234567", "order_update", "en");

        result.Success.Should().BeTrue();
        _http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Meta_errors_come_back_decoded_and_non_retryable()
    {
        _http.Enqueue(HttpStatusCode.BadRequest,
            """{"error":{"message":"(#131047) Re-engagement message","type":"OAuthException","code":131047,"error_data":{"details":"Message failed to send because more than 24 hours have passed"},"fbtrace_id":"x"}}""");

        var result = await Sender().SendTemplateAsync(Tenant, "+2348031234567", "order_update", "en");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(131047);
        result.Retryable.Should().BeFalse();
        result.Error.Should().Contain("Re-engagement message").And.Contain("24 hours");
        _http.Requests.Should().ContainSingle("a 4xx is not retried");
    }

    [Fact]
    public async Task Server_errors_are_retried_up_to_three_times_then_reported_retryable()
    {
        _http.Enqueue(HttpStatusCode.InternalServerError, "oops")
             .Enqueue(HttpStatusCode.BadGateway, "")
             .Enqueue(HttpStatusCode.ServiceUnavailable, "{}");

        var result = await Sender().SendTemplateAsync(Tenant, "+2348031234567", "order_update", "en");

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeTrue();
        _http.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Unconfigured_tenant_gets_the_not_configured_sentinel()
    {
        _settings = null;

        var result = await Sender().SendTextAsync(Tenant, "+2348031234567", "hello");

        result.Error.Should().Be(WhatsAppResult.NotConfiguredReason);
        (await Sender().IsConfiguredAsync(Tenant)).Should().BeFalse();
    }

    [Fact]
    public async Task Known_unapproved_template_is_refused_locally()
    {
        _templates.Rows.Add(new WhatsAppTemplate { TenantId = Tenant, ProviderTemplateId = "1", Name = "order_update", Language = "en", Status = "PENDING" });

        var result = await Sender().SendTemplateAsync(Tenant, "+2348031234567", "order_update", "en");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("PENDING");
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CanSendFreeForm_reflects_session_and_consent()
    {
        var sender = Sender();
        (await sender.CanSendFreeFormAsync(Tenant, "+2348031234567")).Should().BeFalse();

        SessionOpenFor("+2348031234567");
        (await sender.CanSendFreeFormAsync(Tenant, "+2348031234567")).Should().BeTrue();

        _contacts.Rows.Single().ConsentStatus = WhatsAppConsentStatus.OptedOut;
        (await sender.CanSendFreeFormAsync(Tenant, "+2348031234567")).Should().BeFalse();
    }
}
