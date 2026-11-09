using Oluso.Core.Events;

namespace Oluso.Enterprise.Saml.Events;

public class AdminSamlSpCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "SamlServiceProvider";
    public override string? WebhookEventType => "admin.saml_sp_created";

    public required string EntityId { get; init; }
}

public class AdminSamlSpUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "SamlServiceProvider";
    public override string? WebhookEventType => "admin.saml_sp_updated";

    public required string EntityId { get; init; }
}

public class AdminSamlSpDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "SamlServiceProvider";
    public override string? WebhookEventType => "admin.saml_sp_deleted";

    public required string EntityId { get; init; }
}

public class SamlSsoRequestReceivedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.sso_request_received";

    public required string SpEntityId { get; init; }
    public string? RequestId { get; init; }
    public string? AcsUrl { get; init; }
    public bool ForceAuthn { get; init; }
}

public class SamlAssertionIssuedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.assertion_issued";

    public required string SubjectId { get; init; }
    public required string SpEntityId { get; init; }
    public string? NameId { get; init; }
    public string? SessionIndex { get; init; }
}

public class SamlSsoFailedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override bool IsFailure => true;
    public override string? WebhookEventType => "saml.sso_failed";

    public string? SpEntityId { get; init; }
    public required string Error { get; init; }
    public string? ErrorDescription { get; init; }
}

public class SamlLogoutRequestReceivedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.logout_request_received";

    public required string SpEntityId { get; init; }
    public string? NameId { get; init; }
    public string? SessionIndex { get; init; }
}

public class SamlLogoutCompletedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.logout_completed";

    public required string SpEntityId { get; init; }
    public string? SubjectId { get; init; }
    public bool Success { get; init; }
}

public class SamlSpLoginEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.sp_login";

    public required string SubjectId { get; init; }
    public required string IdpEntityId { get; init; }
    public string? NameId { get; init; }
    public string? SessionIndex { get; init; }
}
