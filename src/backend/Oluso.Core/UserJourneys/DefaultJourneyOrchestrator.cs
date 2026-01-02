using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    public DefaultJourneyOrchestrator(
        IServiceProvider serviceProvider,
        IJourneyPolicyStore policyStore,
        IJourneyStateStore stateStore,
        IStepHandlerRegistry stepHandlerRegistry,
        IConditionEvaluator conditionEvaluator,
        ILogger<DefaultJourneyOrchestrator> logger,
        IJourneySubmissionStore? submissionStore = null)
    {
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
            ["loginHint"] = context.LoginHint ?? "",
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

        // Copy any additional properties from context (consent flow passes scope, client_id, etc.)
        foreach (var prop in context.Properties)
        {
            if (!journeyData.ContainsKey(prop.Key))
            {
                journeyData[prop.Key] = prop.Value;
            }
        }

        var state = new JourneyState
        {
            Id = journeyId,
            TenantId = tenantId,
            ClientId = clientId,
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
            var conditionContext = new ConditionEvaluationContext
            {
                JourneyData = state.Data ?? new Dictionary<string, object>(),
                UserId = state.UserId,
                TenantId = state.TenantId,
                ClientId = state.ClientId
            };

            var conditionsMet = await _conditionEvaluator.EvaluateConditionsAsync(step.Conditions, conditionContext, cancellationToken);
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
            // Execute with timeout if specified
            StepHandlerResult result;
            if (timeoutSeconds > 0)
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

            // Update state with any output data
            if (result.OutputData != null)
            {
                foreach (var kvp in result.OutputData)
                {
                    context.JourneyData[kvp.Key] = kvp.Value;
                }
                state = state with { Data = context.JourneyData };
                await _stateStore.SaveAsync(state, cancellationToken);
            }

            // Update user ID if set during step
            if (context.UserId != state.UserId)
            {
                state = state with { UserId = context.UserId };
                await _stateStore.SaveAsync(state, cancellationToken);
            }

            // Mark step as completed for SkipIfCompleted tracking
            if (result.Outcome == StepOutcome.Continue || result.Outcome == StepOutcome.Complete)
            {
                await MarkStepCompletedAsync(state, step.Id, cancellationToken);
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

                StepOutcome.Branch when result.BranchId != null && step.Branches?.TryGetValue(result.BranchId, out var branchStepId) == true
                    => await MoveToStepAsync(journeyId, policy, branchStepId, cancellationToken),

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

        state = state with { Status = JourneyStatus.Completed };
        await _stateStore.SaveAsync(state, cancellationToken);

        _logger.LogInformation("Journey {JourneyId} completed successfully", journeyId);

        // Handle data collection submission persistence
        if (policy.PersistSubmissions && _submissionStore != null)
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

        // For data collection journeys, use the success redirect URL if defined
        if (!policy.RequiresAuthentication && !string.IsNullOrEmpty(policy.SuccessRedirectUrl))
        {
            redirectUri = policy.SuccessRedirectUrl;
        }

        // Build output claims from policy configuration
        var outputClaims = BuildOutputClaims(state, policy);

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

            // Extract submission metadata from journey data
            var metadata = new SubmissionMetadata();
            if (state.Data != null)
            {
                if (state.Data.TryGetValue("ip_address", out var ip))
                    metadata.IpAddress = ip?.ToString();
                if (state.Data.TryGetValue("user_agent", out var ua))
                    metadata.UserAgent = ua?.ToString();
                if (state.Data.TryGetValue("referrer", out var referrer))
                    metadata.Referrer = referrer?.ToString();
                if (state.Data.TryGetValue("country", out var country))
                    metadata.Country = country?.ToString();
                if (state.Data.TryGetValue("locale", out var locale))
                    metadata.Locale = locale?.ToString();
            }

            // Filter out system fields from the data, keep only user-submitted data
            var submittedData = new Dictionary<string, object>();
            var systemFields = new HashSet<string>
            {
                "loginHint", "acrValues", "scopes", "client_id", "enable_local_login",
                "idp_restrictions", "domain_hint", "requires_authentication", "persist_submissions",
                "success_message", "success_redirect_url", "redirectUri", "state", "nonce",
                "completedSteps", "ip_address", "user_agent", "referrer", "country", "locale",
                "lastError", "lastErrorDescription", "failedStepId"
            };

            if (state.Data != null)
            {
                foreach (var kvp in state.Data)
                {
                    if (!systemFields.Contains(kvp.Key))
                    {
                        submittedData[kvp.Key] = kvp.Value;
                    }
                }
            }

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving submission for journey {JourneyId}", journeyId);
            // Don't fail the journey if submission persistence fails
        }
    }

    private IReadOnlyDictionary<string, object>? BuildOutputClaims(JourneyState state, JourneyPolicy policy)
    {
        if (policy.OutputClaims == null || policy.OutputClaims.Count == 0)
        {
            // Return all journey data as claims if no explicit mapping defined
            return state.Data?.ToDictionary(k => k.Key, v => v.Value);
        }

        var claims = new Dictionary<string, object>();

        foreach (var mapping in policy.OutputClaims)
        {
            object? value = mapping.SourceType switch
            {
                "journeyData" when state.Data?.TryGetValue(mapping.SourcePath, out var jv) == true => jv,
                "claim" when state.ClaimsBag.TryGet(mapping.SourcePath, out var cv) => cv,
                "literal" => mapping.SourcePath,
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
}
