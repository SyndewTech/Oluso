using Oluso.Core.Events;

[assembly: WebhookProvider("verification", "Business Verification")]

namespace Oluso.Enterprise.Verification.Events;

/// <summary>
/// A user's claim to act for a business reached a final outcome: verified, rejected or
/// failed. Raised once per outcome, whoever decided it (the registry during a journey,
/// the provider's webhook, a reviewer), with <see cref="OlusoEvent.Origin"/> naming
/// them, so a workflow can provision the merchant when a review completes later.
/// </summary>
[WebhookEvent("Business Verification Completed",
    Description = "A person's claim to act for a business was verified, rejected or failed: by the company registry during sign-up, by the provider's hosted check, or by a reviewer.",
    EnabledByDefault = true)]
public class BusinessVerificationCompletedEvent : OlusoEvent
{
    public override string Category => "Verification";
    public override string? WebhookEventType => "business.verification_completed";
    public override bool IsFailure => Status != "verified";

    public required string BusinessId { get; init; }
    public required string ApplicantId { get; init; }

    /// <summary>verified, rejected or failed.</summary>
    public required string Status { get; init; }

    /// <summary>not_found, inactive, name_mismatch, director_mismatch, rejected_by_provider, rejected_by_reviewer, abandoned.</summary>
    public string? Reason { get; init; }

    /// <summary>The user acting for the business; null when the journey hasn't signed them up yet.</summary>
    public string? SubjectId { get; init; }

    /// <summary>registry_director_match, authorized_representative_declared or provider_verified.</summary>
    public required string Relationship { get; init; }

    /// <summary>The business's own status: pending, in_review, verified, rejected or failed.</summary>
    public required string BusinessStatus { get; init; }

    public required string Country { get; init; }
    public required string RegistrationNumber { get; init; }
    public string? RegisteredName { get; init; }

    /// <summary>The provider of the business's registry snapshot, when it has one.</summary>
    public string? ProviderId { get; init; }

    /// <summary>A reviewer decided this outcome.</summary>
    public bool Reviewed { get; init; }
}

/// <summary>
/// A link to a business made before the applicant had an account was tied to the user
/// the same journey then signed up or in. Carries the link's status at that moment, so a
/// workflow that provisions merchants learns the user of an applicant verified before
/// sign-up.
/// </summary>
[WebhookEvent("Business Applicant Linked",
    Description = "A business verification started before sign-up was tied to the account the journey then created or signed in.",
    EnabledByDefault = true)]
public class BusinessApplicantLinkedEvent : OlusoEvent
{
    public override string Category => "Verification";
    public override string? WebhookEventType => "business.applicant_linked";

    public required string BusinessId { get; init; }
    public required string ApplicantId { get; init; }
    public required string SubjectId { get; init; }

    /// <summary>The link's status: pending, in_review, verified, rejected or failed.</summary>
    public required string Status { get; init; }

    public required string Relationship { get; init; }
    public required string Country { get; init; }
    public required string RegistrationNumber { get; init; }
    public string? RegisteredName { get; init; }
}
