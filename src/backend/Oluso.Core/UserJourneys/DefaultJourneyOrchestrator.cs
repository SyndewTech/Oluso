using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Events;
using Oluso.Core.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Default implementation of the journey orchestrator
/// </summary>
public class DefaultJourneyOrchestrator : IJourneyOrchestrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IJourneyStateStore _stateStore;
    private readonly IJourneySubmissionStore? _submissionStore;
    private readonly IStepHandlerRegistry _stepHandlerRegistry;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly ILogger<DefaultJourneyOrchestrator> _logger;

    private readonly UserJourneyOptions _options;
    private int _stepsThisRequest;

    /// <summary>Journey data holding a completed journey's output claims (JSON).</summary>
    public const string OutputClaimsDataKey = "_journey_output_claims";

    public DefaultJourneyOrchestrator(
        IServiceProvider serviceProvider,
        IJourneyPolicyStore policyStore,
        IJourneyStateStore stateStore,
        IStepHandlerRegistry stepHandlerRegistry,
        IConditionEvaluator conditionEvaluator,
        ILogger<DefaultJourneyOrchestrator> logger,
        IJourneySubmissionStore? submissionStore = null,
        UserJourneyOptions? options = null)
    {
        _options = options ?? new UserJourneyOptions();
        _serviceProvider = serviceProvider;
        _policyStore = policyStore;
        _stateStore = stateStore;
        _submissionStore = submissionStore;
        _stepHandlerRegistry = stepHandlerRegistry;
        _conditionEvaluator = conditionEvaluator;
        _logger = logger;
    }

    public async Task<JourneyState> StartJourneyAsync(
        JourneyPolicy policy,
        JourneyStartContext context,
        CancellationToken cancellationToken = default)
    {
        var firstStep = policy.Steps.OrderBy(s => s.Order).FirstOrDefault();
        if (firstStep == null)
        {
            throw new InvalidOperationException("Policy has no steps defined");
        }
        if (policy.IsModuleOwned
            && (context.Launch is not { Module.Length: > 0 } launch || launch.PolicyId != policy.Id || !policy.UsableByModule(launch.Module!)))
        {
            throw new InvalidOperationException($"Journey {policy.Id} belongs to module {policy.OwnerModule} and starts only from a module launch link");
        }

        var journeyId = Guid.NewGuid().ToString();
        var tenantId = context.Properties.TryGetValue("TenantId", out var tid) ? tid as string ?? "" : "";

        // Get ClientId - try direct property first, then extract from ValidatedClient object
        var clientId = "";
        if (context.Properties.TryGetValue("ClientId", out var cid))
        {
            clientId = cid as string ?? "";
        }
        else if (context.Properties.TryGetValue("ValidatedClient", out var validatedClient) && validatedClient != null)
        {
            // Try to get ClientId property from the validated client object
            var clientIdProp = validatedClient.GetType().GetProperty("ClientId");
            if (clientIdProp != null)
            {
                clientId = clientIdProp.GetValue(validatedClient) as string ?? "";
            }
        }

        // Initialize journey data with protocol context properties
        var journeyData = new Dictionary<string, object>
        {
            ["login_hint"] = context.LoginHint ?? "",
            ["acrValues"] = context.AcrValues ?? "",
            ["scopes"] = context.RequestedScopes != null ? string.Join(" ", context.RequestedScopes) : "",
            ["client_id"] = clientId
        };

        // Pass through client settings that affect step behavior
        if (context.Properties.TryGetValue("EnableLocalLogin", out var enableLocalLogin))
        {
            journeyData["enable_local_login"] = enableLocalLogin;
        }
        if (context.Properties.TryGetValue("IdentityProviderRestrictions", out var idpRestrictions))
        {
            journeyData["idp_restrictions"] = idpRestrictions;
        }
        if (context.Properties.TryGetValue("DomainHint", out var domainHint))
        {
            journeyData["domain_hint"] = domainHint;
        }
        if (context.Properties.TryGetValue("UiLocales", out var uiLocales))
        {
            journeyData["ui_locales"] = uiLocales;
        }

        if (context.Launch != null)
        {
            JourneyLaunchData.Write(journeyData, context.Launch);
        }

        // Store policy settings for data collection journeys
        journeyData["requires_authentication"] = policy.RequiresAuthentication;
        journeyData["persist_submissions"] = policy.PersistSubmissions;
        if (!string.IsNullOrEmpty(policy.SuccessMessage))
        {
            journeyData["success_message"] = policy.SuccessMessage;
        }
        if (!string.IsNullOrEmpty(policy.SuccessRedirectUrl))
        {
            journeyData["success_redirect_url"] = policy.SuccessRedirectUrl;
        }

        // Capture request metadata for data collection journeys
        if (context.HttpContext != null)
        {
            var httpContext = context.HttpContext;

            // IP address - check forwarded headers first
            var ipAddress = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
                ?? httpContext.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(ipAddress))
            {
                journeyData["ip_address"] = ipAddress;
            }

            // User agent
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrEmpty(userAgent))
            {
                journeyData["user_agent"] = userAgent;
            }

            // Referrer
            var referrer = httpContext.Request.Headers.Referer.ToString();
            if (!string.IsNullOrEmpty(referrer))
            {
                journeyData["referrer"] = referrer;
            }

            // Locale from Accept-Language header
            var acceptLanguage = httpContext.Request.Headers.AcceptLanguage.ToString();
            if (!string.IsNullOrEmpty(acceptLanguage))
            {
                var locale = acceptLanguage.Split(',').FirstOrDefault()?.Split(';').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(locale))
                {
                    journeyData["locale"] = locale;
                }
            }
        }

        if (!string.IsNullOrEmpty(context.UserId))
        {
            journeyData[JourneyStartContext.SessionUserIdDataKey] = context.UserId;
            if (context.SessionAuthTime is { } sessionAuthTime)
            {
                journeyData[JourneyStartContext.SessionAuthTimeDataKey] = sessionAuthTime.ToUnixTimeSeconds().ToString();
            }
        }

        // Copy any additional properties from context (consent flow passes scope, client_id, etc.)
        // Only copy serializable types to avoid cycles with EF entities (e.g., ValidatedClient)
        foreach (var prop in context.Properties)
        {
            if (!journeyData.ContainsKey(prop.Key) && IsSerializableValue(prop.Value))
            {
                journeyData[prop.Key] = prop.Value;
            }
        }

        var state = new JourneyState
        {
            Id = journeyId,
            TenantId = tenantId,
            ClientId = clientId,
            UserId = context.UserId,
            PolicyId = policy.Id,
            CurrentStepId = firstStep.Id,
            Status = JourneyStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            CorrelationId = context.CorrelationId,
            CallbackUrl = context.CallbackUrl,
            Data = journeyData
        };

        await _stateStore.SaveAsync(state, cancellationToken);

        _logger.LogInformation(
            "Started journey {JourneyId} with policy {PolicyId}, correlation {CorrelationId}",
            journeyId, policy.Id, context.CorrelationId);

        return state;
    }

    public async Task<JourneyResult> StartJourneyAsync(JourneyContext context, CancellationToken cancellationToken = default)
    {
        // Find matching policy
        var matchContext = new JourneyPolicyMatchContext
        {
            TenantId = context.TenantId,
            ClientId = context.ClientId,
            Type = context.Type,
            Scopes = context.Scopes,
            AcrValues = context.AcrValues
        };

        var policy = context.PolicyId != null
            ? await _policyStore.GetByIdAsync(context.PolicyId, cancellationToken)
            : await _policyStore.FindMatchingAsync(matchContext, cancellationToken);

        if (policy is { Enabled: false })
        {
            _logger.LogWarning("Policy {PolicyId} is disabled", policy.Id);
            return new JourneyResult
            {
                JourneyId = Guid.NewGuid().ToString(),
                Status = JourneyStatus.Failed,
                Error = "policy_disabled",
                ErrorDescription = "This journey is currently unavailable"
            };
        }

        if (policy == null)
        {
            _logger.LogWarning("No matching policy found for journey context");
            return new JourneyResult
            {
                JourneyId = Guid.NewGuid().ToString(),
                Status = JourneyStatus.Failed,
                Error = "no_policy",
                ErrorDescription = "No matching journey policy found"
            };
        }

        // A module's journey starts only from its module's launch link, which a protocol request doesn't carry.
        if (policy.IsModuleOwned)
        {
            _logger.LogWarning("Policy {PolicyId} of module {Module} requested by a protocol request", policy.Id, policy.OwnerModule);
            return new JourneyResult
            {
                JourneyId = Guid.NewGuid().ToString(),
                Status = JourneyStatus.Failed,
                Error = "policy_unavailable",
                ErrorDescription = "This journey is currently unavailable"
            };
        }

        // Create journey state
        var journeyId = Guid.NewGuid().ToString();
        var firstStep = policy.Steps.OrderBy(s => s.Order).FirstOrDefault();

        if (firstStep == null)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "invalid_policy",
                ErrorDescription = "Policy has no steps defined"
            };
        }

        var state = new JourneyState
        {
            Id = journeyId,
            TenantId = context.TenantId,
            ClientId = context.ClientId,
            UserId = context.UserId,
            PolicyId = policy.Id,
            CurrentStepId = firstStep.Id,
            Status = JourneyStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(policy.MaxJourneyDurationMinutes),
            Data = new Dictionary<string, object>
            {
                ["redirectUri"] = context.RedirectUri ?? "",
                ["state"] = context.State ?? "",
                ["nonce"] = context.Nonce ?? "",
                ["completedSteps"] = new List<string>()
            }
        };

        await _stateStore.SaveAsync(state, cancellationToken);

        _logger.LogInformation("Started journey {JourneyId} with policy {PolicyId}", journeyId, policy.Id);

        // Execute first step
        return await ExecuteStepAsync(journeyId, firstStep, policy, null, cancellationToken);
    }

    public async Task<JourneyResult> ContinueJourneyAsync(string journeyId, JourneyStepInput input, CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.GetAsync(journeyId, cancellationToken);
        if (state == null)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "journey_not_found",
                ErrorDescription = "Journey not found or expired"
            };
        }

        // Check if journey has expired
        if (state.ExpiresAt.HasValue && state.ExpiresAt.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("Journey {JourneyId} has expired at {ExpiresAt}", journeyId, state.ExpiresAt);
            state = state with { Status = JourneyStatus.Expired };
            await _stateStore.SaveAsync(state, cancellationToken);

            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Expired,
                Error = "journey_expired",
                ErrorDescription = "Journey has expired. Please start a new session."
            };
        }

        if (state.Status != JourneyStatus.InProgress)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = state.Status,
                Error = "journey_not_active",
                ErrorDescription = "Journey is no longer active"
            };
        }

        var policy = await _policyStore.GetByIdAsync(state.PolicyId, cancellationToken);
        if (policy == null)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "policy_not_found",
                ErrorDescription = "Journey policy not found"
            };
        }

        var currentStep = policy.Steps.FirstOrDefault(s => s.Id == state.CurrentStepId);
        if (currentStep == null)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "step_not_found",
                ErrorDescription = "Current step not found in policy"
            };
        }

        return await ExecuteStepAsync(journeyId, currentStep, policy, input, cancellationToken);
    }

    private async Task<JourneyResult> ExecuteStepAsync(
        string journeyId,
        JourneyPolicyStep step,
        JourneyPolicy policy,
        JourneyStepInput? input,
        CancellationToken cancellationToken)
    {
        using var origin = EventOriginScope.Begin(EventOrigin.Journey(policy.Id, policy.Name));

        // Steps run one after another in a request until one needs the user. A cycle of
        // steps that never do (an OnFailure loop, branches pointing back) would otherwise
        // recurse until the process falls over.
        if (++_stepsThisRequest > _options.MaxStepsPerJourney)
        {
            _logger.LogError("Journey {JourneyId} ran more than {Max} steps in one request at step {StepId}; stopping a loop in policy {PolicyId}",
                journeyId, _options.MaxStepsPerJourney, step.Id, policy.Id);
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "journey_loop",
                ErrorDescription = "This journey is misconfigured: its steps lead back to each other without stopping."
            };
        }

        var state = await _stateStore.GetAsync(journeyId, cancellationToken);
        if (state == null)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "state_not_found",
                ErrorDescription = "Journey state not found"
            };
        }

        // Check step conditions
        if (step.Conditions != null && step.Conditions.Count > 0)
        {
            // Evaluated here, as this step's own failure: otherwise an error loading the
            // values would surface as a failure of the step before it.
            bool conditionsMet;
            try
            {
                var sources = step.Conditions.Select(c => c.Type).ToList();
                var conditionContext = new ConditionEvaluationContext
                {
                    JourneyData = state.Data ?? new Dictionary<string, object>(),
                    UserId = state.UserId,
                    TenantId = state.TenantId,
                    ClientId = state.ClientId,
                    User = JourneyConditionValues.Reads(sources, "user")
                        ? await JourneyConditionValues.LoadUserAsync(_serviceProvider, state.UserId, cancellationToken)
                        : null,
                    Claims = JourneyConditionValues.Reads(sources, "claim")
                        ? await JourneyConditionValues.LoadClaimsAsync(_serviceProvider, state.UserId)
                        : null
                };

                conditionsMet = await _conditionEvaluator.EvaluateConditionsAsync(step.Conditions, conditionContext, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Evaluating the conditions of step {StepId} in journey {JourneyId} failed", step.Id, journeyId);
                return await HandleStepFailure(journeyId, step, policy, "step_conditions_error",
                    step.ErrorMessageTemplate ?? $"Step '{step.DisplayName ?? step.Id}' couldn't check its conditions.", cancellationToken);
            }

            if (!conditionsMet)
            {
                _logger.LogDebug("Step {StepId} conditions not met, skipping", step.Id);
                return await MoveToNextStepAsync(journeyId, step, policy, step.OnSuccess, cancellationToken);
            }
        }

        // Check if step should be skipped (already completed)
        var completedSteps = GetCompletedSteps(state);
        if (step.SkipIfCompleted && completedSteps.Contains(step.Id))
        {
            _logger.LogDebug("Step {StepId} already completed in this session, skipping", step.Id);
            return await MoveToNextStepAsync(journeyId, step, policy, step.OnSuccess, cancellationToken);
        }

        // Check required claims
        if (step.RequiredClaims != null && step.RequiredClaims.Count > 0)
        {
            var missingClaims = step.RequiredClaims
                .Where(claim => !state.Data?.ContainsKey(claim) ?? true)
                .ToList();

            if (missingClaims.Count > 0)
            {
                _logger.LogWarning("Step {StepId} missing required claims: {Claims}", step.Id, string.Join(", ", missingClaims));
                return new JourneyResult
                {
                    JourneyId = journeyId,
                    Status = JourneyStatus.Failed,
                    Error = "missing_claims",
                    ErrorDescription = $"Required claims not present: {string.Join(", ", missingClaims)}"
                };
            }
        }

        var handler = _stepHandlerRegistry.GetHandler(step.Type);

        if (handler == null)
        {
            _logger.LogWarning("No handler found for step type {StepType}", step.Type);
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "handler_not_found",
                ErrorDescription = $"No handler for step type: {step.Type}"
            };
        }

        // Resolve timeout: step-level overrides policy-level default
        var timeoutSeconds = step.TimeoutSeconds ?? policy.DefaultStepTimeoutSeconds;

        // Build pre-completion validators
        var validators = new List<IPreCompletionValidator>();
        if (policy.PersistSubmissions && !policy.AllowDuplicates &&
            _submissionStore != null && policy.DuplicateCheckFields?.Count > 0)
        {
            validators.Add(new DuplicateSubmissionValidator(
                _submissionStore, policy.Id, policy.DuplicateCheckFields));
        }

        var context = new StepExecutionContext
        {
            JourneyId = journeyId,
            StepId = step.Id,
            TenantId = state.TenantId,
            ClientId = state.ClientId,
            PolicyId = policy.Id,
            Configuration = new JourneyStepConfiguration
            {
                Id = step.Id,
                Type = step.Type,
                DisplayName = step.DisplayName,
                Optional = step.Optional,
                Settings = step.Configuration,
                Branches = step.Branches,
                OnSuccess = step.OnSuccess,
                OnFailure = step.OnFailure,
                PluginName = step.PluginName,
                TimeoutSeconds = timeoutSeconds,
                MaxRetries = step.MaxRetries,
                SkipIfCompleted = step.SkipIfCompleted,
                ErrorMessageTemplate = step.ErrorMessageTemplate,
                RequiredClaims = step.RequiredClaims,
                OutputClaims = step.OutputClaims,
                Conditions = step.Conditions
            },
            UserId = state.UserId,
            JourneyData = state.Data ?? new Dictionary<string, object>(),
            Input = input,
            ServiceProvider = _serviceProvider,
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = step.MaxRetries,
            PluginName = step.PluginName,
            RequiredClaims = step.RequiredClaims,
            ExpectedOutputClaims = step.OutputClaims,
            ErrorMessageTemplate = step.ErrorMessageTemplate,
            WasCompletedBefore = completedSteps.Contains(step.Id),
            PreCompletionValidators = validators
        };

        try
        {
            // A sign-in step is already met when the journey runs for a signed-in user.
            var sessionResult = handler is IPrimaryAuthenticationStep primary && primary.SignsIn(context)
                ? await ExistingSessionLogin.TryCompleteAsync(context, cancellationToken)
                : null;

            // Execute with timeout if specified
            StepHandlerResult result;
            if (sessionResult != null)
            {
                result = sessionResult;
            }
            else if (timeoutSeconds > 0)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    result = await handler.ExecuteAsync(context, timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Step {StepId} timed out after {Timeout} seconds", step.Id, timeoutSeconds);
                    return await HandleStepFailure(journeyId, step, policy, "step_timeout",
                        step.ErrorMessageTemplate ?? $"Step timed out after {timeoutSeconds} seconds", cancellationToken);
                }
            }
            else
            {
                result = await handler.ExecuteAsync(context, cancellationToken);
            }

            // A sign-in asked again on a session journey must be the session's own user;
            // anyone else would take over a journey started for someone signed in.
            if (sessionResult == null && handler is IPrimaryAuthenticationStep signIn && signIn.SignsIn(context)
                && ExistingSessionLogin.SessionUserId(context) is { } sessionUserId
                && !string.IsNullOrEmpty(context.UserId)
                && !string.Equals(context.UserId, sessionUserId, StringComparison.Ordinal))
            {
                _logger.LogWarning("Journey {JourneyId} step {StepId} signed in {UserId}, not the session's user {SessionUserId}",
                    journeyId, step.Id, context.UserId, sessionUserId);
                context.UserId = sessionUserId;
                context.JourneyData.Remove("authenticated_at");
                result = StepHandlerResult.Fail("account_mismatch", "Sign in with the account you're already signed in with.");
            }

            // Merge any explicit handler output into the journey-data bag,
            // then persist unconditionally so in-place context.SetData(...)
            // mutations made during ShowUi paths (e.g. email-verify storing
            // the code before requesting input) survive to the next request.
            if (result.OutputData != null)
            {
                foreach (var kvp in result.OutputData)
                {
                    context.JourneyData[kvp.Key] = kvp.Value;
                }
            }
            state = state with { Data = context.JourneyData };
            await _stateStore.SaveAsync(state, cancellationToken);

            // Update user ID if set during step
            if (context.UserId != state.UserId)
            {
                var attached = string.IsNullOrEmpty(state.UserId) && !string.IsNullOrEmpty(context.UserId);
                state = state with { UserId = context.UserId };
                await _stateStore.SaveAsync(state, cancellationToken);
                if (attached)
                {
                    await NotifyUserAttachedAsync(state, context.UserId!, cancellationToken);
                }
            }

            // Mark step as completed for SkipIfCompleted tracking
            if (result.Outcome is StepOutcome.Continue or StepOutcome.Complete or StepOutcome.Branch or StepOutcome.Skip)
            {
                await MarkStepCompletedAsync(state, step.Id, cancellationToken);
            }

            if (result.FailedAttempt && await AttemptLimitReachedAsync(state, step, cancellationToken))
            {
                return new JourneyResult
                {
                    JourneyId = journeyId,
                    Status = JourneyStatus.Failed,
                    Error = "too_many_attempts",
                    ErrorDescription = "Too many failed attempts. Please start again."
                };
            }

            return result.Outcome switch
            {
                StepOutcome.RequireInput => new JourneyResult
                {
                    JourneyId = journeyId,
                    Status = JourneyStatus.InProgress,
                    CurrentStep = result.StepResult
                },

                // Use OnSuccess if defined, otherwise fall back to result.NextStepId or sequential order
                StepOutcome.Continue => await MoveToNextStepAsync(journeyId, step, policy,
                    result.NextStepId ?? step.OnSuccess, cancellationToken),

                StepOutcome.Skip => await MoveToNextStepAsync(journeyId, step, policy, step.OnSuccess, cancellationToken),

                // A branch names either a key of the step's Branches map or a step directly.
                StepOutcome.Branch when result.BranchId != null && step.Branches?.TryGetValue(result.BranchId, out var branchStepId) == true
                    => await MoveToStepAsync(journeyId, policy, branchStepId, cancellationToken),

                StepOutcome.Branch when result.BranchId != null && policy.Steps.Any(s => s.Id == result.BranchId)
                    => await MoveToStepAsync(journeyId, policy, result.BranchId, cancellationToken),

                StepOutcome.Branch => new JourneyResult
                {
                    JourneyId = journeyId,
                    Status = JourneyStatus.Failed,
                    Error = "branch_step_not_found",
                    ErrorDescription = $"This journey is misconfigured: step '{step.Id}' branches to '{result.BranchId}', which is neither a branch of that step nor a step in the journey."
                },

                StepOutcome.Redirect => new JourneyResult
                {
                    JourneyId = journeyId,
                    Status = JourneyStatus.InProgress,
                    CurrentStep = new JourneyStepResult
                    {
                        StepId = step.Id,
                        StepType = step.Type,
                        ViewName = "_Redirect",
                        ViewData = new Dictionary<string, object> { ["redirectUrl"] = result.RedirectUrl ?? "" }
                    }
                },

                StepOutcome.Complete => await CompleteJourneyAsync(journeyId, state, policy, cancellationToken),

                // Handle failure with OnFailure navigation
                StepOutcome.Failed => await HandleStepFailure(journeyId, step, policy,
                    result.Error ?? "step_failed", result.ErrorDescription, cancellationToken),

                _ => new JourneyResult
                {
                    JourneyId = journeyId,
                    Status = JourneyStatus.Failed,
                    Error = "unknown_outcome",
                    ErrorDescription = "Step returned unknown outcome"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing step {StepId} in journey {JourneyId}", step.Id, journeyId);
            return await HandleStepFailure(journeyId, step, policy, "step_error",
                step.ErrorMessageTemplate ?? ex.Message, cancellationToken);
        }
    }

    private async Task NotifyUserAttachedAsync(JourneyState state, string userId, CancellationToken cancellationToken)
    {
        var attached = new JourneyUserAttached(state.Id, state.TenantId, userId,
            new Dictionary<string, object>(state.Data ?? new Dictionary<string, object>()));
        foreach (var handler in _serviceProvider.GetServices<IJourneyUserAttachedHandler>())
        {
            await handler.OnUserAttachedAsync(attached, cancellationToken);
        }
    }

    private async Task<JourneyResult> MoveToNextStepAsync(
        string journeyId,
        JourneyPolicyStep currentStep,
        JourneyPolicy policy,
        string? explicitNextStepId,
        CancellationToken cancellationToken)
    {
        var state = await _stateStore.GetAsync(journeyId, cancellationToken);
        if (state == null)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "state_not_found"
            };
        }

        JourneyPolicyStep? nextStep;

        if (!string.IsNullOrEmpty(explicitNextStepId))
        {
            nextStep = policy.Steps.FirstOrDefault(s => s.Id == explicitNextStepId);
            if (nextStep == null)
            {
                // Finishing here would skip every remaining step (MFA included).
                _logger.LogError("Step {StepId} names next step {NextStepId}, which is not in policy {PolicyId}",
                    currentStep.Id, explicitNextStepId, policy.Id);
                return new JourneyResult
                {
                    JourneyId = journeyId,
                    Status = JourneyStatus.Failed,
                    Error = "next_step_not_found",
                    ErrorDescription = $"This journey is misconfigured: step '{currentStep.Id}' continues to '{explicitNextStepId}', which doesn't exist."
                };
            }
        }
        else
        {
            var orderedSteps = policy.Steps.OrderBy(s => s.Order).ToList();
            var currentIndex = orderedSteps.FindIndex(s => s.Id == currentStep.Id);
            nextStep = currentIndex >= 0 && currentIndex < orderedSteps.Count - 1
                ? orderedSteps[currentIndex + 1]
                : null;
        }

        if (nextStep == null)
        {
            return await CompleteJourneyAsync(journeyId, state, policy, cancellationToken);
        }

        state = state with { CurrentStepId = nextStep.Id };
        await _stateStore.SaveAsync(state, cancellationToken);

        return await ExecuteStepAsync(journeyId, nextStep, policy, null, cancellationToken);
    }

    private async Task<JourneyResult> MoveToStepAsync(
        string journeyId,
        JourneyPolicy policy,
        string stepId,
        CancellationToken cancellationToken)
    {
        var state = await _stateStore.GetAsync(journeyId, cancellationToken);
        if (state == null)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "state_not_found"
            };
        }

        var step = policy.Steps.FirstOrDefault(s => s.Id == stepId);
        if (step == null)
        {
            return new JourneyResult
            {
                JourneyId = journeyId,
                Status = JourneyStatus.Failed,
                Error = "branch_step_not_found",
                ErrorDescription = $"Branch target step not found: {stepId}"
            };
        }

        state = state with { CurrentStepId = stepId };
        await _stateStore.SaveAsync(state, cancellationToken);

        return await ExecuteStepAsync(journeyId, step, policy, null, cancellationToken);
    }

    /// <summary>
    /// Counts a failed attempt at <paramref name="step"/>. At the step's limit the journey is
    /// failed for good, so the session can't be resumed to keep guessing.
    /// </summary>
    private async Task<bool> AttemptLimitReachedAsync(JourneyState state, JourneyPolicyStep step, CancellationToken cancellationToken)
    {
        var key = $"failed_attempts:{step.Id}";
        var data = state.Data ?? new Dictionary<string, object>();
        var attempts = (data.TryGetValue(key, out var value)
            && int.TryParse(Conditions.ConditionOperators.ToValues(value).FirstOrDefault(), out var counted) ? counted : 0) + 1;
        data[key] = attempts;

        var limit = step.MaxRetries > 0 ? step.MaxRetries : JourneyPolicyStep.DefaultMaxFailedAttempts;
        var reached = attempts >= limit;
        if (reached)
        {
            _logger.LogWarning("Journey {JourneyId} failed after {Attempts} failed attempts at step {StepId}",
                state.Id, attempts, step.Id);
        }

        await _stateStore.SaveAsync(state with { Data = data, Status = reached ? JourneyStatus.Failed : state.Status }, cancellationToken);
        return reached;
    }

    private async Task<JourneyResult> HandleStepFailure(
        string journeyId,
        JourneyPolicyStep step,
        JourneyPolicy policy,
        string error,
        string? errorDescription,
        CancellationToken cancellationToken)
    {
        // If OnFailure is defined, navigate to that step instead of failing
        if (!string.IsNullOrEmpty(step.OnFailure))
        {
            _logger.LogDebug("Step {StepId} failed, navigating to OnFailure step {OnFailureStep}",
                step.Id, step.OnFailure);

            // Store the error in journey data for the failure handler step to access
            var state = await _stateStore.GetAsync(journeyId, cancellationToken);
            if (state?.Data != null)
            {
                state.Data["lastError"] = error;
                state.Data["lastErrorDescription"] = errorDescription ?? "";
                state.Data["failedStepId"] = step.Id;
                await _stateStore.SaveAsync(state, cancellationToken);
            }

            return await MoveToStepAsync(journeyId, policy, step.OnFailure, cancellationToken);
        }

        // No OnFailure defined, fail the journey
        return new JourneyResult
        {
            JourneyId = journeyId,
            Status = JourneyStatus.Failed,
            Error = error,
            ErrorDescription = errorDescription
        };
    }

    private async Task<JourneyResult> CompleteJourneyAsync(
        string journeyId,
        JourneyState state,
        JourneyPolicy policy,
        CancellationToken cancellationToken)
    {
        // Note: Duplicate checks now happen at step completion (in ExecuteStepAsync) for better UX
        // Max submissions check happens at journey start (in Start.cshtml.cs)

        // A module's journey hands its answers to that module before it counts as complete, so a module that
        // can't take them fails the journey (the person can submit again) rather than losing what they entered.
        JourneySubmission? moduleRecord = null;
        if (policy.IsModuleOwned)
        {
            var delivery = await DeliverToModuleAsync(journeyId, state, policy, cancellationToken);
            if (delivery.Failure != null) return delivery.Failure;
            moduleRecord = delivery.Record;
        }

        // The claims the journey hands on, kept with it so the protocol callback that
        // issues tokens for this journey can add them.
        var outputClaims = BuildOutputClaims(state, policy);
        var completedData = new Dictionary<string, object>(state.Data ?? new Dictionary<string, object>())
        {
            [OutputClaimsDataKey] = JsonSerializer.Serialize(outputClaims ?? new Dictionary<string, object>()),
        };
        state = state with { Status = JourneyStatus.Completed, Data = completedData };
        await _stateStore.SaveAsync(state, cancellationToken);

        _logger.LogInformation("Journey {JourneyId} completed successfully", journeyId);

        // Handle data collection submission persistence
        if (moduleRecord != null)
        {
            await RecordModuleRunAsync(moduleRecord, state, policy, cancellationToken);
        }
        else if (policy.PersistSubmissions && _submissionStore != null)
        {
            await SaveSubmissionAsync(journeyId, state, policy, cancellationToken);
        }

        // Use CallbackUrl which contains the correlation_id for protocol callback
        string? redirectUri = state.CallbackUrl;

        // Fallback to legacy redirectUri in Data if CallbackUrl not set
        if (string.IsNullOrEmpty(redirectUri) && state.Data?.TryGetValue("redirectUri", out var redirectUriValue) == true)
        {
            redirectUri = redirectUriValue?.ToString();
        }

        // A journey that isn't serving a sign-in request (data collection, or opened
        // directly) goes to its success address: one a step set during the journey, else
        // the policy's. A sign-in request always returns to the application that asked.
        if (string.IsNullOrEmpty(state.CorrelationId) || !policy.RequiresAuthentication)
        {
            var successRedirect = state.Data?.TryGetValue("success_redirect_url", out var stepRedirect) == true
                && stepRedirect?.ToString() is { Length: > 0 } fromJourney
                    ? fromJourney
                    : policy.SuccessRedirectUrl;
            if (!string.IsNullOrEmpty(successRedirect))
            {
                redirectUri = successRedirect;
            }
        }

        return new JourneyResult
        {
            JourneyId = journeyId,
            Status = JourneyStatus.Completed,
            Completion = new JourneyCompletionResult
            {
                UserId = state.UserId ?? "",
                RedirectUri = redirectUri,
                Claims = outputClaims,
                SuccessMessage = policy.SuccessMessage
            }
        };
    }

    private async Task SaveSubmissionAsync(
        string journeyId,
        JourneyState state,
        JourneyPolicy policy,
        CancellationToken cancellationToken)
    {
        if (_submissionStore == null)
        {
            _logger.LogWarning("Submission store not available, skipping submission persistence for journey {JourneyId}", journeyId);
            return;
        }

        try
        {
            // Note: Duplicate and max submissions checks are done in CompleteJourneyAsync
            // before we get here, so we can proceed directly to saving

            var metadata = MetadataFrom(state);
            var submittedData = SubmittedAnswers(state);

            var submission = new JourneySubmission
            {
                Id = Guid.NewGuid().ToString(),
                PolicyId = policy.Id,
                PolicyName = policy.Name,
                TenantId = state.TenantId,
                JourneyId = journeyId,
                Data = submittedData,
                Metadata = metadata,
                Status = SubmissionStatus.New,
                CreatedAt = DateTime.UtcNow
            };

            await _submissionStore.SaveAsync(submission, cancellationToken);

            _logger.LogInformation("Saved submission {SubmissionId} for policy {PolicyId}",
                submission.Id, policy.Id);

            // Raise event for workflow/webhook integration
            try
            {
                var eventService = _serviceProvider.GetService<IOlusoEventService>();
                if (eventService != null)
                {
                    await eventService.RaiseAsync(new JourneySubmissionCreatedEvent
                    {
                        TenantId = state.TenantId,
                        PolicyId = policy.Id,
                        PolicyName = policy.Name,
                        SubmissionId = submission.Id,
                        JourneyId = journeyId,
                        FieldNames = submittedData.Keys.ToList(),
                        Fields = submittedData,
                        FieldCount = submittedData.Count,
                        SubjectUserId = string.IsNullOrEmpty(state.UserId) ? null : state.UserId,
                        LaunchContext = JourneyLaunchData.ReadContext(state.Data)
                    }, cancellationToken);
                }
            }
            catch (Exception evtEx)
            {
                _logger.LogWarning(evtEx, "Failed to raise submission event for {SubmissionId}", submission.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving submission for journey {JourneyId}", journeyId);
            // Don't fail the journey if submission persistence fails
        }
    }

    /// <summary>Journey data that is the engine's or the request's, not something the person entered.</summary>
    private static readonly HashSet<string> SystemDataKeys = new()
    {
        "acrValues", "scopes", "client_id", "enable_local_login",
        "idp_restrictions", "domain_hint", "requires_authentication", "persist_submissions",
        "success_message", "success_redirect_url", "redirectUri", "state", "nonce",
        "completedSteps", "ip_address", "user_agent", "referrer", "country", "locale",
        "lastError", "lastErrorDescription", "failedStepId",
        JourneyStartContext.SessionUserIdDataKey, JourneyStartContext.SessionAuthTimeDataKey, "login_hint"
    };

    /// <summary>What the person entered: the journey data without system keys and the engine's own ("_" keys).</summary>
    private static Dictionary<string, object> SubmittedAnswers(JourneyState state)
    {
        var answers = new Dictionary<string, object>();
        if (state.Data == null) return answers;
        foreach (var kvp in state.Data)
        {
            if (!SystemDataKeys.Contains(kvp.Key) && !kvp.Key.StartsWith('_'))
            {
                answers[kvp.Key] = kvp.Value;
            }
        }
        return answers;
    }

    private static SubmissionMetadata MetadataFrom(JourneyState state)
    {
        var metadata = new SubmissionMetadata();
        if (state.Data == null) return metadata;
        if (state.Data.TryGetValue("ip_address", out var ip)) metadata.IpAddress = ip?.ToString();
        if (state.Data.TryGetValue("user_agent", out var ua)) metadata.UserAgent = ua?.ToString();
        if (state.Data.TryGetValue("referrer", out var referrer)) metadata.Referrer = referrer?.ToString();
        if (state.Data.TryGetValue("country", out var country)) metadata.Country = country?.ToString();
        if (state.Data.TryGetValue("locale", out var locale)) metadata.Locale = locale?.ToString();
        return metadata;
    }

    /// <summary>
    /// Hands a module journey's answers to the module the run belongs to. Returns the platform's answer-free
    /// record of the run to keep, or the failed result when no sink of that module took the answers.
    /// </summary>
    private async Task<(JourneySubmission? Record, JourneyResult? Failure)> DeliverToModuleAsync(
        string journeyId, JourneyState state, JourneyPolicy policy, CancellationToken cancellationToken)
    {
        var module = JourneyRunOwnership.ModuleFor(policy, state.Data?.AsReadOnly())!;

        var sinks = _serviceProvider.GetServices<IJourneyResultSink>()
            .Where(s => string.Equals(s.Module, module, StringComparison.Ordinal))
            .ToList();
        if (sinks.Count == 0)
        {
            _logger.LogError("Journey {JourneyId} of policy {PolicyId} belongs to module {Module}, which receives no journey answers; the journey was not completed",
                journeyId, policy.Id, module);
            return (null, Undelivered(journeyId));
        }

        var record = new JourneySubmission
        {
            Id = Guid.NewGuid().ToString(),
            PolicyId = policy.Id,
            PolicyName = policy.Name,
            TenantId = state.TenantId,
            JourneyId = journeyId,
            Metadata = MetadataFrom(state),
            Status = SubmissionStatus.New,
            CreatedAt = DateTime.UtcNow,
        };
        var answers = new JourneyAnswers(module, state.TenantId, policy.Id, policy.Name, journeyId, record.Id,
            string.IsNullOrEmpty(state.UserId) ? null : state.UserId,
            SubmittedAnswers(state), JourneyLaunchData.ReadContext(state.Data), record.Metadata, record.CreatedAt);

        try
        {
            foreach (var sink in sinks)
            {
                await sink.DeliverAsync(answers, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module} did not take the answers of journey {JourneyId} (policy {PolicyId}); the journey was not completed",
                module, journeyId, policy.Id);
            return (null, Undelivered(journeyId));
        }

        record.Tags = [module];
        return (record, null);
    }

    private static JourneyResult Undelivered(string journeyId) => new()
    {
        JourneyId = journeyId,
        Status = JourneyStatus.Failed,
        Error = "answers_not_delivered",
        ErrorDescription = "Your answers could not be saved. Please try submitting again.",
    };

    /// <summary>Keeps the platform's record of a module journey run: when it happened and its status, never the answers.</summary>
    private async Task RecordModuleRunAsync(JourneySubmission record, JourneyState state, JourneyPolicy policy, CancellationToken cancellationToken)
    {
        if (_submissionStore != null)
        {
            try
            {
                await _submissionStore.SaveAsync(record, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not record module journey run {SubmissionId} of policy {PolicyId}", record.Id, policy.Id);
            }
        }

        try
        {
            var eventService = _serviceProvider.GetService<IOlusoEventService>();
            if (eventService != null)
            {
                var fieldNames = SubmittedAnswers(state).Keys.ToList();
                await eventService.RaiseAsync(new JourneySubmissionCreatedEvent
                {
                    TenantId = state.TenantId,
                    PolicyId = policy.Id,
                    PolicyName = policy.Name,
                    SubmissionId = record.Id,
                    JourneyId = record.JourneyId,
                    Module = record.Tags?.FirstOrDefault(),
                    FieldNames = fieldNames,
                    FieldCount = fieldNames.Count,
                }, cancellationToken);
            }
        }
        catch (Exception evtEx)
        {
            _logger.LogWarning(evtEx, "Failed to raise submission event for {SubmissionId}", record.Id);
        }
    }

    private IReadOnlyDictionary<string, object>? BuildOutputClaims(JourneyState state, JourneyPolicy policy)
    {
        if (policy.OutputClaims == null || policy.OutputClaims.Count == 0)
        {
            // Without a mapping only the user's standard identity claims leave the journey.
            // Journey data also holds request details, form answers, codes and internal
            // state, none of which belongs in a token unless the policy maps it.
            var allowed = new HashSet<string>(_options.EffectiveDefaultOutputClaims, StringComparer.Ordinal);
            return state.Data?
                .Where(kv => allowed.Contains(kv.Key) && kv.Value != null)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        var claims = new Dictionary<string, object>();

        foreach (var mapping in policy.OutputClaims)
        {
            object? value = mapping.SourceType switch
            {
                "journeyData" when state.Data?.TryGetValue(mapping.SourcePath, out var jv) == true => jv,
                "claim" when state.ClaimsBag.TryGet(mapping.SourcePath, out var cv) => cv,
                "literal" => mapping.SourcePath,
                "context" => mapping.SourcePath switch
                {
                    "userId" => state.UserId,
                    "tenantId" => state.TenantId,
                    "clientId" => state.ClientId,
                    "isAuthenticated" => string.IsNullOrEmpty(state.UserId) ? "false" : "true",
                    _ => null
                },
                _ => null
            };

            value ??= mapping.DefaultValue;

            if (value != null)
            {
                claims[mapping.TargetClaimType] = value;
            }
        }

        return claims;
    }

    private static List<string> GetCompletedSteps(JourneyState state)
    {
        if (state.Data?.TryGetValue("completedSteps", out var completedObj) == true)
        {
            if (completedObj is List<string> list)
                return list;
            if (completedObj is IEnumerable<object> enumerable)
                return enumerable.Select(o => o?.ToString() ?? "").ToList();
        }
        return new List<string>();
    }

    private async Task MarkStepCompletedAsync(JourneyState state, string stepId, CancellationToken cancellationToken)
    {
        var completedSteps = GetCompletedSteps(state);
        if (!completedSteps.Contains(stepId))
        {
            completedSteps.Add(stepId);
            var data = state.Data ?? new Dictionary<string, object>();
            data["completedSteps"] = completedSteps;
            var updatedState = state with { Data = data };
            await _stateStore.SaveAsync(updatedState, cancellationToken);
        }
    }

    public Task<JourneyState?> GetStateAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        return _stateStore.GetAsync(journeyId, cancellationToken);
    }

    public async Task CancelJourneyAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.GetAsync(journeyId, cancellationToken);
        if (state != null)
        {
            state = state with { Status = JourneyStatus.Cancelled };
            await _stateStore.SaveAsync(state, cancellationToken);
            _logger.LogInformation("Journey {JourneyId} cancelled", journeyId);
        }
    }

    /// <summary>
    /// Checks if a value is safe to serialize (primitive types, strings, simple collections).
    /// Filters out EF entities and other complex objects that may have circular references.
    /// </summary>
    private static bool IsSerializableValue(object? value)
    {
        if (value == null)
            return true;

        var type = value.GetType();

        // Allow primitive types and strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) ||
            type == typeof(TimeSpan))
            return true;

        // Allow nullable primitives
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null && (underlyingType.IsPrimitive || underlyingType == typeof(decimal) ||
            underlyingType == typeof(DateTime) || underlyingType == typeof(DateTimeOffset) ||
            underlyingType == typeof(Guid) || underlyingType == typeof(TimeSpan)))
            return true;

        // Allow simple collections of strings
        if (value is IEnumerable<string>)
            return true;

        // Reject EF entities (they typically have navigation properties with Id patterns)
        // Look for common entity patterns: has "Id" property and other navigation-like properties
        if (type.GetProperty("Id") != null && type.Namespace?.Contains("Domain.Entities") == true)
            return false;

        // Reject complex objects from Oluso.Core.Domain namespace (entities)
        if (type.Namespace?.StartsWith("Oluso.Core.Domain") == true)
            return false;

        return true;
    }
}
