namespace Oluso.Dto.Notifications;

public class UserNotificationProfileDto
{
    public DateTime? SnoozeUntilUtc { get; set; }
    public string? SnoozeReason { get; set; }
    public int? QuietHoursStartMinute { get; set; }
    public int? QuietHoursEndMinute { get; set; }
    public string? TimeZoneId { get; set; }
    /// <summary>Comma-separated event types; supports <c>category.*</c> wildcard.</summary>
    public string? MutedEventTypes { get; set; }
}
