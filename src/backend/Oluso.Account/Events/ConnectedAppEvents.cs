using Oluso.Core.Events;

namespace Oluso.Account.Events;

public class AppAccessRevokedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "AppAccessRevoked";
    public string? SubjectId { get; set; }
    public string? ClientId { get; set; }
    public int RevokedGrantCount { get; set; }
    public string? IpAddress { get; set; }
}
