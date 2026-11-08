using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// The admin API no longer raises its own copies of the user events
/// (<c>admin.user_created</c> and the rest); it raises the core events with the
/// admin as origin. Webhook endpoints subscribed to an admin copy are moved to
/// the core event, so they keep receiving admin changes. An endpoint already
/// subscribed to the core event just loses the old row.
/// </summary>
[DataMigration(typeof(OlusoDbContext))]
public class RemapAdminUserWebhookSubscriptions : DataMigrationBase
{
    public static readonly IReadOnlyDictionary<string, string> Remap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["admin.user_created"] = "user.created",
        ["admin.user_updated"] = "user.updated",
        ["admin.user_deleted"] = "user.deleted",
        ["admin.user_role_assigned"] = "user.role_assigned",
        ["admin.user_role_removed"] = "user.role_removed",
        ["admin.password_reset"] = "user.password_changed",
    };

    public override string MigrationId => "20261009_RemapAdminUserWebhookSubscriptions";

    public override string Description =>
        "Move webhook subscriptions from the removed admin.user_* events to the core user events";

    public override async Task UpAsync(
        DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger<RemapAdminUserWebhookSubscriptions>();
        var subscriptions = context.Set<WebhookEventSubscriptionEntity>().IgnoreQueryFilters();

        foreach (var (oldType, newType) in Remap)
        {
            var duplicates = await subscriptions
                .Where(s => s.EventType == oldType
                    && subscriptions.Any(o => o.EndpointId == s.EndpointId && o.EventType == newType))
                .ExecuteDeleteAsync(cancellationToken);

            var moved = await subscriptions
                .Where(s => s.EventType == oldType)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.EventType, newType), cancellationToken);

            if (moved + duplicates > 0)
            {
                logger?.LogInformation(
                    "Webhook subscriptions to {OldType}: {Moved} moved to {NewType}, {Duplicates} dropped as already subscribed",
                    oldType, moved, newType, duplicates);
            }
        }
    }
}
