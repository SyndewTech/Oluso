using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Events;
using Oluso.Messaging.WhatsApp;
using Xunit;

namespace Oluso.Tests.Messaging.WhatsApp;

public class WhatsAppWebhookProcessorTests
{
    private const string Tenant = WhatsAppRig.Tenant;
    private readonly InMemoryContactStore _contacts = new();
    private readonly InMemoryInboundStore _inbound = new();
    private readonly CapturingEvents _events = new();

    private WhatsAppWebhookProcessor Processor() =>
        new(WhatsAppRig.ContactService(_contacts, _events), _inbound, WhatsAppRig.Services(_events), NullLogger<WhatsAppWebhookProcessor>.Instance);

    private static string MessagePayload(string text, string id = "wamid.IN1", string from = "2348031234567", string type = "text", long ts = 1758200000) => $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"WABA1","changes":[{"field":"messages","value":{
          "messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"15550001111","phone_number_id":"PN123"},
          "contacts":[{"profile":{"name":"Ada Lovelace"},"wa_id":"{{{from}}}"}],
          "messages":[{"from":"{{{from}}}","id":"{{{id}}}","timestamp":"{{{ts}}}","type":"{{{type}}}","text":{"body":"{{{text}}}"}}]
        }}]}]}
        """;

    private static string StatusPayload(string status, string id = "wamid.OUT1", string errors = "") => $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"WABA1","changes":[{"field":"messages","value":{
          "messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"15550001111","phone_number_id":"PN123"},
          "statuses":[{"id":"{{{id}}}","status":"{{{status}}}","timestamp":"1758200100","recipient_id":"2348031234567",
            "conversation":{"id":"c1","origin":{"type":"utility"}}{{{errors}}}}]
        }}]}]}
        """;

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Signature_check_is_hmac_sha256_over_the_raw_body()
    {
        var body = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account"}""");
        var hex = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("app-secret"), body)).ToLowerInvariant();

        IWhatsAppWebhookProcessor.SignatureIsValid("app-secret", body, "sha256=" + hex).Should().BeTrue();
        IWhatsAppWebhookProcessor.SignatureIsValid("app-secret", body, "sha256=" + hex.ToUpperInvariant()).Should().BeTrue();
        IWhatsAppWebhookProcessor.SignatureIsValid("wrong-secret", body, "sha256=" + hex).Should().BeFalse();
        IWhatsAppWebhookProcessor.SignatureIsValid("app-secret", body, "sha256=zz").Should().BeFalse();
        IWhatsAppWebhookProcessor.SignatureIsValid("app-secret", body, null).Should().BeFalse();
        IWhatsAppWebhookProcessor.SignatureIsValid("", body, "sha256=" + hex).Should().BeFalse();
    }

    [Fact]
    public void Phone_number_ids_are_extracted_for_routing()
    {
        IWhatsAppWebhookProcessor.ExtractPhoneNumberIds(Root(MessagePayload("hi"))).Should().Equal("PN123");
        IWhatsAppWebhookProcessor.ExtractPhoneNumberIds(Root("""{"object":"x"}""")).Should().BeEmpty();
    }

    [Fact]
    public async Task Inbound_text_creates_the_contact_opens_the_session_and_raises_the_event()
    {
        var result = await Processor().ProcessAsync(Tenant, "PN123", Root(MessagePayload("Hello there")));

        result.Should().Be(new WebhookProcessingResult(1, 0, 0, 0));

        var contact = _contacts.Rows.Should().ContainSingle().Subject;
        contact.PhoneNumber.Should().Be("+2348031234567");
        contact.DisplayName.Should().Be("Ada Lovelace");
        contact.LastInboundAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1758200000).UtcDateTime);
        contact.LastInboundMessageId.Should().Be("wamid.IN1");
        contact.ConsentStatus.Should().Be(WhatsAppConsentStatus.OptedIn, "writing to the business number is treated as opting in");
        contact.ConsentSource.Should().Be("inbound_message");
        contact.IsSessionOpen(contact.LastInboundAt!.Value.AddHours(23)).Should().BeTrue();

        var stored = _inbound.Rows.Should().ContainSingle().Subject;
        stored.Text.Should().Be("Hello there");
        stored.ContactId.Should().Be(contact.Id);

        var received = _events.Raised.OfType<WhatsAppMessageReceivedEvent>().Should().ContainSingle().Subject;
        received.TenantId.Should().Be(Tenant);
        received.From.Should().Be("+2348031234567");
        received.Text.Should().Be("Hello there");
        received.ProfileName.Should().Be("Ada Lovelace");
        received.PhoneNumberId.Should().Be("PN123");
        _events.Raised.OfType<WhatsAppConsentChangedEvent>().Should().ContainSingle().Which.ConsentStatus.Should().Be("OptedIn");
    }

    [Fact]
    public async Task Stop_opts_the_number_out_and_start_opts_it_back_in()
    {
        var processor = Processor();

        await processor.ProcessAsync(Tenant, "PN123", Root(MessagePayload("STOP", id: "wamid.1")));
        var contact = _contacts.Rows.Single();
        contact.ConsentStatus.Should().Be(WhatsAppConsentStatus.OptedOut);
        contact.ConsentSource.Should().Be("keyword");
        contact.OptedOutAt.Should().NotBeNull();
        _inbound.Rows.Single().Keyword.Should().Be("STOP");
        _events.Raised.OfType<WhatsAppMessageReceivedEvent>().Single().Keyword.Should().Be("STOP");

        await processor.ProcessAsync(Tenant, "PN123", Root(MessagePayload("start", id: "wamid.2", ts: 1758200500)));
        contact.ConsentStatus.Should().Be(WhatsAppConsentStatus.OptedIn);
        contact.OptedOutAt.Should().BeNull();
        _events.Raised.OfType<WhatsAppConsentChangedEvent>().Select(e => e.ConsentStatus).Should().Equal("OptedOut", "OptedIn");
    }

    [Fact]
    public async Task Redelivered_messages_are_recognised_as_duplicates()
    {
        var processor = Processor();
        var payload = Root(MessagePayload("hi"));

        var first = await processor.ProcessAsync(Tenant, "PN123", payload);
        var second = await processor.ProcessAsync(Tenant, "PN123", payload);

        first.Messages.Should().Be(1);
        second.Should().Be(new WebhookProcessingResult(0, 0, 1, 0));
        _inbound.Rows.Should().HaveCount(1);
        _events.Raised.OfType<WhatsAppMessageReceivedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Changes_for_another_phone_number_id_are_ignored()
    {
        var result = await Processor().ProcessAsync(Tenant, "OTHER", Root(MessagePayload("hi")));

        result.Should().Be(new WebhookProcessingResult(0, 0, 0, 0));
        _contacts.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Delivery_statuses_become_status_events()
    {
        var result = await Processor().ProcessAsync(Tenant, "PN123", Root(StatusPayload("read")));

        result.Statuses.Should().Be(1);
        var evt = _events.Raised.OfType<WhatsAppMessageStatusEvent>().Should().ContainSingle().Subject;
        evt.MessageId.Should().Be("wamid.OUT1");
        evt.Status.Should().Be("read");
        evt.Recipient.Should().Be("+2348031234567");
        evt.ConversationCategory.Should().Be("utility");
        evt.IsFailure.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_status_carries_metas_error()
    {
        var errors = ""","errors":[{"code":131026,"title":"Message undeliverable","error_data":{"details":"Recipient is not a valid WhatsApp user"}}]""";

        await Processor().ProcessAsync(Tenant, "PN123", Root(StatusPayload("failed", errors: errors)));

        var evt = _events.Raised.OfType<WhatsAppMessageStatusEvent>().Single();
        evt.IsFailure.Should().BeTrue();
        evt.ErrorCode.Should().Be(131026);
        evt.ErrorTitle.Should().Be("Message undeliverable");
        evt.ErrorDetail.Should().Contain("not a valid WhatsApp user");
    }

    [Fact]
    public async Task Non_text_messages_are_stored_with_their_type_and_do_not_touch_consent_keywords()
    {
        var payload = """
            {"entry":[{"changes":[{"value":{"metadata":{"phone_number_id":"PN123"},
              "messages":[{"from":"2348031234567","id":"wamid.IMG","timestamp":"1758200000","type":"image","image":{"caption":"STOP sign photo","mime_type":"image/jpeg","id":"m1"}}]}}]}]}
            """;

        await Processor().ProcessAsync(Tenant, "PN123", Root(payload));

        var row = _inbound.Rows.Single();
        row.MessageType.Should().Be("image");
        row.Text.Should().Be("STOP sign photo");
        row.Keyword.Should().BeNull("only plain text can be a keyword");
        _contacts.Rows.Single().ConsentStatus.Should().Be(WhatsAppConsentStatus.OptedIn);
    }
}
