namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Stores a single configuration section for a tenant.
/// Each module owns its own section key (e.g. "SamlIdp", "Ai", "Integrations").
/// Sensitive sections are encrypted at rest via DataProtection.
/// </summary>
public class TenantConfigurationSection
{
    public string TenantId { get; set; } = null!;
    public string SectionKey { get; set; } = null!;

    /// <summary>
    /// The section value — either plaintext JSON or DataProtection-encrypted ciphertext.
    /// </summary>
    public string Value { get; set; } = null!;

    /// <summary>
    /// Whether the Value is encrypted. When true, Value must be decrypted
    /// with purpose string "Oluso.TenantConfig.{SectionKey}" before use.
    /// </summary>
    public bool IsEncrypted { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Updated { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}
