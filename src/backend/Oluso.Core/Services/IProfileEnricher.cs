namespace Oluso.Core.Services;

/// <summary>
/// Interface for enriching the signed-in user's profile with additional data from plugins.
/// Facts land in the profile payloads the portals read, not in tokens; a module that
/// also needs a fact at API authorization time adds it through IClaimsProvider.
/// Plugins (e.g., a module) can implement this to add module-specific facts
/// (is a manager, is a patient, has a linked relative) to the /api/workspace/me
/// and /api/account/me responses without modifying the core controllers.
/// </summary>
public interface IProfileEnricher
{
    /// <summary>
    /// Unique identifier for this enricher (the module's key)
    /// </summary>
    string EnricherId { get; }

    /// <summary>
    /// The module feature this enricher belongs to. When set, the enricher only
    /// runs for tenants that have the feature, so a tenant without the module
    /// never pays for the lookup and never receives its extension key.
    /// </summary>
    string? FeatureKey => null;

    /// <summary>
    /// Priority for ordering enrichers. Lower values run first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Enrich the user's profile with additional data.
    /// Implementations should add entries to the Extensions dictionary.
    /// </summary>
    Task EnrichAsync(ProfileEnricherContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context passed to profile enrichers
/// </summary>
public class ProfileEnricherContext
{
    /// <summary>
    /// The user's subject ID
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// The tenant ID
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The user's email, for modules that link people by email rather than user id.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// The extensions dictionary to populate.
    /// Enrichers should add their data here using their enricher ID as the key.
    /// </summary>
    public IDictionary<string, object> Extensions { get; init; } = new Dictionary<string, object>();
}
