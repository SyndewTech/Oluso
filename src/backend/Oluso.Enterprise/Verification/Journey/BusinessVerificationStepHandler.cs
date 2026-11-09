using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Locale;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Verification.Providers;
using Oluso.Enterprise.Verification.Services;

namespace Oluso.Enterprise.Verification.Journey;

/// <summary>
/// Verifies a business and the applicant's right to act for it, through the
/// organisation's verification provider for the country.
/// <para>Inputs come from the step's form, or from journey data an earlier step wrote (the
/// *Claim settings), in which case no form is shown unless the check needs more.</para>
/// <para>Resumable: on entry the step looks up the applicant's existing link to the
/// business (by user, or by journey before sign-up). Verified links continue with their
/// claims; pending and in-review links wait (or take the pending branch); rejected ones
/// show the reason and, when allowed, a retry.</para>
/// <para>Outputs: kyb_status, kyb_business_id, kyb_applicant_id, kyb_business_name,
/// kyb_registration_number, kyb_country, kyb_relationship. Branches: verified,
/// not_verified, pending, provider_unavailable.</para>
/// </summary>
public class BusinessVerificationStepHandler : IStepHandler, IStepConfigurationTester
{
    public const string StepTypeName = "business_verification";
    public const string FormView = "Journey/_BusinessVerification";
    public const string WaitingView = "Journey/_BusinessVerificationWaiting";
    public const string HostedView = "Journey/_BusinessVerificationHosted";
    public const string RejectedView = "Journey/_BusinessVerificationRejected";

    public const string VerifiedBranch = "verified";
    public const string NotVerifiedBranch = "not_verified";
    public const string PendingBranch = "pending";
    public const string ProviderUnavailableBranch = "provider_unavailable";

    public const string DocumentsField = "documents";

    /// <summary>Journey data: the applicant link this journey is working on.</summary>
    public const string ApplicantDataKey = "kyb_applicant_id";
    internal const string SessionReferenceDataKey = "_kyb_session_ref";
    internal const string SessionProviderDataKey = "_kyb_session_provider";

    public static readonly string[] DefaultDirectorRoles = { "director", "proprietor", "partner", "trustee" };
    public static readonly string[] DefaultDocumentTypes = { FileContentSniffer.Pdf, FileContentSniffer.Jpeg, FileContentSniffer.Png };
    public const int DefaultMaxDocumentSizeMb = 10;
    public const int DefaultMaxDocuments = 3;
    public const int DefaultPendingWaitSeconds = 180;

    public string StepType => StepTypeName;

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var errors = new List<string>();
        if (configuration == null
            || !configuration.TryGetValue("allowedCountries", out var countries)
            || !StepConfigurationHelpers.IsNonEmptyList(countries))
        {
            errors.Add("Choose at least one country businesses can be verified in");
        }
        return Task.FromResult(errors.Count == 0
            ? StepConfigurationValidationResult.Valid()
            : StepConfigurationValidationResult.Invalid(errors));
    }

    /// <summary>
    /// For every allowed country: a provider is routed and enabled, its credentials
    /// authenticate (through the provider's free endpoint; no lookup is bought), and the
    /// step's mode and evidence rule are ones it can serve.
    /// </summary>
    public async Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var countries = AllowedCountries(context);
        if (countries.Count == 0)
            return StepConfigurationTestResult.Failed("Choose at least one country businesses can be verified in");
        if (string.IsNullOrEmpty(context.TenantId))
            return StepConfigurationTestResult.Failed("The step can only be tested inside an organisation");

        var router = context.ServiceProvider.GetRequiredService<IBusinessVerificationRouter>();
        var mode = Mode(context);
        var evidence = Evidence(context);
        var details = new List<string>();
        var ok = true;
        var checkedProviders = new Dictionary<string, VerificationConnectionCheck>(StringComparer.OrdinalIgnoreCase);
        foreach (var country in countries)
        {
            BusinessVerificationRoute route;
            try
            {
                route = await router.ResolveAsync(context.TenantId, country, cancellationToken);
            }
            catch (VerificationConfigurationException ex)
            {
                ok = false;
                details.Add($"{country}: {ex.Message}");
                continue;
            }

            if (!checkedProviders.TryGetValue(route.Provider.Id, out var check))
            {
                check = await route.Provider.CheckConnectionAsync(route.Connection, cancellationToken);
                checkedProviders[route.Provider.Id] = check;
            }
            ok &= check.Success;
            details.Add($"{country}: {route.Provider.DisplayName} ({route.Connection.Environment}): {check.Message}");

            if (mode == BusinessVerificationMode.Hosted)
            {
                if (route.Provider is not IHostedBusinessVerificationProvider hosted
                    || !hosted.HostedBusinessCountries.Contains(country, StringComparer.OrdinalIgnoreCase))
                {
                    ok = false;
                    details.Add($"{country}: {route.Provider.DisplayName} has no hosted business verification. Use the lookup mode or route {country} to a provider that has.");
                }
                else
                {
                    var missing = route.Provider.CredentialFields
                        .Where(f => !f.Required && (!route.Connection.Credentials.TryGetValue(f.Name, out var v) || string.IsNullOrWhiteSpace(v)))
                        .Select(f => f.Label)
                        .ToList();
                    if (missing.Count > 0)
                    {
                        ok = false;
                        details.Add($"{country}: hosted verification needs {string.Join(", ", missing)} saved for {route.Provider.DisplayName}.");
                    }
                }
            }

            if (mode == BusinessVerificationMode.Lookup
                && evidence is ApplicantEvidenceRequirement.DirectorMatch or ApplicantEvidenceRequirement.DirectorOrDocuments
                && route.Provider.Id == DojahVerificationProvider.ProviderId
                && route.Connection.Options.TryGetValue(DojahVerificationProvider.LookupOption, out var tier)
                && string.Equals(tier, DojahVerificationProvider.BasicLookup, StringComparison.OrdinalIgnoreCase))
            {
                ok = false;
                details.Add($"{country}: the step matches applicants against directors, but Dojah's basic lookup returns no directors. Switch the CAC lookup to advance.");
            }
        }

        return StepConfigurationTestResult.Of(ok,
            ok ? "Every allowed country has a working verification provider" : "Some allowed countries can't be verified",
            details);
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<BusinessVerificationStepHandler>>();
        var allowed = AllowedCountries(context);
        if (allowed.Count == 0)
        {
            logger.LogError("Business verification step {StepId} has no allowed countries", context.StepId);
            return StepHandlerResult.Fail("config_error", "Business verification is not configured correctly");
        }
        if (string.IsNullOrEmpty(context.TenantId))
        {
            logger.LogError("Business verification step {StepId} ran outside an organisation", context.StepId);
            return StepHandlerResult.Fail("config_error", "Business verification is only available inside an organisation");
        }

        var service = context.ServiceProvider.GetRequiredService<IBusinessVerificationService>();
        var tenantId = context.TenantId;

        if (context.Input?.Action == "callback")
            return await ResumeFromProviderAsync(context, service, logger, cancellationToken);

        var action = context.GetInput("action");
        var current = context.GetDataString(ApplicantDataKey) is { Length: > 0 } applicantId
            ? await service.GetApplicantAsync(tenantId, applicantId, cancellationToken)
            : null;

        if (action == "start_over")
        {
            Forget(context);
            return ShowForm(context, allowed, current == null ? null : Existing(current.Business, current.Applicant), null);
        }

        if (current != null)
        {
            switch (action)
            {
                case "notify":
                    return await RequestEmailAsync(context, service, current, cancellationToken);
                case "resume_hosted":
                    return await ReopenSessionAsync(context, service, current, cancellationToken);
                case null or "refresh":
                    return await ResolveAsync(context, current, cancellationToken);
            }
        }

        var submitted = context.HasInput && action is null or "submit";
        var input = submitted ? FromForm(context) : FromJourneyData(context);
        if (input == null || input.RegistrationNumber == null)
            return ShowForm(context, allowed, input, submitted ? "business_verification_error_number_required" : null);

        var country = input.Country ?? (allowed.Count == 1 ? allowed[0] : null);
        if (country == null || !allowed.Contains(country, StringComparer.OrdinalIgnoreCase))
            return ShowForm(context, allowed, input, "business_verification_error_country");
        input = input with { Country = country };
        var requireName = context.GetConfig("requireNameMatch", true);
        if (requireName && input.BusinessName == null)
            return ShowForm(context, allowed, input, "business_verification_error_name_required");

        var existing = await service.FindAsync(tenantId, context.UserId, context.JourneyId, country, input.RegistrationNumber, input.RegistrationType, cancellationToken);
        if (existing != null && !ShouldSubmitAgain(context, existing, submitted))
            return await ResolveAsync(context, existing, cancellationToken);

        var documents = new List<AcceptedJourneyFile>();
        var files = context.GetInputFiles(DocumentsField);
        if (files.Count > 0)
        {
            var policy = DocumentPolicy(context);
            var uploads = context.ServiceProvider.GetRequiredService<IJourneyFileUploadService>();
            var error = await uploads.ValidateAsync(files, policy, documents, cancellationToken);
            if (error != null)
                return ShowForm(context, allowed, input, $"journey_upload_error_{error}", askForDocuments: true);
        }

        var mode = Mode(context);
        var attempt = await service.SubmitAsync(new BusinessVerificationRequest
        {
            TenantId = tenantId,
            SubjectId = context.UserId,
            JourneyId = context.JourneyId,
            StepId = context.StepId,
            Country = country,
            RegistrationNumber = input.RegistrationNumber,
            RegistrationType = input.RegistrationType,
            BusinessName = input.BusinessName,
            Mode = mode,
            ReturnUrl = mode == BusinessVerificationMode.Hosted ? Absolute(context, $"/journey/{context.JourneyId}/callback") : null,
            RequireNameMatch = requireName,
            RequireActive = context.GetConfig("requireActive", true),
            Evidence = Evidence(context),
            Applicant = Evidence(context) is ApplicantEvidenceRequirement.DirectorMatch or ApplicantEvidenceRequirement.DirectorOrDocuments
                ? await ApplicantAsync(context, cancellationToken)
                : null,
            DirectorRoles = context.GetConfig<List<string>>("directorRoles", null) is { Count: > 0 } roles ? roles : DefaultDirectorRoles,
            Documents = documents,
        }, cancellationToken);

        switch (attempt.Outcome)
        {
            case BusinessVerificationAttemptOutcome.InvalidInput:
                return ShowForm(context, allowed, input,
                    attempt.InputError == "registration_type_required"
                        ? "business_verification_error_type_required"
                        : "business_verification_error_invalid_number");

            case BusinessVerificationAttemptOutcome.Misconfigured:
                return StepHandlerResult.Fail("business_verification_unavailable",
                    "Business verification isn't available right now. Please contact the organisation.");

            case BusinessVerificationAttemptOutcome.HostedSessionStarted:
                context.SetData(ApplicantDataKey, attempt.Applicant!.Id);
                context.SetData(SessionReferenceDataKey, attempt.Session!.Reference);
                context.SetData(SessionProviderDataKey, attempt.SessionProviderId!);
                return Launch(context, attempt.Session.Launch, new BusinessApplicantView(attempt.Business!, attempt.Applicant));

            default:
                return await ResolveAsync(context, new BusinessApplicantView(attempt.Business!, attempt.Applicant!), cancellationToken);
        }
    }

    /// <summary>
    /// Rejected or failed links are tried again when the applicant submits the form again and
    /// retries are allowed; a link waiting on documents is submitted again with them.
    /// Everything else resumes where it stands.
    /// </summary>
    private static bool ShouldSubmitAgain(StepExecutionContext context, BusinessApplicantView existing, bool submitted)
    {
        var applicant = existing.Applicant;
        if (!submitted) return false;
        return applicant.Status switch
        {
            BusinessVerificationStatus.Rejected or BusinessVerificationStatus.Failed => context.GetConfig("allowRetry", true),
            BusinessVerificationStatus.Pending => true,
            _ => false,
        };
    }

    private async Task<StepHandlerResult> ResumeFromProviderAsync(StepExecutionContext context, IBusinessVerificationService service, ILogger logger, CancellationToken cancellationToken)
    {
        var reference = context.GetDataString(SessionReferenceDataKey);
        var providerId = context.GetDataString(SessionProviderDataKey);
        if (string.IsNullOrEmpty(reference) || string.IsNullOrEmpty(providerId))
        {
            logger.LogWarning("Journey {JourneyId} returned from a verification provider without an open session", context.JourneyId);
            return ShowForm(context, AllowedCountries(context), null, null);
        }

        // The return URL is the browser's; only the provider's own answer is trusted.
        var returned = context.GetInput("reference_id");
        if (returned != null && !string.Equals(returned, reference, StringComparison.Ordinal))
            logger.LogWarning("Journey {JourneyId} returned with reference {Returned}, not its session {Reference}; using the session", context.JourneyId, returned, reference);

        BusinessApplicantView? view;
        try
        {
            view = await service.CompleteHostedSessionAsync(context.TenantId!, providerId, reference, cancellationToken);
        }
        catch (VerificationConfigurationException ex)
        {
            logger.LogError("Journey {JourneyId} can't read hosted session {Reference}: {Reason}", context.JourneyId, reference, ex.Message);
            return StepHandlerResult.Fail("business_verification_unavailable",
                "Business verification isn't available right now. Please contact the organisation.");
        }

        return view == null
            ? ShowForm(context, AllowedCountries(context), null, null)
            : await ResolveAsync(context, view, cancellationToken);
    }

    /// <summary>Acts on where the link stands.</summary>
    private async Task<StepHandlerResult> ResolveAsync(StepExecutionContext context, BusinessApplicantView view, CancellationToken cancellationToken)
    {
        var (business, applicant) = (view.Business, view.Applicant);
        context.SetData(ApplicantDataKey, applicant.Id);
        var outputs = Outputs(business, applicant);

        switch (applicant.Status)
        {
            case BusinessVerificationStatus.Verified:
                return StepHandlerResult.SuccessOrBranch(context, outputs, VerifiedBranch);

            case BusinessVerificationStatus.Pending when applicant.StatusReason == BusinessVerificationReasons.DocumentsRequired:
                return ShowForm(context, AllowedCountries(context), Existing(business, applicant), "business_verification_documents_required", askForDocuments: true);

            case BusinessVerificationStatus.Pending when applicant.StatusReason == BusinessVerificationReasons.ProviderUnavailable:
                if (HasBranch(context, ProviderUnavailableBranch))
                    return StepHandlerResult.Branch(ProviderUnavailableBranch, outputs);
                return ShowForm(context, AllowedCountries(context), Existing(business, applicant), "business_verification_error_unavailable");

            case BusinessVerificationStatus.Pending:
            case BusinessVerificationStatus.InReview:
                if (HasBranch(context, PendingBranch))
                    return StepHandlerResult.Branch(PendingBranch, outputs);
                return await WaitAsync(context, view, emailRequested: false, cancellationToken);

            default:
                if (HasBranch(context, NotVerifiedBranch))
                    return StepHandlerResult.Branch(NotVerifiedBranch, outputs);
                return StepHandlerResult.ShowUi(RejectedView, new BusinessVerificationRejectedViewModel
                {
                    BusinessName = business.RegisteredName ?? applicant.ProvidedBusinessName ?? business.RegistrationNumber,
                    ReasonKey = ReasonKey(applicant.StatusReason),
                    AllowRetry = context.GetConfig("allowRetry", true),
                });
        }
    }

    private async Task<StepHandlerResult> WaitAsync(StepExecutionContext context, BusinessApplicantView view, bool emailRequested, CancellationToken cancellationToken)
    {
        var notifier = context.ServiceProvider.GetRequiredService<BusinessVerificationNotifier>();
        var hostedOpen = view.Applicant.StatusReason == BusinessVerificationReasons.AwaitingProvider
            && view.Applicant.JourneyId == context.JourneyId
            && !string.IsNullOrEmpty(context.GetDataString(SessionReferenceDataKey));
        return StepHandlerResult.ShowUi(WaitingView, new BusinessVerificationWaitingViewModel
        {
            JourneyId = context.JourneyId,
            StepId = context.StepId,
            Revision = JourneyStatusRevision.Read(context.JourneyData),
            BusinessName = view.Business.RegisteredName ?? view.Applicant.ProvidedBusinessName ?? view.Business.RegistrationNumber,
            ReasonKey = ReasonKey(view.Applicant.StatusReason),
            WaitSeconds = Math.Max(30, context.GetConfig("pendingWaitSeconds", DefaultPendingWaitSeconds)),
            CanEmail = notifier.CanEmail,
            Email = view.Applicant.NotifyEmail ?? await KnownEmailAsync(context, cancellationToken),
            EmailRequested = emailRequested || !string.IsNullOrEmpty(view.Applicant.NotifyEmail),
            CanResumeHosted = hostedOpen,
        });
    }

    private async Task<StepHandlerResult> RequestEmailAsync(StepExecutionContext context, IBusinessVerificationService service, BusinessApplicantView view, CancellationToken cancellationToken)
    {
        if (view.Applicant.Status.IsFinal())
            return await ResolveAsync(context, view, cancellationToken);

        var email = Clean(context.GetInput("email")) ?? await KnownEmailAsync(context, cancellationToken);
        if (email == null || !email.Contains('@') || email.Length > 320)
            return await WaitAsync(context, view, emailRequested: false, cancellationToken);

        await service.RequestOutcomeEmailAsync(context.TenantId!, view.Applicant.Id, email, await ResumeUrlAsync(context, cancellationToken), cancellationToken);
        view.Applicant.NotifyEmail = email;
        return await WaitAsync(context, view, emailRequested: true, cancellationToken);
    }

    private async Task<StepHandlerResult> ReopenSessionAsync(StepExecutionContext context, IBusinessVerificationService service, BusinessApplicantView view, CancellationToken cancellationToken)
    {
        var launch = await service.ResumeHostedSessionAsync(context.TenantId!, view, Absolute(context, $"/journey/{context.JourneyId}/callback"), cancellationToken);
        return launch == null
            ? await ResolveAsync(context, view, cancellationToken)
            : Launch(context, launch, view);
    }

    private static StepHandlerResult Launch(StepExecutionContext context, HostedSessionLaunch launch, BusinessApplicantView view) => launch.Kind switch
    {
        HostedSessionLaunchKind.Redirect => StepHandlerResult.Redirect(launch.RedirectUrl
            ?? throw new InvalidOperationException("A redirect session needs a URL.")),
        _ => StepHandlerResult.ShowUi(HostedView, new BusinessVerificationHostedViewModel
        {
            JourneyId = context.JourneyId,
            ScriptUrl = launch.WidgetScriptUrl ?? throw new InvalidOperationException("A widget session needs its script."),
            Options = launch.WidgetOptions ?? new Dictionary<string, object>(),
            BusinessName = view.Business.RegisteredName ?? view.Applicant.ProvidedBusinessName ?? view.Business.RegistrationNumber,
        }),
    };

    private static IDictionary<string, object> Outputs(VerifiedBusiness business, BusinessApplicant applicant)
    {
        var outputs = new Dictionary<string, object>
        {
            ["kyb_status"] = applicant.Status.ToWire(),
            ["kyb_business_id"] = business.Id,
            ["kyb_applicant_id"] = applicant.Id,
            ["kyb_registration_number"] = business.RegistrationNumber,
            ["kyb_country"] = business.Country,
            ["kyb_relationship"] = applicant.Relationship,
        };
        if (business.RegisteredName != null)
            outputs["kyb_business_name"] = business.RegisteredName;
        return outputs;
    }

    internal static string ReasonKey(string? reason) => reason switch
    {
        BusinessVerificationReasons.NotFound => "business_verification_error_not_found",
        BusinessVerificationReasons.NameMismatch => "business_verification_error_name_mismatch",
        BusinessVerificationReasons.DirectorMismatch => "business_verification_error_director_mismatch",
        BusinessVerificationReasons.Inactive => "business_verification_error_inactive",
        BusinessVerificationReasons.RejectedByProvider => "business_verification_error_rejected_by_provider",
        BusinessVerificationReasons.RejectedByReviewer => "business_verification_error_rejected_by_reviewer",
        BusinessVerificationReasons.Abandoned => "business_verification_error_abandoned",
        BusinessVerificationReasons.AwaitingProvider => "business_verification_waiting_provider",
        BusinessVerificationReasons.AwaitingReview => "business_verification_waiting_review",
        _ => "business_verification_error_unavailable",
    };

    private static void Forget(StepExecutionContext context)
    {
        context.JourneyData.Remove(ApplicantDataKey);
        context.JourneyData.Remove(SessionReferenceDataKey);
        context.JourneyData.Remove(SessionProviderDataKey);
    }

    private static bool HasBranch(StepExecutionContext context, string branch) =>
        context.Configuration.Branches?.ContainsKey(branch) == true;

    internal static BusinessVerificationMode Mode(StepExecutionContext context) =>
        string.Equals(context.GetConfig<string?>("mode", null), "hosted", StringComparison.OrdinalIgnoreCase)
            ? BusinessVerificationMode.Hosted
            : BusinessVerificationMode.Lookup;

    internal static ApplicantEvidenceRequirement Evidence(StepExecutionContext context) =>
        context.GetConfig<string?>("applicantEvidence", null)?.ToLowerInvariant() switch
        {
            "director_match" => ApplicantEvidenceRequirement.DirectorMatch,
            "documents" => ApplicantEvidenceRequirement.Documents,
            "director_or_documents" => ApplicantEvidenceRequirement.DirectorOrDocuments,
            _ => ApplicantEvidenceRequirement.None,
        };

    internal static JourneyUploadPolicy DocumentPolicy(StepExecutionContext context)
    {
        var kinds = (context.GetConfig<List<string>>("documentTypes", null) ?? new List<string>())
            .Where(k => FileContentSniffer.Types.ContainsKey(k))
            .Select(k => k.ToLowerInvariant())
            .Distinct()
            .ToList();
        var maxMb = context.GetConfig("maxDocumentSizeMb", DefaultMaxDocumentSizeMb);
        var maxFiles = context.GetConfig("maxDocuments", DefaultMaxDocuments);
        return new JourneyUploadPolicy
        {
            AllowedKinds = kinds.Count > 0 ? kinds : DefaultDocumentTypes,
            MaxBytes = (long)(maxMb > 0 ? maxMb : DefaultMaxDocumentSizeMb) * 1024 * 1024,
            MaxFiles = maxFiles > 0 ? maxFiles : DefaultMaxDocuments,
        };
    }

    private static List<string> AllowedCountries(StepExecutionContext context) =>
        (context.GetConfig<List<string>>("allowedCountries", null) ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

    private static BusinessVerificationInput FromForm(StepExecutionContext context) => new(
        Clean(context.GetInput("country"))?.ToUpperInvariant(),
        Clean(context.GetInput("registrationType")),
        Clean(context.GetInput("registrationNumber")),
        Clean(context.GetInput("businessName")));

    private static BusinessVerificationInput? FromJourneyData(StepExecutionContext context)
    {
        string? Read(string setting)
        {
            var key = context.GetConfig<string?>(setting, null);
            return string.IsNullOrWhiteSpace(key) ? null : Clean(ReadData(context, key));
        }

        var number = Read("registrationNumberClaim");
        return number == null
            ? null
            : new BusinessVerificationInput(Read("countryClaim")?.ToUpperInvariant(), Read("registrationTypeClaim"), number, Read("businessNameClaim"));
    }

    private static BusinessVerificationInput Existing(VerifiedBusiness business, BusinessApplicant applicant) =>
        new(business.Country, business.RegistrationType, business.RegistrationNumber, applicant.ProvidedBusinessName);

    private static async Task<ApplicantName> ApplicantAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        string? Read(string setting, string defaultKey) => Clean(ReadData(context, context.GetConfig(setting, defaultKey)!));

        var first = Read("firstNameClaim", "given_name");
        var last = Read("lastNameClaim", "family_name");
        if ((first == null || last == null) && !string.IsNullOrEmpty(context.UserId))
        {
            var user = await context.ServiceProvider.GetRequiredService<IOlusoUserService>().FindByIdAsync(context.UserId, cancellationToken);
            first ??= Clean(user?.FirstName);
            last ??= Clean(user?.LastName);
        }
        return new ApplicantName(first, last);
    }

    private static async Task<string?> KnownEmailAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(context.UserId))
        {
            var user = await context.ServiceProvider.GetRequiredService<IOlusoUserService>().FindByIdAsync(context.UserId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(user?.Email)) return user.Email;
        }
        return Clean(ReadData(context, "email"));
    }

    /// <summary>The journey's start page on this host, for the outcome email; null when the journey has no slug.</summary>
    private static async Task<string?> ResumeUrlAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var state = await context.ServiceProvider.GetRequiredService<IJourneyStateStore>().GetAsync(context.JourneyId, cancellationToken);
        if (state == null) return null;
        var policy = await context.ServiceProvider.GetRequiredService<IJourneyPolicyStore>().GetByIdAsync(state.PolicyId, cancellationToken);
        return string.IsNullOrEmpty(policy?.Slug) ? null : Absolute(context, $"/journey/start/{Uri.EscapeDataString(policy.Slug)}");
    }

    private static string Absolute(StepExecutionContext context, string path)
    {
        var request = context.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request
            ?? throw new InvalidOperationException("An absolute journey link needs the request it's built from.");
        return $"{request.Scheme}://{request.Host}{request.PathBase}{path}";
    }

    private static string? ReadData(StepExecutionContext context, string key)
    {
        if (context.JourneyData.ContainsKey(key)) return context.GetDataString(key);
        var match = context.JourneyData.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        return match == null ? null : context.GetDataString(match);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private StepHandlerResult ShowForm(StepExecutionContext context, IReadOnlyList<string> allowed, BusinessVerificationInput? input, string? errorKey, bool askForDocuments = false)
    {
        var evidence = Evidence(context);
        var policy = DocumentPolicy(context);
        return StepHandlerResult.ShowUi(FormView, new BusinessVerificationViewModel
        {
            Countries = allowed
                .Select(code => SupportedCountries.GetByCode(code) is { } info
                    ? new BusinessVerificationCountry(info.Code, info.Name, info.Flag)
                    : new BusinessVerificationCountry(code, code, string.Empty))
                .ToList(),
            Country = input?.Country ?? (allowed.Count == 1 ? allowed[0] : null),
            RegistrationType = input?.RegistrationType,
            RegistrationNumber = input?.RegistrationNumber,
            BusinessName = input?.BusinessName,
            RequireBusinessName = context.GetConfig("requireNameMatch", true),
            ErrorKey = errorKey,
            ShowDocuments = askForDocuments || evidence == ApplicantEvidenceRequirement.Documents,
            DocumentsRequired = askForDocuments || evidence == ApplicantEvidenceRequirement.Documents,
            DocumentsAccept = policy.AcceptAttribute,
            MaxDocuments = policy.MaxFiles,
            MaxDocumentSizeMb = (int)(policy.MaxBytes / (1024 * 1024)),
        });
    }

    private sealed record BusinessVerificationInput(string? Country, string? RegistrationType, string? RegistrationNumber, string? BusinessName);
}

public sealed record BusinessVerificationCountry(string Code, string Name, string Flag);

public class BusinessVerificationViewModel
{
    public IReadOnlyList<BusinessVerificationCountry> Countries { get; init; } = Array.Empty<BusinessVerificationCountry>();
    public string? Country { get; init; }
    public string? RegistrationType { get; init; }
    public string? RegistrationNumber { get; init; }
    public string? BusinessName { get; init; }
    public bool RequireBusinessName { get; init; }

    /// <summary>Locale key of the message to show, or null.</summary>
    public string? ErrorKey { get; init; }

    /// <summary>Show the supporting-documents input (letter of authority, CAC certificate).</summary>
    public bool ShowDocuments { get; init; }
    public bool DocumentsRequired { get; init; }
    public string DocumentsAccept { get; init; } = string.Empty;
    public int MaxDocuments { get; init; }
    public int MaxDocumentSizeMb { get; init; }

    /// <summary>Registries whose numbers carry a type prefix, with the types the form offers.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RegistrationTypes = new Dictionary<string, IReadOnlyList<string>>
    {
        ["NG"] = BusinessRegistrationNumber.NigerianTypes,
    };
}

public class BusinessVerificationWaitingViewModel
{
    public required string JourneyId { get; init; }
    public required string StepId { get; init; }

    /// <summary>The journey's status revision when the page rendered; the page reloads when it changes.</summary>
    public long Revision { get; init; }
    public required string BusinessName { get; init; }
    public required string ReasonKey { get; init; }

    /// <summary>How long the page polls before offering to email the outcome.</summary>
    public int WaitSeconds { get; init; }
    public bool CanEmail { get; init; }
    public string? Email { get; init; }
    public bool EmailRequested { get; init; }

    /// <summary>The provider's session can be opened again.</summary>
    public bool CanResumeHosted { get; init; }
}

public class BusinessVerificationHostedViewModel
{
    public required string JourneyId { get; init; }
    public required string ScriptUrl { get; init; }
    public required IReadOnlyDictionary<string, object> Options { get; init; }
    public required string BusinessName { get; init; }
}

public class BusinessVerificationRejectedViewModel
{
    public required string BusinessName { get; init; }
    public required string ReasonKey { get; init; }
    public bool AllowRetry { get; init; }
}
