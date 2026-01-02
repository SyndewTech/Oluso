namespace Oluso.Licensing;

/// <summary>
/// Interface for feature validation
/// </summary>
public interface IFeatureGate
{
    Task<FeatureGateResult> CheckFeatureAsync(string featureKey, string? userId = null, CancellationToken cancellationToken = default);
    FeatureGateResult CheckFeature(string featureKey);
    Task RequireFeatureAsync(string featureKey, string? userId = null, CancellationToken cancellationToken = default);
    void RequireFeature(string featureKey);
}
