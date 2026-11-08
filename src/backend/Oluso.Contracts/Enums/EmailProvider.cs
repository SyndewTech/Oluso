using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Email service providers Oluso can send through. Only providers wired up
/// in TenantAwareEmailSender are listed here — do not add a value without
/// also implementing its send path.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmailProvider
{
    SendGrid = 0,
    Brevo = 1,
    Cloudflare = 2,
    Smtp = 3,
}
