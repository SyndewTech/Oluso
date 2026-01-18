namespace Oluso.Core.Licensing;

/// <summary>
/// Interface for feature validation at runtime.
/// Used for feature gating in APIs and UIs.
/// </summary>
public interface IFeatureGate
{
    /// <summary>
    /// Check if a feature is available asynchronously
    /// </summary>
    Task<FeatureGateResult> CheckFeatureAsync(string featureKey, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quick synchronous check (license only, no async providers)
    /// </summary>
    FeatureGateResult CheckFeature(string featureKey);

    /// <summary>
    /// Throws if feature is not available (async)
    /// </summary>
    Task RequireFeatureAsync(string featureKey, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws if feature is not available (sync)
    /// </summary>
    void RequireFeature(string featureKey);
}

/// <summary>
/// Result of a feature gate check
/// </summary>
public record FeatureGateResult
{
    public bool IsAllowed { get; init; }
    public FeatureDenialReason? Reason { get; init; }
    public string? Message { get; init; }

    public static FeatureGateResult Allowed() => new() { IsAllowed = true };

    public static FeatureGateResult Denied(FeatureDenialReason reason, string? message = null) =>
        new() { IsAllowed = false, Reason = reason, Message = message };
}

/// <summary>
/// Reason why a feature was denied
/// </summary>
public enum FeatureDenialReason
{
    /// <summary>Platform Oluso license doesn't include this feature</summary>
    PlatformLicense,

    /// <summary>A feature provider denied access</summary>
    ProviderDenied,

    /// <summary>Feature is disabled by administrator</summary>
    AdminDisabled
}
