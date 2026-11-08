namespace Oluso.Core.Terminology;

/// <summary>
/// The platform terminology facade — the single Core-level surface the
/// (central) admin Terminology Center and the runtime endpoints call. The
/// implementation lives in whichever module owns the override storage and the
/// tenant industry profile, but everyone depends only on this Core interface,
/// so the admin controller never references a specific module.
/// </summary>
public interface ITerminologyService
{
    /// <summary>
    /// The fully merged <c>key → label</c> map for a tenant + locale across
    /// all contributing modules (generic default ◁ industry pack ◁ tenant
    /// override). Consumed by the Transloco loaders.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        string tenantId, string locale, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Terminology Center authoring grid: every key the tenant is entitled
    /// to (filtered to licensed modules + the caller's seats), with inherited
    /// value and any tenant override.
    /// </summary>
    Task<IReadOnlyList<ResolvedTerminologyKey>> GetAuthoringKeysAsync(
        string tenantId, string locale, string? userId, CancellationToken cancellationToken = default);

    /// <summary>This tenant's overrides for a locale.</summary>
    Task<IReadOnlyList<TerminologyOverrideEntry>> GetOverridesAsync(
        string tenantId, string locale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set (create/update) a tenant override. Returns null if the key is
    /// unknown or not overridable (the caller should surface a 400).
    /// </summary>
    Task<TerminologyOverrideEntry?> SetOverrideAsync(
        string tenantId, string locale, string key, string value,
        TerminologyActor actor, CancellationToken cancellationToken = default);

    /// <summary>Reset a key to its inherited value. True if an override was removed.</summary>
    Task<bool> ResetOverrideAsync(
        string tenantId, string locale, string key,
        TerminologyActor actor, CancellationToken cancellationToken = default);
}

/// <summary>A single key as shown in the Terminology Center grid.</summary>
public sealed record ResolvedTerminologyKey(
    string ModuleKey,
    string Key,
    string Group,
    string? Description,
    bool IsOverridable,
    bool SupportsPlural,
    string InheritedValue,
    string? OverrideValue);

/// <summary>A tenant terminology override.</summary>
public sealed record TerminologyOverrideEntry(string Key, string Locale, string Value);

/// <summary>Who made a terminology change (for audit/events).</summary>
public sealed record TerminologyActor(string UserId, string? UserName, string? IpAddress);

/// <summary>The tenant's industry/type and the choices available.</summary>
public sealed record TenantTerminologyProfile(
    string? Industry,
    IReadOnlyList<IndustryOption> AvailableIndustries);

/// <summary>A selectable industry/type option.</summary>
public sealed record IndustryOption(string Value, string Label);

/// <summary>Tenant-configuration section keys owned by the terminology layer.</summary>
public static class TerminologySections
{
    /// <summary>
    /// Section storing the tenant's industry/type (central tenant setting,
    /// read/written via ITenantConfigurationService).
    /// </summary>
    public const string TenantProfile = "TenantProfile";
}

/// <summary>Shape persisted in the <see cref="TerminologySections.TenantProfile"/> config section.</summary>
public sealed class TenantProfileSettings
{
    /// <summary>The tenant's industry/type, e.g. "Healthcare". Null = generic.</summary>
    public string? Industry { get; set; }
}
