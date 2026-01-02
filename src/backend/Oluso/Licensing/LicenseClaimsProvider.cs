using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;

namespace Oluso.Licensing;

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

            return ClaimsProviderResult.Success(claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get license claims");
            return ClaimsProviderResult.Success(claims);
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

/// <summary>
/// Context for claims provision
/// </summary>
public class ClaimsProviderContext
{
    public string? SubjectId { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public IEnumerable<string> Scopes { get; set; } = Array.Empty<string>();
    public string? SessionId { get; set; }
    public string? Caller { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

/// <summary>
/// Result of claims provision
/// </summary>
public class ClaimsProviderResult
{
    public bool Succeeded { get; set; }
    public IDictionary<string, object> Claims { get; set; } = new Dictionary<string, object>();
    public string? Error { get; set; }

    public static ClaimsProviderResult Success(IDictionary<string, object> claims) =>
        new() { Succeeded = true, Claims = claims };

    public static ClaimsProviderResult Failure(string error) =>
        new() { Succeeded = false, Error = error };
}
