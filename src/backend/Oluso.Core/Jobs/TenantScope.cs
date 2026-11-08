using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Jobs;

/// <summary>
/// Runs work in a fresh DI scope with the tenant context set to <paramref name="tenantId"/>,
/// for jobs that process several tenants' records in one pass: tenant-aware services
/// (settings, AI, stores) then resolve for that record's tenant.
/// </summary>
public static class TenantScope
{
    public static async Task RunAsync(
        IServiceScopeFactory scopeFactory,
        string tenantId,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var accessor = scope.ServiceProvider.GetService<ITenantContextAccessor>();
        if (accessor != null)
        {
            var tenant = await scope.ServiceProvider.GetRequiredService<ITenantStore>().GetByIdAsync(tenantId, cancellationToken)
                ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found");
            accessor.SetTenant(tenant);
        }

        try
        {
            await work(scope.ServiceProvider, cancellationToken);
        }
        finally
        {
            accessor?.ClearTenant();
        }
    }
}
