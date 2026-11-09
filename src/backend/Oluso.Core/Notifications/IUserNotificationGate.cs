namespace Oluso.Core.Notifications;

/// <summary>
/// One-call check every user-targeted notification sink should run before
/// delivering an outbound message (email, push, SMS, in-app). Centralises
/// the "is this user snoozed / muted / in quiet hours?" decision so each
/// channel doesn't reinvent it.
///
/// <para>The default implementation consults
/// <see cref="Domain.Interfaces.IUserNotificationProfileStore"/>; absent
/// row means "permit." Adding new suppression rules (e.g. focus mode,
/// vacation calendar) only touches the gate impl — sinks don't change.</para>
///
/// <para>Sinks should pass the event type string verbatim from
/// <c>OlusoEvent.WebhookEventType</c> so the gate's mute-list semantics
/// (incl. wildcards like <c>approval.*</c>) line up with the rest of the
/// platform's event vocabulary.</para>
/// </summary>
public interface IUserNotificationGate
{
    /// <summary>
    /// True when the message should be suppressed for this user. Always pass
    /// the canonical webhook event type (e.g. <c>"approval.task.assigned"</c>)
    /// — the gate may wildcard-match against the user's muted list.
    /// </summary>
    Task<bool> ShouldSuppressAsync(
        string tenantId,
        string userId,
        string eventType,
        DateTime? nowUtc = null,
        CancellationToken cancellationToken = default);
}
