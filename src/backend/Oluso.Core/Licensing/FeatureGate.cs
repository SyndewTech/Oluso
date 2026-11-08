using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Licensing;

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
/// Authorization filter that checks if a feature is enabled for the current tenant.
/// Returns 403 Forbidden if the feature is not available.
/// Use on controllers or actions: [RequireFeature("<module>")]
/// </summary>
/// <remarks>
/// With <see cref="RequireSeat"/>, the signed-in user must also hold a seat in the module
/// (a <c>UserModuleAccess</c> row), the same rule the workspace app switcher applies. A
/// tenant's <c>MaxSeats</c> caps how many seats are handed out; it doesn't decide whether
/// one is needed. Workspace surfaces set it; admin and customer-facing account surfaces
/// leave it off, since admins and customers don't hold seats.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireFeatureAttribute : Microsoft.AspNetCore.Mvc.TypeFilterAttribute
{
    private readonly string _featureKey;
    private bool _requireSeat;

    public RequireFeatureAttribute(string featureKey)
        : base(typeof(RequireFeatureFilter))
    {
        _featureKey = featureKey;
        Arguments = new object[] { featureKey, false };
    }

    /// <summary>Also require the signed-in user to hold a seat in the module.</summary>
    public bool RequireSeat
    {
        get => _requireSeat;
        set
        {
            _requireSeat = value;
            Arguments = new object[] { _featureKey, value };
        }
    }
}

internal class RequireFeatureFilter : Microsoft.AspNetCore.Mvc.Filters.IAsyncAuthorizationFilter
{
    /// <summary>
    /// The signed-in user holds a seat in the module within the request's tenant. No user,
    /// no tenant or no seat store means no seat.
    /// </summary>
    private async Task<bool> HasSeatAsync(Microsoft.AspNetCore.Http.HttpContext http, CancellationToken cancellationToken)
    {
        var userId = http.User.FindFirst("sub")?.Value;
        var tenantId = http.RequestServices.GetService<ITenantContext>()?.TenantId;
        var seats = http.RequestServices.GetService<IUserModuleAccessStore>();
        return !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(tenantId) && seats != null
            && await seats.HasAccessAsync(tenantId, userId, _featureKey, cancellationToken);
    }

    private readonly string _featureKey;
    private readonly bool _requireSeat;
    private readonly IFeatureGate _featureGate;

    public RequireFeatureFilter(string featureKey, bool requireSeat, IFeatureGate featureGate)
    {
        _featureKey = featureKey;
        _requireSeat = requireSeat;
        _featureGate = featureGate;
    }

    public async Task OnAuthorizationAsync(Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext context)
    {
        var cancellationToken = context.HttpContext.RequestAborted;
        var result = await _featureGate.CheckFeatureAsync(_featureKey, cancellationToken: cancellationToken);
        if (result.IsAllowed && _requireSeat && !await HasSeatAsync(context.HttpContext, cancellationToken))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(new
            {
                Error = "NoModuleSeat",
                Message = $"You don't have a seat in '{_featureKey}'. Ask an administrator to assign you one.",
            })
            {
                StatusCode = 403,
            };
            return;
        }

        if (!result.IsAllowed)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(new
            {
                Error = "FeatureNotAvailable",
                Message = result.Message ?? $"The '{_featureKey}' feature is not available for this tenant.",
                Reason = result.Reason?.ToString(),
            })
            {
                StatusCode = 403,
            };
        }
    }
}

/// <summary>
/// Extension methods for registering feature gating
/// </summary>
public static class FeatureGateExtensions
{
    /// <summary>
    /// Adds feature gate services along with the standard org-level and
    /// tenant-level entitlement providers. Both providers are registered via
    /// TryAddEnumerable, so duplicate calls (e.g. an explicit
    /// AddOrganizationFeatureProvider) are idempotent. This makes feature
    /// gating "just work" for any consumer of AddOluso() without needing to
    /// remember the two extra calls.
    /// </summary>
    public static IServiceCollection AddFeatureGate(this IServiceCollection services)
    {
        services.AddScoped<IFeatureGate, FeatureGate>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureAvailabilityProvider, OrganizationFeatureProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureAvailabilityProvider, TenantFeatureProvider>());
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

    /// <summary>
    /// Adds the organization-level feature entitlement provider.
    /// This checks OrganizationFeature records to control which modules each org can access.
    /// </summary>
    public static IServiceCollection AddOrganizationFeatureProvider(this IServiceCollection services)
    {
        services.AddScoped<IFeatureAvailabilityProvider, OrganizationFeatureProvider>();
        return services;
    }

    /// <summary>
    /// Adds the tenant-level feature entitlement provider.
    /// This checks TenantFeature records to control which modules each tenant can access,
    /// within the bounds of what the parent organization allows.
    /// Priority 125: between organization features (150) and billing (100).
    /// </summary>
    public static IServiceCollection AddTenantFeatureProvider(this IServiceCollection services)
    {
        services.AddScoped<IFeatureAvailabilityProvider, TenantFeatureProvider>();
        return services;
    }

    /// <summary>
    /// Adds the per-user module seat access provider.
    /// When a tenant has MaxSeats configured for a feature, only users with a UserModuleAccess
    /// row can access that module. When MaxSeats is null, all tenant users have access.
    /// Priority 110: between tenant features (125) and billing (100).
    /// </summary>
    public static IServiceCollection AddUserModuleAccessFeatureProvider(this IServiceCollection services)
    {
        services.AddScoped<IFeatureAvailabilityProvider, UserModuleAccessFeatureProvider>();
        return services;
    }
}
