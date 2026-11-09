using Oluso.Core.Events;

namespace Oluso.Enterprise.Ldap.Events;

public class LdapAuthenticationAttemptEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;

    public required string Username { get; init; }
    public string? IpAddress { get; init; }
}

public class LdapAuthenticationSuccessEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "ldap.login_success";

    public required string SubjectId { get; init; }
    public required string Username { get; init; }
    public string? DistinguishedName { get; init; }
    public IReadOnlyList<string>? Groups { get; init; }
    public IReadOnlyList<string>? MappedRoles { get; init; }
    public string? IpAddress { get; init; }
}

public class LdapAuthenticationFailedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override bool IsFailure => true;
    public override string? WebhookEventType => "ldap.login_failed";

    public required string Username { get; init; }
    public required string FailureReason { get; init; }
    public string? IpAddress { get; init; }
}

public class LdapPasswordChangedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "ldap.password_changed";

    public required string Username { get; init; }
    public string? IpAddress { get; init; }
}

public class LdapUserProvisionedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "ldap.user_provisioned";

    public required string SubjectId { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public string? DistinguishedName { get; init; }
}
