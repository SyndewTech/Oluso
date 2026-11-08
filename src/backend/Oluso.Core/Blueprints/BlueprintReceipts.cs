namespace Oluso.Core.Blueprints;

/// <summary>
/// Platform-level constants for the Blueprints surface.
/// </summary>
public static class BlueprintConstants
{
    /// <summary>
    /// Tenant configuration section (via <c>ITenantConfigurationService</c>)
    /// where blueprint application receipts are recorded — the same
    /// configuration-sections pattern modules use for
    /// low-volume, whole-document tenant state.
    /// </summary>
    public const string ReceiptsSection = "Platform.Blueprints";
}

/// <summary>
/// The stored record of which blueprints a tenant has applied, keyed by
/// blueprint key. Persisted in the <see cref="BlueprintConstants.ReceiptsSection"/>
/// tenant configuration section.
/// </summary>
public sealed class BlueprintReceiptsSection
{
    public Dictionary<string, BlueprintApplicationReceipt> Applied { get; set; } = new();
}

/// <summary>Receipt for the most recent application of one blueprint.</summary>
public sealed class BlueprintApplicationReceipt
{
    public required string Key { get; set; }

    /// <summary>When the blueprint was last applied (UTC).</summary>
    public DateTime AppliedAtUtc { get; set; }

    /// <summary>Admin user id that applied it.</summary>
    public string? AppliedBy { get; set; }

    /// <summary>Whether the last application requested overwrite.</summary>
    public bool Overwrite { get; set; }

    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }

    /// <summary>Created-item counts by kind (e.g. <c>noteTemplate → 6</c>).</summary>
    public Dictionary<string, int> CreatedByKind { get; set; } = new();
}
