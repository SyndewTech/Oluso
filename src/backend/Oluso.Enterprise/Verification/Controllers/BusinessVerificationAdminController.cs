using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Locale;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Enterprise.Verification.Dto;
using Oluso.Enterprise.Verification.Stores;

namespace Oluso.Enterprise.Verification.Controllers;

/// <summary>
/// Business (KYB) verification for the organisation in context: provider credentials
/// (stored encrypted, secrets never returned), country routing and result reuse, a free
/// credential check per provider, the businesses and applicants journeys produce with
/// their audit trail and documents, and review of those waiting on a decision.
/// </summary>
[Route("api/admin/business-verification")]
public class BusinessVerificationAdminController : AdminBaseController
{
    private const int MaxReuseDays = 365;
    private const string JourneysRead = "journeys.read";

    private readonly IVerificationProviderRegistry _providers;
    private readonly IIdentityVerificationSettingsService _settings;
    private readonly IBusinessVerificationStore _store;
    private readonly IBusinessVerificationService _verifications;
    private readonly IFileAttachmentService _attachments;
    private readonly IFileAttachmentStore _attachmentStore;
    private readonly IFileUploader _files;
    private readonly IOlusoUserService _users;
    private readonly ITenantStore _tenants;
    private readonly IIssuerResolver _issuers;

    public BusinessVerificationAdminController(
        ITenantContext tenantContext,
        IVerificationProviderRegistry providers,
        IIdentityVerificationSettingsService settings,
        IBusinessVerificationStore store,
        IBusinessVerificationService verifications,
        IFileAttachmentService attachments,
        IFileAttachmentStore attachmentStore,
        IFileUploader files,
        IOlusoUserService users,
        ITenantStore tenants,
        IIssuerResolver issuers) : base(tenantContext)
    {
        _providers = providers;
        _settings = settings;
        _store = store;
        _verifications = verifications;
        _attachments = attachments;
        _attachmentStore = attachmentStore;
        _files = files;
        _users = users;
        _tenants = tenants;
        _issuers = issuers;
    }

    [HttpGet("providers")]
    [RequirePermission(VerificationPermissions.Manage)]
    public async Task<ActionResult<IEnumerable<VerificationProviderInfoDto>>> GetProviders(CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(TenantId, cancellationToken);
        var issuer = tenant == null ? null : (await _issuers.GetIssuerForTenantAsync(tenant, cancellationToken)).TrimEnd('/');
        return Ok(_providers.All.Select(p => new VerificationProviderInfoDto
        {
            Id = p.Id,
            DisplayName = p.DisplayName,
            BusinessCountries = p is IBusinessVerificationProvider business ? business.SupportedBusinessCountries.ToList() : new List<string>(),
            HostedCountries = p is IHostedBusinessVerificationProvider hosted ? hosted.HostedBusinessCountries.ToList() : new List<string>(),
            WebhookUrl = p is IHostedBusinessVerificationProvider && !string.IsNullOrEmpty(issuer)
                ? $"{issuer}{VerificationWebhooksController.RoutePrefix}/{p.Id}"
                : null,
            CredentialFields = p.CredentialFields.Select(f => new VerificationCredentialFieldDto
            {
                Name = f.Name, Label = f.Label, Secret = f.Secret, Description = f.Description, Required = f.Required,
            }).ToList(),
            Options = p.Options.Select(o => new VerificationProviderOptionDto
            {
                Name = o.Name, Label = o.Label, Choices = o.Choices.ToList(), Default = o.Default, Description = o.Description,
            }).ToList(),
        }));
    }

    /// <summary>Countries some provider covers, with the organisation's routing. The journey builder's country picker reads this.</summary>
    [HttpGet("countries")]
    [RequireAnyPermission(VerificationPermissions.Manage, VerificationPermissions.Read, JourneysRead)]
    public async Task<ActionResult<IEnumerable<BusinessVerificationCountryDto>>> GetCountries(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(TenantId, cancellationToken);
        var countries = _providers.BusinessProviders
            .SelectMany(p => p.SupportedBusinessCountries.Select(c => (Country: c.ToUpperInvariant(), Provider: p)))
            .GroupBy(x => x.Country)
            .Select(g =>
            {
                var routed = settings.BusinessRouting.GetValueOrDefault(g.Key);
                var routedProvider = routed == null ? null : _providers.Find(routed);
                return new BusinessVerificationCountryDto
                {
                    Code = g.Key,
                    Name = SupportedCountries.GetByCode(g.Key)?.Name ?? g.Key,
                    ProviderIds = g.Select(x => x.Provider.Id).ToList(),
                    RoutedProviderId = routedProvider?.Id,
                    ProviderName = routedProvider?.DisplayName ?? "Not routed",
                };
            })
            .OrderBy(c => c.Name)
            .ToList();
        return Ok(countries);
    }

    [HttpGet("settings")]
    [RequirePermission(VerificationPermissions.Manage)]
    public async Task<ActionResult<IdentityVerificationSettingsDto>> GetSettings(CancellationToken cancellationToken) =>
        Ok(ToDto(await _settings.GetAsync(TenantId, cancellationToken)));

    [HttpPut("settings")]
    [RequirePermission(VerificationPermissions.Manage)]
    public async Task<ActionResult<IdentityVerificationSettingsDto>> UpdateSettings(
        [FromBody] UpdateIdentityVerificationSettingsRequest request, CancellationToken cancellationToken)
    {
        if (request.BusinessReuseMaxAgeDays is < 0 or > MaxReuseDays)
            return BadRequest(new { Message = $"Reuse window must be between 0 and {MaxReuseDays} days." });

        var settings = await _settings.GetAsync(TenantId, cancellationToken);

        foreach (var incoming in request.Providers)
        {
            var provider = _providers.Find(incoming.ProviderId);
            if (provider == null)
                return BadRequest(new { Message = $"Unknown verification provider '{incoming.ProviderId}'." });
            if (!Enum.TryParse<VerificationEnvironment>(incoming.Environment, ignoreCase: true, out var environment))
                return BadRequest(new { Message = $"{provider.DisplayName}: environment must be Sandbox or Live." });

            settings.Providers.TryGetValue(provider.Id, out var current);
            var updated = new VerificationProviderSettings { Enabled = incoming.Enabled, Environment = environment };

            foreach (var field in provider.CredentialFields)
            {
                var typed = incoming.Credentials.GetValueOrDefault(field.Name)?.Trim();
                var stored = current?.Credentials.GetValueOrDefault(field.Name);
                var value = field.Secret && string.IsNullOrEmpty(typed) ? stored : typed;
                // Changing environment invalidates secrets saved for the other one.
                if (field.Secret && string.IsNullOrEmpty(typed) && current != null && current.Environment != environment)
                    value = null;
                if (!string.IsNullOrEmpty(value))
                    updated.Credentials[field.Name] = value;
            }

            foreach (var option in provider.Options)
            {
                var value = incoming.Options.GetValueOrDefault(option.Name)?.Trim();
                var choice = option.Choices.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
                if (choice == null)
                    return BadRequest(new { Message = $"{provider.DisplayName}: choose a {option.Label} ({string.Join(", ", option.Choices)})." });
                updated.Options[option.Name] = choice;
            }

            if (updated.Enabled)
            {
                var missing = provider.CredentialFields.Where(f => f.Required && !updated.Credentials.ContainsKey(f.Name)).Select(f => f.Label).ToList();
                if (missing.Count > 0)
                    return BadRequest(new { Message = $"{provider.DisplayName} needs {string.Join(", ", missing)} to be enabled." });
            }

            settings.Providers[provider.Id] = updated;
        }

        var routing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawCountry, providerId) in request.BusinessRouting)
        {
            if (string.IsNullOrWhiteSpace(providerId)) continue;
            var country = rawCountry.Trim().ToUpperInvariant();
            if (SupportedCountries.GetByCode(country) == null)
                return BadRequest(new { Message = $"Unknown country code '{rawCountry}'." });
            if (_providers.Find(providerId) is not IBusinessVerificationProvider provider)
                return BadRequest(new { Message = $"'{providerId}' can't verify businesses." });
            if (!provider.SupportedBusinessCountries.Contains(country, StringComparer.OrdinalIgnoreCase))
                return BadRequest(new { Message = $"{provider.DisplayName} doesn't cover business registries in {country}." });
            if (!settings.Providers.TryGetValue(provider.Id, out var configured) || !configured.Enabled)
                return BadRequest(new { Message = $"Enable {provider.DisplayName} before routing {country} to it." });
            routing[country] = provider.Id;
        }

        settings.BusinessRouting = routing;
        settings.BusinessReuseMaxAgeDays = request.BusinessReuseMaxAgeDays;
        await _settings.SaveAsync(TenantId, settings, cancellationToken);
        return Ok(ToDto(settings));
    }

    /// <summary>Checks the saved credentials with the provider's free endpoint; no lookup is bought.</summary>
    [HttpPost("providers/{providerId}/test")]
    [RequirePermission(VerificationPermissions.Manage)]
    public async Task<ActionResult<VerificationConnectionTestResultDto>> TestProvider(string providerId, CancellationToken cancellationToken)
    {
        var provider = _providers.Find(providerId);
        if (provider == null)
            return NotFound();

        var settings = await _settings.GetAsync(TenantId, cancellationToken);
        if (!settings.Providers.TryGetValue(provider.Id, out var configured))
            return Ok(new VerificationConnectionTestResultDto { Success = false, Message = $"Save {provider.DisplayName}'s credentials first." });

        var missing = provider.CredentialFields.Where(f => f.Required && !configured.Credentials.ContainsKey(f.Name)).Select(f => f.Label).ToList();
        if (missing.Count > 0)
            return Ok(new VerificationConnectionTestResultDto { Success = false, Message = $"{provider.DisplayName} is missing {string.Join(", ", missing)}." });

        var check = await provider.CheckConnectionAsync(new VerificationProviderConnection(
            TenantId, configured.Environment, configured.Credentials, configured.Options), cancellationToken);
        return Ok(new VerificationConnectionTestResultDto { Success = check.Success, Message = check.Message });
    }

    [HttpGet("businesses")]
    [RequirePermission(VerificationPermissions.Read)]
    public async Task<ActionResult<VerifiedBusinessListResponse>> ListBusinesses(
        [FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        BusinessVerificationStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!BusinessVerificationStatusNames.TryParse(status, out var parsed))
                return BadRequest(new { Message = $"Unknown status '{status}'." });
            filter = parsed;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var (items, total) = await _store.ListBusinessesAsync(TenantId, filter, search, (page - 1) * pageSize, pageSize, cancellationToken);
        var summaries = new List<VerifiedBusinessSummaryDto>();
        foreach (var business in items)
        {
            var applicants = await _store.ListApplicantsForBusinessAsync(TenantId, business.Id, cancellationToken);
            summaries.Add(Fill(new VerifiedBusinessSummaryDto(), business, applicants.Count));
        }
        return Ok(new VerifiedBusinessListResponse { Items = summaries, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("businesses/{id}")]
    [RequirePermission(VerificationPermissions.Read)]
    public async Task<ActionResult<VerifiedBusinessDetailDto>> GetBusiness(string id, CancellationToken cancellationToken)
    {
        var business = await _store.GetBusinessAsync(TenantId, id, cancellationToken);
        return business == null ? NotFound() : Ok(await DetailAsync(business, cancellationToken));
    }

    /// <summary>Applicants waiting on a decision, oldest first, with their business.</summary>
    [HttpGet("review-queue")]
    [RequirePermission(VerificationPermissions.Read)]
    public async Task<ActionResult<BusinessReviewQueueResponse>> ReviewQueue(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var (applicants, total) = await _store.ListOpenApplicantsAsync(TenantId, (page - 1) * pageSize, pageSize, cancellationToken);
        var businesses = (await _store.GetBusinessesAsync(TenantId, applicants.Select(a => a.BusinessId).Distinct().ToList(), cancellationToken))
            .ToDictionary(b => b.Id);
        var items = new List<BusinessReviewQueueItemDto>();
        foreach (var applicant in applicants)
        {
            if (!businesses.TryGetValue(applicant.BusinessId, out var business)) continue;
            items.Add(new BusinessReviewQueueItemDto
            {
                Applicant = await ApplicantDtoAsync(applicant, cancellationToken),
                Business = Fill(new VerifiedBusinessSummaryDto(), business, 0),
            });
        }
        return Ok(new BusinessReviewQueueResponse { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpPost("businesses/{id}/review")]
    [RequirePermission(VerificationPermissions.Review)]
    public async Task<ActionResult<VerifiedBusinessDetailDto>> ReviewBusiness(
        string id, [FromBody] ReviewBusinessVerificationRequest request, CancellationToken cancellationToken)
    {
        if (ParseDecision(request.Decision) is not { } approve)
            return BadRequest(new { Message = "Decision must be approve or reject." });

        using var origin = EventOriginScope.Begin(EventOrigin.Admin(RequireAdminUserId));
        try
        {
            var business = await _verifications.ReviewBusinessAsync(TenantId, id, approve, RequireAdminUserId, request.Note, cancellationToken);
            return Ok(await DetailAsync(business, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { Message = ex.Message });
        }
    }

    [HttpPost("applicants/{id}/review")]
    [RequirePermission(VerificationPermissions.Review)]
    public async Task<ActionResult<BusinessApplicantDto>> ReviewApplicant(
        string id, [FromBody] ReviewBusinessVerificationRequest request, CancellationToken cancellationToken)
    {
        if (ParseDecision(request.Decision) is not { } approve)
            return BadRequest(new { Message = "Decision must be approve or reject." });

        using var origin = EventOriginScope.Begin(EventOrigin.Admin(RequireAdminUserId));
        try
        {
            var applicant = await _verifications.ReviewApplicantAsync(TenantId, id, approve, RequireAdminUserId, request.Note, cancellationToken);
            return Ok(await ApplicantDtoAsync(applicant, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { Message = ex.Message });
        }
    }

    /// <summary>Asks the provider again how the applicant's open hosted session ended (e.g. after a missed webhook).</summary>
    [HttpPost("applicants/{id}/refresh")]
    [RequirePermission(VerificationPermissions.Review)]
    public async Task<ActionResult<BusinessApplicantDto>> RefreshApplicant(string id, CancellationToken cancellationToken)
    {
        var view = await _verifications.GetApplicantAsync(TenantId, id, cancellationToken);
        if (view == null) return NotFound();
        var checks = await _store.ListChecksForBusinessAsync(TenantId, view.Business.Id, cancellationToken);
        var open = checks.FirstOrDefault(c => c.ApplicantId == id && c.Kind == BusinessCheckKinds.HostedSession && c.Outcome == BusinessCheckOutcomes.Open);
        if (open?.ProviderId == null || open.SessionReference == null)
            return Conflict(new { Message = "The applicant has no hosted session waiting on the provider." });

        using var origin = EventOriginScope.Begin(EventOrigin.Admin(RequireAdminUserId));
        try
        {
            var updated = await _verifications.CompleteHostedSessionAsync(TenantId, open.ProviderId, open.SessionReference, cancellationToken);
            return Ok(await ApplicantDtoAsync(updated?.Applicant ?? view.Applicant, cancellationToken));
        }
        catch (VerificationConfigurationException ex)
        {
            return Conflict(new { Message = ex.Message });
        }
        catch (VerificationDecisionConflictException ex)
        {
            return Conflict(new { Message = ex.Message });
        }
    }

    /// <summary>Streams an applicant's document as a download; never a public URL.</summary>
    [HttpGet("applicants/{applicantId}/documents/{documentId}")]
    [RequirePermission(VerificationPermissions.Read)]
    public async Task<IActionResult> DownloadDocument(string applicantId, string documentId, CancellationToken cancellationToken)
    {
        var applicant = await _store.GetApplicantAsync(TenantId, applicantId, cancellationToken);
        if (applicant == null || !applicant.DocumentIds.Contains(documentId)) return NotFound();

        var file = await _attachmentStore.GetByIdAsync(documentId, cancellationToken);
        if (file == null || file.TenantId != TenantId || file.IsDeleted) return NotFound();
        var bytes = await _files.DownloadAsync(file.StoragePath, cancellationToken);
        if (bytes == null) return NotFound();
        var view = await _attachments.ResolveAsync(file, cancellationToken);
        return File(bytes, view.ContentType, view.FileName);
    }

    private static bool? ParseDecision(string? decision) =>
        string.Equals(decision, "approve", StringComparison.OrdinalIgnoreCase) ? true
        : string.Equals(decision, "reject", StringComparison.OrdinalIgnoreCase) ? false
        : null;

    private async Task<VerifiedBusinessDetailDto> DetailAsync(VerifiedBusiness business, CancellationToken cancellationToken)
    {
        var applicants = await _store.ListApplicantsForBusinessAsync(TenantId, business.Id, cancellationToken);
        var checks = await _store.ListChecksForBusinessAsync(TenantId, business.Id, cancellationToken);
        var dto = Fill(new VerifiedBusinessDetailDto(), business, applicants.Count);
        dto.RegistrationType = business.RegistrationType;
        dto.StatusMessage = business.StatusMessage;
        dto.EntityType = business.EntityType;
        dto.RegistrationDate = business.RegistrationDate;
        dto.RegisteredAddress = business.RegisteredAddress;
        dto.ActivityStatus = business.ActivityStatus.ToString();
        dto.RawStatus = business.RawStatus;
        dto.RegistryEnvironment = business.RegistryEnvironment?.ToString();
        dto.People = business.People.Select(p => new BusinessPersonDto { Name = p.Name, Role = p.Role }).ToList();
        dto.DecidedAt = business.DecidedAt;
        dto.ReviewedBy = business.ReviewedBy;
        dto.ReviewedAt = business.ReviewedAt;
        dto.ReviewNote = business.ReviewNote;
        foreach (var applicant in applicants)
            dto.Applicants.Add(await ApplicantDtoAsync(applicant, cancellationToken));
        dto.Checks = checks.Select(c => new BusinessCheckDto
        {
            Id = c.Id,
            ApplicantId = c.ApplicantId,
            Kind = c.Kind,
            Outcome = c.Outcome,
            ProviderId = c.ProviderId,
            Environment = c.Environment?.ToString(),
            ProviderReference = c.ProviderReference,
            SessionReference = c.SessionReference,
            Message = c.Message,
            ReusedFromCheckId = c.ReusedFromCheckId,
            OriginKind = c.OriginKind,
            OriginId = c.OriginId,
            OriginName = c.OriginName,
            CreatedAt = c.CreatedAt,
            CompletedAt = c.CompletedAt,
        }).ToList();
        return dto;
    }

    private async Task<BusinessApplicantDto> ApplicantDtoAsync(BusinessApplicant applicant, CancellationToken cancellationToken)
    {
        string? subjectName = null;
        if (!string.IsNullOrEmpty(applicant.SubjectId))
        {
            var user = await _users.FindByIdAsync(applicant.SubjectId, cancellationToken);
            subjectName = user == null ? null : $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } full ? $"{full} ({user.Email})" : user.Email;
        }

        return new BusinessApplicantDto
        {
            Id = applicant.Id,
            BusinessId = applicant.BusinessId,
            SubjectId = applicant.SubjectId,
            SubjectName = subjectName,
            Relationship = applicant.Relationship,
            EvidenceRequirement = applicant.EvidenceRequirement.ToString(),
            Status = applicant.Status.ToWire(),
            StatusReason = applicant.StatusReason,
            StatusMessage = applicant.StatusMessage,
            ProvidedBusinessName = applicant.ProvidedBusinessName,
            NameMatched = applicant.NameMatched,
            DirectorMatched = applicant.DirectorMatched,
            MatchedPersonName = applicant.MatchedPersonName,
            MatchedPersonRole = applicant.MatchedPersonRole,
            Documents = await VerificationDocuments.ListAsync(_attachmentStore, _attachments, TenantId, applicant, cancellationToken),
            OutcomeEmailRequested = !string.IsNullOrEmpty(applicant.NotifyEmail),
            CreatedAt = applicant.CreatedAt,
            UpdatedAt = applicant.UpdatedAt,
            DecidedAt = applicant.DecidedAt,
            ReviewedBy = applicant.ReviewedBy,
            ReviewedAt = applicant.ReviewedAt,
            ReviewNote = applicant.ReviewNote,
        };
    }

    private static T Fill<T>(T dto, VerifiedBusiness business, int applicantCount) where T : VerifiedBusinessSummaryDto
    {
        dto.Id = business.Id;
        dto.Status = business.Status.ToWire();
        dto.StatusReason = business.StatusReason;
        dto.Country = business.Country;
        dto.RegistrationNumber = business.RegistrationNumber;
        dto.RegisteredName = business.RegisteredName;
        dto.RegistryProviderId = business.RegistryProviderId;
        dto.RegistryCheckedAt = business.RegistryCheckedAt;
        dto.ApplicantCount = applicantCount;
        dto.CreatedAt = business.CreatedAt;
        dto.UpdatedAt = business.UpdatedAt;
        return dto;
    }

    private IdentityVerificationSettingsDto ToDto(IdentityVerificationSettings settings) => new()
    {
        Providers = settings.Providers
            .Select(kv => (Provider: _providers.Find(kv.Key), Saved: kv.Value))
            .Where(x => x.Provider != null)
            .Select(x => new ConfiguredVerificationProviderDto
            {
                ProviderId = x.Provider!.Id,
                Enabled = x.Saved.Enabled,
                Environment = x.Saved.Environment.ToString(),
                Credentials = x.Provider.CredentialFields
                    .Where(f => !f.Secret && x.Saved.Credentials.ContainsKey(f.Name))
                    .ToDictionary(f => f.Name, f => x.Saved.Credentials[f.Name]),
                StoredSecrets = x.Provider.CredentialFields
                    .Where(f => f.Secret && x.Saved.Credentials.ContainsKey(f.Name))
                    .Select(f => f.Name)
                    .ToList(),
                Options = new Dictionary<string, string>(x.Saved.Options),
            })
            .ToList(),
        BusinessRouting = new Dictionary<string, string>(settings.BusinessRouting),
        BusinessReuseMaxAgeDays = settings.BusinessReuseMaxAgeDays,
    };
}

/// <summary>An applicant's documents as listed to people allowed to see them.</summary>
internal static class VerificationDocuments
{
    public static async Task<List<BusinessDocumentDto>> ListAsync(IFileAttachmentStore store, IFileAttachmentService attachments,
        string tenantId, BusinessApplicant applicant, CancellationToken cancellationToken)
    {
        var documents = new List<BusinessDocumentDto>();
        foreach (var id in applicant.DocumentIds)
        {
            var file = await store.GetByIdAsync(id, cancellationToken);
            if (file == null || file.TenantId != tenantId || file.IsDeleted) continue;
            var view = await attachments.ResolveAsync(file, cancellationToken);
            documents.Add(new BusinessDocumentDto
            {
                Id = file.Id,
                FileName = view.FileName,
                ContentType = view.ContentType,
                SizeBytes = view.SizeBytes,
                CreatedAt = view.CreatedAt,
            });
        }
        return documents;
    }
}
