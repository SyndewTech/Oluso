using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Licensing;

namespace Oluso.Licensing;

/// <summary>
/// Extension methods for registering Oluso licensing
/// </summary>
public static class OlusoLicenseExtensions
{
    /// <summary>
    /// Adds the Oluso licensing system to the service collection
    /// </summary>
    public static IServiceCollection AddOlusoLicensing(
        this IServiceCollection services,
        Action<OlusoLicenseOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<ILicenseValidator, OlusoLicenseValidator>();

        return services;
    }

    /// <summary>
    /// Adds licensing with a license key
    /// </summary>
    public static IServiceCollection AddOlusoLicensing(
        this IServiceCollection services,
        string licenseKey)
    {
        return services.AddOlusoLicensing(options =>
        {
            options.LicenseKey = licenseKey;
        });
    }

    /// <summary>
    /// Adds community licensing (for companies under revenue threshold)
    /// </summary>
    public static IServiceCollection AddOlusoCommunityLicense(
        this IServiceCollection services,
        string? companyName = null,
        decimal? declaredRevenue = null)
    {
        return services.AddOlusoLicensing(options =>
        {
            options.CompanyName = companyName;
            options.DeclaredAnnualRevenue = declaredRevenue;
            options.ValidateSignature = false; // Community doesn't have a signed license
        });
    }

    /// <summary>
    /// Adds development licensing (all features enabled, no validation)
    /// </summary>
    public static IServiceCollection AddOlusoDevelopmentLicense(
        this IServiceCollection services)
    {
        return services.AddOlusoLicensing(options =>
        {
            options.ValidateSignature = false;
            // A development license JWT would be provided that has tier=Development
        });
    }

    /// <summary>
    /// Validates that a feature is licensed, throws if not
    /// </summary>
    public static void RequireLicensedFeature(
        this IServiceProvider services,
        string feature)
    {
        var validator = services.GetService<ILicenseValidator>();
        if (validator == null)
        {
            return; // No licensing configured, allow
        }

        var result = validator.ValidateFeature(feature);
        if (!result.IsValid)
        {
            throw new LicenseException(result.Message ?? $"Feature '{feature}' is not licensed");
        }
    }

    /// <summary>
    /// Checks if a feature is licensed (non-throwing version)
    /// </summary>
    public static bool IsFeatureLicensed(
        this IServiceProvider services,
        string feature)
    {
        var validator = services.GetService<ILicenseValidator>();
        if (validator == null)
        {
            return true; // No licensing configured, allow
        }

        return validator.ValidateFeature(feature).IsValid;
    }
}

/// <summary>
/// Attribute to mark features that require licensing
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiresLicenseAttribute : Attribute
{
    public string Feature { get; }
    public LicenseTier? MinimumTier { get; }

    public RequiresLicenseAttribute(string feature)
    {
        Feature = feature;
    }

    public RequiresLicenseAttribute(LicenseTier minimumTier)
    {
        MinimumTier = minimumTier;
        Feature = minimumTier.ToString().ToLower();
    }
}
