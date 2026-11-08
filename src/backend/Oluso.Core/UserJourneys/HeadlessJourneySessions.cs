using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oluso.Core.Conditions;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Dto.Journeys;

namespace Oluso.Core.UserJourneys;

/// <summary>Journey data a headless session carries.</summary>
public static class HeadlessJourneyData
{
    /// <summary>Only sessions started headless can be continued through the headless API.</summary>
    public const string Headless = "headless";

    /// <summary>The page origin that started the session; the session continues only from it.</summary>
    public const string Origin = "headless_origin";

    /// <summary>The user a module started the session for; it continues only with that user's access token.</summary>
    public const string User = "headless_user";

    public const string ClientId = "headless_client_id";
    public const string CodeChallenge = "headless_code_challenge";
    public const string Scope = "headless_scope";
    public const string DefaultScope = "openid profile email";

    /// <summary>The calling page's origin: the Origin header, else the Referer's.</summary>
    public static string? PageOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin)) return origin.TrimEnd('/');

        var referer = request.Headers.Referer.ToString();
        return Uri.TryCreate(referer, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;
    }

    public static string? Read(JourneyState state, string key) =>
        state.Data?.TryGetValue(key, out var value) == true
            ? ConditionOperators.ToValues(value).FirstOrDefault()
            : null;
}

/// <summary>A module starting one of its journeys for the signed-in user, to run in the module's own page.</summary>
/// <param name="Origin">The page the session runs in; later steps must come from it.</param>
public sealed record UserJourneySessionStart(
    string TenantId,
    string PolicyId,
    string Module,
    string UserId,
    IReadOnlyDictionary<string, string> Context,
    string? Origin);

/// <summary>Starts headless journey sessions and describes where they stand.</summary>
public interface IHeadlessJourneySessions
{
    /// <summary>
    /// Starts a module's journey for a signed-in user and runs it to its first step. The session continues only with
    /// that user's access token, so its id alone is useless to anyone else. Throws
    /// <see cref="InvalidOperationException"/> when the module may not run the journey for the tenant.
    /// </summary>
    Task<JourneySessionResponse> StartForUserAsync(UserJourneySessionStart start, CancellationToken cancellationToken = default);

    /// <summary>The session after a start or a step: the next step to render, or how it finished.</summary>
    Task<JourneySessionResponse> RespondAsync(JourneyPolicy policy, JourneyResult result, CancellationToken cancellationToken = default);

    /// <summary>Where the session stands, without running anything.</summary>
    JourneySessionResponse Status(JourneyPolicy policy, JourneyState state);
}

public sealed class HeadlessJourneySessions : IHeadlessJourneySessions
{
    private static readonly JsonSerializerOptions StepModelJson = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IJourneyOrchestrator _orchestrator;
    private readonly IJourneyPolicyStore _policies;
    private readonly IJourneyStateStore _states;
    private readonly IAuthorizationCodeIssuer _codes;
    private readonly IJourneyStepForms _forms;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<HeadlessJourneySessions> _logger;
    private readonly IJourneyStepHtmlRenderer? _html;

    public HeadlessJourneySessions(
        IJourneyOrchestrator orchestrator,
        IJourneyPolicyStore policies,
        IJourneyStateStore states,
        IAuthorizationCodeIssuer codes,
        IJourneyStepForms forms,
        IHttpContextAccessor http,
        ILogger<HeadlessJourneySessions> logger,
        IJourneyStepHtmlRenderer? html = null)
    {
        _orchestrator = orchestrator;
        _policies = policies;
        _states = states;
        _codes = codes;
        _forms = forms;
        _http = http;
        _logger = logger;
        _html = html;
    }

    public async Task<JourneySessionResponse> StartForUserAsync(UserJourneySessionStart start, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(start.TenantId)) throw new ArgumentException("tenantId required", nameof(start));
        if (string.IsNullOrWhiteSpace(start.UserId)) throw new ArgumentException("userId required", nameof(start));
        if (string.IsNullOrWhiteSpace(start.Module)) throw new ArgumentException("module required", nameof(start));
        var httpContext = _http.HttpContext
            ?? throw new InvalidOperationException("A journey session is started from a request");

        var policy = await _policies.GetAsync(start.PolicyId, cancellationToken);
        if (policy == null || (policy.TenantId != null && policy.TenantId != start.TenantId))
            throw new InvalidOperationException($"Journey '{start.PolicyId}' is not available to tenant '{start.TenantId}'");
        if (!policy.Enabled)
            throw new InvalidOperationException($"Journey '{policy.Name}' is disabled");
        if (!policy.UsableByModule(start.Module))
            throw new InvalidOperationException($"Journey '{policy.Name}' belongs to another module and is not shared");

        var properties = new Dictionary<string, object>
        {
            ["TenantId"] = start.TenantId,
            ["ClientId"] = "",
            [HeadlessJourneyData.Headless] = true,
            [HeadlessJourneyData.User] = start.UserId,
        };
        if (!string.IsNullOrEmpty(start.Origin))
        {
            properties[HeadlessJourneyData.Origin] = start.Origin.TrimEnd('/');
        }

        var state = await _orchestrator.StartJourneyAsync(policy, new JourneyStartContext
        {
            HttpContext = httpContext,
            ProtocolName = "headless",
            UserId = start.UserId,
            Launch = new JourneyLaunchTicket(
                start.TenantId,
                policy.Id,
                new Dictionary<string, string>(start.Context),
                ReturnUrl: null,
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
                Module: start.Module),
            Properties = properties
        }, cancellationToken);

        var result = await _orchestrator.ContinueJourneyAsync(state.Id, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Action = "init"
        }, cancellationToken);

        _logger.LogInformation("Module {Module} started journey {PolicyId} as session {SessionId} for user {UserId}",
            start.Module, policy.Id, state.Id, start.UserId);
        return await RespondAsync(policy, result, cancellationToken);
    }

    public async Task<JourneySessionResponse> RespondAsync(JourneyPolicy policy, JourneyResult result, CancellationToken cancellationToken = default)
    {
        var state = await _states.GetAsync(result.JourneyId, cancellationToken);
        var response = new JourneySessionResponse
        {
            SessionId = result.JourneyId,
            Slug = policy.Slug,
            Status = StatusName(result.Status),
            CompletedSteps = state == null ? Array.Empty<string>() : CompletedSteps(state),
            ExpiresAt = state?.ExpiresAt
        };

        switch (result.Status)
        {
            case JourneyStatus.InProgress:
                response.Success = true;
                response.CurrentStep = response.NextStep = state?.CurrentStepId;
                if (result.CurrentStep?.ViewName == "_Redirect")
                {
                    response.RedirectUrl = result.CurrentStep.ViewData?.TryGetValue("redirectUrl", out var url) == true
                        ? url?.ToString()
                        : null;
                }
                else
                {
                    response.Step = await StepViewAsync(policy, state?.CurrentStepId, result.CurrentStep, cancellationToken);
                }
                break;

            case JourneyStatus.Completed:
                response.Completed = true;
                var clientId = state == null ? null : HeadlessJourneyData.Read(state, HeadlessJourneyData.ClientId);
                if (clientId == null)
                {
                    response.Success = true;
                    response.Message = policy.SuccessMessage;
                    response.RedirectUrl = policy.SuccessRedirectUrl;
                }
                else if (string.IsNullOrEmpty(result.Completion?.UserId))
                {
                    response.Status = StatusName(JourneyStatus.Failed);
                    response.Error = "access_denied";
                    response.ErrorDescription = "The journey completed without signing anyone in";
                }
                else
                {
                    // Issued like every code returned to a page: the request the session started with is
                    // validated again (client still enabled, scopes, PKCE, origin) and the code carries the
                    // journey's session and methods.
                    var issued = await _codes.IssueForJourneyAsync(new DirectAuthorizationRequest
                    {
                        ClientId = clientId,
                        Scope = HeadlessJourneyData.Read(state!, HeadlessJourneyData.Scope),
                        CodeChallenge = HeadlessJourneyData.Read(state!, HeadlessJourneyData.CodeChallenge),
                        CodeChallengeMethod = "S256",
                        Origin = HeadlessJourneyData.Read(state!, HeadlessJourneyData.Origin),
                        RedirectUri = DirectAuthorizationRequest.PostMessage
                    }, state!, result.Completion, cancellationToken);

                    if (issued.Succeeded)
                    {
                        response.Success = true;
                        response.Code = issued.Code;
                        _logger.LogInformation("Headless journey {SessionId} issued an authorization code for client {ClientId}",
                            result.JourneyId, clientId);
                    }
                    else
                    {
                        response.Status = StatusName(JourneyStatus.Failed);
                        response.Error = issued.Error;
                        response.ErrorDescription = issued.ErrorDescription;
                        _logger.LogWarning("Headless journey {SessionId} completed but no code was issued for client {ClientId}: {Error}",
                            result.JourneyId, clientId, issued.Error);
                    }
                }
                break;

            default:
                response.Error = result.Error ?? StatusName(result.Status);
                response.ErrorDescription = result.ErrorDescription;
                break;
        }

        return response;
    }

    public JourneySessionResponse Status(JourneyPolicy policy, JourneyState state)
    {
        var inProgress = state.Status == JourneyStatus.InProgress;
        var step = policy.Steps.FirstOrDefault(s => s.Id == state.CurrentStepId);
        return new JourneySessionResponse
        {
            SessionId = state.Id,
            Slug = policy.Slug,
            Status = StatusName(state.Status),
            Success = state.Status is JourneyStatus.InProgress or JourneyStatus.Completed,
            Completed = state.Status == JourneyStatus.Completed,
            CurrentStep = inProgress ? state.CurrentStepId : null,
            NextStep = inProgress ? state.CurrentStepId : null,
            CompletedSteps = CompletedSteps(state),
            ExpiresAt = state.ExpiresAt,
            Step = inProgress && step != null
                ? new JourneyStepView { Id = step.Id, Type = step.Type, DisplayName = step.DisplayName }
                : null
        };
    }

    private async Task<JourneyStepView?> StepViewAsync(JourneyPolicy policy, string? stepId, JourneyStepResult? current, CancellationToken cancellationToken)
    {
        var definition = policy.Steps.FirstOrDefault(s => s.Id == stepId);
        if (definition == null && current == null) return null;

        var view = new JourneyStepView
        {
            Id = definition?.Id ?? current!.StepId,
            Type = definition?.Type ?? current!.StepType,
            DisplayName = definition?.DisplayName ?? current?.DisplayName,
            View = current?.ViewName,
            Model = SerializeStepModel(current?.ViewModel ?? (object?)current?.ViewData, stepId),
            Actions = current?.AvailableActions?.Select(a => new JourneyStepActionView
            {
                Id = a.Id,
                Label = a.Label,
                IsPrimary = a.IsPrimary,
                IsCancel = a.IsCancel
            }).ToList() ?? (IReadOnlyList<JourneyStepActionView>)Array.Empty<JourneyStepActionView>()
        };

        if (!string.IsNullOrEmpty(current?.ViewName))
        {
            var description = new JourneyStepDescription(view.Id, view.Type, current.ViewName, current.ViewModel, current.ViewData);
            view.Form = _forms.Describe(description);
            if (view.Form == null && _html != null)
            {
                view.Html = await _html.RenderAsync(description, cancellationToken);
            }
            if (view.Form == null && view.Html == null)
            {
                _logger.LogWarning("Step {StepId} view {View} has no description or HTML for headless clients", view.Id, current.ViewName);
            }
        }

        return view;
    }

    private JsonElement? SerializeStepModel(object? model, string? stepId)
    {
        if (model == null) return null;
        try
        {
            return JsonSerializer.SerializeToElement(model, model.GetType(), StepModelJson);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or JsonException)
        {
            _logger.LogError(ex, "Step {StepId} model {ModelType} can't be sent to a headless client", stepId, model.GetType().Name);
            return null;
        }
    }

    private static IReadOnlyList<string> CompletedSteps(JourneyState state) =>
        state.Data?.TryGetValue("completedSteps", out var value) == true
            ? ConditionOperators.ToValues(value)
            : Array.Empty<string>();

    public static string StatusName(JourneyStatus status) => status switch
    {
        JourneyStatus.InProgress => "in_progress",
        JourneyStatus.Completed => "completed",
        JourneyStatus.Failed => "failed",
        JourneyStatus.Cancelled => "cancelled",
        JourneyStatus.Expired => "expired",
        _ => status.ToString().ToLowerInvariant()
    };
}
