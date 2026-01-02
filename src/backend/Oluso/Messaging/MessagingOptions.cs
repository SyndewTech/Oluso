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
    /// SendGrid specific settings
    /// </summary>
    public SendGridSettings SendGrid { get; set; } = new();

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
