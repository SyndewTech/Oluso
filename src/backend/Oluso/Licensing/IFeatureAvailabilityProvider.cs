namespace Oluso.Licensing;

/// <summary>
/// Extension point for additional feature availability providers.
/// Billing plugins can implement this to add subscription-based features.
/// </summary>
public interface IFeatureAvailabilityProvider
{
    /// <summary>Provider priority (higher = processed first)</summary>
    int Priority { get; }

    /// <summary>
    /// Check if a specific feature is available for the tenant/user
    /// </summary>
    Task<bool> IsFeatureAvailableAsync(
        string featureKey,
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get additional claims to add to tokens (for subscription info, etc.)
    /// </summary>
    Task<IDictionary<string, object>> GetFeatureClaimsAsync(
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default);
}
