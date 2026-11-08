using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Storage;
using Oluso.Enterprise.Verification.Dto;
using Oluso.Enterprise.Verification.Events;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>
/// Reviewers, provider webhooks and browser callbacks deciding the same business or applicant at
/// once, each on its own DbContext and connection to one SQLite database: one decision wins, the
/// rest change nothing and reviewers are told it was already decided.
/// </summary>
public class DecisionRaceTests : IDisposable
{
    private const string Tenant = VerificationHarness.Tenant;

    private readonly VerificationHarness _h = new(new ScriptedBusinessProvider("scripted", "NG"), new ScriptedHostedProvider());

    public DecisionRaceTests()
    {
        _h.Provider.Answer = _ => ScriptedBusinessProvider.Found("ACME FOODS LIMITED");
    }

    public void Dispose() => _h.Dispose();

    private ScriptedHostedProvider Hosted => (ScriptedHostedProvider)_h.Providers[1];

    private async Task<BusinessApplicant> HostedApplicantAsync()
    {
        _h.Settings.ByTenant[Tenant] = VerificationHarness.Routed("hosted");
        using var scope = _h.Scope();
        var attempt = await _h.Service(scope).SubmitAsync(VerificationHarness.Request(mode: BusinessVerificationMode.Hosted));
        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.HostedSessionStarted);
        return attempt.Applicant!;
    }

    private async Task<BusinessApplicant> ApplicantAwaitingReviewAsync()
    {
        using var scope = _h.Scope();
        var letter = new AcceptedJourneyFile(Upload.File("documents", "authority.pdf", Upload.Pdf), FileContentSniffer.Types[FileContentSniffer.Pdf]);
        var attempt = await _h.Service(scope).SubmitAsync(VerificationHarness.Request(evidence: ApplicantEvidenceRequirement.Documents, documents: new[] { letter }));
        attempt.Applicant!.Status.Should().Be(BusinessVerificationStatus.InReview);
        attempt.Business!.Status.Should().Be(BusinessVerificationStatus.Verified);
        return attempt.Applicant;
    }

    private Task<ActionResult<BusinessApplicantDto>> ReviewApplicantAsync(string applicantId, string decision, string reviewer) => Task.Run(async () =>
    {
        using var scope = _h.Scope();
        return await AdminApiTests.Controller(_h, scope, reviewer)
            .ReviewApplicant(applicantId, new ReviewBusinessVerificationRequest { Decision = decision }, default);
    });

    private Task<ActionResult<VerifiedBusinessDetailDto>> ReviewBusinessAsync(string businessId, string decision) => Task.Run(async () =>
    {
        using var scope = _h.Scope();
        return await AdminApiTests.Controller(_h, scope)
            .ReviewBusiness(businessId, new ReviewBusinessVerificationRequest { Decision = decision }, default);
    });

    private Task<BusinessApplicantView?> CloseSessionAsync() => Task.Run(async () =>
    {
        using var scope = _h.Scope();
        return await _h.Service(scope).CompleteHostedSessionAsync(Tenant, "hosted", "sess-1");
    });

    private async Task<(VerifiedBusiness Business, BusinessApplicant Applicant, IReadOnlyList<BusinessVerificationCheck> Checks)> StoredAsync(string applicantId)
    {
        using var scope = _h.Scope();
        var store = _h.Store(scope);
        var applicant = (await store.GetApplicantAsync(Tenant, applicantId))!;
        var business = (await store.GetBusinessAsync(Tenant, applicant.BusinessId))!;
        return (business, applicant, await store.ListChecksForBusinessAsync(Tenant, business.Id));
    }

    private static string ConflictMessage(IConvertToActionResult result)
    {
        var conflict = result.Convert().Should().BeOfType<ConflictObjectResult>().Subject;
        return (string)conflict.Value!.GetType().GetProperty("Message")!.GetValue(conflict.Value)!;
    }

    private static bool IsConflict(IConvertToActionResult result) => result.Convert() is ConflictObjectResult;

    [Fact]
    public async Task A_reviewer_and_a_webhook_racing_on_one_applicant_decide_it_once()
    {
        var applicant = await HostedApplicantAsync();
        Hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Approved };

        _h.Gate.Arm(2);
        var review = ReviewApplicantAsync(applicant.Id, "reject", "admin-1");
        var webhook = CloseSessionAsync();
        await Task.WhenAll(review, webhook);

        var completed = _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle().Subject;
        var (_, stored, checks) = await StoredAsync(applicant.Id);
        checks.Should().ContainSingle(c => c.Kind == BusinessCheckKinds.HostedSession)
            .Which.Outcome.Should().Be(BusinessCheckOutcomes.Approved, "the provider's answer is kept for audit either way");

        if (IsConflict(review.Result))
        {
            ConflictMessage(review.Result).Should().StartWith("This was already decided (Verified by the verification provider at ")
                .And.EndWith("Refresh to see the latest.");
            stored.Status.Should().Be(BusinessVerificationStatus.Verified);
            stored.ReviewedBy.Should().BeNull();
            checks.Should().NotContain(c => c.Kind == BusinessCheckKinds.Review, "the losing review recorded nothing");
            completed.Reviewed.Should().BeFalse();
        }
        else
        {
            stored.Status.Should().Be(BusinessVerificationStatus.Rejected);
            stored.ReviewedBy.Should().Be("admin-1");
            webhook.Result!.Applicant.Status.Should().Be(BusinessVerificationStatus.Rejected, "the webhook that lost returns the reviewer's decision");
            completed.Reviewed.Should().BeTrue();
        }
        completed.Status.Should().Be(stored.Status.ToWire());
    }

    [Fact]
    public async Task A_reviewer_acting_after_the_provider_decided_gets_a_conflict_and_changes_nothing()
    {
        var applicant = await HostedApplicantAsync();
        Hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Approved };
        await CloseSessionAsync();

        var review = await ReviewApplicantAsync(applicant.Id, "reject", "admin-1");

        ConflictMessage(review).Should().MatchRegex(@"^This was already decided \(Verified by the verification provider at \d{4}-\d{2}-\d{2} \d{2}:\d{2} UTC\)\. Refresh to see the latest\.$");
        (await StoredAsync(applicant.Id)).Applicant.Status.Should().Be(BusinessVerificationStatus.Verified);
        _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Two_reviewers_racing_on_one_applicant_one_wins_and_the_other_gets_409()
    {
        var applicant = await ApplicantAwaitingReviewAsync();

        _h.Gate.Arm(2);
        var approve = ReviewApplicantAsync(applicant.Id, "approve", "admin-1");
        var reject = ReviewApplicantAsync(applicant.Id, "reject", "admin-2");
        await Task.WhenAll(approve, reject);

        var results = new[] { approve.Result, reject.Result };
        results.Count(IsConflict).Should().Be(1);
        var (winner, loser, expected) = IsConflict(reject.Result)
            ? (approve.Result, reject.Result, BusinessVerificationStatus.Verified)
            : (reject.Result, approve.Result, BusinessVerificationStatus.Rejected);
        winner.Result.Should().BeOfType<OkObjectResult>();
        ConflictMessage(loser).Should().Contain("already decided").And.Contain("by a reviewer");

        var (_, stored, checks) = await StoredAsync(applicant.Id);
        stored.Status.Should().Be(expected);
        checks.Count(c => c.Kind == BusinessCheckKinds.Review).Should().Be(1);
        _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle().Which.Status.Should().Be(expected.ToWire());
    }

    [Fact]
    public async Task A_business_review_racing_a_session_close_decides_the_waiting_applicant_once()
    {
        var applicant = await HostedApplicantAsync();
        (await StoredAsync(applicant.Id)).Business.Status.Should().Be(BusinessVerificationStatus.InReview);
        Hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Rejected, Message = "documents unreadable" };

        _h.Gate.Arm(2);
        var review = ReviewBusinessAsync(applicant.BusinessId, "approve");
        var webhook = CloseSessionAsync();
        await Task.WhenAll(review, webhook);

        var completed = _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle().Subject;
        var (business, stored, checks) = await StoredAsync(applicant.Id);
        checks.Should().ContainSingle(c => c.Kind == BusinessCheckKinds.HostedSession).Which.Outcome.Should().Be(BusinessCheckOutcomes.Rejected);

        if (IsConflict(review.Result))
        {
            ConflictMessage(review.Result).Should().Contain("already decided (Rejected by the verification provider");
            business.Status.Should().Be(BusinessVerificationStatus.Rejected);
            stored.Status.Should().Be(BusinessVerificationStatus.Rejected);
            checks.Should().NotContain(c => c.Kind == BusinessCheckKinds.Review);
        }
        else
        {
            business.Status.Should().Be(BusinessVerificationStatus.Verified);
            business.ReviewedBy.Should().Be("admin-1");
            stored.Status.Should().Be(BusinessVerificationStatus.Verified, "the session that closed later doesn't overturn the reviewer");
        }
        completed.Status.Should().Be(stored.Status.ToWire());
    }
}
