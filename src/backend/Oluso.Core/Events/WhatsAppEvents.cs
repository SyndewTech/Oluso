namespace Oluso.Core.Events;

/// <summary>
/// Raised for every inbound WhatsApp message accepted on the webhook, after
/// the contact record and consent keywords have been processed. Tenant
/// automations use it for auto-replies, ticket creation or CRM logging.
/// </summary>
[WebhookEvent("WhatsApp Message Received",
    Description = "An inbound WhatsApp message arrived on the tenant's business number.",
    EnabledByDefault = true)]
public class WhatsAppMessageReceivedEvent : OlusoEvent
{
    public override string Category => "Messaging";
    public override string? WebhookEventType => "whatsapp.message.received";

    public required string MessageId { get; init; }
    public required string ContactId { get; init; }

    /// <summary>Sender, E.164.</summary>
    public required string From { get; init; }
    public string? ProfileName { get; init; }
    public required string MessageType { get; init; }
    public string? Text { get; init; }

    /// <summary>Set when the text was an opt-in or opt-out keyword (e.g. STOP, START).</summary>
    public string? Keyword { get; init; }
    public string? InReplyToMessageId { get; init; }
    public DateTime ReceivedAt { get; init; }

    /// <summary>The business phone number id the message was sent to.</summary>
    public required string PhoneNumberId { get; init; }
}

/// <summary>
/// Delivery status callback for a message the tenant sent: sent, delivered,
/// read or failed. Correlate with the provider message id recorded at send.
/// </summary>
[WebhookEvent("WhatsApp Message Status",
    Description = "Meta reported a delivery status (sent, delivered, read, failed) for an outbound WhatsApp message.")]
public class WhatsAppMessageStatusEvent : OlusoEvent
{
    public override string Category => "Messaging";
    public override string? WebhookEventType => "whatsapp.message.status";
    public override bool IsFailure => string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase);

    public required string MessageId { get; init; }

    /// <summary>Recipient, E.164.</summary>
    public required string Recipient { get; init; }

    /// <summary>sent, delivered, read, failed, deleted.</summary>
    public required string Status { get; init; }
    public DateTime StatusAt { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorTitle { get; init; }
    public string? ErrorDetail { get; init; }

    /// <summary>Conversation category Meta billed this under (utility, marketing, authentication, service), when supplied.</summary>
    public string? ConversationCategory { get; init; }
    public required string PhoneNumberId { get; init; }
}

/// <summary>
/// A recipient's consent changed: they replied STOP or START, an administrator
/// changed it, or an integration recorded it.
/// </summary>
[WebhookEvent("WhatsApp Consent Changed",
    Description = "A WhatsApp recipient opted in or out of messages.",
    EnabledByDefault = true)]
public class WhatsAppConsentChangedEvent : OlusoEvent
{
    public override string Category => "Messaging";
    public override string? WebhookEventType => "whatsapp.consent.changed";

    public required string ContactId { get; init; }
    public required string PhoneNumber { get; init; }

    /// <summary>OptedIn, OptedOut or Unknown.</summary>
    public required string ConsentStatus { get; init; }
    public required string Source { get; init; }
    public string? ChangedByUserId { get; init; }
}
