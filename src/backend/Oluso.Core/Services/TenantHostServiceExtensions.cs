using DnsClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

public static class TenantHostServiceExtensions
{
    /// <summary>
    /// Registers the tenant URL and DNS services every host needs, web or headless: the issuer resolver
    /// (tenant-routable public URLs) and custom-domain DNS verification. Functions hosts get them through
    /// <c>AddOlusoIdentityCoreForFunctions</c>, so module job runners that depend on them resolve there too.
    /// </summary>
    public static IServiceCollection AddOlusoTenantHostServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IIssuerResolver, IssuerResolver>();
        services.TryAddScoped<ITenantDiscoveryService, TenantDiscoveryService>();
        services.TryAddSingleton<ILookupClient>(_ => new LookupClient());
        services.TryAddScoped<IDnsVerificationService, DnsVerificationService>();
        return services;
    }
}
