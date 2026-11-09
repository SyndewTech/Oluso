using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Admin.Authorization;
using Oluso.Core.Events;
using Oluso.Core.UserJourneys;
using Oluso.Admin.Dto.Journeys;
using Oluso.Admin.Journeys;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing user journey policies per tenant
/// </summary>
[Route("api/admin/journeys")]
public class UserJourneyPoliciesController : AdminBaseController
{
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IJourneyStepSecretProtector? _secretProtector;
    private readonly IOlusoEventService? _eventService;
    private readonly IExtendedStepHandlerRegistry? _stepRegistry;
    private readonly IPluginStore? _pluginStore;
    private readonly ITenantContext _tenantContext;
    private readonly IHtmlSanitizationService _htmlSanitizer;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ITenantConfigurationService _configService;
    private readonly IFeatureGate? _featureGate;
    private readonly IAuthorizationService? _authorizationService;
    private readonly ApplicationPartManager? _partManager;
    private readonly ILogger<UserJourneyPoliciesController> _logger;

    // Configuration keys that contain HTML content and need sanitization
    private static readonly HashSet<string> HtmlConfigKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "termsContent",
        "privacyContent",
        "htmlContent",
        "messageHtml",
        "successMessageHtml",
        "errorMessageHtml"
    };

    public UserJourneyPoliciesController(
        IJourneyPolicyStore policyStore,
        ITenantContext tenantContext,
        IHtmlSanitizationService htmlSanitizer,
        IIssuerResolver issuerResolver,
        ITenantConfigurationService configService,
        ILogger<UserJourneyPoliciesController> logger,
        IExtendedStepHandlerRegistry? stepRegistry = null,
        IPluginStore? pluginStore = null,
        IFeatureGate? featureGate = null,
        IAuthorizationService? authorizationService = null,
        ApplicationPartManager? partManager = null,
        IJourneyStepSecretProtector? secretProtector = null,
        IOlusoEventService? eventService = null) : base(tenantContext)
    {
        _policyStore = policyStore;
        _secretProtector = secretProtector;
        _eventService = eventService;
        _stepRegistry = stepRegistry;
        _pluginStore = pluginStore;
        _tenantContext = tenantContext;
        _htmlSanitizer = htmlSanitizer;
        _issuerResolver = issuerResolver;
        _configService = configService;
        _featureGate = featureGate;
        _authorizationService = authorizationService;
        _partManager = partManager;
        _logger = logger;
    }

    /// <summary>
    /// Get all policies for the current tenant
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public async Task<ActionResult<IEnumerable<PolicyListItemDto>>> GetPolicies(
        [FromQuery] bool includeGlobal = true,
        CancellationToken cancellationToken = default)
    {
        return Ok(await ListPoliciesAsync(_tenantContext.TenantId, includeGlobal, cancellationToken));
    }

    /// <summary>
    /// Journey policies of a specific tenant, for admins managing that tenant
    /// (Organizations → tenant → settings) rather than the tenant active in their
    /// own context. Gated like the other tenants/{tenantId} settings reads.
    /// </summary>
    [HttpGet("~/api/admin/tenants/{tenantId}/journeys")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult<IEnumerable<PolicyListItemDto>>> GetPoliciesForTenant(
        string tenantId,
        [FromQuery] bool includeGlobal = true,
        CancellationToken cancellationToken = default)
    {
        var tenantStore = HttpContext.RequestServices.GetService<ITenantStore>();
        var tenant = tenantStore == null ? null : await tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();
        if (!CanAccessTenant(tenant))
            return Forbid();

        return Ok(await ListPoliciesAsync(tenantId, includeGlobal, cancellationToken));
    }

    private async Task<IEnumerable<PolicyListItemDto>> ListPoliciesAsync(string? tenantId, bool includeGlobal, CancellationToken cancellationToken)
    {
        var policies = await _policyStore.GetByTenantAsync(tenantId, cancellationToken);

        if (!includeGlobal)
        {
            policies = policies.Where(p => p.TenantId == tenantId);
        }

        return policies.Select(p => new PolicyListItemDto
        {
            Id = p.Id,
            Slug = p.Slug,
            Name = p.Name,
            Description = p.Description,
            Type = p.Type.ToString(),
            Enabled = p.Enabled,
            Priority = p.Priority,
            IsGlobal = p.TenantId == null,
            OwnerModule = p.OwnerModule,
            OwnerModuleName = ModuleName(p.OwnerModule),
            Sharing = p.Sharing.ToString(),
            StepCount = p.Steps.Count,
            Version = p.Version,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        }).ToList();
    }

    /// <summary>
    /// Get a specific policy
    /// </summary>
    [HttpGet("{policyId}")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public async Task<ActionResult<PolicyDetailDto>> GetPolicy(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);

        if (policy == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        return Ok(MapToDetailDtoRedacted(policy));
    }

    /// <summary>
    /// Get the public share URL for a journey/data-collection policy.
    /// The URL points at the POLICY's tenant host (auto-subdomain
    /// {tenantIdentifier}.{issuerHost} or, in future, the tenant's
    /// CustomDomain), not the request's tenant — a SuperAdmin operating
    /// in _platform context viewing a default-tenant policy must still
    /// generate a URL that lands the public visitor on default.{host}
    /// so middleware resolves the right tenant for the lookup.
    ///
    /// Falls back to the configured platform issuer when the policy is
    /// global (TenantId == null) or scoped to the platform meta-tenant.
    /// </summary>
    [HttpGet("{policyId}/share-url")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public async Task<ActionResult<JourneyShareUrlDto>> GetShareUrl(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);
        if (policy == null) return NotFound();
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        // Single source of truth: IIssuerResolver. The admin's request
        // already resolves to the policy's tenant (a tenant admin viewing
        // their own policies, or a SuperAdmin who has explicitly switched
        // into the tenant via X-Tenant-Id), so the resolver returns the
        // correct per-tenant issuer host (auto-subdomain or CustomDomain).
        // Route is defined at Pages/Journey/Start.cshtml — path segment,
        // lowercase. Don't change shape without updating both ends.
        var issuer = (await _issuerResolver.GetIssuerAsync(cancellationToken)).TrimEnd('/');
        return Ok(new JourneyShareUrlDto
        {
            Url = $"{issuer}/journey/start/{Uri.EscapeDataString(policy.Slug)}",
        });
    }

    /// <summary>
    /// Create a new policy
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.JourneysWrite)]
    public async Task<ActionResult<PolicyDetailDto>> CreatePolicy(
        [FromBody] CreatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validation = await ValidatePolicyAsync(request.Steps, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            request.Slug = await FreeSlugAsync(Slugify(request.Name), cancellationToken);
        }
        else
        {
            request.Slug = request.Slug.Trim();
            if (await CheckSlugAsync(request.Slug, null, cancellationToken) is { } slugProblem)
            {
                return slugProblem;
            }
        }

        if (await CheckOwnershipAsync(null, request.OwnerModule, request.Sharing, cancellationToken) is { } ownershipProblem)
        {
            return ownershipProblem;
        }

        // Sanitize HTML content in step configurations to prevent XSS
        SanitizeStepConfigurations(request.Steps);

        var policy = BuildPolicy(request);

        await _policyStore.SaveAsync(policy, cancellationToken);

        _logger.LogInformation("Created policy {PolicyId} for tenant {TenantId}",
            policy.Id, _tenantContext.TenantId);

        await RaisePolicyCreatedAsync(policy, cancellationToken);

        return CreatedAtAction(nameof(GetPolicy), new { policyId = policy.Id }, MapToDetailDtoRedacted(policy));
    }

    /// <summary>
    /// Download a policy as a portable journey file. The source tenant, the
    /// embed API key and protected step secrets never leave the server.
    /// </summary>
    [HttpGet("{policyId}/export")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public async Task<IActionResult> ExportPolicy(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);
        if (policy == null)
        {
            return NotFound();
        }
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        var redacted = MapToDetailDtoRedacted(policy);
        var document = new JourneyExportDocument
        {
            SourceName = policy.Name,
            Policy = new CreatePolicyRequest
            {
                Slug = policy.Slug,
                OwnerModule = redacted.OwnerModule,
                Sharing = redacted.Sharing,
                Name = redacted.Name,
                Description = redacted.Description,
                Type = redacted.Type,
                Enabled = redacted.Enabled,
                Priority = redacted.Priority,
                Steps = redacted.Steps,
                Conditions = redacted.Conditions,
                OutputClaims = redacted.OutputClaims,
                Session = redacted.Session,
                Ui = redacted.Ui,
                RequiresAuthentication = redacted.RequiresAuthentication,
                PersistSubmissions = redacted.PersistSubmissions,
                SubmissionCollection = redacted.SubmissionCollection,
                MaxSubmissions = redacted.MaxSubmissions,
                AllowDuplicates = redacted.AllowDuplicates,
                DuplicateCheckFields = redacted.DuplicateCheckFields,
                SuccessRedirectUrl = redacted.SuccessRedirectUrl,
                SuccessMessage = redacted.SuccessMessage,
                AllowedOrigins = redacted.AllowedOrigins,
                RequireApiKey = redacted.RequireApiKey
            }
        };

        var bytes = System.Text.Encoding.UTF8.GetBytes(JourneyExportEnvelope.Wrap(document));
        return File(bytes, JourneyExportEnvelope.ContentType, JourneyExportEnvelope.FileNameFor(policy.Name));
    }

    /// <summary>
    /// Create a new policy in the current tenant from an exported journey file.
    /// It keeps the file's slug, so clients that name it (<c>?policy=</c>, share
    /// links) keep working, unless <see cref="ImportPolicyRequest.NewSlug"/> or
    /// <see cref="ImportPolicyRequest.GenerateSlug"/> says otherwise. A slug in use is a
    /// 409 with a suggested free one. The policy gets a new ID, never overwrites an
    /// existing policy, starts disabled and has no API key. Settings that were redacted on export are
    /// dropped and reported so the admin re-enters them before enabling.
    /// </summary>
    [HttpPost("import")]
    [RequirePermission(AdminPermissions.JourneysWrite)]
    public async Task<ActionResult<ImportPolicyResultDto>> ImportPolicy(
        [FromBody] ImportPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!JourneyExportEnvelope.TryUnwrap(request.Content, out var document, out var unwrapError))
        {
            return BadRequest(new { errors = new[] { unwrapError } });
        }

        var incoming = document!.Policy;
        incoming.ApiKey = null;
        incoming.Enabled = false;
        if (!string.IsNullOrWhiteSpace(request.NewName))
        {
            incoming.Name = request.NewName.Trim();
        }

        // The slug the admin or the file names must be free; otherwise one is derived from the name.
        var requestedSlug = !string.IsNullOrWhiteSpace(request.NewSlug) ? request.NewSlug.Trim()
            : !request.GenerateSlug && !string.IsNullOrWhiteSpace(incoming.Slug) ? incoming.Slug.Trim()
            : null;

        if (!Enum.TryParse<JourneyType>(incoming.Type, ignoreCase: true, out _))
        {
            return BadRequest(new { errors = new[] { $"Unknown journey type '{incoming.Type}'." } });
        }

        var redactedSettings = StripRedactedSettings(incoming.Steps);

        var validation = await ValidatePolicyAsync(incoming.Steps, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        if (requestedSlug == null)
        {
            incoming.Slug = await FreeSlugAsync(Slugify(incoming.Name), cancellationToken);
        }
        else if (await CheckSlugAsync(requestedSlug, null, cancellationToken) is { } slugProblem)
        {
            return slugProblem;
        }
        else
        {
            incoming.Slug = requestedSlug;
        }

        if (await CheckOwnershipAsync(null, incoming.OwnerModule, incoming.Sharing, cancellationToken) is { } ownershipProblem)
        {
            return ownershipProblem;
        }

        SanitizeStepConfigurations(incoming.Steps);

        var policy = BuildPolicy(incoming);
        await _policyStore.SaveAsync(policy, cancellationToken);

        _logger.LogInformation("Imported policy {PolicyId} ({Name}) into tenant {TenantId}; {RedactedCount} redacted settings dropped",
            policy.Id, policy.Name, _tenantContext.TenantId, redactedSettings.Count);

        await RaisePolicyCreatedAsync(policy, cancellationToken);

        return CreatedAtAction(nameof(GetPolicy), new { policyId = policy.Id }, new ImportPolicyResultDto
        {
            Policy = MapToDetailDtoRedacted(policy),
            RedactedSettings = redactedSettings,
            Warnings = validation.Warnings
        });
    }

    private static List<string> StripRedactedSettings(List<JourneyPolicyStepDto> steps)
    {
        var removed = new List<string>();
        foreach (var step in steps)
        {
            if (step.Configuration == null) continue;
            foreach (var key in step.Configuration
                         .Where(kv => kv.Value is string v && v == IJourneyStepSecretProtector.RedactedValue)
                         .Select(kv => kv.Key)
                         .ToList())
            {
                step.Configuration.Remove(key);
                removed.Add($"{step.Id}.{key}");
            }
        }
        return removed;
    }

    private async Task RaisePolicyCreatedAsync(JourneyPolicy policy, CancellationToken cancellationToken)
    {
        await RaiseAsync(new AdminJourneyPolicyCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = policy.Id,
            ResourceName = policy.Name,
            PolicyId = policy.Id,
            PolicyType = policy.Type.ToString()
        }, cancellationToken);
    }

    private const int SlugMaxLength = 64;

    private static readonly System.Text.RegularExpressions.Regex SlugPattern =
        new("^[A-Za-z0-9][A-Za-z0-9._-]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"Business Sign-in (EU)" → "business-sign-in-eu".</summary>
    private static string Slugify(string name)
    {
        var slug = System.Text.RegularExpressions.Regex
            .Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-")
            .Trim('-');
        if (slug.Length > SlugMaxLength) slug = slug[..SlugMaxLength].TrimEnd('-');
        return slug.Length > 0 ? slug : "journey";
    }

    /// <summary>
    /// Null when <paramref name="slug"/> can name the policy; otherwise the response to
    /// return. Slugs appear in URLs (<c>?policy=</c>, share links) and are unique within a
    /// tenant (global policies' slugs included), so one in use is never taken over.
    /// </summary>
    private async Task<ActionResult?> CheckSlugAsync(string slug, string? exceptPolicyId, CancellationToken cancellationToken)
    {
        if (slug.Length > SlugMaxLength || !SlugPattern.IsMatch(slug))
        {
            return BadRequest(new
            {
                errors = new[]
                {
                    $"Slug '{slug}' must be up to {SlugMaxLength} letters, digits, '.', '_' or '-', starting with a letter or digit."
                }
            });
        }

        if (await _policyStore.IsSlugTakenAsync(slug, exceptPolicyId, cancellationToken))
        {
            return Conflict(new PolicySlugConflictDto
            {
                Slug = slug,
                SuggestedSlug = await FreeSlugAsync(slug, cancellationToken),
                Errors = new[] { $"The slug '{slug}' is already in use by another journey." }
            });
        }

        return null;
    }

    /// <summary><paramref name="slug"/> if it's free, else the first free "-2", "-3"… variant.</summary>
    private async Task<string> FreeSlugAsync(string slug, CancellationToken cancellationToken)
    {
        if (!await _policyStore.IsSlugTakenAsync(slug, null, cancellationToken))
        {
            return slug;
        }

        for (var n = 2; ; n++)
        {
            var suffix = $"-{n}";
            var candidate = slug[..Math.Min(slug.Length, SlugMaxLength - suffix.Length)] + suffix;
            if (!await _policyStore.IsSlugTakenAsync(candidate, null, cancellationToken))
            {
                return candidate;
            }
        }
    }

    private JourneyPolicy BuildPolicy(CreatePolicyRequest request)
    {
        var journeyType = Enum.Parse<JourneyType>(request.Type, ignoreCase: true);

        // Determine defaults based on journey type
        var isDataCollectionType = journeyType is JourneyType.Waitlist or JourneyType.ContactForm
            or JourneyType.Survey or JourneyType.Feedback or JourneyType.DataCollection;

        return new JourneyPolicy
        {
            Id = Guid.NewGuid().ToString(),
            Slug = request.Slug!,
            Name = request.Name,
            Description = request.Description,
            Type = journeyType,
            TenantId = _tenantContext.TenantId, // Always scoped to current tenant
            Enabled = request.Enabled,
            Priority = request.Priority,
            OwnerModule = string.IsNullOrWhiteSpace(request.OwnerModule) ? null : request.OwnerModule.Trim(),
            Sharing = ParseSharing(request.Sharing) ?? JourneySharing.Private,
            Steps = request.Steps.Select(s => new JourneyPolicyStep
            {
                Id = s.Id,
                Type = s.Type,
                DisplayName = s.DisplayName,
                Configuration = s.Configuration,
                Optional = s.Optional,
                Order = s.Order,
                Branches = s.Branches,
                Conditions = s.Conditions,
                Layout = s.Layout,
                OnSuccess = s.OnSuccess,
                OnFailure = s.OnFailure,
                SkipIfCompleted = s.SkipIfCompleted,
                MaxRetries = s.MaxRetries,
                TimeoutSeconds = s.TimeoutSeconds,
                ErrorMessageTemplate = s.ErrorMessageTemplate,
                RequiredClaims = s.RequiredClaims,
                OutputClaims = s.OutputClaims,
                PluginName = s.PluginName
            }).ToList(),
            Conditions = request.Conditions,
            OutputClaims = request.OutputClaims,
            Session = request.Session,
            Ui = request.Ui,
            // Data collection properties - use smart defaults for data collection types
            RequiresAuthentication = request.RequiresAuthentication ?? !isDataCollectionType,
            PersistSubmissions = request.PersistSubmissions ?? isDataCollectionType,
            SubmissionCollection = request.SubmissionCollection,
            MaxSubmissions = request.MaxSubmissions ?? 0,
            AllowDuplicates = request.AllowDuplicates ?? false,
            DuplicateCheckFields = request.DuplicateCheckFields,
            SuccessRedirectUrl = request.SuccessRedirectUrl,
            SuccessMessage = request.SuccessMessage,
            // Embed settings
            AllowedOrigins = request.AllowedOrigins,
            ApiKey = request.ApiKey,
            RequireApiKey = request.RequireApiKey ?? false
        };
    }

    /// <summary>
    /// Update an existing policy
    /// </summary>
    [HttpPut("{policyId}")]
    [RequirePermission(AdminPermissions.JourneysWrite)]
    public async Task<ActionResult<PolicyDetailDto>> UpdatePolicy(
        string policyId,
        [FromBody] UpdatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await _policyStore.GetAsync(policyId, cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        // Check tenant access - can only update own policies
        if (existing.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }
        if (!await CanManageAsync(existing, cancellationToken))
        {
            return Forbid();
        }

        // Validate steps
        if (request.Steps != null)
        {
            var validation = await ValidatePolicyAsync(request.Steps, cancellationToken);
            if (!validation.IsValid)
            {
                return BadRequest(new { errors = validation.Errors });
            }

            // Sanitize HTML content in step configurations to prevent XSS
            SanitizeStepConfigurations(request.Steps);
        }

        if (request.OwnerModule != null || request.Sharing != null)
        {
            var newOwner = request.OwnerModule == null ? existing.OwnerModule
                : string.IsNullOrWhiteSpace(request.OwnerModule) ? null : request.OwnerModule.Trim();
            if (await CheckOwnershipAsync(existing.OwnerModule, newOwner, request.Sharing, cancellationToken) is { } ownershipProblem)
            {
                return ownershipProblem;
            }
            existing.OwnerModule = newOwner;
            existing.Sharing = ParseSharing(request.Sharing) ?? existing.Sharing;
        }

        var newSlug = request.Slug?.Trim();
        if (!string.IsNullOrEmpty(newSlug) && newSlug != existing.Slug)
        {
            if (await CheckSlugAsync(newSlug, existing.Id, cancellationToken) is { } slugProblem)
            {
                return slugProblem;
            }
            existing.Slug = newSlug;
        }

        // Update fields
        existing.Name = request.Name ?? existing.Name;
        existing.Description = request.Description ?? existing.Description;
        existing.Enabled = request.Enabled ?? existing.Enabled;
        existing.Priority = request.Priority ?? existing.Priority;
        if (request.Steps != null)
        {
            // What the editor sends back for a secret is the redaction marker it
            // was given (or nothing), so carry the stored value forward instead of
            // overwriting a live credential with a placeholder.
            var previousSteps = existing.Steps?.ToList() ?? new List<JourneyPolicyStep>();

            existing.Steps = request.Steps.Select(s => new JourneyPolicyStep
            {
                Id = s.Id,
                Type = s.Type,
                DisplayName = s.DisplayName,
                Configuration = s.Configuration,
                Optional = s.Optional,
                Order = s.Order,
                Branches = s.Branches,
                Conditions = s.Conditions,
                Layout = s.Layout,
                OnSuccess = s.OnSuccess,
                OnFailure = s.OnFailure,
                SkipIfCompleted = s.SkipIfCompleted,
                MaxRetries = s.MaxRetries,
                TimeoutSeconds = s.TimeoutSeconds,
                ErrorMessageTemplate = s.ErrorMessageTemplate,
                RequiredClaims = s.RequiredClaims,
                OutputClaims = s.OutputClaims,
                PluginName = s.PluginName
            }).ToList();

            _secretProtector?.PreserveUnchanged(existing.Steps, previousSteps);
        }
        existing.Conditions = request.Conditions ?? existing.Conditions;
        existing.OutputClaims = request.OutputClaims ?? existing.OutputClaims;
        existing.Session = request.Session ?? existing.Session;
        existing.Ui = request.Ui ?? existing.Ui;

        // Data collection properties
        existing.RequiresAuthentication = request.RequiresAuthentication ?? existing.RequiresAuthentication;
        existing.PersistSubmissions = request.PersistSubmissions ?? existing.PersistSubmissions;
        existing.SubmissionCollection = request.SubmissionCollection ?? existing.SubmissionCollection;
        existing.MaxSubmissions = request.MaxSubmissions ?? existing.MaxSubmissions;
        existing.AllowDuplicates = request.AllowDuplicates ?? existing.AllowDuplicates;
        existing.DuplicateCheckFields = request.DuplicateCheckFields ?? existing.DuplicateCheckFields;
        existing.SuccessRedirectUrl = request.SuccessRedirectUrl ?? existing.SuccessRedirectUrl;
        existing.SuccessMessage = request.SuccessMessage ?? existing.SuccessMessage;

        // Embed settings
        existing.AllowedOrigins = request.AllowedOrigins ?? existing.AllowedOrigins;
        existing.ApiKey = request.ApiKey ?? existing.ApiKey;
        existing.RequireApiKey = request.RequireApiKey ?? existing.RequireApiKey;

        existing.UpdatedAt = DateTime.UtcNow;
        existing.Version++;

        await _policyStore.SaveAsync(existing, cancellationToken);

        _logger.LogInformation("Updated policy {PolicyId}", policyId);

        await RaiseAsync(new AdminJourneyPolicyUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = existing.Id,
            ResourceName = existing.Name,
            PolicyType = existing.Type.ToString()
        }, cancellationToken);

        return Ok(MapToDetailDtoRedacted(existing));
    }

    /// <summary>
    /// Delete a policy
    /// </summary>
    [HttpDelete("{policyId}")]
    [RequirePermission(AdminPermissions.JourneysDelete)]
    public async Task<IActionResult> DeletePolicy(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _policyStore.GetAsync(policyId, cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        // Check tenant access - can only delete own policies
        if (existing.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }
        if (!await CanManageAsync(existing, cancellationToken))
        {
            return Forbid();
        }

        await _policyStore.DeleteAsync(policyId, cancellationToken);

        _logger.LogInformation("Deleted policy {PolicyId}", policyId);

        await RaiseAsync(new AdminJourneyPolicyDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = policyId,
            ResourceName = existing.Name,
            PolicyId = policyId
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Clone a policy (including global policies for customization)
    /// </summary>
    [HttpPost("{policyId}/clone")]
    [RequirePermission(AdminPermissions.JourneysWrite)]
    public async Task<ActionResult<PolicyDetailDto>> ClonePolicy(
        string policyId,
        [FromBody] ClonePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await _policyStore.GetAsync(policyId, cancellationToken);

        if (source == null)
        {
            return NotFound();
        }
        if (!await CanManageAsync(source, cancellationToken))
        {
            return Forbid();
        }

        var cloneName = request.NewName ?? $"{source.Name} (Copy)";
        string cloneSlug;
        if (string.IsNullOrWhiteSpace(request.NewSlug))
        {
            cloneSlug = await FreeSlugAsync(Slugify(cloneName), cancellationToken);
        }
        else
        {
            cloneSlug = request.NewSlug.Trim();
            if (await CheckSlugAsync(cloneSlug, null, cancellationToken) is { } slugProblem)
            {
                return slugProblem;
            }
        }

        var cloned = new JourneyPolicy
        {
            Id = Guid.NewGuid().ToString(),
            Slug = cloneSlug,
            Name = cloneName,
            Description = source.Description,
            Type = source.Type,
            TenantId = _tenantContext.TenantId, // Clone to current tenant
            Enabled = false, // Start disabled
            Priority = source.Priority,
            // A copy of a module's journey stays that module's, so its answers stay out of the platform.
            OwnerModule = source.OwnerModule,
            Sharing = source.Sharing,
            Steps = source.Steps.Select(s => new JourneyPolicyStep
            {
                Id = s.Id,
                Type = s.Type,
                DisplayName = s.DisplayName,
                Configuration = s.Configuration != null
                    ? new Dictionary<string, object>(s.Configuration)
                    : null,
                Branches = s.Branches,
                Optional = s.Optional,
                Order = s.Order,
                Conditions = s.Conditions?.ToList(),
                Layout = s.Layout,
                OnSuccess = s.OnSuccess,
                OnFailure = s.OnFailure,
                SkipIfCompleted = s.SkipIfCompleted,
                MaxRetries = s.MaxRetries,
                TimeoutSeconds = s.TimeoutSeconds,
                ErrorMessageTemplate = s.ErrorMessageTemplate,
                RequiredClaims = s.RequiredClaims?.ToList(),
                OutputClaims = s.OutputClaims?.ToList(),
                PluginName = s.PluginName
            }).ToList(),
            Conditions = source.Conditions?.ToList(),
            OutputClaims = source.OutputClaims?.ToList(),
            Session = source.Session,
            Ui = source.Ui,
            // Data collection properties
            RequiresAuthentication = source.RequiresAuthentication,
            PersistSubmissions = source.PersistSubmissions,
            SubmissionCollection = source.SubmissionCollection,
            MaxSubmissions = source.MaxSubmissions,
            AllowDuplicates = source.AllowDuplicates,
            DuplicateCheckFields = source.DuplicateCheckFields?.ToList(),
            SuccessRedirectUrl = source.SuccessRedirectUrl,
            SuccessMessage = source.SuccessMessage,
            // Embed settings
            AllowedOrigins = source.AllowedOrigins,
            ApiKey = source.ApiKey,
            RequireApiKey = source.RequireApiKey
        };

        await _policyStore.SaveAsync(cloned, cancellationToken);

        _logger.LogInformation("Cloned policy {SourceId} to {NewId}", policyId, cloned.Id);

        return CreatedAtAction(nameof(GetPolicy), new { policyId = cloned.Id }, MapToDetailDtoRedacted(cloned));
    }

    /// <summary>
    /// Enable or disable a policy
    /// </summary>
    [HttpPatch("{policyId}/status")]
    [RequirePermission(AdminPermissions.JourneysPublish)]
    public async Task<IActionResult> SetPolicyStatus(
        string policyId,
        [FromBody] SetStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);

        if (policy == null)
        {
            return NotFound();
        }

        if (policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }
        if (!await CanManageAsync(policy, cancellationToken))
        {
            return Forbid();
        }

        policy.Enabled = request.Enabled;
        await _policyStore.SaveAsync(policy, cancellationToken);

        return Ok(new { enabled = policy.Enabled });
    }

    /// <summary>
    /// Get available step types. Steps belonging to a module that isn't
    /// licensed for the current tenant are filtered out — same pattern the
    /// webhooks/events picker uses, so admins don't see steps they can't
    /// actually use (and so requests like <c>/billing/plans</c> for a
    /// tenant without the billing feature don't surface their 403 in the
    /// step config form). Core steps (Module == null) are always returned.
    /// </summary>
    [HttpGet("step-types")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public async Task<ActionResult<IEnumerable<StepTypeInfoDto>>> GetStepTypes(CancellationToken cancellationToken)
    {
        if (_stepRegistry != null)
        {
            var allowed = new List<StepTypeInfoDto>();
            foreach (var s in _stepRegistry.GetRegisteredTypes())
            {
                if (!await IsStepLicensedAsync(s.Module, cancellationToken)) continue;

                allowed.Add(new StepTypeInfoDto
                {
                    Type = s.Type,
                    TypeId = s.Type,
                    Category = s.Category ?? "Other",
                    DisplayName = s.DisplayName ?? s.Type,
                    Description = s.Description ?? "",
                    Icon = s.Icon,
                    Module = s.Module,
                    IsAvailable = true,
                    Testable = _stepRegistry.GetHandler(s.Type) is IStepConfigurationTester,
                    ConfigurationSchema = s.ConfigurationSchema,
                    AvailableBranches = s.AvailableBranches?.Select(b => new StepBranchInfoDto
                    {
                        Id = b.Id,
                        DisplayName = b.DisplayName,
                        Description = b.Description
                    }).ToList()
                });
            }

            return Ok(allowed
                .OrderBy(s => GetCategoryOrder(s.Category))
                .ThenBy(s => s.DisplayName));
        }

        // Without the journey engine there are no step types to offer.
        return Ok(Array.Empty<StepTypeInfoDto>());
    }

    /// <summary>
    /// Lists the custom-page Razor templates the host has compiled in under
    /// <c>Views/Shared/Journey/Templates/</c>. Drives the <c>templatePath</c>
    /// dropdown for the <c>custom_page</c> step. Returns the bare names
    /// (e.g. "Sample" for _Sample.cshtml) — the value a step's
    /// <c>templatePath</c> expects. Empty when no part manager is available.
    /// </summary>
    [HttpGet("journey-templates")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public ActionResult<IEnumerable<JourneyTemplateDto>> GetJourneyTemplates()
    {
        if (_partManager == null)
        {
            return Ok(Array.Empty<JourneyTemplateDto>());
        }

        var feature = new ViewsFeature();
        _partManager.PopulateFeature(feature);

        const string marker = "/Journey/Templates/_";
        const string ext = ".cshtml";

        var templates = feature.ViewDescriptors
            .Select(d => d.RelativePath?.Replace('\\', '/'))
            .Where(p => !string.IsNullOrEmpty(p)
                && p!.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                && p.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(p =>
            {
                var file = p!.Substring(p.LastIndexOf('/') + 1); // _Sample.cshtml
                return file.Substring(1, file.Length - 1 - ext.Length); // Sample
            })
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new JourneyTemplateDto
            {
                Name = name,
                DisplayName = HumanizeTemplateName(name),
                Description = $"Views/Shared/Journey/Templates/_{name}{ext}"
            })
            .ToList();

        return Ok(templates);
    }

    private static string HumanizeTemplateName(string name)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", " $1");
        return spaced.Replace('_', ' ').Replace('-', ' ').Trim();
    }

    /// <summary>
    /// The modules the current admin may make a journey's owner: registered, licensed for the tenant, and
    /// administered by the admin (<c>{featureKey}.admin</c>).
    /// </summary>
    [HttpGet("owner-modules")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public async Task<ActionResult<IEnumerable<JourneyOwnerModuleDto>>> GetOwnerModules(CancellationToken cancellationToken)
    {
        var modules = new List<JourneyOwnerModuleDto>();
        var receiving = ModulesReceivingAnswers();
        foreach (var module in ModuleRegistry.Instance.GetRegisteredModules())
        {
            if (receiving.Contains(module.FeatureKey) && await AdministersModuleAsync(module.FeatureKey, cancellationToken))
                modules.Add(new JourneyOwnerModuleDto { Key = module.FeatureKey, Name = module.DisplayName });
        }
        return Ok(modules.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Why the journey can't move from <paramref name="currentOwner"/> to <paramref name="newOwner"/> with
    /// <paramref name="sharing"/>, or null when it can. A module's answers stay private to it, so taking a
    /// journey from a module, or giving it to one, needs that module's admin permission.
    /// </summary>
    private async Task<ActionResult?> CheckOwnershipAsync(string? currentOwner, string? newOwner, string? sharing, CancellationToken cancellationToken)
    {
        if (sharing != null && ParseSharing(sharing) == null)
        {
            return BadRequest(new { errors = new[] { $"Unknown sharing '{sharing}'. Use Private or Shared." } });
        }
        newOwner = string.IsNullOrWhiteSpace(newOwner) ? null : newOwner.Trim();
        if (string.Equals(currentOwner, newOwner, StringComparison.Ordinal))
        {
            return newOwner == null || sharing == null || await AdministersModuleAsync(newOwner, cancellationToken)
                ? null
                : Forbid();
        }
        if (newOwner != null && !ModuleRegistry.Instance.IsRegistered(newOwner))
        {
            return BadRequest(new { errors = new[] { $"'{newOwner}' is not a module of this platform." } });
        }
        if (newOwner != null && !ModulesReceivingAnswers().Contains(newOwner))
        {
            return BadRequest(new { errors = new[] { $"The {ModuleName(newOwner)} module doesn't receive journey answers, so it can't own a journey." } });
        }
        foreach (var module in new[] { currentOwner, newOwner })
        {
            if (module != null && !await AdministersModuleAsync(module, cancellationToken))
            {
                return Forbid();
            }
        }
        return null;
    }

    /// <summary>
    /// A platform journey is managed with the journey permissions alone; a module's journey also needs that
    /// module's admin permission, so no one else can change where its answers go.
    /// </summary>
    private async Task<bool> CanManageAsync(JourneyPolicy policy, CancellationToken cancellationToken) =>
        !policy.IsModuleOwned || await AdministersModuleAsync(policy.OwnerModule!, cancellationToken);

    /// <summary>True when the module is licensed for the tenant and the current admin holds its admin permission.</summary>
    private async Task<bool> AdministersModuleAsync(string featureKey, CancellationToken cancellationToken)
    {
        if (_featureGate != null)
        {
            var gate = await _featureGate.CheckFeatureAsync(featureKey, cancellationToken: cancellationToken);
            if (!gate.IsAllowed) return false;
        }
        if (_authorizationService == null || User?.Identity?.IsAuthenticated != true) return false;
        var auth = await _authorizationService.AuthorizeAsync(User, $"{PermissionPolicyConstants.PolicyPrefix}{featureKey}.admin");
        return auth.Succeeded;
    }

    /// <summary>Modules with a <see cref="IJourneyResultSink"/>: only they can own a journey, since its answers go nowhere else.</summary>
    private HashSet<string> ModulesReceivingAnswers() =>
        HttpContext.RequestServices.GetServices<IJourneyResultSink>().Select(s => s.Module).ToHashSet(StringComparer.Ordinal);

    private static JourneySharing? ParseSharing(string? value) =>
        value != null && Enum.TryParse<JourneySharing>(value, ignoreCase: true, out var sharing) && Enum.IsDefined(sharing)
            ? sharing
            : null;

    private static string? ModuleName(string? featureKey) =>
        featureKey == null ? null : ModuleRegistry.Instance.GetModule(featureKey)?.DisplayName ?? featureKey;

    /// <summary>
    /// Returns true when a step is visible to the current user. Mirrors the
    /// feature-gate + permission-gate pattern used by the permissions, roles,
    /// and webhook-events endpoints — same identifier (the module's
    /// <see cref="Oluso.Core.Licensing.IModuleDescriptor.FeatureKey"/>)
    /// drives all three filters.
    ///
    /// Steps are hidden when:
    /// 1. The module isn't licensed for the current tenant (matches the
    ///    pattern in <c>WebhooksController.FilterEventsByFeatureGateAsync</c>).
    /// 2. The user lacks the module's admin permission (matches the gate
    ///    fired by <c>[RequirePermission("billing.admin")]</c> on the
    ///    backing data endpoints — without this, the step would appear in
    ///    the picker but its config form would 403 on its enum sources).
    ///
    /// Steps with no <see cref="StepTypeInfo.Module"/> (core steps) and
    /// steps whose module string doesn't match a registered
    /// <see cref="Oluso.Core.Licensing.IModuleDescriptor"/> always pass —
    /// suppressing unknown modules silently would hide third-party plugins.
    ///
    /// Permission convention: <c>{featureKey}.admin</c>.
    /// </summary>
    private async Task<bool> IsStepLicensedAsync(string? featureKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(featureKey)) return true;
        if (!ModuleRegistry.Instance.IsRegistered(featureKey)) return true;

        if (_featureGate != null)
        {
            var gate = await _featureGate.CheckFeatureAsync(featureKey, cancellationToken: cancellationToken);
            if (!gate.IsAllowed) return false;
        }

        if (_authorizationService != null && User?.Identity?.IsAuthenticated == true)
        {
            var policyName = $"{PermissionPolicyConstants.PolicyPrefix}{featureKey}.admin";
            var auth = await _authorizationService.AuthorizeAsync(User, policyName);
            if (!auth.Succeeded) return false;
        }

        return true;
    }

    /// <summary>
    /// Get available plugins for use in CustomPlugin steps
    /// </summary>
    [HttpGet("available-plugins")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public async Task<ActionResult<IEnumerable<AvailablePluginDto>>> GetAvailablePlugins(
        CancellationToken cancellationToken = default)
    {
        if (_pluginStore == null)
        {
            return Ok(Array.Empty<AvailablePluginDto>());
        }

        var plugins = await _pluginStore.GetAvailablePluginsAsync(
            _tenantContext.TenantId,
            cancellationToken);

        var result = plugins.Select(p => new AvailablePluginDto
        {
            Name = p.Name,
            DisplayName = p.DisplayName ?? p.Name,
            Description = p.Description,
            Version = p.Version,
            Author = p.Author,
            Scope = p.Scope.ToString(),
            IsGlobal = p.Scope == PluginScope.Global,
            RequiredClaims = p.GetRequiredClaimsList(),
            OutputClaims = p.GetOutputClaimsList(),
            ConfigSchema = p.GetConfigSchemaObject()
        });

        return Ok(result);
    }

    [HttpGet("{policyId}/locale-overrides/{locale}")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public async Task<ActionResult<Oluso.Admin.Dto.Settings.LocaleStringsEditorResponse>> GetJourneyLocaleOverrides(
        string policyId, string locale, CancellationToken ct)
    {
        var policy = await _policyStore.GetAsync(policyId, ct);
        if (policy == null) return NotFound();
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId) return Forbid();

        var defaults = Oluso.Core.Locale.UiStringDefaults.GetDefaults(locale)
            ?? Oluso.Core.Locale.UiStringDefaults.GetDefaults("en") ?? new();

        // Layer tenant overrides
        var tenantOverrides = new Dictionary<string, string>();
        if (_tenantContext.TenantId != null)
        {
            var section = await _configService.GetSectionAsync<UiTranslationOverrides>(
                _tenantContext.TenantId, $"UiTranslations:{locale}", ct);
            if (section?.Overrides != null)
            {
                foreach (var (k, v) in section.Overrides) tenantOverrides[k] = v;
            }
        }

        // Journey-level overrides on top
        var journeyOverrides = new Dictionary<string, string>(tenantOverrides);
        if (policy.Ui?.LocaleOverrides != null &&
            policy.Ui.LocaleOverrides.TryGetValue(locale, out var policyLocale))
        {
            foreach (var (k, v) in policyLocale) journeyOverrides[k] = v;
        }

        return Ok(new Oluso.Admin.Dto.Settings.LocaleStringsEditorResponse
        {
            Locale = locale,
            Defaults = defaults,
            Overrides = journeyOverrides
        });
    }

    [HttpPut("{policyId}/locale-overrides/{locale}")]
    [RequirePermission(AdminPermissions.JourneysWrite)]
    public async Task<ActionResult> SaveJourneyLocaleOverrides(
        string policyId, string locale,
        [FromBody] Oluso.Admin.Dto.Settings.SaveLocaleOverridesRequest request,
        CancellationToken ct)
    {
        var policy = await _policyStore.GetAsync(policyId, ct);
        if (policy == null) return NotFound();
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId) return Forbid();

        policy.Ui ??= new JourneyUiConfiguration();
        policy.Ui.LocaleOverrides ??= new Dictionary<string, IDictionary<string, string>>();

        var cleaned = request.Overrides
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (cleaned.Count == 0)
            policy.Ui.LocaleOverrides.Remove(locale);
        else
            policy.Ui.LocaleOverrides[locale] = cleaned;

        policy.UpdatedAt = DateTime.UtcNow;
        await _policyStore.SaveAsync(policy, ct);
        return NoContent();
    }

    /// <summary>
    /// Returns the locale codes available for UI localization. Used by the
    /// journey builder's step config form to populate the locale switcher
    /// when editing <c>x-localizable</c> fields.
    /// </summary>
    [HttpGet("ui-locales")]
    [RequirePermission(AdminPermissions.JourneysRead)]
    public ActionResult<IEnumerable<string>> GetUiLocales()
    {
        return Ok(Oluso.Core.Locale.UiStringDefaults.AvailableLocales);
    }

    /// <summary>
    /// Validate a policy configuration
    /// </summary>
    [HttpPost("validate")]
    [RequirePermission(AdminPermissions.JourneysWrite)]
    public async Task<ActionResult<ValidationResultDto>> ValidatePolicy(
        [FromBody] ValidatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidatePolicyAsync(request.Steps, cancellationToken);
        return Ok(new ValidationResultDto
        {
            IsValid = validation.IsValid,
            Errors = validation.Errors,
            Warnings = validation.Warnings
        });
    }

    /// <summary>
    /// Validate a single step's configuration. Used for real-time validation in the UI.
    /// </summary>
    [HttpPost("validate-step")]
    [RequirePermission(AdminPermissions.JourneysWrite)]
    public async Task<ActionResult<StepValidationResultDto>> ValidateStepConfiguration(
        [FromBody] ValidateStepRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Type))
        {
            return BadRequest(new { error = "Step type is required" });
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        // Single source of truth: the handler validates its own config.
        if (_stepRegistry != null)
        {
            var handler = _stepRegistry.GetHandler(request.Type);
            if (handler != null)
            {
                var result = await handler.ValidateConfigurationAsync(request.Configuration);
                errors.AddRange(result.Errors);
                warnings.AddRange(result.Warnings);
            }
            else if (!Enum.TryParse<StepType>(request.Type, ignoreCase: true, out _))
            {
                errors.Add($"Unknown step type: {request.Type}");
            }
        }

        return Ok(new StepValidationResultDto
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        });
    }

    /// <summary>
    /// Runs a step's own configuration test (<see cref="IStepConfigurationTester"/>) with the
    /// settings in the builder. Secrets the builder shows redacted are filled in from the
    /// saved journey step named by <c>PolicyId</c>/<c>StepId</c>.
    /// </summary>
    [HttpPost("test-step")]
    [RequirePermission(AdminPermissions.JourneysWrite)]
    public async Task<ActionResult<StepTestResultDto>> TestStepConfiguration(
        [FromBody] TestStepRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Type))
        {
            return BadRequest(new { error = "Step type is required" });
        }

        if (_stepRegistry?.GetHandler(request.Type) is not IStepConfigurationTester tester)
        {
            return Ok(new StepTestResultDto { Success = false, Message = $"Testing isn't available for {request.Type} steps" });
        }

        var step = new JourneyPolicyStep
        {
            Id = request.StepId ?? "test",
            Type = request.Type,
            Configuration = request.Configuration ?? new Dictionary<string, object>()
        };

        if (!string.IsNullOrEmpty(request.PolicyId) && !string.IsNullOrEmpty(request.StepId))
        {
            var policy = await _policyStore.GetByIdAsync(request.PolicyId, cancellationToken);
            if (policy == null)
            {
                return NotFound();
            }
            if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId)
            {
                return Forbid();
            }
            _secretProtector?.PreserveUnchanged(new[] { step }, policy.Steps);
        }

        var secrets = HttpContext.RequestServices.GetService<IJourneyStepSecretResolver>();
        if (secrets != null && secrets.GetSecretPaths(step.Type)
                .Any(path => secrets.ReadValue(step.Configuration, path) == IJourneyStepSecretProtector.RedactedValue))
        {
            return Ok(new StepTestResultDto { Success = false, Message = "Save the journey first, so the test can use the stored secrets" });
        }

        var result = await tester.TestConfigurationAsync(new StepExecutionContext
        {
            JourneyId = request.PolicyId ?? "test",
            StepId = step.Id,
            TenantId = _tenantContext.TenantId,
            Configuration = new JourneyStepConfiguration { Id = step.Id, Type = step.Type, Settings = step.Configuration },
            ServiceProvider = HttpContext.RequestServices,
        }, cancellationToken);

        return Ok(new StepTestResultDto { Success = result.Success, Message = result.Message, Details = result.Details.ToList() });
    }

    /// <summary>
    /// Sanitizes HTML content in step configurations to prevent XSS attacks.
    /// </summary>
    private void SanitizeStepConfigurations(List<JourneyPolicyStepDto> steps)
    {
        foreach (var step in steps)
        {
            if (step.Configuration == null) continue;

            foreach (var key in HtmlConfigKeys)
            {
                if (step.Configuration.TryGetValue(key, out var value) && value is string htmlContent)
                {
                    step.Configuration[key] = _htmlSanitizer.Sanitize(htmlContent);
                }
            }
        }
    }

    private async Task<(bool IsValid, List<string> Errors, List<string> Warnings)> ValidatePolicyAsync(
        List<JourneyPolicyStepDto> steps,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (steps == null || steps.Count == 0)
        {
            errors.Add("At least one step is required");
            return (false, errors, warnings);
        }

        var stepIds = new HashSet<string>();
        var allKnownTypes = _stepRegistry?.GetRegisteredTypes().Select(t => t.Type).ToList() ?? new List<string>();

        foreach (var step in steps)
        {
            if (string.IsNullOrEmpty(step.Id))
            {
                errors.Add("Each step must have an ID");
                continue;
            }

            if (!stepIds.Add(step.Id))
            {
                errors.Add($"Duplicate step ID: {step.Id}");
            }

            // Resolve the step type. Use ignoreCase since the registry uses
            // snake_case while the enum uses PascalCase.
            var typeIsBuiltIn = Enum.TryParse<StepType>(step.Type, ignoreCase: true, out _);
            var typeIsCustom = !typeIsBuiltIn
                && allKnownTypes.Any(t => t.Equals(step.Type, StringComparison.OrdinalIgnoreCase));

            if (!typeIsBuiltIn && !typeIsCustom && _stepRegistry != null)
            {
                errors.Add($"Unknown step type: {step.Type} in step {step.Id}");
                // Skip per-step validation when the type is unknown — there's
                // no handler to ask, and the type-rule helper would silently
                // no-op on the unknown switch arm.
                continue;
            }

            // Per-step config validation. Each handler owns the rules for
            // its own type via ValidateConfigurationAsync — see the
            // built-in handlers (ApiCallStepHandler, WebhookStepHandler,
            // etc.) for examples. The previous design had a duplicate
            // controller-side switch with hardcoded cases per type; that
            // switch silently no-op'd on snake_case input and the case
            // labels drifted out of sync with the registry, so all built-in
            // step validation was dead. This pass is the single source of
            // truth now.
            if (_stepRegistry != null)
            {
                var handler = _stepRegistry.GetHandler(step.Type);
                if (handler != null)
                {
                    try
                    {
                        var handlerResult = await handler.ValidateConfigurationAsync(step.Configuration);
                        foreach (var err in handlerResult.Errors)
                        {
                            errors.Add($"Step {step.Id}: {err}");
                        }
                        foreach (var warn in handlerResult.Warnings)
                        {
                            warnings.Add($"Step {step.Id}: {warn}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // A buggy handler shouldn't take the whole validate
                        // call down; log + surface the type so the operator
                        // knows which handler misbehaved.
                        _logger.LogWarning(ex, "Step handler {Type} threw during ValidateConfigurationAsync", step.Type);
                        errors.Add($"Step {step.Id}: validator for type '{step.Type}' threw an exception ({ex.GetType().Name}).");
                    }
                }
            }

            // Validate branch references
            if (step.Branches != null)
            {
                foreach (var (key, targetId) in step.Branches)
                {
                    if (!steps.Any(s => s.Id == targetId))
                    {
                        errors.Add($"Step {step.Id} branch '{key}' references non-existent step: {targetId}");
                    }
                }
            }

            // Sign-up that requires a verified email signs the user in only once a later step
            // verifies it; without one the journey ends with no session.
            if (string.Equals(step.Type, "signup", StringComparison.OrdinalIgnoreCase))
            {
                var signUpSettings = step.Configuration == null ? default : System.Text.Json.JsonSerializer.SerializeToElement(step.Configuration);
                var requiresVerification = signUpSettings.ValueKind != System.Text.Json.JsonValueKind.Object
                    || signUpSettings.GetBoolSetting("requireEmailVerification", true);
                var verifiedLater = !string.IsNullOrEmpty(signUpSettings.ValueKind == System.Text.Json.JsonValueKind.Object ? signUpSettings.GetStringSetting("verificationStepId") : null)
                    || steps.Any(s => s.Order > step.Order && s.Type is "email_verification" or "passwordless_email");
                if (requiresVerification && !verifiedLater)
                {
                    warnings.Add($"Step {step.Id} requires email verification, but no email_verification or passwordless_email step follows it, so new users won't be signed in.");
                }

                foreach (var unknown in await UnknownRoleIdsAsync(signUpSettings, "defaultRoles", cancellationToken))
                {
                    errors.Add($"Step {step.Id} default role '{unknown}' isn't a role of this organisation; pick it again from the role list.");
                }
            }

            if (string.Equals(step.Type, "assign_roles", StringComparison.OrdinalIgnoreCase))
            {
                var assignSettings = step.Configuration == null ? default : System.Text.Json.JsonSerializer.SerializeToElement(step.Configuration);
                foreach (var unknown in await UnknownRoleIdsAsync(assignSettings, "roles", cancellationToken))
                {
                    errors.Add($"Step {step.Id} role '{unknown}' isn't a role of this organisation; pick it again from the role list.");
                }
            }

            // A next step that doesn't exist would end the journey early at runtime.
            if (!string.IsNullOrEmpty(step.OnSuccess) && !steps.Any(s => s.Id == step.OnSuccess))
            {
                errors.Add($"Step {step.Id} continues on success to non-existent step: {step.OnSuccess}");
            }
            if (!string.IsNullOrEmpty(step.OnFailure) && !steps.Any(s => s.Id == step.OnFailure))
            {
                errors.Add($"Step {step.Id} continues on failure to non-existent step: {step.OnFailure}");
            }
            if (!string.IsNullOrEmpty(step.OnFailure) && step.OnFailure == step.Id)
            {
                errors.Add($"Step {step.Id} continues on failure to itself, which would repeat forever.");
            }

            // Step targets named inside a step's own settings: a step ID, or a key of its Branches.
            var stepSchema = _stepRegistry?.GetRegisteredTypes()
                .FirstOrDefault(t => string.Equals(t.Type, step.Type, StringComparison.OrdinalIgnoreCase))?.ConfigurationSchema;
            foreach (var (setting, target) in JourneyStepReferences.FromSettings(stepSchema, step.Configuration))
            {
                if (!steps.Any(s => s.Id == target) && step.Branches?.ContainsKey(target) != true)
                {
                    errors.Add($"Step {step.Id} setting '{setting}' names '{target}', which is neither a step in this journey nor a branch of the step.");
                }
            }

            // Conditions — verify the operator is one the runtime
            // evaluator recognises, and that the value matches the
            // operator's expected shape (numeric for gt/lt, regex for
            // matches, etc.). Catches admin typos before they become
            // silent always-false branches at runtime.
            if (step.Conditions != null)
            {
                foreach (var condition in step.Conditions)
                {
                    var (condErrors, condWarnings) = ValidateCondition(condition);
                    foreach (var err in condErrors)
                        errors.Add($"Step {step.Id} condition: {err}");
                    foreach (var warn in condWarnings)
                        warnings.Add($"Step {step.Id} condition: {warn}");
                }
            }

            // Templated string values inside step.Configuration — admins
            // typically write "{claim:email}" or "{data:returnUrl}" to
            // pull from journey state. We don't have a "step output"
            // catalogue per handler, but we can catch typos in the
            // placeholder grammar itself: dot-syntax instead of colon
            // ("{user.id}" → meant "{user:id}"), and unknown placeholder
            // types ("{claims:foo}" instead of "{claim:foo}").
            if (step.Configuration != null)
            {
                foreach (var warn in ValidateConfigurationPlaceholders(step.Configuration))
                {
                    warnings.Add($"Step {step.Id}: {warn}");
                }
            }
        }

        // Branch-cycle detection — DFS over the explicit branch graph
        // looking for back-edges. The journey orchestrator guards against
        // infinite loops at runtime, but a cycle in the graph almost
        // always means the admin wired something wrong, so flag it.
        foreach (var cycleWarning in DetectBranchCycles(steps))
        {
            warnings.Add(cycleWarning);
        }

        return (errors.Count == 0, errors, warnings);
    }

    /// <summary>Role ids in a step's <paramref name="setting"/> that this tenant can't grant (it grants platform-wide roles and its own).</summary>
    private async Task<List<string>> UnknownRoleIdsAsync(System.Text.Json.JsonElement settings, string setting, CancellationToken cancellationToken)
    {
        if (settings.ValueKind != System.Text.Json.JsonValueKind.Object
            || !settings.TryGetPropertyIgnoreCase(setting, out var value)
            || value.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return new List<string>();
        }

        var ids = value.EnumerateArray().Select(JsonLookup.AsString).OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0) return ids;

        var roleStore = HttpContext?.RequestServices.GetService(typeof(IRoleStore)) as IRoleStore;
        if (roleStore == null) return new List<string>();

        var known = (await roleStore.GetByIdsAsync(ids, cancellationToken))
            .Where(r => r.TenantId == null || r.TenantId == _tenantContext.TenantId)
            .Select(r => r.Id)
            .ToHashSet();
        return ids.Where(id => !known.Contains(id)).ToList();
    }

    /// <summary>
    /// Validate a single <see cref="StepCondition"/> against the operator
    /// vocabulary the runtime evaluator (see <c>IConditionEvaluator</c>) actually
    /// supports, plus operator-specific value shape checks.
    /// </summary>
    private static readonly HashSet<string> ConditionSourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "journeyData", "data", "claim", "context", "user", "path"
    };

    private static (List<string> Errors, List<string> Warnings) ValidateCondition(StepCondition condition)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(condition.Type))
        {
            errors.Add("type is required (e.g. 'claim', 'data', 'context')");
        }
        if (string.IsNullOrWhiteSpace(condition.Field))
        {
            errors.Add("field is required");
        }
        if (string.IsNullOrWhiteSpace(condition.Operator))
        {
            errors.Add("operator is required");
            return (errors, warnings);
        }

        var op = Oluso.Core.Conditions.ConditionOperators.Normalize(condition.Operator);
        if (op == null)
        {
            errors.Add($"unknown operator '{condition.Operator}' (supported: {string.Join(", ", Oluso.Core.Conditions.ConditionOperators.Names)})");
            return (errors, warnings);
        }

        if (!string.IsNullOrWhiteSpace(condition.Type) && !ConditionSourceTypes.Contains(condition.Type))
        {
            errors.Add($"unknown type '{condition.Type}' (supported: {string.Join(", ", ConditionSourceTypes)})");
        }

        // Operator-specific value shape checks.
        var numericOps = Oluso.Core.Conditions.ConditionOperators.NumericOperators;
        var listOps = Oluso.Core.Conditions.ConditionOperators.ListOperators;
        var noValueOps = Oluso.Core.Conditions.ConditionOperators.NoValueOperators;

        if (noValueOps.Contains(op))
        {
            if (!string.IsNullOrEmpty(condition.Value))
            {
                warnings.Add($"operator '{op}' ignores the value field, but a value was provided");
            }
        }
        else if (string.IsNullOrEmpty(condition.Value))
        {
            errors.Add($"operator '{op}' requires a value");
        }
        else if (numericOps.Contains(op))
        {
            if (!double.TryParse(condition.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)
                && !DateTimeOffset.TryParse(condition.Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out _))
            {
                warnings.Add($"operator '{op}' compares numbers or dates, but '{condition.Value}' is neither — the condition will never match");
            }
        }
        else if (op == "regex")
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(condition.Value!);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"regex pattern is invalid: {ex.Message}");
            }
        }
        else if (listOps.Contains(op))
        {
            // Accept either a JSON array or a comma-separated string. Warn
            // if neither — the runtime will treat the value as a single
            // string and the condition will only ever pass on exact match.
            var trimmed = condition.Value!.Trim();
            var looksJsonArray = trimmed.StartsWith("[") && trimmed.EndsWith("]");
            var looksCsv = trimmed.Contains(',');
            if (!looksJsonArray && !looksCsv)
            {
                warnings.Add($"operator '{op}' expects a list — provide a JSON array or comma-separated values");
            }
        }

        return (errors, warnings);
    }

    /// <summary>
    /// Walk every string value in <paramref name="config"/> recursively,
    /// flagging templated placeholders that look like typos. The journey
    /// runtime supports <c>{claim:x} {state:x} {data:x} {input:x} {user:id}
    /// {tenant:id}</c>; anything else inside braces is almost certainly
    /// an admin mistake and would fall through unsubstituted at runtime.
    /// </summary>
    private static IEnumerable<string> ValidateConfigurationPlaceholders(IDictionary<string, object> config)
    {
        var supportedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "claim", "state", "data", "input", "user", "tenant"
        };

        // {prefix:key} — matches the runtime's substitution shape.
        var colonPattern = new System.Text.RegularExpressions.Regex(@"\{(?<prefix>[a-zA-Z_]+):(?<key>[^{}]+)\}");
        // {prefix.key} — common typo, runtime won't substitute it.
        var dotPattern = new System.Text.RegularExpressions.Regex(@"\{(?<prefix>[a-zA-Z_]+)\.(?<key>[a-zA-Z_][a-zA-Z0-9_]*)\}");

        foreach (var stringValue in EnumerateStringValues(config))
        {
            foreach (System.Text.RegularExpressions.Match match in colonPattern.Matches(stringValue))
            {
                var prefix = match.Groups["prefix"].Value;
                if (!supportedPrefixes.Contains(prefix))
                {
                    yield return $"placeholder '{match.Value}' uses unknown prefix '{prefix}' (supported: {string.Join(", ", supportedPrefixes.OrderBy(s => s))})";
                }
            }
            foreach (System.Text.RegularExpressions.Match match in dotPattern.Matches(stringValue))
            {
                var prefix = match.Groups["prefix"].Value;
                if (supportedPrefixes.Contains(prefix))
                {
                    yield return $"placeholder '{match.Value}' uses '.' but the runtime expects ':' — did you mean '{{{prefix}:{match.Groups["key"].Value}}}'?";
                }
            }
        }
    }

    /// <summary>
    /// Yields every string leaf value reachable from a config dict so the
    /// placeholder validator only deals with strings (the runtime only
    /// substitutes inside string values). Walks nested dicts and lists.
    /// </summary>
    private static IEnumerable<string> EnumerateStringValues(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case string s:
                yield return s;
                yield break;
            case System.Text.Json.JsonElement json:
                if (json.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var v = json.GetString();
                    if (v != null) yield return v;
                }
                else if (json.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in json.EnumerateArray())
                    foreach (var inner in EnumerateStringValues(item))
                        yield return inner;
                }
                else if (json.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in json.EnumerateObject())
                    foreach (var inner in EnumerateStringValues(prop.Value))
                        yield return inner;
                }
                yield break;
            case IDictionary<string, object?> dict:
                foreach (var kv in dict)
                foreach (var inner in EnumerateStringValues(kv.Value))
                    yield return inner;
                yield break;
            case System.Collections.IEnumerable enumerable:
                foreach (var item in enumerable)
                foreach (var inner in EnumerateStringValues(item))
                    yield return inner;
                yield break;
        }
    }

    /// <summary>
    /// DFS over the explicit branch graph (step.Branches.Values) looking
    /// for back-edges. Returns one warning per cycle detected. Linear
    /// step ordering isn't included — only the explicit branches — since
    /// the linear path is acyclic by construction (steps execute in the
    /// declared order, can't loop back to an earlier index).
    /// </summary>
    private static IEnumerable<string> DetectBranchCycles(List<JourneyPolicyStepDto> steps)
    {
        var stepById = steps.Where(s => !string.IsNullOrEmpty(s.Id))
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();
        var reportedCycles = new HashSet<string>();

        foreach (var start in stepById.Keys)
        {
            if (visited.Contains(start)) continue;
            foreach (var cycle in DfsForCycles(start, stepById, visited, inStack, new List<string>(), reportedCycles))
                yield return cycle;
        }
    }

    private static IEnumerable<string> DfsForCycles(
        string node,
        Dictionary<string, JourneyPolicyStepDto> stepById,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path,
        HashSet<string> reportedCycles)
    {
        if (inStack.Contains(node))
        {
            // Found a cycle — surface from the first occurrence of `node`
            // in the current path forward.
            var cycleStart = path.IndexOf(node);
            if (cycleStart >= 0)
            {
                var cyclePath = string.Join(" → ", path.Skip(cycleStart).Append(node));
                if (reportedCycles.Add(cyclePath))
                {
                    yield return $"Branch cycle detected: {cyclePath}. The runtime guards against infinite loops, but a cycle usually means the graph is wired incorrectly.";
                }
            }
            yield break;
        }
        if (visited.Contains(node)) yield break;

        inStack.Add(node);
        path.Add(node);

        if (stepById.TryGetValue(node, out var step) && step.Branches != null)
        {
            foreach (var target in step.Branches.Values.Distinct())
            {
                if (!stepById.ContainsKey(target)) continue;
                foreach (var cycle in DfsForCycles(target, stepById, visited, inStack, path, reportedCycles))
                    yield return cycle;
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(node);
        visited.Add(node);
    }

    /// <summary>
    /// Raises an admin event when the event service is available. Optional so a
    /// host that composes the admin API without eventing still works.
    /// </summary>
    private Task RaiseAsync(AdminActionEvent adminEvent, CancellationToken cancellationToken) =>
        _eventService?.RaiseAsync(adminEvent, cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Maps for an API response, replacing step secrets with a redaction marker.
    /// The step config is a free-form dictionary, so SensitiveDataFilter — which
    /// only sees typed properties — can't reach into it; redaction has to happen
    /// here or the credential goes out in the clear.
    /// </summary>
    private PolicyDetailDto MapToDetailDtoRedacted(JourneyPolicy policy)
    {
        var dto = MapToDetailDto(policy);

        if (_secretProtector != null)
        {
            _secretProtector.Redact(dto.Steps.Select(s => new JourneyPolicyStep
            {
                Id = s.Id,
                Type = s.Type,
                Configuration = s.Configuration
            }).ToList());
        }

        return dto;
    }

    private static PolicyDetailDto MapToDetailDto(JourneyPolicy policy)
    {
        return new PolicyDetailDto
        {
            Id = policy.Id,
            Slug = policy.Slug,
            Name = policy.Name,
            Description = policy.Description,
            Type = policy.Type.ToString(),
            TenantId = policy.TenantId,
            Enabled = policy.Enabled,
            Priority = policy.Priority,
            OwnerModule = policy.OwnerModule,
            OwnerModuleName = ModuleName(policy.OwnerModule),
            Sharing = policy.Sharing.ToString(),
            Steps = policy.Steps.Select(s => new JourneyPolicyStepDto
            {
                Id = s.Id,
                Type = s.Type,
                DisplayName = s.DisplayName,
                Optional = s.Optional,
                Order = s.Order,
                Configuration = s.Configuration,
                Branches = s.Branches,
                Conditions = s.Conditions,
                Layout = s.Layout,
                OnSuccess = s.OnSuccess,
                OnFailure = s.OnFailure,
                SkipIfCompleted = s.SkipIfCompleted,
                MaxRetries = s.MaxRetries,
                TimeoutSeconds = s.TimeoutSeconds ?? 0,
                ErrorMessageTemplate = s.ErrorMessageTemplate,
                RequiredClaims = s.RequiredClaims,
                OutputClaims = s.OutputClaims,
                PluginName = s.PluginName
            }).ToList(),
            Conditions = policy.Conditions?.ToList(),
            OutputClaims = policy.OutputClaims?.ToList(),
            Session = policy.Session,
            Ui = policy.Ui,
            Version = policy.Version,
            CreatedAt = policy.CreatedAt,
            UpdatedAt = policy.UpdatedAt,
            // Data collection properties
            RequiresAuthentication = policy.RequiresAuthentication,
            PersistSubmissions = policy.PersistSubmissions,
            SubmissionCollection = policy.SubmissionCollection,
            MaxSubmissions = policy.MaxSubmissions,
            AllowDuplicates = policy.AllowDuplicates,
            DuplicateCheckFields = policy.DuplicateCheckFields?.ToList(),
            SuccessRedirectUrl = policy.SuccessRedirectUrl,
            SuccessMessage = policy.SuccessMessage,
            // Embed settings
            AllowedOrigins = policy.AllowedOrigins,
            ApiKey = policy.ApiKey,
            RequireApiKey = policy.RequireApiKey
        };
    }

    private static int GetCategoryOrder(string category) => category switch
    {
        "Authentication" => 0,
        "User Interaction" => 1,
        "Logic" => 2,
        "Account Management" => 3,
        "Billing" => 4,
        "Custom" => 5,
        _ => 6
    };
}
