namespace Oluso.Core.Terminology;

/// <summary>
/// Implemented by each module that contributes overridable domain vocabulary.
/// The platform's terminology layer enumerates all
/// registered contributors — the same DI-collection pattern used for
/// permission providers and webhook mappers — so the Terminology Center has
/// no hardcoded knowledge of any specific module.
///
/// <para>A contributor supplies two things: the catalogue of keys it owns
/// (for the admin authoring grid) and the resolved label values for a given
/// industry + locale (for the runtime bundle). Tenant overrides are layered
/// on top centrally; a contributor never sees them.</para>
/// </summary>
public interface ITerminologyContributor
{
    /// <summary>
    /// The module's feature key — the SAME key used for licensing and seats.
    /// The Terminology Center filters groups by this so a
    /// tenant only ever sees vocabulary for modules it actually has.
    /// </summary>
    string ModuleKey { get; }

    /// <summary>
    /// The keys this module owns, with grouping/help/overridability metadata
    /// for the authoring UI. Industry-agnostic.
    /// </summary>
    IReadOnlyList<TerminologyKeyDescriptor> GetKeys();

    /// <summary>
    /// The resolved label values for this module at the given industry +
    /// locale — i.e. the module's generic defaults already merged with the
    /// industry pack. Returns a flat <c>key → value</c> map. Unknown industry
    /// or locale falls back to the generic defaults.
    /// </summary>
    /// <param name="industry">
    /// The tenant's configured industry (e.g. "Healthcare", "Hospitality").
    /// Null/empty yields the generic defaults.
    /// </param>
    /// <param name="locale">BCP-47 locale code (e.g. "en-US").</param>
    IReadOnlyDictionary<string, string> GetBundle(string? industry, string locale);
}

/// <summary>
/// Metadata describing a single overridable (or locked) terminology key, used
/// to render the Terminology Center grid.
/// </summary>
public sealed class TerminologyKeyDescriptor
{
    /// <summary>Stable key, e.g. <c>fm.space.allocatable.singular</c>.</summary>
    public required string Key { get; init; }

    /// <summary>UI grouping label within the module, e.g. "Spaces".</summary>
    public required string Group { get; init; }

    /// <summary>Help text shown on hover.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// When false the key is shown but locked (chrome/system terms that must
    /// not diverge per tenant). Defaults to true.
    /// </summary>
    public bool IsOverridable { get; init; } = true;

    /// <summary>
    /// True when this key participates in a singular/plural pair, so the UI
    /// can present them together.
    /// </summary>
    public bool SupportsPlural { get; init; }
}
