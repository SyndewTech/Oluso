namespace Oluso.Dto.Notifications;

public class SnoozeRequest
{
    public int DurationMinutes { get; set; }
    public string? Reason { get; set; }
}
