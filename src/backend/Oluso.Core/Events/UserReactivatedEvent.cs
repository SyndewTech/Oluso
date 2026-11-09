namespace Oluso.Core.Events;

/// <summary>
/// Raised when a deactivated user account is switched back on, so subscribers
/// that acted on <see cref="UserDeactivatedEvent"/> can restore it.
/// </summary>
[WebhookEvent("User Reactivated", Description = "Triggered when a deactivated user account is reactivated")]
public class UserReactivatedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.reactivated";

    public required string SubjectId { get; init; }
    public string? Email { get; init; }
}
