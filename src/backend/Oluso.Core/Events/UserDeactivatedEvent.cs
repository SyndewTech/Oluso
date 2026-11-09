namespace Oluso.Core.Events;

/// <summary>
/// Raised when a user account is switched off without being deleted: an admin
/// or an HR status change, or a SCIM client deprovisioning the user. The account
/// and its history remain, and it can be reactivated, so subscribers that mirror
/// users should stop treating it as active rather than erase it. The
/// <see cref="OlusoEvent.Origin"/> says who switched it off.
/// </summary>
[WebhookEvent("User Deactivated", Description = "Triggered when a user account is deactivated, including SCIM deprovisioning", EnabledByDefault = true)]
public class UserDeactivatedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.deactivated";

    public required string SubjectId { get; init; }
    public string? Email { get; init; }
}
