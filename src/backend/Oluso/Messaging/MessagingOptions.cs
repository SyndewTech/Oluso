namespace Oluso.Messaging;

/// <summary>
/// Configuration options for email messaging
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Oluso:Messaging:Email";

    /// <summary>
    /// The email provider to use (e.g., "SendGrid", "Smtp")
    /// </summary>
    public string Provider { get; set; } = "SendGrid";

    /// <summary>
    /// Default sender email address
    /// </summary>
    public string? FromAddress { get; set; }

    /// <summary>
    /// Default sender display name
    /// </summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Platform default reply-to, used for tenants sending from the platform
    /// address when they have not confirmed a reply-to inbox of their own.
    /// </summary>
    public string? ReplyToAddress { get; set; }

    /// <summary>
    /// SendGrid specific settings
    /// </summary>
    public SendGridSettings SendGrid { get; set; } = new();

    /// <summary>
    /// Cloudflare Email Sending specific settings
    /// </summary>
    public CloudflareEmailSettings Cloudflare { get; set; } = new();

    /// <summary>
    /// SMTP specific settings
    /// </summary>
    public SmtpSettings Smtp { get; set; } = new();
}

/// <summary>
/// SendGrid specific settings
/// </summary>
public class SendGridSettings
{
    /// <summary>
    /// SendGrid API key
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Cloudflare Email Sending settings (public beta service — REST API
/// at https://api.cloudflare.com/client/v4/accounts/{account_id}/email/sending/send).
/// The sender domain must be onboarded in the Cloudflare dashboard under
/// Email → Sending → Onboard Domain before any send will succeed.
/// </summary>
public class CloudflareEmailSettings
{
    /// <summary>
    /// Cloudflare account ID — forms the {account_id} segment of the send URL.
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>
    /// Cloudflare API token with the "Email Sending" permission.
    /// </summary>
    public string? ApiToken { get; set; }
}

/// <summary>
/// SMTP specific settings
/// </summary>
public class SmtpSettings
{
    /// <summary>
    /// SMTP server host
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// SMTP server port
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// SMTP username
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SMTP password
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Use SSL/TLS
    /// </summary>
    public bool UseSsl { get; set; } = true;
}

/// <summary>
/// Configuration options for SMS messaging
/// </summary>
public class SmsOptions
{
    public const string SectionName = "Oluso:Messaging:Sms";

    /// <summary>
    /// The SMS provider to use (e.g., "Infobip", "Twilio")
    /// </summary>
    public string Provider { get; set; } = "Infobip";

    /// <summary>
    /// Default sender ID/phone number
    /// </summary>
    public string? FromNumber { get; set; }

    /// <summary>
    /// Infobip specific settings
    /// </summary>
    public InfobipSettings Infobip { get; set; } = new();

    /// <summary>
    /// Twilio specific settings
    /// </summary>
    public TwilioSettings Twilio { get; set; } = new();
}

/// <summary>
/// Infobip specific settings
/// </summary>
public class InfobipSettings
{
    /// <summary>
    /// Infobip API key
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Infobip base URL (e.g., "https://api.infobip.com")
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.infobip.com";

    /// <summary>
    /// Sender ID for SMS messages
    /// </summary>
    public string? SenderId { get; set; }
}

/// <summary>
/// Configuration options for WhatsApp Business API messaging
/// </summary>
public class WhatsAppOptions
{
    public const string SectionName = "Oluso:Messaging:WhatsApp";

    /// <summary>
    /// The WhatsApp provider to use. Only "Meta" (Cloud API) is implemented;
    /// any other value fails at startup rather than silently sending via Meta.
    /// </summary>
    public string Provider { get; set; } = "Meta";

    /// <summary>
    /// Meta WhatsApp Business API settings
    /// </summary>
    public MetaWhatsAppSettings Meta { get; set; } = new();
}

/// <summary>
/// Meta WhatsApp Business API settings
/// </summary>
public class MetaWhatsAppSettings
{
    /// <summary>
    /// Permanent access token for the WhatsApp Business API
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Phone number ID registered in Meta Business Manager
    /// </summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>
    /// WhatsApp Business Account ID
    /// </summary>
    public string? BusinessAccountId { get; set; }

    /// <summary>
    /// API version (default: v21.0)
    /// </summary>
    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>
    /// Meta app secret. Verifies the X-Hub-Signature-256 header on webhooks
    /// delivered to the shared platform URL (tenants that bring their own Meta
    /// app configure their own secret in tenant settings instead).
    /// </summary>
    public string? AppSecret { get; set; }

    /// <summary>
    /// Verify token for the webhook subscription handshake on the shared
    /// platform URL.
    /// </summary>
    public string? WebhookVerifyToken { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country used to complete numbers typed without a
    /// country code when the tenant has not set its own default.
    /// </summary>
    public string? DefaultCountry { get; set; }
}

/// <summary>
/// Twilio specific settings
/// </summary>
public class TwilioSettings
{
    /// <summary>
    /// Twilio Account SID
    /// </summary>
    public string? AccountSid { get; set; }

    /// <summary>
    /// Twilio Auth Token
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Twilio phone number for sending SMS
    /// </summary>
    public string? FromNumber { get; set; }
}
