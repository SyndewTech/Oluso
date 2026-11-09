using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Gives every journey policy that predates slugs a slug equal to its ID.
/// <para>
/// Clients used to name a policy by its ID (<c>?policy=</c>, <c>/journey/start/{id}</c>,
/// the public journey API). They now name it by slug, so existing links keep working only
/// if each existing policy's slug is its old ID. IDs are unique, so these slugs are too.
/// </para>
/// </summary>
[DataMigration(typeof(OlusoDbContext))]
public class BackfillJourneyPolicySlugs : DataMigrationBase
{
    public override string MigrationId => "20261005_BackfillJourneyPolicySlugs";

    public override string Description => "Set each existing journey policy's slug to its ID";

    public override async Task UpAsync(
        DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<BackfillJourneyPolicySlugs>();

        // IgnoreQueryFilters: JourneyPolicyEntity is a TenantEntity, and this must reach
        // every tenant's policies rather than whatever tenant context happens to be set.
        var updated = await context.Set<JourneyPolicyEntity>()
            .IgnoreQueryFilters()
            .Where(p => p.Slug == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Slug, p => p.Id), cancellationToken);

        logger?.LogInformation("Set the slug of {Count} existing journey policy(ies) to their ID", updated);
    }

    // No Down: the slug column goes with the schema migration's Down.
}
