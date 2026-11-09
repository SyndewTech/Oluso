using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Terminology;

/// <summary>
/// A tenant's per-locale rename of a single terminology key (e.g. a NICU
/// preferring "Cot" over the inherited "Bed"). Authored in the Terminology
/// Center; layered on top of (generic default ◁ industry pack) at resolve
/// time. Clearing the override reverts to the inherited value.
///
/// <para>Platform-owned: overrides for any module's keys (common.* and
/// each module's own prefix) share this one table on the platform DbContext —
/// terminology is a platform concern, not a module's.</para>
/// </summary>
public class TerminologyOverride : AuditableTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>BCP-47 locale this override applies to (e.g. "en-US").</summary>
    public string Locale { get; set; } = "en-US";

    /// <summary>The terminology key, e.g. "fm.space.allocatable.singular".</summary>
    public string Key { get; set; } = null!;

    /// <summary>The tenant's replacement label.</summary>
    public string Value { get; set; } = null!;
}
