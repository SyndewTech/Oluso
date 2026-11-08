using Oluso.Core.Events;

namespace Oluso.Account.Events;

public class SessionRevokedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "SessionRevoked";
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string? RevokedBySessionId { get; set; }
    public string? IpAddress { get; set; }
}

public class AllSessionsRevokedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "AllSessionsRevoked";
    public string? SubjectId { get; set; }
    public int RevokedCount { get; set; }
    public string? CurrentSessionId { get; set; }
    public string? IpAddress { get; set; }
}
