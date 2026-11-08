using Oluso.Core.Events;

namespace Oluso.Core.Terminology;

/// <summary>
/// Fired when a tenant's terminology changes: an override was set or reset in
/// the Terminology Center (<see cref="Key"/>/<see cref="Locale"/> populated),
/// or the tenant's industry/type changed (<see cref="Key"/> null — the whole
/// bundle re-resolves). Drives a live relabel of open UIs via the platform
/// realtime <c>terminology</c> channel — an internal UI-refresh signal, not a
/// webhook.
/// </summary>
public sealed class TerminologyUpdatedEvent : OlusoEvent
{
    public override string Category => "terminology";

    /// <summary>The changed key, or null when the tenant's industry changed (full re-resolve).</summary>
    public string? Key { get; set; }

    /// <summary>Locale the change applies to; null when the whole bundle is affected.</summary>
    public string? Locale { get; set; }

    public string? AdminUserId { get; set; }
    public string? AdminUserName { get; set; }
}
