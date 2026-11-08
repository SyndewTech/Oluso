using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Cloudflare;

public static class CloudflareExtensions
{
    /// <summary>
    /// Registers Cloudflare Custom Hostnames as the edge domain provisioning provider.
    /// Requires configuration:
    ///   Oluso:Cloudflare:ZoneId   — Cloudflare zone ID
    ///   Oluso:Cloudflare:ApiToken — API token with Custom Hostname:Edit permission
    /// </summary>
    public static IServiceCollection AddCloudflareCustomHostnames(this IServiceCollection services)
    {
        services.AddHttpClient("Cloudflare");
        services.AddScoped<ICustomDomainProvisioningService, CloudflareCustomHostnameService>();
        return services;
    }
}
