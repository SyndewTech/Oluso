using System.Linq.Expressions;

namespace Oluso.Core.Locale;

/// <summary>
/// A range of amounts given in major units (1,000.50), applied to rows that hold minor units of their own
/// currency: 1,000 matches ¥1,000 and £1,000.00 alike. Builds one comparison per currency exponent, so the
/// filter translates to SQL.
/// </summary>
public static class MinorUnitsRange
{
    /// <summary>
    /// A filter keeping rows whose amount lies within <paramref name="min"/>..<paramref name="max"/> (inclusive) in the
    /// row's own currency, or null when neither bound is set. A row whose currency isn't supported never matches.
    /// </summary>
    public static Expression<Func<T, bool>>? Filter<T>(
        Expression<Func<T, string>> currency, Expression<Func<T, long>> amountInMinorUnits, decimal? min, decimal? max)
    {
        if (min == null && max == null) return null;

        var row = Expression.Parameter(typeof(T), "row");
        var code = new Rebind(currency.Parameters[0], row).Visit(currency.Body);
        var amount = new Rebind(amountInMinorUnits.Parameters[0], row).Visit(amountInMinorUnits.Body);

        Expression? any = null;
        foreach (var group in SupportedCurrencies.All.GroupBy(c => c.DecimalPlaces))
        {
            var codes = group.Select(c => c.Code.ToUpperInvariant()).ToList();
            var scale = 1m;
            for (var i = 0; i < group.Key; i++) scale *= 10m;

            Expression inRange = Expression.Call(Expression.Constant(codes), typeof(List<string>).GetMethod(nameof(List<string>.Contains))!, code);
            if (min is { } lower)
                inRange = Expression.AndAlso(inRange, Expression.GreaterThanOrEqual(amount, Expression.Constant(Clamp(Math.Ceiling(lower * scale)))));
            if (max is { } upper)
                inRange = Expression.AndAlso(inRange, Expression.LessThanOrEqual(amount, Expression.Constant(Clamp(Math.Floor(upper * scale)))));
            any = any == null ? inRange : Expression.OrElse(any, inRange);
        }

        return Expression.Lambda<Func<T, bool>>(any ?? Expression.Constant(false), row);
    }

    private static long Clamp(decimal minorUnits) => (long)Math.Clamp(minorUnits, long.MinValue, long.MaxValue);

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
