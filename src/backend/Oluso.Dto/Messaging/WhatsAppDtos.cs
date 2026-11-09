namespace Oluso.Dto.Messaging;

/// <summary>Tenant WhatsApp configuration as shown to administrators. Secrets are never returned, only whether they are set.</summary>
public class WhatsAppSettingsDto
{
    public bool Enabled { get; set; }
    public bool HasAccessToken { get; set; }
    public bool HasAppSecret { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? BusinessAccountId { get; set; }
    public string? ApiVersion { get; set; }
    public string? DefaultCountry { get; set; }

    /// <summary>Shown in clear so the administrator can paste it into Meta's webhook configuration.</summary>
    public string? WebhookVerifyToken { get; set; }

    /// <summary>Where the effective credentials come from: TenantSettings, Platform, or null when unconfigured.</summary>
    public string? EffectiveSource { get; set; }
    public bool IsConfigured { get; set; }

    /// <summary>Callback URL on the tenant's own host to register in Meta's webhook configuration.</summary>
    public string? WebhookUrl { get; set; }

    public WhatsAppAccountDto? Account { get; set; }
}

public class UpdateWhatsAppSettingsRequest
{
    public bool Enabled { get; set; }

    /// <summary>Blank keeps the stored value.</summary>
    public string? AccessToken { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? BusinessAccountId { get; set; }

    /// <summary>Blank keeps the stored value.</summary>
    public string? AppSecret { get; set; }
    public string? WebhookVerifyToken { get; set; }
    public string? ApiVersion { get; set; }
    public string? DefaultCountry { get; set; }
}

public class WhatsAppAccountDto
{
    public string PhoneNumberId { get; set; } = null!;
    public string? BusinessAccountId { get; set; }
    public string? DisplayPhoneNumber { get; set; }
    public string? VerifiedName { get; set; }
    public string? QualityRating { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
}

public class WhatsAppTestConnectionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public WhatsAppAccountDto? Account { get; set; }
}

public class WhatsAppTemplateDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Language { get; set; } = null!;
    public string? Category { get; set; }
    public string Status { get; set; } = null!;
    public string? BodyText { get; set; }
    public int BodyParameterCount { get; set; }
    public string? HeaderFormat { get; set; }
    public DateTime SyncedAt { get; set; }
}

public class WhatsAppTemplateSyncResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int Count { get; set; }
}

public class WhatsAppContactDto
{
    public string Id { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string ConsentStatus { get; set; } = null!;
    public string? ConsentSource { get; set; }
    public DateTime? ConsentAt { get; set; }
    public DateTime? LastInboundAt { get; set; }
    public DateTime? LastOutboundAt { get; set; }

    /// <summary>True while a free-form text would be accepted (inbound within 24 hours, not opted out).</summary>
    public bool SessionOpen { get; set; }
}

public class WhatsAppContactListResponse
{
    public List<WhatsAppContactDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class UpdateWhatsAppConsentRequest
{
    /// <summary>OptedIn or OptedOut.</summary>
    public string ConsentStatus { get; set; } = null!;
}

public class WhatsAppSendTestRequest
{
    public string To { get; set; } = null!;

    /// <summary>Free-form text; only delivered inside an open 24-hour session.</summary>
    public string? Text { get; set; }
    public string? TemplateName { get; set; }
    public string? TemplateLanguage { get; set; }
    public List<string>? TemplateParameters { get; set; }
}

public class WhatsAppSendTestResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }
    public string? NormalizedPhone { get; set; }
}

/// <summary>Which notification channels this tenant can actually deliver on; drives the preference toggles a user sees.</summary>
public class NotificationChannelAvailabilityDto
{
    public bool Email { get; set; } = true;
    public bool Sms { get; set; }
    public bool WhatsApp { get; set; }
    public bool Push { get; set; }
}
