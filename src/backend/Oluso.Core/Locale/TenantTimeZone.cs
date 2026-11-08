using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Locale;

/// <summary>
/// Raised when something needs the organisation's time zone and the tenant hasn't set one.
/// The message is safe to show to the user.
/// </summary>
public sealed class TenantTimeZoneNotSetException : InvalidOperationException
{
    public TenantTimeZoneNotSetException()
        : base("Set your organisation's time zone in Settings → Locale first.") { }
}

public static class TenantTimeZoneExtensions
{
    /// <summary>
    /// The tenant's time zone from its locale settings. There is no fallback: throws
    /// <see cref="TenantTimeZoneNotSetException"/> when the tenant hasn't set one.
    /// </summary>
    public static async Task<TimeZoneInfo> GetTimeZoneAsync(this ITenantSettingsProvider settings, CancellationToken cancellationToken = default) =>
        Resolve((await settings.GetLocaleSettingsAsync(cancellationToken)).Timezone)
        ?? throw new TenantTimeZoneNotSetException();

    /// <summary>The tenant's time zone, or null when it hasn't set one.</summary>
    public static async Task<TimeZoneInfo?> FindTimeZoneAsync(this ITenantSettingsProvider settings, CancellationToken cancellationToken = default) =>
        Resolve((await settings.GetLocaleSettingsAsync(cancellationToken)).Timezone);

    /// <summary>
    /// A named tenant's time zone, or null when it hasn't set one; for background work
    /// that runs outside a request.
    /// </summary>
    public static async Task<TimeZoneInfo?> FindTimeZoneAsync(this ITenantConfigurationService config, string tenantId, CancellationToken cancellationToken = default) =>
        Resolve((await config.GetSectionAsync<LocaleSettings>(tenantId, "Locale", cancellationToken))?.Timezone);

    /// <summary>
    /// Converts a local date and time in <paramref name="zone"/> to UTC. A local time
    /// skipped by a daylight-saving change moves to the first valid minute after the gap.
    /// </summary>
    public static DateTime ToUtc(this TimeZoneInfo zone, DateOnly date, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>Today's date in <paramref name="zone"/>.</summary>
    public static DateOnly Today(this TimeZoneInfo zone, DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone));

    private static TimeZoneInfo? Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone)) return zone;
        throw new InvalidOperationException($"The organisation's time zone '{id}' is not a known time zone.");
    }
}
