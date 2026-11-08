namespace Oluso.Core.Domain.Entities;

/// <summary>
/// A tenant-scoped outbound email sender configuration. Replaces the single
/// TenantEmailSettings blob: tenants can configure multiple profiles
/// (e.g. transactional default + per-product marketing senders) and pick
/// which profile a send goes through. Exactly one profile per tenant is the
/// default, used by system / transactional mail when no profile is specified.
///
/// Credentials are NOT stored on the entity. They live in
/// TenantConfigurationSections under section key "SenderProfile:{Id}",
/// encrypted at rest via DataProtection.
/// </summary>
public class SenderProfile : AuditableTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name used in admin lists and the workspace "Send from:" dropdown.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Which provider this profile sends through.
    /// </summary>
    public EmailProvider Provider { get; set; } = EmailProvider.SendGrid;

    /// <summary>
    /// Sender email address (must be verified at the provider side by the tenant).
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Sender display name.
    /// </summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Optional reply-to. When null, replies go back to FromAddress.
    /// </summary>
    public string? ReplyToAddress { get; set; }

    /// <summary>
    /// Whether the profile may be selected for new sends. Inactive profiles
    /// are hidden from workspace dropdowns and rejected by the sender.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Exactly one profile per tenant is the default — used by system /
    /// transactional mail when the caller does not pin a specific profile.
    /// Enforced at the application layer (the store flips others to false
    /// when one is marked default) to stay portable across providers.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Last time the "Send test to me" action succeeded for this profile.
    /// </summary>
    public DateTime? LastTestSendAt { get; set; }

    /// <summary>
    /// Verbatim error from the most recent failed test send, or null if the
    /// last test succeeded (or none has been run yet). Surfaced in admin UI.
    /// </summary>
    public string? LastTestError { get; set; }
}
