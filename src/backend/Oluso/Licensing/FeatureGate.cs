using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;

namespace Oluso.Licensing;

/// <summary>
/// Validates feature availability at runtime.
/// Used for feature gating in APIs and UIs.
///
/// Checks in order:
/// 1. Platform License (ILicenseValidator) - Is this feature in your Oluso license?
/// 2. Feature Providers (IFeatureAvailabilityProvider) - Extensible for additional checks
/// </summary>
public class FeatureGate : IFeatureGate
{
    private readonly ILicenseValidator? _licenseValidator;
    private readonly IEnumerable<IFeatureAvailabilityProvider> _featureProviders;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FeatureGate> _logger;

    public FeatureGate(
        ITenantContext tenantContext,
        ILogger<FeatureGate> logger,
        ILicenseValidator? licenseValidator = null,
        IEnumerable<IFeatureAvailabilityProvider>? featureProviders = null)
    {
        _tenantContext = tenantContext;
        _logger = logger;
        _licenseValidator = licenseValidator;
        _featureProviders = featureProviders ?? Array.Empty<IFeatureAvailabilityProvider>();
    }

    /// <summary>
    /// Check if a feature is available
    /// </summary>
    public async Task<FeatureGateResult> CheckFeatureAsync(
        string featureKey,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Check platform license first
        if (_licenseValidator != null)
        {
            var licenseResult = _licenseValidator.ValidateFeature(featureKey);
            if (!licenseResult.IsValid)
            {
                return FeatureGateResult.Denied(
                    FeatureDenialReason.PlatformLicense,
                    $"Feature '{featureKey}' requires a license upgrade. {licenseResult.Message}");
            }
        }

        // 2. Check additional providers (extensibility for billing, etc.)
        var tenantId = _tenantContext.TenantId;

        foreach (var provider in _featureProviders.OrderByDescending(p => p.Priority))
        {
            try
            {
                var isAvailable = await provider.IsFeatureAvailableAsync(
                    featureKey, tenantId, userId, cancellationToken);

                if (!isAvailable)
                {
                    return FeatureGateResult.Denied(
                        FeatureDenialReason.ProviderDenied,
                        $"Feature '{featureKey}' is not available.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Feature provider {Provider} failed checking {Feature}",
                    provider.GetType().Name, featureKey);
                // Continue to next provider on error
            }
        }

        return FeatureGateResult.Allowed();
    }

    /// <summary>
    /// Quick synchronous check (license only, no async providers)
    /// </summary>
    public FeatureGateResult CheckFeature(string featureKey)
    {
        if (_licenseValidator != null)
        {
            var licenseResult = _licenseValidator.ValidateFeature(featureKey);
            if (!licenseResult.IsValid)
            {
                return FeatureGateResult.Denied(
                    FeatureDenialReason.PlatformLicense,
                    licenseResult.Message);
            }
        }

        return FeatureGateResult.Allowed();
    }

    /// <summary>
    /// Throws if feature is not available
    /// </summary>
    public async Task RequireFeatureAsync(
        string featureKey,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await CheckFeatureAsync(featureKey, userId, cancellationToken);
        if (!result.IsAllowed)
        {
            throw new LicenseException(
                result.Message ?? $"Feature '{featureKey}' is not available");
        }
    }

    /// <summary>
    /// Synchronous require (license only)
    /// </summary>
    public void RequireFeature(string featureKey)
    {
        var result = CheckFeature(featureKey);
        if (!result.IsAllowed)
        {
            throw new LicenseException(
                result.Message ?? $"Feature '{featureKey}' is not available");
        }
    }
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

/// <summary>
/// Attribute to require a feature for a controller or action
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiresFeatureAttribute : Attribute
{
    public string FeatureKey { get; }

    public RequiresFeatureAttribute(string featureKey)
    {
        FeatureKey = featureKey;
    }
}

/// <summary>
/// Extension methods for registering feature gating
/// </summary>
public static class FeatureGateExtensions
{
    /// <summary>
    /// Adds feature gate services
    /// </summary>
    public static IServiceCollection AddFeatureGate(this IServiceCollection services)
    {
        services.AddScoped<IFeatureGate, FeatureGate>();
        return services;
    }

    /// <summary>
    /// Adds license claims provider
    /// </summary>
    public static IServiceCollection AddLicenseClaims(this IServiceCollection services)
    {
        services.AddScoped<IClaimsProvider, LicenseClaimsProvider>();
        return services;
    }

    /// <summary>
    /// Adds a feature availability provider
    /// </summary>
    public static IServiceCollection AddFeatureProvider<T>(this IServiceCollection services)
        where T : class, IFeatureAvailabilityProvider
    {
        services.AddScoped<IFeatureAvailabilityProvider, T>();
        return services;
    }
}
