namespace Oluso.Core.Notifications;

/// <summary>
/// Outbound push delivery to a single user, targeted by external user id (the
/// Oluso user id the mobile client sets via the push SDK). Implementations are
/// no-ops when push isn't configured, so callers needn't guard.
/// </summary>
public interface IPushSender
{
    /// <summary>
    /// True when push credentials are configured, so <see cref="SendAsync"/> actually delivers.
    /// Callers with another channel to fall back on check this instead of assuming delivery.
    /// </summary>
    bool IsEnabled { get; }

    Task SendAsync(
        string userId,
        string title,
        string body,
        string eventType,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);
}
