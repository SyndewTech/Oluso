using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Admin.Search.Providers;

namespace Oluso.Admin.Search;

public static class AdminSearchExtensions
{
    /// <summary>Registers the aggregator and the core record providers. Called by AddOlusoAdmin.</summary>
    public static IServiceCollection AddAdminSearch(this IServiceCollection services)
    {
        services.TryAddScoped<IAdminSearchService, AdminSearchService>();
        services.AddAdminSearchProvider<UsersSearchProvider>();
        services.AddAdminSearchProvider<ClientsSearchProvider>();
        services.AddAdminSearchProvider<TenantsSearchProvider>();
        services.AddAdminSearchProvider<OrganizationsSearchProvider>();
        services.AddAdminSearchProvider<TeamsSearchProvider>();
        services.AddAdminSearchProvider<RolesSearchProvider>();
        services.AddAdminSearchProvider<JourneysSearchProvider>();
        services.AddAdminSearchProvider<IdentityProvidersSearchProvider>();
        return services;
    }

    /// <summary>Contributes one searchable record kind; modules call this from their descriptor.</summary>
    public static IServiceCollection AddAdminSearchProvider<T>(this IServiceCollection services)
        where T : class, IAdminSearchProvider
    {
        services.AddScoped<IAdminSearchProvider, T>();
        return services;
    }
}
