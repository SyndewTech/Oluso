using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Enterprise.Verification.EntityFramework;
using Oluso.Enterprise.Verification.Events;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>Businesses, the people acting for them, the check trail and reviews.</summary>
public class BusinessModelTests : IDisposable
{
    private readonly VerificationHarness _h = new();

    public BusinessModelTests()
    {
        _h.Provider.Answer = _ => ScriptedBusinessProvider.Found("ACME FOODS LIMITED", BusinessActivityStatus.Active,
            new BusinessPerson("JOHN ADEWALE MUSA", "Director"), new BusinessPerson("AMAKA OBI", "Secretary"));
    }

    public void Dispose() => _h.Dispose();

    private async Task<BusinessVerificationAttempt> SubmitAsync(BusinessVerificationRequest request)
    {
        using var scope = _h.Scope();
        using var origin = EventOriginScope.Begin(EventOrigin.Journey("policy-1", "Merchant sign-up"));
        return await _h.Service(scope).SubmitAsync(request);
    }

    [Fact]
    public async Task A_verified_lookup_records_the_business_the_applicant_and_the_check()
    {
        var attempt = await SubmitAsync(VerificationHarness.Request());

        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.Verified);
        attempt.Business!.Status.Should().Be(BusinessVerificationStatus.Verified);
        attempt.Business.RegisteredName.Should().Be("ACME FOODS LIMITED");
        attempt.Business.People.Should().HaveCount(2);
        attempt.Applicant!.SubjectId.Should().Be("user-1");
        attempt.Applicant.Relationship.Should().Be(BusinessApplicantRelationships.AuthorizedRepresentativeDeclared);
        attempt.Applicant.NameMatched.Should().BeTrue();

        using var scope = _h.Scope();
        var checks = await _h.Store(scope).ListChecksForBusinessAsync(VerificationHarness.Tenant, attempt.Business.Id);
        checks.Should().ContainSingle().Which.Should().Match<BusinessVerificationCheck>(c =>
            c.Kind == BusinessCheckKinds.Lookup && c.Outcome == BusinessCheckOutcomes.Found
            && c.ProviderReference == "ref-1" && c.OriginKind == EventOriginKinds.Journey && c.OriginId == "policy-1");

        var evt = _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle().Subject;
        evt.Status.Should().Be("verified");
        evt.BusinessId.Should().Be(attempt.Business.Id);
        evt.ApplicantId.Should().Be(attempt.Applicant.Id);
        evt.SubjectId.Should().Be("user-1");
        evt.Origin!.Kind.Should().Be(EventOriginKinds.Journey);
    }

    [Fact]
    public async Task One_business_per_number_with_several_people_and_one_person_with_several_businesses()
    {
        _h.Settings.ByTenant[VerificationHarness.Tenant] = VerificationHarness.Routed(reuseDays: 30);

        var first = await SubmitAsync(VerificationHarness.Request(subject: "user-1", journey: "j1", number: "RC1234567"));
        var second = await SubmitAsync(VerificationHarness.Request(subject: "user-2", journey: "j2", number: "rc-1234567"));
        var other = await SubmitAsync(VerificationHarness.Request(subject: "user-1", journey: "j3", number: "RC7654321"));

        second.Business!.Id.Should().Be(first.Business!.Id);
        other.Business!.Id.Should().NotBe(first.Business.Id);
        _h.Provider.Lookups.Should().HaveCount(2, "the second applicant reuses the fresh registry snapshot");

        using var scope = _h.Scope();
        var store = _h.Store(scope);
        (await store.ListApplicantsForBusinessAsync(VerificationHarness.Tenant, first.Business.Id)).Select(a => a.SubjectId)
            .Should().BeEquivalentTo(new[] { "user-1", "user-2" });
        (await store.ListApplicantsForSubjectAsync(VerificationHarness.Tenant, "user-1")).Select(a => a.BusinessId)
            .Should().BeEquivalentTo(new[] { first.Business.Id, other.Business.Id });
        var checks = await store.ListChecksForBusinessAsync(VerificationHarness.Tenant, first.Business.Id);
        checks.Select(c => c.Kind).Should().BeEquivalentTo(new[] { BusinessCheckKinds.Lookup, BusinessCheckKinds.Reuse });
        checks.Single(c => c.Kind == BusinessCheckKinds.Reuse).ReusedFromCheckId.Should().Be(first.Business.RegistryCheckId);
    }

    [Fact]
    public async Task The_database_refuses_a_second_business_with_the_same_number()
    {
        using var scope = _h.Scope();
        var db = scope.ServiceProvider.GetRequiredService<VerificationDbContext>();
        db.Businesses.Add(new VerifiedBusiness { TenantId = "t1", Country = "NG", RegistrationNumber = "RC1" });
        db.Businesses.Add(new VerifiedBusiness { TenantId = "t1", Country = "NG", RegistrationNumber = "RC1" });

        await db.Invoking(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Without_a_reuse_window_every_attempt_buys_a_lookup_but_keeps_one_business()
    {
        await SubmitAsync(VerificationHarness.Request(subject: "user-1"));
        _h.Clock.Advance(TimeSpan.FromDays(1));
        await SubmitAsync(VerificationHarness.Request(subject: "user-2", journey: "j2"));

        _h.Provider.Lookups.Should().HaveCount(2);
        using var scope = _h.Scope();
        var (items, total) = await _h.Store(scope).ListBusinessesAsync(VerificationHarness.Tenant, null, null, 0, 10);
        total.Should().Be(1);
        items.Single().RegistryCheckedAt.Should().Be(_h.Clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task A_name_mismatch_rejects_the_applicant_not_the_business()
    {
        var attempt = await SubmitAsync(VerificationHarness.Request(name: "Zenith Foods"));

        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.Rejected);
        attempt.Business!.Status.Should().Be(BusinessVerificationStatus.Verified);
        attempt.Applicant!.Status.Should().Be(BusinessVerificationStatus.Rejected);
        attempt.Applicant.StatusReason.Should().Be(BusinessVerificationReasons.NameMismatch);
        _h.Events<BusinessVerificationCompletedEvent>().Single().Reason.Should().Be("name_mismatch");
    }

    [Fact]
    public async Task Not_found_rejects_both()
    {
        _h.Provider.Answer = _ => BusinessLookupResult.Failure(BusinessLookupOutcome.NotFound, "no such company");

        var attempt = await SubmitAsync(VerificationHarness.Request());

        attempt.Business!.Status.Should().Be(BusinessVerificationStatus.Rejected);
        attempt.Business.StatusReason.Should().Be(BusinessVerificationReasons.NotFound);
        attempt.Applicant!.StatusReason.Should().Be(BusinessVerificationReasons.NotFound);
    }

    [Fact]
    public async Task A_listed_director_acts_for_the_business_on_the_registry_match()
    {
        var attempt = await SubmitAsync(VerificationHarness.Request(evidence: ApplicantEvidenceRequirement.DirectorMatch,
            applicant: new ApplicantName("John", "Musa")));

        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.Verified);
        attempt.Applicant!.Relationship.Should().Be(BusinessApplicantRelationships.RegistryDirectorMatch);
        attempt.Applicant.MatchedPersonName.Should().Be("JOHN ADEWALE MUSA");
        attempt.Applicant.MatchedPersonRole.Should().Be("Director");
    }

    [Fact]
    public async Task Someone_not_listed_is_rejected_when_only_a_director_may_act()
    {
        var attempt = await SubmitAsync(VerificationHarness.Request(evidence: ApplicantEvidenceRequirement.DirectorMatch,
            applicant: new ApplicantName("Amaka", "Obi")));

        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.Rejected);
        attempt.Applicant!.StatusReason.Should().Be(BusinessVerificationReasons.DirectorMismatch);
        attempt.Applicant.DirectorMatched.Should().BeFalse("a secretary isn't a director");
    }

    [Fact]
    public async Task Someone_not_listed_uploads_documents_for_review_without_a_second_lookup()
    {
        var request = VerificationHarness.Request(subject: null, evidence: ApplicantEvidenceRequirement.DirectorOrDocuments,
            applicant: new ApplicantName("Ada", "Eze"));

        var first = await SubmitAsync(request);
        first.Outcome.Should().Be(BusinessVerificationAttemptOutcome.DocumentsRequired);
        first.Applicant!.Status.Should().Be(BusinessVerificationStatus.Pending);

        var letter = new AcceptedJourneyFile(Upload.File("documents", "authority.pdf", Upload.Pdf), FileContentSniffer.Types[FileContentSniffer.Pdf]);
        var second = await SubmitAsync(request with { Documents = new[] { letter } });

        second.Outcome.Should().Be(BusinessVerificationAttemptOutcome.InReview);
        second.Applicant!.Id.Should().Be(first.Applicant.Id);
        second.Applicant.StatusReason.Should().Be(BusinessVerificationReasons.AwaitingReview);
        second.Applicant.DocumentIds.Should().ContainSingle();
        _h.Provider.Lookups.Should().ContainSingle("the documents settle the same submission against the snapshot just taken");

        var file = _h.Attachments.Rows[second.Applicant.DocumentIds[0]];
        file.EntityType.Should().Be(VerificationFileEntityTypes.BusinessApplicant);
        file.EntityId.Should().Be(second.Applicant.Id);
        file.ContentType.Should().Be("application/pdf");
        file.UploadedByUserId.Should().Be(JourneyUploads.PendingUploader("j1"));
        file.StoragePath.Should().StartWith($"attachments/{VerificationHarness.Tenant}/business_applicant/");
        _h.Events<BusinessVerificationCompletedEvent>().Should().BeEmpty("nothing is final yet");
    }

    [Fact]
    public async Task An_unreachable_provider_leaves_the_link_pending_until_a_reviewer_decides_the_business()
    {
        _h.Provider.Answer = _ => BusinessLookupResult.Failure(BusinessLookupOutcome.ProviderUnavailable, "503");

        var attempt = await SubmitAsync(VerificationHarness.Request(journey: "j-wait"));
        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.ProviderUnavailable);
        attempt.Business!.Status.Should().Be(BusinessVerificationStatus.Pending);
        await _h.Journeys.SaveAsync(Journey("j-wait"));

        using (var scope = _h.Scope())
        using (EventOriginScope.Begin(EventOrigin.Admin("admin-1")))
        {
            var reviewed = await _h.Service(scope).ReviewBusinessAsync(VerificationHarness.Tenant, attempt.Business.Id, approve: true, "admin-1", "Checked the certificate");
            reviewed.Status.Should().Be(BusinessVerificationStatus.Verified);
            reviewed.ReviewedBy.Should().Be("admin-1");
        }

        using var read = _h.Scope();
        var applicant = await _h.Store(read).GetApplicantAsync(VerificationHarness.Tenant, attempt.Applicant!.Id);
        applicant!.Status.Should().Be(BusinessVerificationStatus.Verified);
        var evt = _h.Events<BusinessVerificationCompletedEvent>().Single();
        evt.Reviewed.Should().BeTrue();
        evt.Origin!.Kind.Should().Be(EventOriginKinds.Admin);
        Oluso.Core.UserJourneys.JourneyStatusRevision.Read((await _h.Journeys.GetAsync("j-wait"))!.Data).Should().Be(1, "the waiting page is told to refresh");
        (await _h.Store(read).ListChecksForBusinessAsync(VerificationHarness.Tenant, attempt.Business.Id))
            .Should().Contain(c => c.Kind == BusinessCheckKinds.Review && c.OriginKind == EventOriginKinds.Admin && c.Message == "Checked the certificate");
    }

    [Fact]
    public async Task A_reviewer_approves_an_applicant_only_once_the_business_is_verified()
    {
        var letter = new AcceptedJourneyFile(Upload.File("documents", "authority.pdf", Upload.Pdf), FileContentSniffer.Types[FileContentSniffer.Pdf]);
        var attempt = await SubmitAsync(VerificationHarness.Request(evidence: ApplicantEvidenceRequirement.Documents, documents: new[] { letter }));
        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.InReview);

        using var scope = _h.Scope();
        var applicant = await _h.Service(scope).ReviewApplicantAsync(VerificationHarness.Tenant, attempt.Applicant!.Id, approve: true, "admin-1", null);

        applicant.Status.Should().Be(BusinessVerificationStatus.Verified);
        applicant.ReviewedBy.Should().Be("admin-1");
        _h.Events<BusinessVerificationCompletedEvent>().Single().Reviewed.Should().BeTrue();
    }

    [Fact]
    public async Task Approving_an_applicant_of_an_unverified_business_is_refused()
    {
        _h.Provider.Answer = _ => BusinessLookupResult.Failure(BusinessLookupOutcome.ProviderUnavailable, "503");
        var attempt = await SubmitAsync(VerificationHarness.Request());

        using var scope = _h.Scope();
        await _h.Service(scope).Invoking(s => s.ReviewApplicantAsync(VerificationHarness.Tenant, attempt.Applicant!.Id, true, "admin-1", null))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_country_without_a_provider_records_the_check_but_no_business()
    {
        _h.Settings.ByTenant[VerificationHarness.Tenant] = new IdentityVerificationSettings();

        var attempt = await SubmitAsync(VerificationHarness.Request());

        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.Misconfigured);
        attempt.Business.Should().BeNull();
        using var scope = _h.Scope();
        var db = scope.ServiceProvider.GetRequiredService<VerificationDbContext>();
        (await db.Businesses.CountAsync()).Should().Be(0);
        (await db.Checks.SingleAsync()).Outcome.Should().Be(BusinessCheckOutcomes.Misconfigured);
        _h.Sink.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_malformed_number_never_reaches_the_provider()
    {
        var attempt = await SubmitAsync(VerificationHarness.Request(number: "12-34-56-xx"));

        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.InvalidInput);
        _h.Provider.Lookups.Should().BeEmpty();
    }

    // ── before the account exists ──────────────────────────────────────────

    [Fact]
    public async Task A_link_made_before_sign_up_is_tied_to_the_user_the_journey_signs_up()
    {
        var letter = new AcceptedJourneyFile(Upload.File("documents", "authority.pdf", Upload.Pdf), FileContentSniffer.Types[FileContentSniffer.Pdf]);
        var attempt = await SubmitAsync(VerificationHarness.Request(subject: null, journey: "j-new", evidence: ApplicantEvidenceRequirement.Documents, documents: new[] { letter }));
        attempt.Applicant!.SubjectId.Should().BeNull();

        using (var scope = _h.Scope())
            await _h.Service(scope).ClaimJourneyApplicantsAsync(VerificationHarness.Tenant, "j-new", "new-user");

        using var read = _h.Scope();
        var applicant = await _h.Store(read).GetApplicantAsync(VerificationHarness.Tenant, attempt.Applicant.Id);
        applicant!.SubjectId.Should().Be("new-user");
        _h.Attachments.Rows[applicant.DocumentIds[0]].UploadedByUserId.Should().Be("new-user");
        var linked = _h.Events<BusinessApplicantLinkedEvent>().Should().ContainSingle().Subject;
        linked.SubjectId.Should().Be("new-user");
        linked.Status.Should().Be("in_review");
    }

    [Fact]
    public async Task A_user_who_already_acts_for_the_business_keeps_one_link_after_signing_in()
    {
        await SubmitAsync(VerificationHarness.Request(subject: "user-1", journey: "j-old", name: "Wrong Name"));
        var anonymous = await SubmitAsync(VerificationHarness.Request(subject: null, journey: "j-new"));

        using (var scope = _h.Scope())
            await _h.Service(scope).ClaimJourneyApplicantsAsync(VerificationHarness.Tenant, "j-new", "user-1");

        using var read = _h.Scope();
        var links = await _h.Store(read).ListApplicantsForBusinessAsync(VerificationHarness.Tenant, anonymous.Business!.Id);
        links.Should().ContainSingle().Which.Should().Match<BusinessApplicant>(a =>
            a.SubjectId == "user-1" && a.Status == BusinessVerificationStatus.Verified && a.JourneyId == "j-new");
    }

    // ── access ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Only_linked_users_and_administrators_with_the_permission_can_see_a_business()
    {
        var attempt = await SubmitAsync(VerificationHarness.Request(subject: "user-1"));
        _h.Profiles.Setup(p => p.GetUserPermissionsAsync("admin-1", VerificationHarness.Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { VerificationPermissions.Read });
        _h.Profiles.Setup(p => p.GetUserPermissionsAsync(It.IsNotIn("admin-1"), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        using var scope = _h.Scope();
        var store = _h.Store(scope);
        var profiles = scope.ServiceProvider.GetRequiredService<IProfileService>();
        async Task<bool> Can(string user) =>
            await Services.BusinessVerificationAccess.CanSeeBusinessAsync(store, profiles, VerificationHarness.Tenant, user, attempt.Business!.Id, default);

        (await Can("user-1")).Should().BeTrue();
        (await Can("admin-1")).Should().BeTrue();
        (await Can("someone-else")).Should().BeFalse();
        (await Services.BusinessVerificationAccess.CanSeeBusinessAsync(store, profiles, "other-tenant", "user-1", attempt.Business!.Id, default))
            .Should().BeFalse("links are per organisation");
    }

    private static Oluso.Core.UserJourneys.JourneyState Journey(string id) => new()
    {
        Id = id,
        TenantId = VerificationHarness.Tenant,
        ClientId = "client",
        PolicyId = "policy-1",
        CurrentStepId = "kyb",
        Status = Oluso.Core.UserJourneys.JourneyStatus.InProgress,
        Data = new Dictionary<string, object>(),
    };
}
