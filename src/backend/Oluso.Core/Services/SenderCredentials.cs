namespace Oluso.Core.Services;

/// <summary>
/// Per-profile credential payload stored in TenantConfigurationSections under
/// section key "SenderProfile:{Id}". Encrypted at rest via DataProtection
/// (registered as an encrypted prefix in MessagingExtensions).
///
/// The shape is union-typed: only the fields relevant to the profile's
/// Provider are expected to be set. Empty fields are normal for providers
/// that don't use them.
/// </summary>
public class SenderCredentials
{
    public const string SectionKeyPrefix = "SenderProfile:";

    /// <summary>
    /// API key for SendGrid / Brevo / Cloudflare API token.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Cloudflare account ID (required when Provider = Cloudflare).
    /// </summary>
    public string? CloudflareAccountId { get; set; }

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;

    public static string BuildSectionKey(string profileId) => $"{SectionKeyPrefix}{profileId}";
}
