using Oluso.Core.Events;

namespace Oluso.Account.Events;

public class CibaRequestApprovedEvent : OlusoEvent
{
    public override string Category => "Authentication";
    public override string EventType => "CibaRequestApproved";
    public string? SubjectId { get; set; }
    public string? AuthReqId { get; set; }
    public string? SessionId { get; set; }
    public string? IpAddress { get; set; }
}

public class CibaRequestDeniedEvent : OlusoEvent
{
    public override string Category => "Authentication";
    public override string EventType => "CibaRequestDenied";
    public string? SubjectId { get; set; }
    public string? AuthReqId { get; set; }
    public string? IpAddress { get; set; }
}
