namespace Oluso.Core.Services;

/// <summary>
/// Per-tenant WhatsApp Business (Meta Cloud API) configuration. Stored as the
/// encrypted "WhatsApp" tenant configuration section, so the access token and
/// app secret never sit in plaintext.
/// </summary>
public class WhatsAppSettings
{
    public const string SectionKey = "WhatsApp";

    public bool Enabled { get; set; }

    /// <summary>System-user or permanent access token for the Graph API.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Meta phone number id (not the phone number itself).</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>WhatsApp Business Account id; needed to sync templates.</summary>
    public string? BusinessAccountId { get; set; }

    /// <summary>Meta app secret; verifies X-Hub-Signature-256 on webhooks.</summary>
    public string? AppSecret { get; set; }

    /// <summary>Token pasted into the Meta webhook configuration for the GET handshake.</summary>
    public string? WebhookVerifyToken { get; set; }

    public string? ApiVersion { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country used to complete numbers typed without a country code.</summary>
    public string? DefaultCountry { get; set; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);
}
