using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Locale;

/// <summary>
/// Raised when something needs the organisation's currency and the tenant hasn't set one.
/// The message is safe to show to the user.
/// </summary>
public sealed class TenantCurrencyNotSetException : InvalidOperationException
{
    public TenantCurrencyNotSetException()
        : base("Set your organisation's currency in Settings → Locale first.") { }
}

public static class TenantCurrencyExtensions
{
    /// <summary>
    /// The tenant's currency from its locale settings, upper-cased. There is no fallback:
    /// throws <see cref="TenantCurrencyNotSetException"/> when the tenant hasn't set one.
    /// </summary>
    public static async Task<string> GetCurrencyAsync(this ITenantSettingsProvider settings, CancellationToken cancellationToken = default)
    {
        var locale = await settings.GetLocaleSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(locale.CurrencyCode))
            throw new TenantCurrencyNotSetException();
        return locale.CurrencyCode.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// The tenant's currency (upper-cased), or null when it hasn't set one. For labelling amounts that are already
    /// in the tenant's currency (an event payload, a report); never for choosing the currency of a new amount.
    /// </summary>
    public static async Task<string?> FindCurrencyAsync(this ITenantSettingsProvider settings, CancellationToken cancellationToken = default) =>
        Normalise((await settings.GetLocaleSettingsAsync(cancellationToken)).CurrencyCode);

    /// <summary>
    /// A named tenant's currency (upper-cased), or null when it hasn't set one; for background work that runs
    /// outside a request. The same use as the ambient-tenant overload.
    /// </summary>
    public static async Task<string?> FindCurrencyAsync(this ITenantConfigurationService config, string tenantId, CancellationToken cancellationToken = default) =>
        Normalise((await config.GetSectionAsync<LocaleSettings>(tenantId, "Locale", cancellationToken))?.CurrencyCode);

    private static string? Normalise(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    /// <summary>The request's currency when it names one, otherwise the tenant's.</summary>
    public static async Task<string> ResolveCurrencyAsync(this ITenantSettingsProvider settings, string? requested, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(requested)
            ? await settings.GetCurrencyAsync(cancellationToken)
            : requested.Trim().ToUpperInvariant();
}

/// <summary>Returns <see cref="TenantCurrencyNotSetException"/> as a 400 with its message.</summary>
public sealed class TenantCurrencyNotSetExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not TenantCurrencyNotSetException ex) return;
        context.Result = new BadRequestObjectResult(new { Message = ex.Message });
        context.ExceptionHandled = true;
    }
}
