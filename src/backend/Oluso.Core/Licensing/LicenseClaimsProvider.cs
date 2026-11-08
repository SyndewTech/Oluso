using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Licensing;

/// <summary>
/// Claims provider that adds license-validated feature claims to tokens.
///
/// For multi-tenant scenarios, this validates features against:
/// 1. Platform license (ILicenseValidator) - your Oluso license tier
/// 2. Additional providers (IFeatureAvailabilityProvider) - extensible for billing plugins
///
/// Features are exposed as claims so client apps can check access.
/// </summary>
public class LicenseClaimsProvider : IClaimsProvider
{
    private readonly ITenantContext _tenantContext;
    private readonly ILicenseValidator? _licenseValidator;
    private readonly IEnumerable<IFeatureAvailabilityProvider> _featureProviders;
    private readonly ILogger<LicenseClaimsProvider> _logger;

    public LicenseClaimsProvider(
        ITenantContext tenantContext,
        ILogger<LicenseClaimsProvider> logger,
        ILicenseValidator? licenseValidator = null,
        IEnumerable<IFeatureAvailabilityProvider>? featureProviders = null)
    {
        _tenantContext = tenantContext;
        _licenseValidator = licenseValidator;
        _featureProviders = featureProviders ?? Array.Empty<IFeatureAvailabilityProvider>();
        _logger = logger;
    }

    public string ProviderId => "license-features";
    public int Priority => 200;
    public IEnumerable<string>? TriggerScopes => new[] { "openid" };

    public bool ShouldProvide(ClaimsProviderContext context) => true;

    public async Task<ClaimsProviderResult> GetClaimsAsync(
        ClaimsProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var claims = new Dictionary<string, object>();

        try
        {
            // Get license info
            if (_licenseValidator != null)
            {
                var license = _licenseValidator.GetCurrentLicense();
                if (license != null)
                {
                    claims[LicenseClaimTypes.Tier] = license.Tier.ToString().ToLowerInvariant();

                    if (license.ExpiresAt != default)
                    {
                        claims[LicenseClaimTypes.ExpiresAt] =
                            new DateTimeOffset(license.ExpiresAt).ToUnixTimeSeconds();
                    }
                }

                // Add licensed features
                var features = _licenseValidator.GetLicensedFeatures();
                if (features.Any())
                {
                    claims[LicenseClaimTypes.Features] = features.ToArray();
                }
            }

            // Check additional feature providers (extensibility point for billing, etc.)
            var tenantId = context.TenantId ?? _tenantContext.TenantId;
            if (!string.IsNullOrEmpty(tenantId))
            {
                claims[LicenseClaimTypes.TenantId] = tenantId;

                foreach (var provider in _featureProviders.OrderByDescending(p => p.Priority))
                {
                    try
                    {
                        var providerClaims = await provider.GetFeatureClaimsAsync(
                            tenantId, context.SubjectId, cancellationToken);

                        foreach (var claim in providerClaims)
                        {
                            claims[claim.Key] = claim.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Feature provider {Provider} failed for tenant {TenantId}",
                            provider.GetType().Name, tenantId);
                    }
                }
            }

            return ClaimsProviderResult.Succeeded(claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get license claims");
            return ClaimsProviderResult.Succeeded(claims);
        }
    }
}

/// <summary>
/// Claim types for license information
/// </summary>
public static class LicenseClaimTypes
{
    /// <summary>License tier (community, professional, enterprise)</summary>
    public const string Tier = "license_tier";

    /// <summary>License expiration as Unix timestamp</summary>
    public const string ExpiresAt = "license_expires";

    /// <summary>Array of licensed feature keys</summary>
    public const string Features = "licensed_features";

    /// <summary>Tenant ID for multi-tenant scenarios</summary>
    public const string TenantId = "tenant_id";
}
