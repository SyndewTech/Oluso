using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Storage;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Verification.Events;
using Oluso.Enterprise.Verification.Stores;

namespace Oluso.Enterprise.Verification.Services;

public sealed class BusinessVerificationService : IBusinessVerificationService
{
    private readonly IBusinessVerificationStore _store;
    private readonly IBusinessVerificationRouter _router;
    private readonly IIdentityVerificationSettingsService _settings;
    private readonly IOlusoEventService _events;
    private readonly IJourneyFileUploadService _uploads;
    private readonly IFileAttachmentStore _attachments;
    private readonly IJourneyStateStore _journeys;
    private readonly BusinessVerificationNotifier _notifier;
    private readonly ILogger<BusinessVerificationService> _logger;
    private readonly TimeProvider _clock;

    public BusinessVerificationService(
        IBusinessVerificationStore store,
        IBusinessVerificationRouter router,
        IIdentityVerificationSettingsService settings,
        IOlusoEventService events,
        IJourneyFileUploadService uploads,
        IFileAttachmentStore attachments,
        IJourneyStateStore journeys,
        BusinessVerificationNotifier notifier,
        ILogger<BusinessVerificationService> logger,
        TimeProvider? clock = null)
    {
        _store = store;
        _router = router;
        _settings = settings;
        _events = events;
        _uploads = uploads;
        _attachments = attachments;
        _journeys = journeys;
        _notifier = notifier;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    private const int MaxDecisionAttempts = 3;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<BusinessVerificationAttempt> SubmitAsync(BusinessVerificationRequest request, CancellationToken cancellationToken = default)
    {
        var number = BusinessRegistrationNumber.TryParse(request.Country, request.RegistrationNumber, request.RegistrationType, out var inputError);
        if (number == null)
            return new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.InvalidInput, null, null, inputError);

        BusinessVerificationRoute route;
        try
        {
            route = await _router.ResolveAsync(request.TenantId, number.Country, cancellationToken);
        }
        catch (VerificationConfigurationException ex)
        {
            var settings = await _settings.GetAsync(request.TenantId, cancellationToken);
            await AddCheckAsync(request, number, null, null, BusinessCheckKinds.Lookup, settings.BusinessRouting.GetValueOrDefault(number.Country), null,
                BusinessCheckOutcomes.Misconfigured, ex.Message, cancellationToken);
            return new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.Misconfigured, null, null);
        }

        var business = await _store.FindBusinessAsync(request.TenantId, number.Country, number.Canonical, cancellationToken);
        var applicant = await FindLinkAsync(request.TenantId, business?.Id, request.SubjectId, request.JourneyId, cancellationToken);

        var reviewerDecided = business?.ReviewedAt != null && business.Status is BusinessVerificationStatus.Verified or BusinessVerificationStatus.Rejected;
        var reusable = business != null && !reviewerDecided && await CanReuseSnapshotAsync(request.TenantId, business, applicant, cancellationToken);

        if (request.Mode == BusinessVerificationMode.Hosted && !reusable && !reviewerDecided)
            return await StartHostedAsync(request, number, route, business, applicant, cancellationToken);

        BusinessLookupResult? lookup = null;
        if (reusable)
        {
            await AddCheckAsync(request, number, business, applicant, BusinessCheckKinds.Reuse, business!.RegistryProviderId, business.RegistryEnvironment,
                BusinessCheckOutcomes.Found, null, cancellationToken, reusedFrom: business.RegistryCheckId);
        }
        else if (!reviewerDecided)
        {
            try
            {
                lookup = await route.Provider.LookupBusinessAsync(route.Connection, new BusinessLookupRequest
                {
                    Country = number.Country,
                    RegistrationNumber = number.Canonical,
                    RegistrationType = number.Type,
                    BusinessName = Clean(request.BusinessName),
                }, cancellationToken);
            }
            catch (VerificationConfigurationException ex)
            {
                _logger.LogError("Business verification provider {Provider} for tenant {TenantId} is misconfigured: {Reason}", route.Provider.Id, request.TenantId, ex.Message);
                await AddCheckAsync(request, number, business, applicant, BusinessCheckKinds.Lookup, route.Provider.Id, route.Connection.Environment,
                    BusinessCheckOutcomes.Misconfigured, ex.Message, cancellationToken);
                return new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.Misconfigured, business, applicant);
            }

            if (lookup.Outcome is BusinessLookupOutcome.InvalidRequest or BusinessLookupOutcome.Unauthorized)
            {
                if (lookup.Outcome == BusinessLookupOutcome.Unauthorized)
                    _logger.LogError("Business verification provider {Provider} rejected tenant {TenantId}'s credentials: {Reason}", route.Provider.Id, request.TenantId, lookup.Message);
                await AddCheckAsync(request, number, business, applicant, BusinessCheckKinds.Lookup, route.Provider.Id, route.Connection.Environment,
                    BusinessCheckOutcomes.From(lookup.Outcome), lookup.Message, cancellationToken, providerReference: lookup.ProviderReference);
                return lookup.Outcome == BusinessLookupOutcome.InvalidRequest
                    ? new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.InvalidInput, business, applicant, "invalid_registration_number")
                    : new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.Misconfigured, business, applicant);
            }
        }

        business ??= await CreateBusinessAsync(request.TenantId, number, cancellationToken);
        applicant = await UpsertApplicantAsync(request, business, applicant, cancellationToken);

        if (lookup != null)
        {
            var check = await AddCheckAsync(request, number, business, applicant, BusinessCheckKinds.Lookup, route.Provider.Id, route.Connection.Environment,
                BusinessCheckOutcomes.From(lookup.Outcome), lookup.Message, cancellationToken, providerReference: lookup.ProviderReference);
            ApplyLookup(business, lookup, check, route);
        }

        if (business.Status is BusinessVerificationStatus.Pending or BusinessVerificationStatus.InReview && !business.HasRegistrySnapshot)
        {
            SetStatus(applicant, BusinessVerificationStatus.Pending, BusinessVerificationReasons.ProviderUnavailable, business.StatusMessage);
            await SaveAsync(business, applicant, cancellationToken);
            return new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.ProviderUnavailable, business, applicant);
        }

        await EvaluateAsync(request, business, applicant, cancellationToken);
        await SaveAsync(business, applicant, cancellationToken);
        if (applicant.Status.IsFinal())
            await RaiseCompletedAsync(business, applicant, reviewed: false, cancellationToken);

        return new BusinessVerificationAttempt(Outcome(applicant), business, applicant);
    }

    public async Task<BusinessApplicantView?> FindAsync(string tenantId, string? subjectId, string? journeyId, string country, string registrationNumber,
        string? registrationType, CancellationToken cancellationToken = default)
    {
        var number = BusinessRegistrationNumber.TryParse(country, registrationNumber, registrationType, out _);
        if (number == null) return null;
        var business = await _store.FindBusinessAsync(tenantId, number.Country, number.Canonical, cancellationToken);
        if (business == null) return null;
        var applicant = await FindLinkAsync(tenantId, business.Id, subjectId, journeyId, cancellationToken);
        return applicant == null ? null : new BusinessApplicantView(business, applicant);
    }

    public async Task<BusinessApplicantView?> GetApplicantAsync(string tenantId, string applicantId, CancellationToken cancellationToken = default)
    {
        var applicant = await _store.GetApplicantAsync(tenantId, applicantId, cancellationToken);
        if (applicant == null) return null;
        var business = await _store.GetBusinessAsync(tenantId, applicant.BusinessId, cancellationToken);
        return business == null ? null : new BusinessApplicantView(business, applicant);
    }

    public async Task<BusinessApplicantView?> CompleteHostedSessionAsync(string tenantId, string providerId, string sessionReference, CancellationToken cancellationToken = default)
    {
        var check = await _store.FindSessionAsync(tenantId, providerId, sessionReference, cancellationToken);
        if (check?.ApplicantId == null) return null;

        var view = await GetApplicantAsync(tenantId, check.ApplicantId, cancellationToken);
        if (view == null || check.Outcome != BusinessCheckOutcomes.Open) return view;

        var route = await _router.ResolveProviderAsync(tenantId, providerId, cancellationToken);
        if (route.Provider is not IHostedBusinessVerificationProvider hosted)
            throw new VerificationConfigurationException($"{route.Provider.DisplayName} has no hosted business verification.");

        var result = await hosted.GetBusinessSessionResultAsync(route.Connection, sessionReference, cancellationToken);
        switch (result.State)
        {
            case HostedSessionState.Pending:
                return view;
            case HostedSessionState.Unavailable:
                _logger.LogWarning("{Provider} couldn't report hosted session {Reference}: {Reason}", providerId, sessionReference, result.Message);
                return view;
        }

        var (business, applicant) = (view.Business, view.Applicant);
        for (var attempt = 1; ; attempt++)
        {
            var decides = ApplySessionResult(check, business, applicant, result, route);
            var write = await _store.TryDecideAsync(new VerificationDecisionWrite { CloseSession = check, Business = business, Applicant = applicant }, cancellationToken);
            if (write.Applied)
            {
                if (decides) await AfterOutsideDecisionAsync(business, applicant, reviewed: false, cancellationToken);
                return new BusinessApplicantView(business, applicant);
            }

            if (check.Outcome != BusinessCheckOutcomes.Open) return new BusinessApplicantView(business, applicant);
            if (attempt == MaxDecisionAttempts)
                throw new VerificationDecisionConflictException(
                    $"Hosted session {sessionReference} couldn't be closed: the business or applicant kept changing. Try again.");
        }
    }

    /// <summary>
    /// Applies the provider's answer to the session and, unless a reviewer already decided the
    /// applicant (directly or through the business), to the business and applicant. True when it
    /// decides the applicant.
    /// </summary>
    private bool ApplySessionResult(BusinessVerificationCheck check, VerifiedBusiness business, BusinessApplicant applicant, HostedSessionResult result, BusinessVerificationRoute route)
    {
        var now = Now;
        var reviewerDecided = applicant.Status.IsFinal();
        switch (result.State)
        {
            case HostedSessionState.Approved:
                if (!reviewerDecided)
                {
                    if (result.Record != null) ApplySnapshot(business, result.Record, check.Id, route, now);
                    if (business.ReviewedAt == null || business.Status != BusinessVerificationStatus.Rejected)
                        SetStatus(business, BusinessVerificationStatus.Verified, null, null);
                    applicant.Relationship = BusinessApplicantRelationships.ProviderVerified;
                    SetStatus(applicant, BusinessVerificationStatus.Verified, null, null);
                }
                check.Outcome = BusinessCheckOutcomes.Approved;
                break;

            case HostedSessionState.Rejected:
                if (!reviewerDecided)
                {
                    if (business.Status != BusinessVerificationStatus.Verified)
                        SetStatus(business, BusinessVerificationStatus.Rejected, BusinessVerificationReasons.RejectedByProvider, result.Message);
                    SetStatus(applicant, BusinessVerificationStatus.Rejected, BusinessVerificationReasons.RejectedByProvider, result.Message);
                }
                check.Outcome = BusinessCheckOutcomes.Rejected;
                break;

            case HostedSessionState.Abandoned:
                if (!reviewerDecided)
                {
                    if (business.Status == BusinessVerificationStatus.InReview && business.StatusReason == BusinessVerificationReasons.AwaitingProvider)
                        SetStatus(business, BusinessVerificationStatus.Pending, BusinessVerificationReasons.Abandoned, null);
                    SetStatus(applicant, BusinessVerificationStatus.Failed, BusinessVerificationReasons.Abandoned, result.Message);
                }
                check.Outcome = BusinessCheckOutcomes.Abandoned;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.State, null);
        }

        check.CompletedAt = now;
        check.ProviderReference = result.ProviderReference ?? check.ProviderReference;
        check.Message = Truncate(result.Message);
        if (!reviewerDecided) business.LatestCheckId = check.Id;
        return !reviewerDecided;
    }

    public async Task<HostedSessionLaunch?> ResumeHostedSessionAsync(string tenantId, BusinessApplicantView view, string returnUrl, CancellationToken cancellationToken = default)
    {
        var check = await _store.FindOpenSessionForApplicantAsync(tenantId, view.Applicant.Id, cancellationToken);
        if (check?.ProviderId == null || check.SessionReference == null) return null;
        var route = await _router.ResolveProviderAsync(tenantId, check.ProviderId, cancellationToken);
        return route.Provider is IHostedBusinessVerificationProvider hosted
            ? hosted.ResumeBusinessSession(route.Connection, check.SessionReference, new HostedSessionRequest
            {
                Country = view.Business.Country,
                RegistrationNumber = view.Business.RegistrationNumber,
                RegistrationType = view.Business.RegistrationType,
                BusinessName = view.Applicant.ProvidedBusinessName,
                ReturnUrl = returnUrl,
                Metadata = SessionMetadata(view.Applicant),
            })
            : null;
    }

    public async Task<VerifiedBusiness> ReviewBusinessAsync(string tenantId, string businessId, bool approve, string reviewerId, string? note, CancellationToken cancellationToken = default)
    {
        var business = await _store.GetBusinessAsync(tenantId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Business '{businessId}' not found.");
        if (business.Status is not (BusinessVerificationStatus.Pending or BusinessVerificationStatus.InReview))
            throw AlreadyDecided(business.Status, business.ReviewedAt, business.DecidedAt ?? business.UpdatedAt);

        var now = Now;
        SetStatus(business, approve ? BusinessVerificationStatus.Verified : BusinessVerificationStatus.Rejected,
            approve ? null : BusinessVerificationReasons.RejectedByReviewer, null);
        business.ReviewedBy = reviewerId;
        business.ReviewedAt = now;
        business.ReviewNote = Clean(note);

        var check = new BusinessVerificationCheck
        {
            TenantId = tenantId,
            BusinessId = business.Id,
            Country = business.Country,
            RegistrationNumber = business.RegistrationNumber,
            Kind = BusinessCheckKinds.Review,
            Outcome = approve ? BusinessCheckOutcomes.Approved : BusinessCheckOutcomes.Rejected,
            Message = Truncate(business.ReviewNote),
            CompletedAt = now,
        };
        StampOrigin(check);
        business.LatestCheckId = check.Id;

        var followers = new List<BusinessApplicant>();
        foreach (var applicant in await _store.ListApplicantsForBusinessAsync(tenantId, business.Id, cancellationToken))
        {
            if (applicant.Status is not (BusinessVerificationStatus.Pending or BusinessVerificationStatus.InReview)) continue;

            if (!approve)
            {
                SetStatus(applicant, BusinessVerificationStatus.Rejected, BusinessVerificationReasons.RejectedByReviewer, null);
            }
            else if (applicant.StatusReason == BusinessVerificationReasons.DocumentsRequired
                || (applicant.Status == BusinessVerificationStatus.InReview && applicant.StatusReason == BusinessVerificationReasons.AwaitingReview))
            {
                continue;
            }
            else if (applicant.EvidenceRequirement == ApplicantEvidenceRequirement.None)
            {
                SetStatus(applicant, BusinessVerificationStatus.Verified, null, null);
            }
            else
            {
                SetStatus(applicant, BusinessVerificationStatus.InReview, BusinessVerificationReasons.AwaitingReview, null);
            }
            followers.Add(applicant);
        }

        var write = await _store.TryDecideAsync(new VerificationDecisionWrite { Business = business, Followers = followers, NewChecks = new[] { check } }, cancellationToken);
        if (!write.Applied)
            throw AlreadyDecided(business.Status, business.ReviewedAt, business.DecidedAt ?? business.UpdatedAt);

        foreach (var applicant in write.AppliedFollowers)
            await AfterOutsideDecisionAsync(business, applicant, reviewed: true, cancellationToken);

        return business;
    }

    public async Task<BusinessApplicant> ReviewApplicantAsync(string tenantId, string applicantId, bool approve, string reviewerId, string? note, CancellationToken cancellationToken = default)
    {
        var view = await GetApplicantAsync(tenantId, applicantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Applicant '{applicantId}' not found.");
        var (business, applicant) = (view.Business, view.Applicant);
        if (applicant.Status is not (BusinessVerificationStatus.Pending or BusinessVerificationStatus.InReview))
            throw AlreadyDecided(applicant);
        if (approve && business.Status != BusinessVerificationStatus.Verified)
            throw new InvalidOperationException("Verify the business before approving someone to act for it.");

        var now = Now;
        SetStatus(applicant, approve ? BusinessVerificationStatus.Verified : BusinessVerificationStatus.Rejected,
            approve ? null : BusinessVerificationReasons.RejectedByReviewer, null);
        applicant.ReviewedBy = reviewerId;
        applicant.ReviewedAt = now;
        applicant.ReviewNote = Clean(note);

        var check = new BusinessVerificationCheck
        {
            TenantId = tenantId,
            BusinessId = business.Id,
            ApplicantId = applicant.Id,
            Country = business.Country,
            RegistrationNumber = business.RegistrationNumber,
            Kind = BusinessCheckKinds.Review,
            Outcome = approve ? BusinessCheckOutcomes.Approved : BusinessCheckOutcomes.Rejected,
            Message = Truncate(applicant.ReviewNote),
            CompletedAt = now,
        };
        StampOrigin(check);

        var write = await _store.TryDecideAsync(new VerificationDecisionWrite { Applicant = applicant, NewChecks = new[] { check } }, cancellationToken);
        if (!write.Applied) throw AlreadyDecided(applicant);

        await AfterOutsideDecisionAsync(business, applicant, reviewed: true, cancellationToken);
        return applicant;
    }

    private static VerificationDecisionConflictException AlreadyDecided(BusinessApplicant applicant) =>
        AlreadyDecided(applicant.Status, applicant.ReviewedAt, applicant.DecidedAt ?? applicant.UpdatedAt);

    private static VerificationDecisionConflictException AlreadyDecided(BusinessVerificationStatus status, DateTime? reviewedAt, DateTime changedAt)
    {
        var label = status switch
        {
            BusinessVerificationStatus.Pending => "Pending",
            BusinessVerificationStatus.InReview => "In review",
            BusinessVerificationStatus.Verified => "Verified",
            BusinessVerificationStatus.Rejected => "Rejected",
            BusinessVerificationStatus.Failed => "Failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
        var by = reviewedAt != null ? "by a reviewer" : "by the verification provider";
        var at = (reviewedAt ?? changedAt).ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        return new VerificationDecisionConflictException(status.IsFinal()
            ? $"This was already decided ({label} {by} at {at}). Refresh to see the latest."
            : $"This has already changed (now {label}, at {at}). Refresh to see the latest.");
    }

    public async Task RequestOutcomeEmailAsync(string tenantId, string applicantId, string email, string? resumeUrl, CancellationToken cancellationToken = default)
    {
        if (!_notifier.CanEmail)
            throw new InvalidOperationException("No email sender is registered, so outcomes can't be emailed.");
        var applicant = await _store.GetApplicantAsync(tenantId, applicantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Applicant '{applicantId}' not found.");
        applicant.NotifyEmail = email.Trim();
        applicant.ResumeUrl = resumeUrl;
        await _store.UpdateApplicantAsync(applicant, cancellationToken);
    }

    public async Task ClaimJourneyApplicantsAsync(string tenantId, string journeyId, string subjectId, CancellationToken cancellationToken = default)
    {
        foreach (var unclaimed in await _store.ListUnclaimedApplicantsForJourneyAsync(tenantId, journeyId, cancellationToken))
        {
            var business = await _store.GetBusinessAsync(tenantId, unclaimed.BusinessId, cancellationToken);
            if (business == null) continue;

            var link = unclaimed;
            var existing = await _store.FindApplicantForSubjectAsync(tenantId, unclaimed.BusinessId, subjectId, cancellationToken);
            if (existing == null)
            {
                unclaimed.SubjectId = subjectId;
                unclaimed.UpdatedAt = Now;
                await _store.UpdateApplicantAsync(unclaimed, cancellationToken);
            }
            else
            {
                // The user already acts for this business; the journey's newer submission replaces it.
                existing.JourneyId = unclaimed.JourneyId;
                existing.StepId = unclaimed.StepId;
                existing.Relationship = unclaimed.Relationship;
                existing.EvidenceRequirement = unclaimed.EvidenceRequirement;
                existing.ProvidedBusinessName = unclaimed.ProvidedBusinessName;
                existing.NameMatched = unclaimed.NameMatched;
                existing.DirectorMatched = unclaimed.DirectorMatched;
                existing.MatchedPersonName = unclaimed.MatchedPersonName;
                existing.MatchedPersonRole = unclaimed.MatchedPersonRole;
                existing.DocumentIds = existing.DocumentIds.Concat(unclaimed.DocumentIds).Distinct().ToList();
                existing.Status = unclaimed.Status;
                existing.StatusReason = unclaimed.StatusReason;
                existing.StatusMessage = unclaimed.StatusMessage;
                existing.NotifyEmail = unclaimed.NotifyEmail ?? existing.NotifyEmail;
                existing.ResumeUrl = unclaimed.ResumeUrl ?? existing.ResumeUrl;
                existing.DecidedAt = unclaimed.DecidedAt;
                existing.UpdatedAt = Now;
                await _store.RemoveApplicantAsync(unclaimed, cancellationToken);
                await _store.UpdateApplicantAsync(existing, cancellationToken);
                link = existing;
            }

            await ClaimDocumentsAsync(tenantId, journeyId, subjectId, link, cancellationToken);

            await _events.RaiseAsync(new BusinessApplicantLinkedEvent
            {
                TenantId = tenantId,
                BusinessId = business.Id,
                ApplicantId = link.Id,
                SubjectId = subjectId,
                Status = link.Status.ToWire(),
                Relationship = link.Relationship,
                Country = business.Country,
                RegistrationNumber = business.RegistrationNumber,
                RegisteredName = business.RegisteredName,
            }, cancellationToken);
        }
    }

    private async Task ClaimDocumentsAsync(string tenantId, string journeyId, string subjectId, BusinessApplicant link, CancellationToken cancellationToken)
    {
        var pending = JourneyUploads.PendingUploader(journeyId);
        foreach (var id in link.DocumentIds)
        {
            var file = await _attachments.GetByIdAsync(id, cancellationToken);
            if (file == null || file.TenantId != tenantId || file.UploadedByUserId != pending) continue;
            file.UploadedByUserId = subjectId;
            file.CreatedBy = subjectId;
            if (file.EntityId != link.Id && file.EntityType == VerificationFileEntityTypes.BusinessApplicant)
                file.EntityId = link.Id;
            await _attachments.UpdateAsync(file, cancellationToken);
        }
    }

    private async Task<BusinessVerificationAttempt> StartHostedAsync(BusinessVerificationRequest request, BusinessRegistrationNumber number,
        BusinessVerificationRoute route, VerifiedBusiness? business, BusinessApplicant? applicant, CancellationToken cancellationToken)
    {
        if (route.Provider is not IHostedBusinessVerificationProvider hosted
            || !hosted.HostedBusinessCountries.Contains(number.Country, StringComparer.OrdinalIgnoreCase))
        {
            var message = $"{route.Provider.DisplayName} has no hosted business verification for {number.Country}.";
            _logger.LogError("Business verification for tenant {TenantId}: {Reason}", request.TenantId, message);
            await AddCheckAsync(request, number, business, applicant, BusinessCheckKinds.HostedSession, route.Provider.Id, route.Connection.Environment,
                BusinessCheckOutcomes.Misconfigured, message, cancellationToken);
            return new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.Misconfigured, business, applicant);
        }

        var returnUrl = request.ReturnUrl ?? throw new InvalidOperationException("A hosted verification needs the journey's return URL.");
        business ??= await CreateBusinessAsync(request.TenantId, number, cancellationToken);
        applicant = await UpsertApplicantAsync(request, business, applicant, cancellationToken);

        HostedSessionStart session;
        try
        {
            session = await hosted.StartBusinessSessionAsync(route.Connection, new HostedSessionRequest
            {
                Country = number.Country,
                RegistrationNumber = number.Canonical,
                RegistrationType = number.Type,
                BusinessName = Clean(request.BusinessName),
                ReturnUrl = returnUrl,
                Metadata = SessionMetadata(applicant),
            }, cancellationToken);
        }
        catch (VerificationConfigurationException ex)
        {
            _logger.LogError("{Provider} can't start a hosted session for tenant {TenantId}: {Reason}", route.Provider.Id, request.TenantId, ex.Message);
            await AddCheckAsync(request, number, business, applicant, BusinessCheckKinds.HostedSession, route.Provider.Id, route.Connection.Environment,
                BusinessCheckOutcomes.Misconfigured, ex.Message, cancellationToken);
            return new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.Misconfigured, business, applicant);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Provider} couldn't start a hosted session for tenant {TenantId}", route.Provider.Id, request.TenantId);
            await AddCheckAsync(request, number, business, applicant, BusinessCheckKinds.HostedSession, route.Provider.Id, route.Connection.Environment,
                BusinessCheckOutcomes.ProviderUnavailable, ex.Message, cancellationToken);
            SetStatus(applicant, BusinessVerificationStatus.Pending, BusinessVerificationReasons.ProviderUnavailable, ex.Message);
            await SaveAsync(business, applicant, cancellationToken);
            return new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.ProviderUnavailable, business, applicant);
        }

        var check = await AddCheckAsync(request, number, business, applicant, BusinessCheckKinds.HostedSession, route.Provider.Id, route.Connection.Environment,
            BusinessCheckOutcomes.Open, null, cancellationToken, sessionReference: session.Reference);
        business.LatestCheckId = check.Id;
        if (business.Status != BusinessVerificationStatus.Verified)
            SetStatus(business, BusinessVerificationStatus.InReview, BusinessVerificationReasons.AwaitingProvider, null);
        applicant.Relationship = BusinessApplicantRelationships.ProviderVerified;
        SetStatus(applicant, BusinessVerificationStatus.InReview, BusinessVerificationReasons.AwaitingProvider, null);
        await SaveAsync(business, applicant, cancellationToken);
        return new BusinessVerificationAttempt(BusinessVerificationAttemptOutcome.HostedSessionStarted, business, applicant, Session: session, SessionProviderId: route.Provider.Id);
    }

    /// <summary>
    /// Decides the applicant against a business that has an answer: rejected businesses
    /// reject the applicant (unless only inactivity, which the step may accept), then the
    /// name match, then the step's evidence rule.
    /// </summary>
    private async Task EvaluateAsync(BusinessVerificationRequest request, VerifiedBusiness business, BusinessApplicant applicant, CancellationToken cancellationToken)
    {
        applicant.NameMatched = null;
        applicant.DirectorMatched = null;
        applicant.MatchedPersonName = null;
        applicant.MatchedPersonRole = null;

        if (business.Status == BusinessVerificationStatus.Rejected
            && !(business.StatusReason == BusinessVerificationReasons.Inactive && !request.RequireActive))
        {
            SetStatus(applicant, BusinessVerificationStatus.Rejected, business.StatusReason, business.StatusMessage);
            return;
        }

        if (request.RequireNameMatch && business.RegisteredName != null)
        {
            applicant.NameMatched = BusinessNameMatcher.BusinessNamesMatch(applicant.ProvidedBusinessName, business.RegisteredName);
            if (applicant.NameMatched == false)
            {
                SetStatus(applicant, BusinessVerificationStatus.Rejected, BusinessVerificationReasons.NameMismatch, null);
                return;
            }
        }

        var wantsDirector = request.Evidence is ApplicantEvidenceRequirement.DirectorMatch or ApplicantEvidenceRequirement.DirectorOrDocuments;
        BusinessPerson? director = null;
        if (wantsDirector && business.HasRegistrySnapshot)
        {
            director = business.People
                .Where(p => request.DirectorRoles.Count == 0
                    || request.DirectorRoles.Any(role => p.Role?.Contains(role, StringComparison.OrdinalIgnoreCase) == true))
                .FirstOrDefault(p => BusinessNameMatcher.PersonNameMatches(request.Applicant?.FirstName, request.Applicant?.LastName, p.Name));
            applicant.DirectorMatched = director != null;
            applicant.MatchedPersonName = director?.Name;
            applicant.MatchedPersonRole = director?.Role;
        }

        if (director != null)
        {
            applicant.Relationship = BusinessApplicantRelationships.RegistryDirectorMatch;
            SetStatus(applicant, BusinessVerificationStatus.Verified, null, null);
            return;
        }

        applicant.Relationship = BusinessApplicantRelationships.AuthorizedRepresentativeDeclared;
        switch (request.Evidence)
        {
            case ApplicantEvidenceRequirement.None:
                SetStatus(applicant, BusinessVerificationStatus.Verified, null, null);
                return;

            case ApplicantEvidenceRequirement.DirectorMatch when !business.HasRegistrySnapshot:
                // A reviewer verified the business without a registry snapshot; the reviewer settles the director question too.
                SetStatus(applicant, BusinessVerificationStatus.InReview, BusinessVerificationReasons.AwaitingReview, null);
                return;

            case ApplicantEvidenceRequirement.DirectorMatch:
                SetStatus(applicant, BusinessVerificationStatus.Rejected, BusinessVerificationReasons.DirectorMismatch,
                    business.People.Count == 0 ? "The provider returned no directors for the business." : null);
                return;

            default:
                if (request.Documents.Count == 0 && applicant.DocumentIds.Count == 0)
                {
                    SetStatus(applicant, BusinessVerificationStatus.Pending, BusinessVerificationReasons.DocumentsRequired, null);
                    return;
                }
                await StoreDocumentsAsync(request, applicant, cancellationToken);
                SetStatus(applicant, BusinessVerificationStatus.InReview, BusinessVerificationReasons.AwaitingReview, null);
                return;
        }
    }

    private async Task StoreDocumentsAsync(BusinessVerificationRequest request, BusinessApplicant applicant, CancellationToken cancellationToken)
    {
        if (request.Documents.Count == 0) return;
        var ids = applicant.DocumentIds.ToList();
        foreach (var document in request.Documents)
        {
            var saved = await _uploads.SaveAsync(new JourneyUploadRequest
            {
                TenantId = request.TenantId,
                JourneyId = request.JourneyId ?? throw new InvalidOperationException("Documents can only be uploaded from a journey."),
                UserId = request.SubjectId,
                File = document,
                EntityType = VerificationFileEntityTypes.BusinessApplicant,
                EntityId = applicant.Id,
            }, cancellationToken);
            ids.Add(saved.Id);
        }
        applicant.DocumentIds = ids;
    }

    private async Task<bool> CanReuseSnapshotAsync(string tenantId, VerifiedBusiness business, BusinessApplicant? applicant, CancellationToken cancellationToken)
    {
        if (!business.HasRegistrySnapshot) return false;

        // The applicant was asked for documents moments ago against this snapshot.
        if (applicant is { Status: BusinessVerificationStatus.Pending, StatusReason: BusinessVerificationReasons.DocumentsRequired })
            return true;

        var settings = await _settings.GetAsync(tenantId, cancellationToken);
        return settings.BusinessReuseMaxAgeDays > 0 && business.RegistryCheckedAt >= Now.AddDays(-settings.BusinessReuseMaxAgeDays);
    }

    private void ApplyLookup(VerifiedBusiness business, BusinessLookupResult lookup, BusinessVerificationCheck check, BusinessVerificationRoute route)
    {
        business.LatestCheckId = check.Id;
        var decidedByReviewer = business.ReviewedAt != null;
        switch (lookup.Outcome)
        {
            case BusinessLookupOutcome.Found:
                ApplySnapshot(business, lookup.Record!, check.Id, route, Now);
                if (decidedByReviewer) return;
                if (lookup.Record!.ActivityStatus == BusinessActivityStatus.Inactive)
                    SetStatus(business, BusinessVerificationStatus.Rejected, BusinessVerificationReasons.Inactive, $"The registry lists the business as '{lookup.Record.RawStatus}'.");
                else
                    SetStatus(business, BusinessVerificationStatus.Verified, null, null);
                return;

            case BusinessLookupOutcome.NotFound:
                if (!decidedByReviewer)
                    SetStatus(business, BusinessVerificationStatus.Rejected, BusinessVerificationReasons.NotFound, lookup.Message);
                return;

            case BusinessLookupOutcome.ProviderUnavailable:
                if (business.Status is not (BusinessVerificationStatus.Verified or BusinessVerificationStatus.Rejected))
                    SetStatus(business, BusinessVerificationStatus.Pending, BusinessVerificationReasons.ProviderUnavailable, lookup.Message);
                return;
        }
    }

    private static void ApplySnapshot(VerifiedBusiness business, BusinessRegistryRecord record, string checkId, BusinessVerificationRoute route, DateTime now)
    {
        business.RegisteredName = record.RegisteredName;
        business.EntityType = record.EntityType;
        business.RegistrationDate = record.RegistrationDate;
        business.RegisteredAddress = record.Address;
        business.ActivityStatus = record.ActivityStatus;
        business.RawStatus = record.RawStatus;
        business.People = record.People;
        business.RegistryCheckedAt = now;
        business.RegistryCheckId = checkId;
        business.RegistryProviderId = route.Provider.Id;
        business.RegistryEnvironment = route.Connection.Environment;
    }

    private async Task<VerifiedBusiness> CreateBusinessAsync(string tenantId, BusinessRegistrationNumber number, CancellationToken cancellationToken)
    {
        var business = new VerifiedBusiness
        {
            TenantId = tenantId,
            Country = number.Country,
            RegistrationNumber = number.Canonical,
            RegistrationType = number.Type,
            Status = BusinessVerificationStatus.Pending,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        await _store.AddBusinessAsync(business, cancellationToken);
        return business;
    }

    private async Task<BusinessApplicant?> FindLinkAsync(string tenantId, string? businessId, string? subjectId, string? journeyId, CancellationToken cancellationToken)
    {
        if (businessId == null) return null;
        if (!string.IsNullOrEmpty(subjectId))
        {
            var mine = await _store.FindApplicantForSubjectAsync(tenantId, businessId, subjectId, cancellationToken);
            if (mine != null) return mine;
        }
        return string.IsNullOrEmpty(journeyId) ? null : await _store.FindUnclaimedApplicantForJourneyAsync(tenantId, businessId, journeyId, cancellationToken);
    }

    private async Task<BusinessApplicant> UpsertApplicantAsync(BusinessVerificationRequest request, VerifiedBusiness business, BusinessApplicant? applicant, CancellationToken cancellationToken)
    {
        var now = Now;
        if (applicant == null)
        {
            applicant = new BusinessApplicant
            {
                TenantId = request.TenantId,
                BusinessId = business.Id,
                SubjectId = Clean(request.SubjectId),
                JourneyId = request.JourneyId,
                StepId = request.StepId,
                EvidenceRequirement = request.Evidence,
                ProvidedBusinessName = Clean(request.BusinessName),
                Status = BusinessVerificationStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await _store.AddApplicantAsync(applicant, cancellationToken);
            return applicant;
        }

        applicant.SubjectId ??= Clean(request.SubjectId);
        applicant.JourneyId = request.JourneyId ?? applicant.JourneyId;
        applicant.StepId = request.StepId ?? applicant.StepId;
        applicant.EvidenceRequirement = request.Evidence;
        applicant.ProvidedBusinessName = Clean(request.BusinessName) ?? applicant.ProvidedBusinessName;
        applicant.DecidedAt = null;
        applicant.ReviewedAt = null;
        applicant.ReviewedBy = null;
        applicant.ReviewNote = null;
        return applicant;
    }

    private void SetStatus(VerifiedBusiness business, BusinessVerificationStatus status, string? reason, string? message)
    {
        business.Status = status;
        business.StatusReason = reason;
        business.StatusMessage = Truncate(message);
        business.UpdatedAt = Now;
        business.DecidedAt = status.IsFinal() ? Now : null;
    }

    private void SetStatus(BusinessApplicant applicant, BusinessVerificationStatus status, string? reason, string? message)
    {
        applicant.Status = status;
        applicant.StatusReason = reason;
        applicant.StatusMessage = Truncate(message);
        applicant.UpdatedAt = Now;
        applicant.DecidedAt = status.IsFinal() ? Now : null;
    }

    private async Task SaveAsync(VerifiedBusiness business, BusinessApplicant applicant, CancellationToken cancellationToken)
    {
        await _store.UpdateBusinessAsync(business, cancellationToken);
        await _store.UpdateApplicantAsync(applicant, cancellationToken);
    }

    /// <summary>
    /// After a decision made outside the applicant's browser (webhook, reviewer): the event
    /// on a final outcome, a nudge to a journey still waiting on the step, and the email
    /// the applicant asked for.
    /// </summary>
    private async Task AfterOutsideDecisionAsync(VerifiedBusiness business, BusinessApplicant applicant, bool reviewed, CancellationToken cancellationToken)
    {
        if (applicant.Status.IsFinal())
            await RaiseCompletedAsync(business, applicant, reviewed, cancellationToken);

        if (!string.IsNullOrEmpty(applicant.JourneyId))
            await JourneyStatusRevision.BumpAsync(_journeys, applicant.JourneyId, cancellationToken);

        await _notifier.SendOutcomeAsync(business, applicant, cancellationToken);
    }

    private Task RaiseCompletedAsync(VerifiedBusiness business, BusinessApplicant applicant, bool reviewed, CancellationToken cancellationToken) =>
        _events.RaiseAsync(new BusinessVerificationCompletedEvent
        {
            TenantId = business.TenantId,
            BusinessId = business.Id,
            ApplicantId = applicant.Id,
            Status = applicant.Status.ToWire(),
            Reason = applicant.StatusReason,
            SubjectId = applicant.SubjectId,
            Relationship = applicant.Relationship,
            BusinessStatus = business.Status.ToWire(),
            Country = business.Country,
            RegistrationNumber = business.RegistrationNumber,
            RegisteredName = business.RegisteredName,
            ProviderId = business.RegistryProviderId,
            Reviewed = reviewed,
        }, cancellationToken);

    private async Task<BusinessVerificationCheck> AddCheckAsync(
        BusinessVerificationRequest request, BusinessRegistrationNumber number, VerifiedBusiness? business, BusinessApplicant? applicant,
        string kind, string? providerId, VerificationEnvironment? environment, string outcome, string? message, CancellationToken cancellationToken,
        string? providerReference = null, string? sessionReference = null, string? reusedFrom = null)
    {
        var check = new BusinessVerificationCheck
        {
            TenantId = request.TenantId,
            BusinessId = business?.Id,
            ApplicantId = applicant?.Id,
            Country = number.Country,
            RegistrationNumber = number.Canonical,
            Kind = kind,
            ProviderId = providerId,
            Environment = environment,
            Outcome = outcome,
            Message = Truncate(message),
            ProviderReference = providerReference,
            SessionReference = sessionReference,
            JourneyId = request.JourneyId,
            StepId = request.StepId,
            ReusedFromCheckId = reusedFrom,
            CreatedAt = Now,
            CompletedAt = outcome == BusinessCheckOutcomes.Open ? null : Now,
        };
        StampOrigin(check);
        await _store.AddCheckAsync(check, cancellationToken);
        return check;
    }

    private static void StampOrigin(BusinessVerificationCheck check)
    {
        var origin = EventOriginScope.Current;
        check.OriginKind = origin?.Kind;
        check.OriginId = origin?.Id;
        check.OriginName = origin?.Name;
    }

    private static IReadOnlyDictionary<string, string> SessionMetadata(BusinessApplicant applicant) =>
        new Dictionary<string, string> { ["applicant_id"] = applicant.Id };

    private static BusinessVerificationAttemptOutcome Outcome(BusinessApplicant applicant) => applicant.Status switch
    {
        BusinessVerificationStatus.Verified => BusinessVerificationAttemptOutcome.Verified,
        BusinessVerificationStatus.InReview => BusinessVerificationAttemptOutcome.InReview,
        BusinessVerificationStatus.Pending when applicant.StatusReason == BusinessVerificationReasons.DocumentsRequired => BusinessVerificationAttemptOutcome.DocumentsRequired,
        BusinessVerificationStatus.Pending => BusinessVerificationAttemptOutcome.ProviderUnavailable,
        _ => BusinessVerificationAttemptOutcome.Rejected,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? message) => message is { Length: > 1000 } ? message[..1000] : message;
}
