using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Functions.Common;

/// <summary>
/// Helper for queue-triggered Function workers that need to run a tenant-scoped
/// job. Creates a DI scope, pins <see cref="ITenantContextAccessor"/> to the
/// resolved tenant, invokes the supplied work, and clears the context on exit.
///
/// <para>Mirrors what <c>TenantAwareBackgroundService&lt;TRunner&gt;</c> does
/// on the in-proc host. Without it, Function workers leave
/// <c>CurrentTenantId</c> null — and <c>OlusoDbContext</c>'s global tenant
/// query filter (<c>e =&gt; CurrentTenantId == null || e.TenantId == CurrentTenantId || e.TenantId == null</c>)
/// falls open, passing every tenant's rows through. Stores that filter by
/// <c>tenantId</c> explicitly stay safe, but any query that forgets the
/// clause silently leaks cross-tenant. This helper closes that gap.</para>
///
/// <para>Requires <see cref="ITenantContextAccessor"/> and <see cref="ITenantStore"/>
/// in the container (provided by <c>AddOlusoIdentityCoreForFunctions</c>). If
/// the accessor isn't registered the scope is created and the action runs
/// unchanged — same fail-open behaviour as the dispatcher's eligibility check.</para>
/// </summary>
public static class TenantJobScope
{
    /// <summary>
    /// Runs <paramref name="action"/> inside a fresh DI scope with the tenant
    /// context pinned. Tenant lookup goes through <see cref="ITenantStore"/>;
    /// an unknown <paramref name="tenantId"/> leaves the context unset and
    /// lets the action decide how to handle it (the runner will still get the
    /// id as a parameter).
    /// </summary>
    public static async Task RunAsync(
        IServiceProvider serviceProvider,
        string tenantId,
        Func<IServiceProvider, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var accessor = sp.GetService<ITenantContextAccessor>();
        if (accessor != null)
        {
            var tenantStore = sp.GetService<ITenantStore>();
            if (tenantStore != null)
            {
                var tenant = await tenantStore.GetByIdAsync(tenantId, cancellationToken);
                if (tenant != null) accessor.SetTenant(tenant);
            }
        }

        try
        {
            await action(sp, cancellationToken);
        }
        finally
        {
            accessor?.ClearTenant();
        }
    }
}
