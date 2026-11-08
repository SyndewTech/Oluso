namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Per-user, per-tenant notification preferences kept as a Core primitive so
/// any module that emits user-targeted notifications (approval engine,
/// future SignalR / push channels, etc.) can consult one source of truth
/// before delivering. Sits next to <c>Team</c> and <c>OlusoUser</c> in the
/// "things every tenant has about a user" tier — meaningful whether a given
/// module is installed or not, the same way Microsoft 365 Admin lets you set
/// quiet hours and time zone on a user without any module in the picture.
///
/// <para>Schema is intentionally narrow: a small set of universally-useful
/// preferences. New profile concerns (work location, language, on-call
/// rotation) get their own small entities rather than bloating this one.</para>
///
/// <para>One row per <c>(TenantId, UserId)</c> — the store upserts; absence
/// of a row means "no overrides, deliver normally." Null/empty fields are
/// permissive by design: a user who never opens the preferences page should
/// receive everything.</para>
/// </summary>
public class UserNotificationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = null!;
    public string UserId { get; set; } = null!;

    /// <summary>
    /// Suppresses all notifications until this UTC instant. Used for
    /// short-term snoozes ("not now, ask me in 2 hours") without changing
    /// permanent preferences. Null = no active snooze.
    /// </summary>
    public DateTime? SnoozeUntilUtc { get; set; }

    /// <summary>
    /// Optional reason the user gave when snoozing. Surfaced in admin
    /// notification logs so an audit ("why didn't this email get sent?")
    /// has context.
    /// </summary>
    public string? SnoozeReason { get; set; }

    /// <summary>
    /// Daily quiet-hours window — local to <see cref="TimeZoneId"/>.
    /// When both are set, notifications scheduled inside the window are
    /// suppressed. Stored as minutes-since-midnight (0–1439) to avoid
    /// cross-DB time-type quirks. Null = no quiet hours configured.
    /// </summary>
    public int? QuietHoursStartMinute { get; set; }
    public int? QuietHoursEndMinute { get; set; }

    /// <summary>
    /// IANA time zone id used to interpret <see cref="QuietHoursStartMinute"/>
    /// and <see cref="QuietHoursEndMinute"/>. Falls back to UTC when null.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// Comma-separated list of webhook event types the user has muted
    /// globally — e.g. <c>"approval.task.assigned,&lt;module&gt;.&lt;event&gt;"</c>.
    /// Wildcard supported via <c>category.*</c>. Empty/null = nothing muted.
    /// </summary>
    public string? MutedEventTypes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
