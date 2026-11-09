using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.EntityFramework;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Verifies that the global tenant query filter on TenantEntity types is
/// correctly isolated across DbContext instances and DI scopes.
///
/// Regression guard: EF caches one IModel per DbContext type. When a global
/// query filter bakes _tenantId via Expression.Constant(_tenantId), the value
/// captured by the FIRST DbContext built (often null, from a design-time or
/// pre-tenant-resolution instance) gets frozen for every subsequent instance —
/// silently breaking isolation. The fix is OlusoModelCacheKeyFactory, which
/// keys the model cache by tenant. This test asserts the end-state invariant.
/// </summary>
public class TenantQueryFilterIsolationTests : IntegrationTestBase
{
    public TenantQueryFilterIsolationTests(OlusoWebApplicationFactory factory) : base(factory) { }

    /// <summary>
    /// Two DbContext instances in two separate scopes, each with a different
    /// tenant context, must each see only their own tenant's clients — never
    /// the other tenant's, regardless of which scope queries first.
    /// </summary>
    [Theory]
    [InlineData("default", PlatformConstants.TenantId)]
    [InlineData(PlatformConstants.TenantId, "default")]
    public async Task Clients_query_isolated_per_tenant_across_scopes(string firstTenantId, string secondTenantId)
    {
        var first = await QueryClientsForTenantAsync(firstTenantId);
        var second = await QueryClientsForTenantAsync(secondTenantId);

        first.Should().NotBeEmpty($"the seed creates clients in tenant '{firstTenantId}'");
        second.Should().NotBeEmpty($"the seed creates clients in tenant '{secondTenantId}'");

        first.Should().OnlyContain(c => c.TenantId == firstTenantId,
            $"DbContext scoped to '{firstTenantId}' must not leak clients from any other tenant");
        second.Should().OnlyContain(c => c.TenantId == secondTenantId,
            $"DbContext scoped to '{secondTenantId}' must not leak clients from any other tenant " +
            "— specifically, the previous query for '{firstTenantId}' must not poison the model cache");
    }

    /// <summary>
    /// Tightens the regression guard: a DbContext constructed with no tenant
    /// context (the design-time / migrations path) must not poison the model
    /// cache for a subsequent DbContext that has a real tenant.
    /// </summary>
    [Fact]
    public async Task Null_tenant_context_does_not_poison_subsequent_tenant_scoped_queries()
    {
        // First: a query with no tenant context. With the legacy "_tenantId == null"
        // branch, this returns everything — that's allowed for admin/login paths.
        // The contract is that this must NOT change what a later tenant-scoped
        // DbContext sees.
        await QueryClientsWithNoTenantAsync();

        var defaultClients = await QueryClientsForTenantAsync("default");

        defaultClients.Should().NotBeEmpty();
        defaultClients.Should().OnlyContain(c => c.TenantId == "default",
            "a tenant-scoped DbContext must always filter to its tenant, even if a " +
            "preceding null-tenant query already populated the model cache");
    }

    private async Task<List<Client>> QueryClientsForTenantAsync(string tenantId)
    {
        using var scope = Factory.Services.CreateScope();

        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenant = await tenantStore.GetByIdAsync(tenantId);
        Assert.NotNull(tenant);

        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        accessor.SetTenant(tenant);

        var db = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
        return await db.Clients.AsNoTracking().ToListAsync();
    }

    private async Task<List<Client>> QueryClientsWithNoTenantAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
        return await db.Clients.AsNoTracking().ToListAsync();
    }
}
