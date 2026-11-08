using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Notifications;

/// <summary>
/// EF-backed default gate. Cheap to call (one keyed read per check) — the
/// row is small + indexed by (TenantId, UserId), and absence short-circuits.
///
/// <para>Suppression rules, in order:
/// <list type="number">
///   <item><see cref="UserNotificationProfile.SnoozeUntilUtc"/> in the future.</item>
///   <item>Event type matches an entry in <see cref="UserNotificationProfile.MutedEventTypes"/>
///         — exact match OR wildcard like <c>approval.*</c>.</item>
///   <item>"Now" falls inside the quiet-hours window (interpreted in
///         <see cref="UserNotificationProfile.TimeZoneId"/> or UTC).</item>
/// </list>
/// </para>
/// </summary>
public class DefaultUserNotificationGate : IUserNotificationGate
{
    private readonly IUserNotificationProfileStore _store;

    public DefaultUserNotificationGate(IUserNotificationProfileStore store)
    {
        _store = store;
    }

    public async Task<bool> ShouldSuppressAsync(
        string tenantId,
        string userId,
        string eventType,
        DateTime? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantId))
            return false;

        var profile = await _store.GetAsync(tenantId, userId, cancellationToken);
        if (profile == null) return false;

        var now = nowUtc ?? DateTime.UtcNow;

        if (profile.SnoozeUntilUtc.HasValue && profile.SnoozeUntilUtc.Value > now)
            return true;

        if (MatchesMutedList(profile.MutedEventTypes, eventType))
            return true;

        if (IsInQuietHours(profile, now))
            return true;

        return false;
    }

    private static bool MatchesMutedList(string? mutedCsv, string eventType)
    {
        if (string.IsNullOrWhiteSpace(mutedCsv) || string.IsNullOrEmpty(eventType)) return false;
        var entries = mutedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            if (string.Equals(entry, eventType, StringComparison.OrdinalIgnoreCase)) return true;
            // category.* wildcard — matches "approval.task.assigned" when entry = "approval.*".
            if (entry.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = entry[..^1]; // keep the dot, drop only the *
                if (eventType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static bool IsInQuietHours(UserNotificationProfile profile, DateTime nowUtc)
    {
        if (!profile.QuietHoursStartMinute.HasValue || !profile.QuietHoursEndMinute.HasValue)
            return false;

        // Convert "now" into the user's local minutes-since-midnight. Falls
        // back to UTC if the timezone id is unrecognised — the user gets a
        // slightly-wrong window for one cycle, not a 500.
        TimeZoneInfo tz = TimeZoneInfo.Utc;
        if (!string.IsNullOrEmpty(profile.TimeZoneId))
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZoneId); }
            catch { /* unrecognised id — use UTC */ }
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var nowMin = local.Hour * 60 + local.Minute;

        var start = profile.QuietHoursStartMinute.Value;
        var end = profile.QuietHoursEndMinute.Value;

        if (start == end) return false; // zero-length window = no quiet hours

        // Wrap-around case (e.g. 22:00 → 07:00). "Inside" = before-end OR after-start.
        return start < end
            ? nowMin >= start && nowMin < end
            : nowMin >= start || nowMin < end;
    }
}
