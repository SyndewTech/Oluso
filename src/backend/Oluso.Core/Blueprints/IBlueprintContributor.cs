namespace Oluso.Core.Blueprints;

/// <summary>
/// Implemented by each module that ships one or more "blueprints" — on-demand
/// starter packs of operational configuration (note templates, order sets,
/// automation workflows, …) a tenant admin can apply in one click instead of
/// hand-assembling. The platform's Blueprints surface enumerates all
/// registered contributors — the same DI-collection pattern used for
/// <c>ITerminologyContributor</c> and permission providers — so the admin
/// endpoint has no hardcoded knowledge of any specific module.
///
/// <para>Contributors are registered by their own module (typically as a
/// singleton in <c>IModuleDescriptor.ConfigureServices</c>) and resolve their
/// scoped stores from the <c>IServiceProvider</c> passed to
/// <see cref="ApplyAsync"/> — mirroring how
/// <c>IModuleDescriptor.SeedTenantAsync</c> receives ambient services.</para>
/// </summary>
public interface IBlueprintContributor
{
    /// <summary>
    /// The module's feature key — the SAME key used for licensing and seats
    /// (e.g. <c>"emr"</c>). The Blueprints surface filters by this so a
    /// tenant only ever sees packs for modules it is actually licensed for.
    /// </summary>
    string ModuleKey { get; }

    /// <summary>The blueprints this module offers (static metadata; no I/O).</summary>
    IEnumerable<BlueprintDescriptor> GetBlueprints();

    /// <summary>
    /// Apply one blueprint to a tenant. MUST be idempotent: items that already
    /// exist (matched by name) are skipped unless
    /// <see cref="BlueprintApplyOptions.Overwrite"/> is set. Any seeded
    /// automation workflows MUST land disabled/draft so nothing fires until an
    /// admin reviews and enables them.
    /// </summary>
    /// <param name="key">The blueprint key (one of this contributor's <see cref="BlueprintDescriptor.Key"/>s).</param>
    /// <param name="tenantId">Target tenant.</param>
    /// <param name="options">Apply options (overwrite, …).</param>
    /// <param name="services">
    /// Ambient service provider used to resolve the module's scoped stores —
    /// the same pattern as <c>IModuleDescriptor.SeedTenantAsync</c>.
    /// </param>
    Task<BlueprintApplyResult> ApplyAsync(
        string key,
        string tenantId,
        BlueprintApplyOptions options,
        IServiceProvider services,
        CancellationToken cancellationToken = default);
}

/// <summary>Metadata describing one applyable blueprint (starter pack).</summary>
public sealed class BlueprintDescriptor
{
    /// <summary>Stable key, e.g. <c>"emr.urgent-care"</c>.</summary>
    public required string Key { get; init; }

    /// <summary>The owning module's feature key, e.g. <c>"emr"</c>.</summary>
    public required string ModuleKey { get; init; }

    /// <summary>Display title, e.g. "Urgent Care starter pack".</summary>
    public required string Title { get; init; }

    /// <summary>What the pack sets up, in tenant-admin language.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Summary of the pack's contents: item count per kind
    /// (e.g. <c>noteTemplate → 6, orderSet → 5, workflow → 4</c>).
    /// </summary>
    public IReadOnlyDictionary<string, int> ItemCounts { get; init; } =
        new Dictionary<string, int>();
}

/// <summary>Options for a blueprint application.</summary>
public sealed class BlueprintApplyOptions
{
    /// <summary>
    /// When true, items that already exist (matched by name) are replaced with
    /// the pack's version instead of being skipped. Overwritten automation
    /// workflows return to the disabled/draft state for re-review.
    /// </summary>
    public bool Overwrite { get; init; }
}

/// <summary>One item a blueprint touched (or deliberately did not).</summary>
/// <param name="Kind">Item kind, e.g. <c>"noteTemplate"</c>, <c>"orderSet"</c>, <c>"workflow"</c>.</param>
/// <param name="Name">Human-readable item name.</param>
public sealed record BlueprintItemOutcome(string Kind, string Name);

/// <summary>The outcome of applying one blueprint to one tenant.</summary>
public sealed class BlueprintApplyResult
{
    /// <summary>The blueprint that was applied.</summary>
    public required string Key { get; init; }

    /// <summary>Items created (or replaced, when overwrite was requested).</summary>
    public List<BlueprintItemOutcome> Created { get; init; } = [];

    /// <summary>Items skipped because they already existed (or their backing service is unavailable).</summary>
    public List<BlueprintItemOutcome> Skipped { get; init; } = [];
}
