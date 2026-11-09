using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Oluso.Core.Licensing;

public static class ModuleRegistrationExtensions
{
    /// <summary>
    /// Register a module using its descriptor. Resolves DB config from:
    ///   1. Module-specific: Oluso:Modules:{featureKey}:Database:Provider/ConnectionString
    ///   2. App-level fallback: Oluso:Database:Provider + ConnectionStrings:OlusoDb
    /// </summary>
    public static IServiceCollection AddModule<TDescriptor>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TDescriptor : IModuleDescriptor, new()
    {
        var descriptor = new TDescriptor();
        ModuleRegistry.Instance.Register(descriptor);

        // Apply display name override from configuration (e.g., renaming a module for a tenant)
        var displayNameOverride = configuration[$"Oluso:Modules:{descriptor.FeatureKey}:DisplayName"];
        if (!string.IsNullOrEmpty(displayNameOverride))
            ModuleBrandingRegistry.SetDisplayName(descriptor.FeatureKey, displayNameOverride);

        // Register module services (permissions, stores, webhooks, etc.)
        descriptor.ConfigureServices(services, configuration);

        // Resolve database provider — module-specific override or app-level default
        var moduleDbSection = configuration.GetSection($"Oluso:Modules:{descriptor.FeatureKey}:Database");
        var provider = moduleDbSection["Provider"]
            ?? configuration.GetValue<string>("Oluso:Database:Provider", "Sqlite")!;
        var connectionString = moduleDbSection["ConnectionString"]
            ?? configuration.GetConnectionString("OlusoDb");

        descriptor.ConfigureDatabase(services, provider, connectionString!);

        return services;
    }
}
