using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Oluso.Core.ActionCenter;

public static class ActionCenterExtensions
{
    /// <summary>
    /// Registers the action center aggregation service.
    /// Call this once in your application startup.
    /// </summary>
    public static IServiceCollection AddActionCenter(this IServiceCollection services)
    {
        services.TryAddScoped<IActionCenterService, ActionCenterService>();
        return services;
    }

    /// <summary>
    /// Registers a module-specific action center provider.
    /// Each module calls this to contribute items to the unified inbox.
    /// </summary>
    public static IServiceCollection AddActionCenterProvider<T>(this IServiceCollection services)
        where T : class, IActionCenterProvider
    {
        services.AddScoped<IActionCenterProvider, T>();
        return services;
    }
}
