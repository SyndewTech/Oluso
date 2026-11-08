using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Conditions;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Events;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Api;

/// <summary>
/// Public API controller for journey/form interactions from the SDK. Single-step forms use
/// GET/submit; any journey can also run step by step through the headless endpoints
/// (start, sessions/{id}/step, sessions/{id}). No authentication required, but validates:
/// - Origin header against policy AllowedOrigins
/// - X-API-Key header if policy RequireApiKey is set
/// - Tenant isolation via X-Tenant-ID header
/// - For authentication journeys: the request is validated by <see cref="IAuthorizationCodeIssuer"/>,
///   like every sign-in that returns a code to a page (enabled client, allowed scopes, PKCE S256, an
///   origin the client registered). They complete with an authorization code bound to it.
/// </summary>
[ApiController]
[Route("api/public/journeys")]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "public")]
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class PublicJourneyController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IJourneyOrchestrator _orchestrator;
    private readonly IJourneyStateStore _stateStore;
    private readonly IAuthorizationCodeIssuer _codes;
    private readonly IHeadlessJourneySessions _sessions;
    private readonly IJourneySubmissionStore? _submissionStore;
    private readonly IOlusoEventService? _eventService;
    private readonly ILogger<PublicJourneyController> _logger;

    public PublicJourneyController(
        ITenantContext tenantContext,
        IJourneyPolicyStore policyStore,
        IJourneyOrchestrator orchestrator,
        IJourneyStateStore stateStore,
        IAuthorizationCodeIssuer codes,
        IHeadlessJourneySessions sessions,
        ILogger<PublicJourneyController> logger,
        IJourneySubmissionStore? submissionStore = null,
        IOlusoEventService? eventService = null)
    {
        _tenantContext = tenantContext;
        _policyStore = policyStore;
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _codes = codes;
        _sessions = sessions;
        _submissionStore = submissionStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get journey metadata (fields, UI config) for rendering the form
    /// </summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetJourney(string slug, CancellationToken cancellationToken)
    {
        var policy = await _policyStore.GetBySlugAsync(slug, cancellationToken);

        if (policy == null || !policy.Enabled)
        {
            return NotFound(new { message = "Journey not found" });
        }

        if (policy.RequiresAuthentication)
        {
            return Unauthorized(new { message = "This journey requires authentication" });
        }

        var validationResult = ValidateAccess(policy);
        if (validationResult != null) return validationResult;

        var response = new
        {
            id = policy.Id,
            slug = policy.Slug,
            name = policy.Name,
            type = policy.Type.ToString(),
            description = policy.Description,
            fields = ExtractFields(policy.Steps),
            ui = policy.Ui,
            requiresAuthentication = policy.RequiresAuthentication,
            successMessage = policy.SuccessMessage,
            successRedirectUrl = policy.SuccessRedirectUrl,
        };

        return Ok(response);
    }

    /// <summary>
    /// Submit data to a data-collection journey
    /// </summary>
    [HttpPost("{slug}/submit")]
    public async Task<IActionResult> Submit(string slug, [FromBody] PublicSubmissionRequest request, CancellationToken cancellationToken)
    {
        var policy = await _policyStore.GetBySlugAsync(slug, cancellationToken);

        if (policy == null || !policy.Enabled)
        {
            return NotFound(new { message = "Journey not found" });
        }

        if (policy.RequiresAuthentication)
        {
            return Unauthorized(new { message = "This journey requires authentication" });
        }

        // This endpoint stores answers with the platform; a module's journey runs only from its module's launch link,
        // so its answers go only to that module.
        if (policy.IsModuleOwned)
        {
            return StatusCode(403, new { message = "This journey belongs to a module and is started from it." });
        }

        var validationResult = ValidateAccess(policy);
        if (validationResult != null) return validationResult;

        // Check max submissions
        if (policy.MaxSubmissions > 0 && _submissionStore != null)
        {
            var count = await _submissionStore.CountByPolicyAsync(policy.Id, cancellationToken);
            if (count >= policy.MaxSubmissions)
            {
                return UnprocessableEntity(new { message = "Maximum submissions reached" });
            }
        }

        // Check duplicates
        if (!policy.AllowDuplicates && policy.DuplicateCheckFields?.Count > 0 && _submissionStore != null)
        {
            foreach (var field in policy.DuplicateCheckFields)
            {
                if (request.Data.TryGetValue(field, out var value) && value != null)
                {
                    var exists = await _submissionStore.HasDuplicateAsync(
                        policy.Id, field, value.ToString()!, cancellationToken);
                    if (exists)
                    {
                        return Conflict(new { message = $"A submission with this {field} already exists" });
                    }
                }
            }
        }

        // Persist submission
        if (policy.PersistSubmissions && _submissionStore != null)
        {
            var submission = new JourneySubmission
            {
                PolicyId = policy.Id,
                TenantId = _tenantContext.TenantId,
                Data = request.Data.Where(kv => kv.Value != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!),
                Metadata = new SubmissionMetadata
                {
                    IpAddress = GetClientIp(),
                    UserAgent = Request.Headers.UserAgent.ToString(),
                    Referrer = request.Metadata?.GetValueOrDefault("referrer"),
                },
            };

            await _submissionStore.SaveAsync(submission, cancellationToken);

            _logger.LogInformation(
                "Journey submission saved for policy {PolicyId} from {Origin}",
                policy.Id, Request.Headers.Origin.ToString());

            // Raise event for workflow/webhook integration
            if (_eventService != null)
            {
                try
                {
                    var submittedData = request.Data
                        .Where(kv => kv.Value != null)
                        .ToDictionary(kv => kv.Key, kv => kv.Value!);

                    await _eventService.RaiseAsync(new JourneySubmissionCreatedEvent
                    {
                        TenantId = _tenantContext.TenantId,
                        PolicyId = policy.Id,
                        PolicyName = policy.Name,
                        SubmissionId = submission.Id,
                        FieldNames = submittedData.Keys.ToList(),
                        Fields = submittedData,
                        FieldCount = submittedData.Count
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to raise submission event for policy {PolicyId}", policy.Id);
                }
            }
        }

        return Ok(new
        {
            success = true,
            message = policy.SuccessMessage ?? "Submission received",
            redirectUrl = policy.SuccessRedirectUrl,
        });
    }

    /// <summary>
    /// Start a journey and run its first step: with <see cref="StartJourneyRequest.Data"/> as
    /// that step's input, or without it to get the step to render.
    /// </summary>
    [HttpPost("{slug}/start")]
    public async Task<IActionResult> StartJourney(string slug, [FromBody] StartJourneyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _policyStore.GetBySlugAsync(slug, cancellationToken);
        if (policy == null || !policy.Enabled)
        {
            return NotFound(new { message = "Journey not found" });
        }

        // A module's journey is started by its module for the signed-in user (IHeadlessJourneySessions.StartForUserAsync).
        if (policy.IsModuleOwned)
        {
            return StatusCode(403, new { message = "This journey belongs to a module and is started from it." });
        }

        var accessProblem = ValidateAccess(policy);
        if (accessProblem != null) return accessProblem;

        var origin = RequestOrigin();
        var properties = new Dictionary<string, object>
        {
            ["TenantId"] = _tenantContext.TenantId ?? "",
            [HeadlessJourneyData.Headless] = true,
        };
        if (origin != null)
        {
            properties[HeadlessJourneyData.Origin] = origin;
        }

        // An app whose user signed in through Oluso starts the journey for that user with their access token: the
        // journey acts on their account, and the session continues only with that user's token.
        string? signedInUser = null;
        if (!policy.RequiresAuthentication && Request.Headers.ContainsKey("Authorization"))
        {
            var authenticated = await HttpContext.AuthenticateAsync(OidcConstants.AccessTokenAuthenticationScheme);
            signedInUser = authenticated.Succeeded ? authenticated.Principal?.FindFirst("sub")?.Value : null;
            var tokenTenant = authenticated.Principal?.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrEmpty(signedInUser)
                || (!string.IsNullOrEmpty(tokenTenant) && !string.Equals(tokenTenant, _tenantContext.TenantId, StringComparison.Ordinal)))
            {
                return Unauthorized(new { message = "The access token is not valid for this tenant" });
            }
            properties[HeadlessJourneyData.User] = signedInUser;
        }

        if (policy.RequiresAuthentication)
        {
            var (scopes, authProblem) = await ValidateAuthenticationStartAsync(policy, request, origin, cancellationToken);
            if (authProblem != null) return authProblem;

            properties["ClientId"] = request.ClientId!;
            properties[HeadlessJourneyData.ClientId] = request.ClientId!;
            properties[HeadlessJourneyData.CodeChallenge] = request.CodeChallenge!;
            properties[HeadlessJourneyData.Scope] = string.Join(' ', scopes!);
        }

        var state = await _orchestrator.StartJourneyAsync(policy, new JourneyStartContext
        {
            HttpContext = HttpContext,
            ProtocolName = "headless",
            UserId = signedInUser,
            Properties = properties
        }, cancellationToken);

        var result = await _orchestrator.ContinueJourneyAsync(state.Id, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Values = ToStepValues(request.Data),
            Action = request.Data == null ? "init" : null
        }, cancellationToken);

        return Ok(await _sessions.RespondAsync(policy, result, cancellationToken));
    }

    /// <summary>Submit input (or an action) for the session's current step.</summary>
    [HttpPost("sessions/{sessionId}/step")]
    [Consumes("application/json")]
    public async Task<IActionResult> SubmitStep(string sessionId, [FromBody] JourneyStepRequest request, CancellationToken cancellationToken) =>
        await ContinueAsync(sessionId, ToStepValues(request.Data), files: null, request.Action, cancellationToken);

    /// <summary>
    /// Submit the current step as a form, the way the hosted page posts it: every field as a value and files under
    /// their field's name. A step action goes in <see cref="StepActionField"/>.
    /// </summary>
    [HttpPost("sessions/{sessionId}/step")]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxStepUploadBytes)]
    [RequestSizeLimit(MaxStepUploadBytes)]
    public async Task<IActionResult> SubmitStepForm(string sessionId, CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var values = new Dictionary<string, object>();
        foreach (var (key, value) in form)
        {
            if (key == StepActionField) continue;
            values[key] = value.ToString();
        }
        var files = form.Files
            .Select(f => new JourneyInputFile(f.Name, f.FileName, f.ContentType, f.Length, f.OpenReadStream))
            .ToList();
        return await ContinueAsync(sessionId, values, files, form[StepActionField].FirstOrDefault(), cancellationToken);
    }

    /// <summary>
    /// Where the session stands, without running anything (a step's model comes with the
    /// start/step response that produced it).
    /// </summary>
    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, CancellationToken cancellationToken)
    {
        var (state, policy, problem) = await LoadHeadlessSessionAsync(sessionId, cancellationToken);
        if (problem != null) return problem;

        return Ok(_sessions.Status(policy!, state!));
    }

    /// <summary>The form field a multipart step post carries its step action in.</summary>
    public const string StepActionField = "__journey_action";

    private const long MaxStepUploadBytes = 64L * 1024 * 1024;

    private async Task<IActionResult> ContinueAsync(
        string sessionId, IDictionary<string, object>? values, IReadOnlyList<JourneyInputFile>? files, string? action, CancellationToken cancellationToken)
    {
        var (state, policy, problem) = await LoadHeadlessSessionAsync(sessionId, cancellationToken);
        if (problem != null) return problem;

        // A finished session never runs again, so it can't complete (and mint a code or file answers) twice.
        if (state!.Status != JourneyStatus.InProgress)
        {
            return Ok(_sessions.Status(policy!, state));
        }

        var result = await _orchestrator.ContinueJourneyAsync(sessionId, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Values = values,
            Files = files is { Count: > 0 } ? files : null,
            Action = action
        }, cancellationToken);

        return Ok(await _sessions.RespondAsync(policy!, result, cancellationToken));
    }

    /// <summary>
    /// The sign-in request an authentication journey completes with, validated before the user is asked for
    /// anything. Returns the scopes it may grant.
    /// </summary>
    private async Task<(IReadOnlyList<string>? Scopes, IActionResult? Problem)> ValidateAuthenticationStartAsync(
        JourneyPolicy policy, StartJourneyRequest request, string? origin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return (null, BadRequest(new { error = "invalid_request", error_description = "client_id is required for an authentication journey" }));
        }

        var validation = await _codes.ValidateAsync(new DirectAuthorizationRequest
        {
            ClientId = request.ClientId,
            Scope = request.Scope,
            CodeChallenge = request.CodeChallenge,
            CodeChallengeMethod = request.CodeChallengeMethod,
            Origin = origin,
            RedirectUri = DirectAuthorizationRequest.PostMessage
        }, cancellationToken);

        if (validation.OriginRejected)
        {
            _logger.LogWarning("Origin {Origin} not allowed to run journey {PolicyId} for client {ClientId}",
                origin, policy.Id, request.ClientId);
            return (null, origin == null ? Forbidden("Origin header required") : Forbidden("Origin not allowed"));
        }
        if (!validation.IsValid)
        {
            return (null, BadRequest(new { error = validation.Error, error_description = validation.ErrorDescription }));
        }

        return (validation.Scopes, null);
    }

    /// <summary>
    /// A headless session continues only from the page origin that started it and, when a module started it for a
    /// user, only with that user's access token. Anyone else gets the same answer as for a session that doesn't exist.
    /// </summary>
    private async Task<(JourneyState? State, JourneyPolicy? Policy, IActionResult? Problem)> LoadHeadlessSessionAsync(
        string sessionId, CancellationToken cancellationToken)
    {
        var notFound = NotFound(new { message = "Journey session not found" });
        var state = await _stateStore.GetAsync(sessionId, cancellationToken);
        if (state == null || HeadlessJourneyData.Read(state, HeadlessJourneyData.Headless) != "true")
        {
            return (null, null, notFound);
        }

        var startedFrom = HeadlessJourneyData.Read(state, HeadlessJourneyData.Origin);
        if (startedFrom != null && !string.Equals(startedFrom, RequestOrigin(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Journey session {SessionId} used from {Origin}, started from {StartedFrom}",
                sessionId, RequestOrigin(), startedFrom);
            return (null, null, Forbidden("Origin not allowed"));
        }

        var boundUser = HeadlessJourneyData.Read(state, HeadlessJourneyData.User);
        if (boundUser != null)
        {
            var authenticated = await HttpContext.AuthenticateAsync(OidcConstants.AccessTokenAuthenticationScheme);
            var caller = authenticated.Succeeded ? authenticated.Principal?.FindFirst("sub")?.Value : null;
            if (!string.Equals(caller, boundUser, StringComparison.Ordinal))
            {
                _logger.LogWarning("Journey session {SessionId} of user {UserId} used by {Caller}",
                    sessionId, boundUser, caller ?? "an unauthenticated caller");
                return (null, null, notFound);
            }
        }

        var policy = await _policyStore.GetByIdAsync(state.PolicyId, cancellationToken);
        if (policy == null)
        {
            return (null, null, NotFound(new { message = "Journey not found" }));
        }

        return (state, policy, null);
    }

    /// <summary>Step input the way a posted form carries it: strings, list items joined by commas.</summary>
    private static IDictionary<string, object>? ToStepValues(Dictionary<string, object?>? data) =>
        data?
            .Where(kv => kv.Value != null)
            .ToDictionary(kv => kv.Key, kv => (object)string.Join(",", ConditionOperators.ToValues(kv.Value)));




    private string? RequestOrigin() => HeadlessJourneyData.PageOrigin(Request);

    /// <summary>
    /// Validate origin and API key against policy configuration
    /// </summary>
    private IActionResult? ValidateAccess(JourneyPolicy policy)
    {
        // Validate API key if required
        if (policy.RequireApiKey)
        {
            var apiKey = Request.Headers["X-API-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(policy.ApiKey)
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(apiKey), System.Text.Encoding.UTF8.GetBytes(policy.ApiKey)))
            {
                _logger.LogWarning(
                    "API key validation failed for journey {PolicyId} from {Origin}",
                    policy.Id, Request.Headers.Origin.ToString());
                return Unauthorized(new { message = "Invalid or missing API key" });
            }
        }

        // Validate origin if AllowedOrigins is configured
        if (!string.IsNullOrEmpty(policy.AllowedOrigins))
        {
            var origin = Request.Headers.Origin.ToString();
            var referer = Request.Headers.Referer.ToString();

            // Use origin header, fall back to referer
            var requestOrigin = !string.IsNullOrEmpty(origin)
                ? origin
                : !string.IsNullOrEmpty(referer)
                    ? new Uri(referer).GetLeftPart(UriPartial.Authority)
                    : null;

            if (string.IsNullOrEmpty(requestOrigin))
            {
                // No origin — could be server-side request. Allow if no API key required,
                // otherwise the API key check above already validated.
                if (!policy.RequireApiKey)
                {
                    _logger.LogWarning(
                        "No origin header for journey {PolicyId} with AllowedOrigins configured",
                        policy.Id);
                    return Forbidden("Origin header required");
                }
            }
            else
            {
                var allowedOrigins = policy.AllowedOrigins
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim().TrimEnd('/'))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!allowedOrigins.Contains(requestOrigin.TrimEnd('/')))
                {
                    _logger.LogWarning(
                        "Origin {Origin} not allowed for journey {PolicyId}. Allowed: {AllowedOrigins}",
                        requestOrigin, policy.Id, policy.AllowedOrigins);
                    return Forbidden("Origin not allowed");
                }
            }
        }

        return null; // Access granted
    }

    private IActionResult Forbidden(string message)
    {
        return StatusCode(403, new { message });
    }

    private string? GetClientIp()
    {
        return Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Extract form field definitions from journey steps.
    /// Data-collection journeys store fields in the first form/data_collection step's config.
    /// </summary>
    private static List<object>? ExtractFields(IList<JourneyPolicyStep> steps)
    {
        var dataStep = steps.FirstOrDefault(s =>
            s.Type is "data_collection" or "form" or "DynamicForm" or "dynamic_form");

        if (dataStep?.Configuration?.TryGetValue("fields", out var fieldsObj) == true)
        {
            if (fieldsObj is System.Text.Json.JsonElement jsonEl)
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<object>>(jsonEl.GetRawText());
            }

            if (fieldsObj is List<object> list)
            {
                return list;
            }
        }

        return null;
    }
}

