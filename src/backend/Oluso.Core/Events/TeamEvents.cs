namespace Oluso.Core.Events;

/// <summary>
/// Base for team lifecycle events. Tenant-scoped, since teams belong to a tenant.
/// </summary>
public abstract class TeamEvent : OlusoEvent
{
    public override string Category => EventCategories.Team;

    public required string TeamId { get; init; }
    public required string TeamName { get; init; }
    public string? TeamCode { get; init; }

    /// <summary>User who performed the change, when known.</summary>
    public string? ActorUserId { get; init; }
}

[WebhookEvent("Team Created", Description = "Triggered when a team is created")]
public class TeamCreatedEvent : TeamEvent
{
    public override string? WebhookEventType => "team.created";
}

[WebhookEvent("Team Updated", Description = "Triggered when a team's details or parent change")]
public class TeamUpdatedEvent : TeamEvent
{
    public override string? WebhookEventType => "team.updated";

    /// <summary>
    /// The parent the team had before this change, when the change moved it; null when the parent stayed. The old
    /// ancestors lose the team's members from their effective membership, so consumers that hold it refresh them too.
    /// </summary>
    public string? PreviousParentTeamId { get; init; }

    /// <summary>True when this change moved the team to another parent (or to or from the top level).</summary>
    public bool ParentChanged { get; init; }
}

[WebhookEvent("Team Archived", Description = "Triggered when a team is archived")]
public class TeamArchivedEvent : TeamEvent
{
    public override string? WebhookEventType => "team.archived";
}

[WebhookEvent("Team Restored", Description = "Triggered when an archived team is restored")]
public class TeamRestoredEvent : TeamEvent
{
    public override string? WebhookEventType => "team.restored";
}

[WebhookEvent("Team Deleted", Description = "Triggered when a team is permanently deleted")]
public class TeamDeletedEvent : TeamEvent
{
    public override string? WebhookEventType => "team.deleted";
}

[WebhookEvent("Team Member Added", Description = "Triggered when a user joins a team or rejoins after leaving")]
public class TeamMemberAddedEvent : TeamEvent
{
    public override string? WebhookEventType => "team.member_added";

    public required string UserId { get; init; }
    public string? MemberRole { get; init; }
}

[WebhookEvent("Team Member Removed", Description = "Triggered when a user leaves a team")]
public class TeamMemberRemovedEvent : TeamEvent
{
    public override string? WebhookEventType => "team.member_removed";

    public required string UserId { get; init; }
}

[WebhookEvent("Team Member Role Changed", Description = "Triggered when a member's role in a team changes (for example to or from Lead)")]
public class TeamMemberRoleChangedEvent : TeamEvent
{
    public override string? WebhookEventType => "team.member_role_changed";

    public required string UserId { get; init; }
    public required string MemberRole { get; init; }
    public string? PreviousRole { get; init; }
}
