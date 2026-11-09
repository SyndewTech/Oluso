using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Locale;
using static Oluso.EntityFramework.DataMigrations.DataMigrationSqlHelpers;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Moves amounts stored in hundredths of their currency, whatever the currency, to that currency's own
/// ISO 4217 minor units: ×10 for three-decimal currencies (KWD, BHD…), ÷100 for zero-decimal ones (JPY, KRW…).
/// Two-decimal currencies are unchanged. Everything runs in one transaction, and nothing is rounded: an amount
/// in a zero-decimal currency that isn't a whole number of units, or a non-zero amount whose currency isn't a
/// supported code, stops the migration with every such tenant, table and id named, before anything changes.
/// </summary>
public static class MinorUnitsRescale
{
    private const int ListedIdLimit = 20;

    /// <summary>Amount columns of one table that share a currency, given as a SQL expression evaluated per row.</summary>
    public sealed record Group(string Label, string Table, string IdColumn, string? TenantColumn, IReadOnlyList<string> Columns, string CurrencySql);

    /// <summary>Amounts in the row's own currency column.</summary>
    public static Group OwnCurrency<T>(DbContext context, string currencyProperty, params string[] amountProperties) where T : class =>
        Build<T>(context, $"UPPER({QualifiedTable<T>(context)}.{Column<T>(context, currencyProperty)})", amountProperties);

    /// <summary>Amounts in the currency of the parent row the foreign key points at (lines, applications).</summary>
    public static Group ParentCurrency<T, TParent>(DbContext context, string foreignKeyProperty, string parentCurrencyProperty, params string[] amountProperties)
        where T : class where TParent : class =>
        Build<T>(context,
            $"(SELECT MAX(UPPER(p.{Column<TParent>(context, parentCurrencyProperty)})) FROM {QualifiedTable<TParent>(context)} p " +
            $"WHERE p.{Column<TParent>(context, "Id")} = {QualifiedTable<T>(context)}.{Column<T>(context, foreignKeyProperty)})",
            amountProperties);

    /// <summary>Amounts in a currency given by an arbitrary SQL expression over the row (a settings lookup, for example).</summary>
    public static Group WithCurrency<T>(DbContext context, string currencySql, params string[] amountProperties) where T : class =>
        Build<T>(context, currencySql, amountProperties);

    /// <summary>
    /// Amounts in their tenant's currency, from <paramref name="tenantCurrencies"/> (see <see cref="TenantCurrenciesAsync"/>).
    /// A row of a tenant missing from it has no currency, so it is refused when it has a non-zero amount.
    /// </summary>
    public static Group TenantCurrency<T>(DbContext context, IReadOnlyDictionary<string, string> tenantCurrencies, params string[] amountProperties)
        where T : class
    {
        if (tenantCurrencies.Count == 0) return Build<T>(context, "NULL", amountProperties);
        var tenant = $"{QualifiedTable<T>(context)}.{Column<T>(context, "TenantId")}";
        var whens = string.Join(" ", tenantCurrencies.Select(t => $"WHEN '{t.Key.Replace("'", "''")}' THEN '{t.Value.Replace("'", "''")}'"));
        return Build<T>(context, $"(CASE {tenant} {whens} END)", amountProperties);
    }

    /// <summary>
    /// Each listed tenant's currency from its locale settings, upper-cased; tenants that haven't set one are left out.
    /// Empty when the host has no tenant configuration service.
    /// </summary>
    public static async Task<Dictionary<string, string>> TenantCurrenciesAsync(
        IServiceProvider? services, IEnumerable<string?> tenantIds, CancellationToken cancellationToken = default)
    {
        var currencies = new Dictionary<string, string>();
        using var scope = services?.CreateScope();
        var tenantConfig = scope?.ServiceProvider.GetService<ITenantConfigurationService>();
        if (tenantConfig == null) return currencies;

        foreach (var tenantId in tenantIds.OfType<string>().Distinct())
        {
            var currency = await tenantConfig.FindCurrencyAsync(tenantId, cancellationToken);
            if (currency != null) currencies[tenantId] = currency;
        }
        return currencies;
    }

    public static async Task RunAsync(DbContext context, IEnumerable<Group> groups, CancellationToken cancellationToken = default)
    {
        var groupList = groups.ToList();
        await using var transaction = context.Database.CurrentTransaction == null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var supported = string.Join(", ", SupportedCurrencies.All.Select(c => $"'{c.Code.ToUpperInvariant()}'").OrderBy(c => c, StringComparer.Ordinal));
        var byExponent = SupportedCurrencies.All
            .Where(c => c.DecimalPlaces != 2)
            .GroupBy(c => c.DecimalPlaces)
            .Select(g => (Exponent: g.Key, Codes: string.Join(", ", g.Select(c => $"'{c.Code.ToUpperInvariant()}'").OrderBy(c => c, StringComparer.Ordinal))))
            .ToList();

        var refused = new List<(string? Tenant, string Table, string Id, string Why)>();
        foreach (var group in groupList)
        {
            var nonZero = string.Join(" OR ", group.Columns.Select(c => $"COALESCE({c}, 0) <> 0"));
            foreach (var row in await RowsAsync(context, group, $"({group.CurrencySql} IS NULL OR {group.CurrencySql} NOT IN ({supported})) AND ({nonZero})", cancellationToken))
                refused.Add((row.TenantId, group.Label, row.Id, "no supported currency"));

            foreach (var (exponent, codes) in byExponent.Where(e => e.Exponent < 2))
            {
                var divisor = Pow10(2 - exponent);
                var remainder = string.Join(" OR ", group.Columns.Select(c => $"COALESCE({c}, 0) % {divisor} <> 0"));
                foreach (var row in await RowsAsync(context, group, $"{group.CurrencySql} IN ({codes}) AND ({remainder})", cancellationToken))
                    refused.Add((row.TenantId, group.Label, row.Id, "not a whole number of units"));
            }
        }
        if (refused.Count > 0)
            throw new InvalidOperationException(RefusalMessage(refused));

        foreach (var group in groupList)
        {
            foreach (var (exponent, codes) in byExponent)
            {
                var op = exponent > 2 ? $"* {Pow10(exponent - 2)}" : $"/ {Pow10(2 - exponent)}";
                await context.Database.ExecuteSqlRawAsync(
                    $"UPDATE {group.Table} SET {string.Join(", ", group.Columns.Select(c => $"{c} = {c} {op}"))} WHERE {group.CurrencySql} IN ({codes})",
                    cancellationToken);
            }
        }

        if (transaction != null) await transaction.CommitAsync(cancellationToken);
    }

    private sealed class RowRef
    {
        public string? TenantId { get; set; }
        public string Id { get; set; } = string.Empty;
    }

    private static Task<List<RowRef>> RowsAsync(DbContext context, Group group, string where, CancellationToken cancellationToken)
    {
        var tenant = group.TenantColumn == null ? "NULL" : $"{group.Table}.{group.TenantColumn}";
        return context.Database
            .SqlQueryRaw<RowRef>(
                $"SELECT {tenant} AS {Quote(context, nameof(RowRef.TenantId))}, {group.Table}.{group.IdColumn} AS {Quote(context, nameof(RowRef.Id))} " +
                $"FROM {group.Table} WHERE {where}")
            .ToListAsync(cancellationToken);
    }

    private static string RefusalMessage(IReadOnlyList<(string? Tenant, string Table, string Id, string Why)> refused) =>
        "Amounts could not be moved to their currency's minor units, so nothing was changed. Correct these rows and restart: " +
        string.Join("; ", refused
            .GroupBy(r => (r.Tenant, r.Table, r.Why))
            .OrderBy(g => g.Key.Tenant, StringComparer.Ordinal).ThenBy(g => g.Key.Table, StringComparer.Ordinal)
            .Select(g => $"tenant {g.Key.Tenant ?? "(none)"}, {g.Key.Table} ({g.Key.Why}): " +
                string.Join(", ", g.Select(r => r.Id).Take(ListedIdLimit)) + (g.Count() > ListedIdLimit ? $" and {g.Count() - ListedIdLimit} more" : "")));

    private static Group Build<T>(DbContext context, string currencySql, string[] amountProperties) where T : class
    {
        var entity = context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} not found in the model");
        var tenant = entity.FindProperty("TenantId") == null ? null : Column<T>(context, "TenantId");
        return new Group(typeof(T).Name, QualifiedTable<T>(context), Column<T>(context, "Id"), tenant,
            amountProperties.Select(p => Column<T>(context, p)).ToList(), currencySql);
    }

    private static long Pow10(int exponent)
    {
        var value = 1L;
        for (var i = 0; i < exponent; i++) value *= 10;
        return value;
    }
}
