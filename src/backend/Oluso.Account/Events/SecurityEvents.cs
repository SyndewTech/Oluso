using Oluso.Core.Events;

namespace Oluso.Account.Events;

// Note: We use Account-specific events here to include additional context
// like IpAddress and UserAgent. The core UserPasswordChangedEvent is simpler.

public class AccountPasswordChangedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "AccountPasswordChanged";
    public string? SubjectId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public class UserMfaEnabledEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "UserMfaEnabled";
    public string? SubjectId { get; set; }
    public string? Method { get; set; }
    public string? IpAddress { get; set; }
}

public class UserMfaDisabledEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "UserMfaDisabled";
    public string? SubjectId { get; set; }
    public string? IpAddress { get; set; }
}

public class PhoneVerifiedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "PhoneVerified";
    public string? SubjectId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? IpAddress { get; set; }
}

public class ExternalLoginRemovedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "ExternalLoginRemoved";
    public string? SubjectId { get; set; }
    public string? Provider { get; set; }
    public string? IpAddress { get; set; }
}
