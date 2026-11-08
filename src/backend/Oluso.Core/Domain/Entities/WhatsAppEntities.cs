namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Consent state of a WhatsApp recipient. Unknown means nobody has recorded
/// anything yet; sending is allowed but the tenant carries the onboarding
/// obligation. OptedOut blocks every send until the person opts back in.
/// </summary>
public enum WhatsAppConsentStatus
{
    Unknown = 0,
    OptedIn = 1,
    OptedOut = 2,
}

/// <summary>
/// One WhatsApp number the tenant has messaged or been messaged by. Holds the
/// consent record and the timestamps that decide whether a free-form text is
/// allowed (Meta only accepts free-form inside 24 hours of the person's last
/// inbound message; outside that window a pre-approved template is required).
/// </summary>
public class WhatsAppContact : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>E.164, with the leading plus sign.</summary>
    public string PhoneNumber { get; set; } = null!;

    /// <summary>Profile name Meta sent with the last inbound message.</summary>
    public string? DisplayName { get; set; }

    public WhatsAppConsentStatus ConsentStatus { get; set; } = WhatsAppConsentStatus.Unknown;

    /// <summary>Where the consent state came from: keyword, inbound_message, admin, api.</summary>
    public string? ConsentSource { get; set; }
    public DateTime? ConsentAt { get; set; }
    public DateTime? OptedOutAt { get; set; }

    public DateTime? LastInboundAt { get; set; }
    public string? LastInboundMessageId { get; set; }
    public DateTime? LastOutboundAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Length of the customer-service window Meta grants after an inbound message.</summary>
    public static readonly TimeSpan SessionWindow = TimeSpan.FromHours(24);

    public bool IsSessionOpen(DateTime nowUtc) =>
        LastInboundAt.HasValue && nowUtc - LastInboundAt.Value < SessionWindow;
}

/// <summary>
/// A message template as Meta knows it, synced from the tenant's WhatsApp
/// Business Account. Only APPROVED templates can be sent.
/// </summary>
public class WhatsAppTemplate : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Meta's id for the template.</summary>
    public string ProviderTemplateId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Language { get; set; } = null!;

    /// <summary>MARKETING, UTILITY or AUTHENTICATION.</summary>
    public string? Category { get; set; }

    /// <summary>APPROVED, PENDING, REJECTED, PAUSED, DISABLED.</summary>
    public string Status { get; set; } = null!;

    /// <summary>Text of the BODY component, with {{1}}, {{2}} placeholders.</summary>
    public string? BodyText { get; set; }

    /// <summary>Number of positional body parameters the template expects.</summary>
    public int BodyParameterCount { get; set; }

    /// <summary>HEADER component format when present: TEXT, IMAGE, DOCUMENT, VIDEO.</summary>
    public string? HeaderFormat { get; set; }

    /// <summary>Raw components array from Meta, for the editor's preview.</summary>
    public string? ComponentsJson { get; set; }

    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    public bool IsApproved => string.Equals(Status, "APPROVED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// An inbound WhatsApp message as received on the webhook. Kept for replay
/// protection (Meta redelivers on non-2xx) and for the conversation timeline.
/// </summary>
public class WhatsAppInboundMessage : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Meta's message id (wamid.*).</summary>
    public string ProviderMessageId { get; set; } = null!;
    public string ContactId { get; set; } = null!;

    /// <summary>Sender number, E.164.</summary>
    public string From { get; set; } = null!;

    /// <summary>text, image, document, audio, video, sticker, location, button, interactive, reaction, unknown.</summary>
    public string MessageType { get; set; } = null!;
    public string? Text { get; set; }

    /// <summary>Set when the text matched an opt-in or opt-out keyword.</summary>
    public string? Keyword { get; set; }

    /// <summary>Message id this one replies to, when Meta supplied a context.</summary>
    public string? InReplyToMessageId { get; set; }

    public DateTime ReceivedAt { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public string? RawJson { get; set; }
}

/// <summary>
/// Maps a Meta phone number id to the tenant that owns it, so a webhook
/// delivered to the shared platform URL can be routed before its signature is
/// checked with that tenant's app secret. Maintained whenever the tenant saves
/// or tests its WhatsApp settings.
/// </summary>
public class WhatsAppAccount : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PhoneNumberId { get; set; } = null!;
    public string? BusinessAccountId { get; set; }
    public string? DisplayPhoneNumber { get; set; }
    public string? VerifiedName { get; set; }
    public string? QualityRating { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
