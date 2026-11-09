using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Preserves ID-token contents for clients that predate
/// <c>AlwaysIncludeUserClaimsInIdToken</c> being honoured.
/// <para>
/// The flag existed on the client and was editable, but nothing read it — the ID
/// token always carried the identity claims released by the granted scopes. Now
/// that <c>TokenService.CreateIdTokenAsync</c> respects it (OIDC Core §5.4: with
/// an access token issued, those claims come from /userinfo unless the client
/// opts in), leaving existing rows at the <c>false</c> default would silently
/// drop claims their applications already read.
/// </para>
/// <para>
/// Only pre-existing clients are affected. New clients keep the <c>false</c>
/// default, which is the spec behaviour.
/// </para>
/// </summary>
[DataMigration(typeof(OlusoDbContext))]
public class BackfillAlwaysIncludeUserClaimsInIdToken : DataMigrationBase
{
    public override string MigrationId => "20260809_BackfillAlwaysIncludeUserClaimsInIdToken";

    public override string Description =>
        "Opt existing clients into user claims in the ID token, preserving pre-flag behaviour";

    public override async Task UpAsync(
        DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetService<ILoggerFactory>()
            ?.CreateLogger<BackfillAlwaysIncludeUserClaimsInIdToken>();

        // IgnoreQueryFilters: Client is a TenantEntity, and this must reach every
        // tenant's clients rather than whatever tenant context happens to be set.
        var updated = await context.Set<Client>()
            .IgnoreQueryFilters()
            .Where(c => !c.AlwaysIncludeUserClaimsInIdToken)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.AlwaysIncludeUserClaimsInIdToken, true),
                cancellationToken);

        logger?.LogInformation(
            "Opted {Count} existing client(s) into user claims in the ID token", updated);
    }

    // No Down: the original per-client values are not recoverable, and reverting
    // every client to false would drop claims from tokens rather than restore a
    // prior state. Turning it off is a deliberate per-client change.
}
