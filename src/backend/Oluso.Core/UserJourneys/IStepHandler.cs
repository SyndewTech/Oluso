using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Locale;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Handles execution of a journey step
/// </summary>
public interface IStepHandler
{
    /// <summary>
    /// The step type this handler processes
    /// </summary>
    string StepType { get; }

    /// <summary>
    /// Executes the step
    /// </summary>
    Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates step configuration. Override to add custom validation.
    /// </summary>
    Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }
}

/// <summary>
/// Marks a step that signs the user in (a first factor: password, passkey, external IdP,
/// directory, one-time link). When a journey runs for a user already signed in by an
/// existing session, the orchestrator completes these steps from the session instead of
/// running them. Step-up steps such as MFA must not carry this marker.
/// </summary>
public interface IPrimaryAuthenticationStep : IStepHandler
{
    /// <summary>
    /// Whether this run of the step signs someone in. A step configured only to verify an address returns false, so
    /// a signed-in session never stands in for the check.
    /// </summary>
    bool SignsIn(StepExecutionContext context) => true;
}

/// <summary>
/// Marker interface for custom step handlers that can be registered by external modules
/// </summary>
public interface ICustomStepHandler : IStepHandler
{
}

/// <summary>
/// Interface for custom step handlers that provide their own metadata for Admin UI discovery.
/// Implement this alongside ICustomStepHandler to have your step appear in the journey builder.
/// </summary>
/// <summary>
/// A step that describes its own settings. The registry uses this schema for the step's
/// type, so the builder offers exactly the settings the step reads.
/// </summary>
public interface IStepSettingsSchema
{
    /// <summary>The step's settings, keyed by name (same shape as a registry schema).</summary>
    Dictionary<string, object> GetConfigurationSchema();
}

/// <summary>
/// A step that can check its own settings from the journey builder ("Test"): reach the
/// services it calls, verify credentials, confirm the providers it names exist. Runs with
/// the step's saved settings (secrets included) and no user, so it must not change data.
/// </summary>
public interface IStepConfigurationTester
{
    Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default);
}

public sealed class StepConfigurationTestResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();

    public static StepConfigurationTestResult Passed(string message, params string[] details) =>
        new() { Success = true, Message = message, Details = details };

    public static StepConfigurationTestResult Failed(string message, params string[] details) =>
        new() { Success = false, Message = message, Details = details };

    public static StepConfigurationTestResult Of(bool success, string message, IEnumerable<string> details) =>
        new() { Success = success, Message = message, Details = details.ToList() };
}

public interface ICustomStepHandlerMetadata
{
    /// <summary>
    /// Display name for the step type (e.g., "Subscription Selection")
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of what the step does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Category for grouping in the UI (e.g., "Integrations", "Authentication")
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Icon shown for the step in the journey builder (a ui-icon name, e.g. <c>"key"</c>).
    /// Null uses the category's icon.
    /// </summary>
    string? Icon => null;

    /// <summary>
    /// FeatureKey of the owning <see cref="Oluso.Core.Licensing.IModuleDescriptor"/>
    /// (e.g. <c>"&lt;module&gt;"</c>). Null when the step is part of core. The admin
    /// API's step-types endpoint feature-gates and permission-gates by this
    /// value the same way the permissions / roles / webhook-events endpoints
    /// do, so a step is hidden in the picker when its module isn't licensed
    /// or the user lacks the module's admin permission.
    /// Reference the descriptor's <c>FeatureKeyConst</c> so a rename catches
    /// at compile time.
    /// </summary>
    string? Module => null;

    /// <summary>
    /// JSON Schema for the step's configuration options.
    /// Return null if the step has no configurable options.
    /// </summary>
    Dictionary<string, object>? GetConfigurationSchema() => null;
}

/// <summary>
/// Context provided to step handlers during execution.
/// </summary>
/// <remarks>
/// <para><strong>Authentication Step Convention:</strong></para>
/// <para>
/// Step handlers that perform user authentication (login, signup, external IdP, etc.)
/// MUST set the following data when authentication succeeds:
/// </para>
/// <list type="bullet">
///   <item>
///     <term><c>authenticated_at</c></term>
///     <description>
///       <c>DateTime.UtcNow</c> - Timestamp when authentication occurred. This flag is used
///       by the journey page to determine if a cookie session should be issued on completion.
///       Without this flag, no session is issued even if UserId is set.
///     </description>
///   </item>
///   <item>
///     <term><c>auth_method</c></term>
///     <description>
///       Authentication method used (e.g., "pwd", "fido2", "saml", "ldap", provider name).
///       Maps to the "amr" claim in tokens.
///     </description>
///   </item>
///   <item>
///     <term><c>idp</c></term>
///     <description>
///       Identity provider identifier for external logins (e.g., "google", "saml:okta").
///     </description>
///   </item>
/// </list>
/// <para>
/// Example usage in a custom authentication step:
/// </para>
/// <code>
/// // After successful authentication
/// context.UserId = user.Id;
/// context.SetData("authenticated_at", DateTime.UtcNow);
/// context.SetData("auth_method", "custom");
/// </code>
/// <para>
/// This convention allows journey policies of type SignIn to be used for non-login
/// activities (e.g., collecting claims, showing terms) without accidentally issuing sessions.
/// Only journeys where an actual authentication step succeeds will result in session issuance.
/// </para>
/// </remarks>
public class StepExecutionContext
{
    public required string JourneyId { get; init; }
    public required string StepId { get; init; }
    public required JourneyStepConfiguration Configuration { get; init; }
    public string? UserId { get; set; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }

    /// <summary>The journey policy the run follows (the sign-up or sign-in policy), as opposed to <see cref="JourneyId"/>, the run itself.</summary>
    public string? PolicyId { get; init; }
    public IDictionary<string, object> JourneyData { get; init; } = new Dictionary<string, object>();
    public JourneyStepInput? Input { get; init; }
    public IServiceProvider ServiceProvider { get; init; } = null!;

    /// <summary>
    /// Timeout for this step execution in seconds
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Number of retry attempts remaining for this step
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum retry attempts allowed
    /// </summary>
    public int MaxRetries { get; init; }

    /// <summary>
    /// Plugin name for CustomPlugin steps
    /// </summary>
    public string? PluginName { get; init; }

    /// <summary>
    /// Claims required to execute this step
    /// </summary>
    public IList<string>? RequiredClaims { get; init; }

    /// <summary>
    /// Claims this step should output
    /// </summary>
    public IList<string>? ExpectedOutputClaims { get; init; }

    /// <summary>
    /// Custom error message template for this step
    /// </summary>
    public string? ErrorMessageTemplate { get; init; }

    /// <summary>
    /// Whether this step has already been completed in the current session
    /// </summary>
    public bool WasCompletedBefore { get; init; }

    /// <summary>
    /// User input from form submission (convenience accessor for Input.Values)
    /// </summary>
    public IDictionary<string, object> UserInput => Input?.Values ?? new Dictionary<string, object>();

    /// <summary>
    /// Get typed configuration value from step settings
    /// </summary>
    public T? GetConfig<T>(string key, T? defaultValue = default)
    {
        if (Configuration.Settings == null) return defaultValue;
        if (!Configuration.Settings.TryGetValue(key, out var value)) return defaultValue;
        return StepValueReader.Read(value, defaultValue, problem => ReportSettingProblem(key, problem));
    }

    private void ReportSettingProblem(string key, string problem) =>
        ServiceProvider?.GetService<ILoggerFactory>()?
            .CreateLogger("Oluso.UserJourneys.StepSettings")
            .LogWarning("Step {StepId} ({StepType}) setting '{Setting}': {Problem}", StepId, Configuration.Type, key, problem);

    /// <summary>
    /// Get a string value from user input
    /// </summary>
    public string? GetInput(string key)
    {
        if (UserInput.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return null;
    }

    /// <summary>
    /// Check if user has submitted input for this step
    /// </summary>
    public bool HasInput => Input?.Values != null && Input.Values.Count > 0;

    /// <summary>Files posted with this step's form (empty when none were).</summary>
    public IReadOnlyList<JourneyInputFile> InputFiles => Input?.Files ?? Array.Empty<JourneyInputFile>();

    /// <summary>Non-empty files posted under one form field.</summary>
    public IReadOnlyList<JourneyInputFile> GetInputFiles(string fieldName) =>
        InputFiles.Where(f => f.Length > 0 && string.Equals(f.FieldName, fieldName, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Get or set a value in the journey data bag
    /// </summary>
    public void SetData(string key, object value) => JourneyData[key] = value;

    /// <summary>
    /// Get a value from the journey data bag
    /// </summary>
    public T? GetData<T>(string key, T? defaultValue = default)
    {
        if (!JourneyData.TryGetValue(key, out var value)) return defaultValue;
        return StepValueReader.Read(value, defaultValue);
    }

    /// <summary>
    /// The resolved locale for this journey, derived from the OIDC <c>ui_locales</c>
    /// parameter, the browser's <c>Accept-Language</c> header, or the tenant default.
    /// Returns <c>"en"</c> when no locale information is available.
    /// </summary>
    public string ResolvedLocale
    {
        get
        {
            var uiLocales = GetDataString("ui_locales");
            if (!string.IsNullOrEmpty(uiLocales))
                return uiLocales.Split(' ', ',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "en";

            var locale = GetDataString("locale");
            if (!string.IsNullOrEmpty(locale))
                return locale;

            return "en";
        }
    }

    public string? GetDataString(string key)
    {
        if (!JourneyData.TryGetValue(key, out var value)) return null;
        if (value is string s) return s;
        if (value is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.String)
            return el.GetString();
        return value?.ToString();
    }

    /// <summary>
    /// Gets a config value that may be a <see cref="LocalizedString"/> (either a
    /// plain string or a locale-keyed object). Resolves to the best match for the
    /// current journey locale.
    /// </summary>
    public string? GetLocalizedConfig(string key, string? defaultValue = null)
    {
        if (Configuration.Settings == null) return defaultValue;
        if (!Configuration.Settings.TryGetValue(key, out var value)) return defaultValue;

        if (value is string s) return s;

        if (value is System.Text.Json.JsonElement element)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                return element.GetString() ?? defaultValue;

            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var ls = System.Text.Json.JsonSerializer.Deserialize<LocalizedString>(element.GetRawText());
                return ls?.Resolve(ResolvedLocale) ?? defaultValue;
            }
        }

        if (value is LocalizedString localized)
            return localized.Resolve(ResolvedLocale);

        return value?.ToString() ?? defaultValue;
    }

    /// <summary>
    /// Marks the user as authenticated. Call this after successful authentication
    /// to ensure a session cookie is issued when the journey completes.
    /// </summary>
    /// <param name="userId">The authenticated user's ID</param>
    /// <param name="authMethod">
    /// Authentication method used (e.g., "pwd", "fido2", "saml", "ldap").
    /// This maps to the "amr" claim in tokens.
    /// </param>
    /// <param name="identityProvider">
    /// Optional identity provider identifier for external logins (e.g., "google", "saml:okta").
    /// </param>
    /// <example>
    /// <code>
    /// // After verifying user credentials
    /// context.SetAuthenticated(user.Id, "pwd");
    ///
    /// // For external IdP login
    /// context.SetAuthenticated(user.Id, "google", identityProvider: "google");
    /// </code>
    /// </example>
    public void SetAuthenticated(string userId, string authMethod, string? identityProvider = null)
    {
        UserId = userId;
        SetData("authenticated_at", DateTime.UtcNow);
        SetData("auth_method", authMethod);
        if (!string.IsNullOrEmpty(identityProvider))
        {
            SetData("idp", identityProvider);
        }
    }

    /// <summary>
    /// Checks if the user has been authenticated during this journey.
    /// </summary>
    /// <returns>True if authentication occurred (authenticated_at flag is set)</returns>
    public bool IsAuthenticated => JourneyData.ContainsKey("authenticated_at");

    /// <summary>
    /// Pre-completion validators that run before a step completes.
    /// Used to check for things like duplicate submissions before accepting data.
    /// </summary>
    public IList<IPreCompletionValidator> PreCompletionValidators { get; init; } = new List<IPreCompletionValidator>();

    /// <summary>
    /// Validates the output data before completing the step.
    /// Returns an error message if validation fails, or null if valid.
    /// </summary>
    /// <param name="outputData">The data that would be saved if step completes successfully</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Error message if validation fails, null if valid</returns>
    public async Task<string?> ValidateBeforeCompletionAsync(
        IDictionary<string, object>? outputData,
        CancellationToken cancellationToken = default)
    {
        foreach (var validator in PreCompletionValidators)
        {
            var error = await validator.ValidateAsync(this, outputData, cancellationToken);
            if (error != null)
            {
                return error;
            }
        }
        return null;
    }
}

/// <summary>
/// Validator that runs before a step completes to check for errors
/// </summary>
public interface IPreCompletionValidator
{
    /// <summary>
    /// Validates the step output before completion.
    /// </summary>
    /// <param name="context">The step execution context</param>
    /// <param name="outputData">The data that would be saved</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Error message if validation fails, null if valid</returns>
    Task<string?> ValidateAsync(
        StepExecutionContext context,
        IDictionary<string, object>? outputData,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for a journey step from the policy
/// </summary>
public class JourneyStepConfiguration
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? DisplayName { get; init; }
    public bool Optional { get; init; }
    public IDictionary<string, object>? Settings { get; init; }
    public IDictionary<string, string>? Branches { get; init; }

    /// <summary>
    /// Next step ID on success (null = next in order)
    /// </summary>
    public string? OnSuccess { get; init; }

    /// <summary>
    /// Next step ID on failure (null = end journey with error)
    /// </summary>
    public string? OnFailure { get; init; }

    /// <summary>
    /// Plugin name for CustomPlugin steps
    /// </summary>
    public string? PluginName { get; init; }

    /// <summary>
    /// Timeout in seconds
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Maximum retries allowed
    /// </summary>
    public int MaxRetries { get; init; }

    /// <summary>
    /// Skip if already completed in session
    /// </summary>
    public bool SkipIfCompleted { get; init; }

    /// <summary>
    /// Custom error message template
    /// </summary>
    public string? ErrorMessageTemplate { get; init; }

    /// <summary>
    /// Required claims to execute
    /// </summary>
    public IList<string>? RequiredClaims { get; init; }

    /// <summary>
    /// Expected output claims
    /// </summary>
    public IList<string>? OutputClaims { get; init; }

    /// <summary>
    /// Conditions for step execution
    /// </summary>
    public IList<StepCondition>? Conditions { get; init; }
}

/// <summary>
/// Result from executing a step handler
/// </summary>
public class StepHandlerResult
{
    public StepOutcome Outcome { get; init; }
    public string? NextStepId { get; init; }
    public string? BranchId { get; init; }
    public JourneyStepResult? StepResult { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public IDictionary<string, object>? OutputData { get; init; }
    public string? RedirectUrl { get; init; }

    /// <summary>
    /// The step is shown again because its input failed (a wrong code or password). These count
    /// toward the step's attempt limit (<see cref="JourneyPolicyStep.MaxRetries"/>), after which
    /// the journey fails.
    /// </summary>
    public bool FailedAttempt { get; init; }

    /// <summary>
    /// Continue to the next step in sequence
    /// </summary>
    public static StepHandlerResult Continue(string? nextStepId = null) =>
        new() { Outcome = StepOutcome.Continue, NextStepId = nextStepId };

    /// <summary>
    /// Continue with output claims
    /// </summary>
    public static StepHandlerResult Success(IDictionary<string, object>? outputData = null) =>
        new() { Outcome = StepOutcome.Continue, OutputData = outputData };

    /// <summary>
    /// Show UI to collect user input
    /// </summary>
    public static StepHandlerResult RequireInput(JourneyStepResult stepResult) =>
        new() { Outcome = StepOutcome.RequireInput, StepResult = stepResult };

    /// <summary>
    /// Show the step again after input that failed (a wrong code or password). Counts toward
    /// the step's attempt limit.
    /// </summary>
    public static StepHandlerResult FailedAttemptUi(string viewName, object? viewModel = null) =>
        ShowUi(viewName, viewModel).AsFailedAttempt();

    /// <summary>This result, counted as a failed attempt at the step.</summary>
    public StepHandlerResult AsFailedAttempt() => new()
    {
        Outcome = Outcome,
        NextStepId = NextStepId,
        BranchId = BranchId,
        StepResult = StepResult,
        Error = Error,
        ErrorDescription = ErrorDescription,
        OutputData = OutputData,
        RedirectUrl = RedirectUrl,
        FailedAttempt = true
    };

    /// <summary>
    /// Show a view with the specified view model
    /// </summary>
    public static StepHandlerResult ShowUi(string viewName, object? viewModel = null) =>
        new()
        {
            Outcome = StepOutcome.RequireInput,
            StepResult = new JourneyStepResult
            {
                StepId = "",
                StepType = "",
                ViewName = viewName,
                ViewModel = viewModel
            }
        };

    /// <summary>
    /// Show a dynamic form (for runtime plugins without bundled views)
    /// </summary>
    public static StepHandlerResult ShowDynamicForm(DynamicFormSchema formSchema) =>
        new()
        {
            Outcome = StepOutcome.RequireInput,
            StepResult = new JourneyStepResult
            {
                StepId = "",
                StepType = "",
                ViewName = "DynamicForm",
                ViewModel = formSchema
            }
        };

    /// <summary>
    /// Redirect to an external URL
    /// </summary>
    public static StepHandlerResult Redirect(string url) =>
        new() { Outcome = StepOutcome.Redirect, RedirectUrl = url };

    /// <summary>
    /// Branch to a different step
    /// </summary>
    public static StepHandlerResult Branch(string branchId) =>
        new() { Outcome = StepOutcome.Branch, BranchId = branchId };

    /// <summary>
    /// Branch to a different step with output claims
    /// </summary>
    public static StepHandlerResult Branch(string branchId, IDictionary<string, object>? outputData) =>
        new() { Outcome = StepOutcome.Branch, BranchId = branchId, OutputData = outputData };

    /// <summary>
    /// Branch if the step has a mapping for <paramref name="branchId"/>,
    /// otherwise fall back to <see cref="Success"/>. Tries each candidate
    /// in order so callers can provide specific-then-generic fallbacks
    /// (e.g. "auth_google", "auth_external").
    /// </summary>
    public static StepHandlerResult SuccessOrBranch(
        StepExecutionContext context,
        IDictionary<string, object>? outputData,
        params string[] branchIds)
    {
        var branches = context.Configuration.Branches;
        if (branches != null)
        {
            foreach (var id in branchIds)
            {
                if (branches.ContainsKey(id))
                    return Branch(id, outputData);
            }
        }
        return Success(outputData);
    }

    /// <summary>
    /// Branch if the step has a mapping for <paramref name="branchId"/>,
    /// otherwise fall back to <see cref="Skip"/>.
    /// </summary>
    public static StepHandlerResult SkipOrBranch(
        StepExecutionContext context,
        params string[] branchIds)
    {
        var branches = context.Configuration.Branches;
        if (branches != null)
        {
            foreach (var id in branchIds)
            {
                if (branches.ContainsKey(id))
                    return Branch(id);
            }
        }
        return Skip();
    }

    /// <summary>
    /// Skip this step and continue to the next
    /// </summary>
    public static StepHandlerResult Skip() =>
        new() { Outcome = StepOutcome.Skip };

    /// <summary>
    /// Wait for user input (pause journey execution)
    /// </summary>
    public static StepHandlerResult WaitForInput() =>
        new() { Outcome = StepOutcome.RequireInput };

    /// <summary>
    /// Journey completed successfully
    /// </summary>
    public static StepHandlerResult Complete() =>
        new() { Outcome = StepOutcome.Complete };

    /// <summary>
    /// Step failed with an error
    /// </summary>
    public static StepHandlerResult Fail(string error, string? description = null) =>
        new() { Outcome = StepOutcome.Failed, Error = error, ErrorDescription = description };

    /// <summary>
    /// Alias for Fail with error code and message
    /// </summary>
    public static StepHandlerResult Failure(string errorCode, string errorMessage) =>
        Fail(errorCode, errorMessage);
}

/// <summary>
/// Outcome of step handler execution
/// </summary>
public enum StepOutcome
{
    /// <summary>Continue to the next step in sequence</summary>
    Continue,

    /// <summary>Wait for user input before continuing</summary>
    RequireInput,

    /// <summary>Branch to a different step</summary>
    Branch,

    /// <summary>Journey completed successfully</summary>
    Complete,

    /// <summary>Step failed, journey should be aborted</summary>
    Failed,

    /// <summary>Skip this step and continue to the next</summary>
    Skip,

    /// <summary>Redirect to an external URL</summary>
    Redirect
}

/// <summary>
/// Registry for step handler types
/// </summary>
public interface IStepHandlerRegistry
{
    /// <summary>
    /// Gets a step handler for the given step type
    /// </summary>
    IStepHandler? GetHandler(string stepType);

    /// <summary>
    /// Gets all registered step types
    /// </summary>
    IEnumerable<StepTypeInfo> GetRegisteredTypes();
}

/// <summary>
/// Information about a registered step type
/// </summary>
public class StepTypeInfo
{
    public required string Type { get; init; }

    /// <summary>
    /// Display name for the step type (e.g., "Subscription Selection").
    /// Falls back to Type if not set.
    /// </summary>
    public string? DisplayName { get; init; }

    public string? Description { get; init; }
    public string? Category { get; init; }

    /// <summary>Icon for the journey builder (a ui-icon name). Null uses the category's icon.</summary>
    public string? Icon { get; init; }

    public string? Module { get; init; }
    public Type? HandlerType { get; init; }

    /// <summary>
    /// JSON Schema for the step's configuration options.
    /// This schema describes what settings are available when configuring this step type.
    /// </summary>
    public Dictionary<string, object>? ConfigurationSchema { get; init; }

    /// <summary>
    /// Branch IDs this step type can emit. Each entry describes a named
    /// outcome the admin can wire to a target step in the journey builder.
    /// </summary>
    public IReadOnlyList<StepBranchInfo>? AvailableBranches { get; init; }
}

/// <summary>
/// Describes a branch ID a step handler can emit.
/// </summary>
public class StepBranchInfo
{
    public required string Id { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Builder for configuring step type metadata
/// </summary>
public class StepTypeBuilder
{
    internal string? DisplayName { get; private set; }
    internal string? Description { get; private set; }
    internal string? Category { get; private set; }
    internal string? Icon { get; private set; }
    internal string? Module { get; private set; }
    internal string? RequiredFeature { get; private set; }
    internal Dictionary<string, object>? ConfigurationSchema { get; private set; }
    internal List<StepBranchInfo>? AvailableBranches { get; private set; }

    public StepTypeBuilder WithDisplayName(string displayName)
    {
        DisplayName = displayName;
        return this;
    }

    public StepTypeBuilder WithDescription(string description)
    {
        Description = description;
        return this;
    }

    public StepTypeBuilder InCategory(string category)
    {
        Category = category;
        return this;
    }

    /// <summary>Icon for the journey builder (a ui-icon name, e.g. <c>"key"</c>).</summary>
    public StepTypeBuilder WithIcon(string icon)
    {
        Icon = icon;
        return this;
    }

    public StepTypeBuilder FromModule(string module)
    {
        Module = module;
        return this;
    }

    public StepTypeBuilder RequiresFeature(string feature)
    {
        RequiredFeature = feature;
        return this;
    }

    public StepTypeBuilder WithBranch(string id, string displayName, string? description = null)
    {
        AvailableBranches ??= new List<StepBranchInfo>();
        AvailableBranches.Add(new StepBranchInfo { Id = id, DisplayName = displayName, Description = description });
        return this;
    }

    /// <summary>
    /// Sets the JSON schema for the step's configuration options.
    /// </summary>
    /// <param name="schema">JSON Schema as a dictionary describing the configuration properties</param>
    public StepTypeBuilder WithConfigurationSchema(Dictionary<string, object> schema)
    {
        ConfigurationSchema = schema;
        return this;
    }

    /// <summary>
    /// Builds a simple schema from property definitions.
    /// Use this for common cases where you just need to define properties.
    /// </summary>
    public StepTypeBuilder WithSchema(Action<SchemaBuilder> configure)
    {
        var builder = new SchemaBuilder();
        configure(builder);
        ConfigurationSchema = builder.Build();
        return this;
    }
}

/// <summary>
/// Fluent builder for creating JSON schemas for step configuration
/// </summary>
public class SchemaBuilder
{
    private readonly Dictionary<string, object> _properties = new();
    private readonly List<string> _required = new();
    private string? _title;
    private string? _description;

    public SchemaBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    public SchemaBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public SchemaBuilder StringProperty(string name, string title, string? description = null, bool required = false, string? defaultValue = null)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "string",
            ["title"] = title
        };
        if (description != null) prop["description"] = description;
        if (defaultValue != null) prop["default"] = defaultValue;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public SchemaBuilder BooleanProperty(string name, string title, string? description = null, bool required = false, bool? defaultValue = null)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "boolean",
            ["title"] = title
        };
        if (description != null) prop["description"] = description;
        if (defaultValue != null) prop["default"] = defaultValue;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public SchemaBuilder NumberProperty(string name, string title, string? description = null, bool required = false, int? minimum = null, int? maximum = null, double? defaultValue = null)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "number",
            ["title"] = title
        };
        if (description != null) prop["description"] = description;
        if (minimum != null) prop["minimum"] = minimum;
        if (maximum != null) prop["maximum"] = maximum;
        if (defaultValue != null) prop["default"] = defaultValue;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public SchemaBuilder IntegerProperty(string name, string title, string? description = null, bool required = false, int? minimum = null, int? maximum = null, int? defaultValue = null)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "integer",
            ["title"] = title
        };
        if (description != null) prop["description"] = description;
        if (minimum != null) prop["minimum"] = minimum;
        if (maximum != null) prop["maximum"] = maximum;
        if (defaultValue != null) prop["default"] = defaultValue;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public SchemaBuilder EnumProperty(string name, string title, string[] options, string? description = null, bool required = false, string? defaultValue = null)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "string",
            ["title"] = title,
            ["enum"] = options
        };
        if (description != null) prop["description"] = description;
        if (defaultValue != null) prop["default"] = defaultValue;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public SchemaBuilder ArrayProperty(string name, string title, string itemType = "string", string? description = null, bool required = false)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "array",
            ["title"] = title,
            ["items"] = new Dictionary<string, object> { ["type"] = itemType }
        };
        if (description != null) prop["description"] = description;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add an array property with complex object items
    /// </summary>
    public SchemaBuilder ArrayObjectProperty(string name, string title, Dictionary<string, object> itemProperties, string? description = null, bool required = false)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "array",
            ["title"] = title,
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = itemProperties
            }
        };
        if (description != null) prop["description"] = description;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add an object property with nested properties
    /// </summary>
    public SchemaBuilder ObjectProperty(string name, string title, Dictionary<string, object> nestedProperties, string? description = null, bool required = false)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["title"] = title,
            ["properties"] = nestedProperties
        };
        if (description != null) prop["description"] = description;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add an object property that accepts any key-value pairs (dictionary pattern)
    /// </summary>
    public SchemaBuilder DictionaryProperty(string name, string title, string valueType = "string", string? description = null, bool required = false)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["title"] = title,
            ["additionalProperties"] = new Dictionary<string, object> { ["type"] = valueType }
        };
        if (description != null) prop["description"] = description;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add an object property that accepts any key-value pairs with complex value types
    /// </summary>
    public SchemaBuilder DictionaryProperty(string name, string title, Dictionary<string, object> valueProperties, string? description = null, bool required = false)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["title"] = title,
            ["additionalProperties"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = valueProperties
            }
        };
        if (description != null) prop["description"] = description;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add a raw property definition (for advanced/complex schemas)
    /// </summary>
    public SchemaBuilder RawProperty(string name, Dictionary<string, object> definition, bool required = false)
    {
        _properties[name] = definition;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add a string property with x-enumSource for dynamic enum population from an API endpoint.
    /// This allows AdminUI to fetch enum options dynamically from the specified endpoint.
    /// </summary>
    /// <param name="name">Property name</param>
    /// <param name="title">Display title</param>
    /// <param name="endpoint">API endpoint to fetch options from</param>
    /// <param name="valueField">Field in response objects to use as option value</param>
    /// <param name="labelField">Field in response objects to use as option label</param>
    /// <param name="description">Optional description</param>
    /// <param name="required">Whether the property is required</param>
    /// <param name="filters">Optional filters to pass to the API</param>
    public SchemaBuilder EnumSourceProperty(
        string name,
        string title,
        string endpoint,
        string valueField = "id",
        string labelField = "name",
        string? description = null,
        bool required = false,
        Dictionary<string, object>? filters = null)
    {
        var enumSource = new Dictionary<string, object>
        {
            ["source"] = "api",
            ["endpoint"] = endpoint,
            ["valueField"] = valueField,
            ["labelField"] = labelField
        };
        if (filters != null) enumSource["filters"] = filters;

        var prop = new Dictionary<string, object>
        {
            ["type"] = "string",
            ["title"] = title,
            ["x-enumSource"] = enumSource
        };
        if (description != null) prop["description"] = description;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add a string array property with x-enumSource for dynamic multi-select from an API endpoint.
    /// </summary>
    public SchemaBuilder EnumSourceArrayProperty(
        string name,
        string title,
        string endpoint,
        string valueField = "id",
        string labelField = "name",
        string? description = null,
        bool required = false,
        Dictionary<string, object>? filters = null)
    {
        var enumSource = new Dictionary<string, object>
        {
            ["source"] = "api",
            ["endpoint"] = endpoint,
            ["valueField"] = valueField,
            ["labelField"] = labelField
        };
        if (filters != null) enumSource["filters"] = filters;

        var prop = new Dictionary<string, object>
        {
            ["type"] = "array",
            ["title"] = title,
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["x-enumSource"] = enumSource
            },
            ["x-enumSource"] = enumSource
        };
        if (description != null) prop["description"] = description;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add a string property whose value is stored as a <c>LocalizedString</c>
    /// (locale-keyed map). The admin UI renders locale tabs for this field.
    /// At runtime, step handlers resolve the value via
    /// <see cref="StepExecutionContext.GetLocalizedConfig"/>.
    /// </summary>
    public SchemaBuilder LocalizableStringProperty(string name, string title, string? description = null, bool required = false, string? defaultValue = null)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "string",
            ["title"] = title,
            ["x-localizable"] = true
        };
        if (description != null) prop["description"] = description;
        if (defaultValue != null) prop["default"] = defaultValue;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    /// <summary>
    /// Add an enum property whose option labels are localizable. The underlying
    /// value stays constant across locales; only the display label for each
    /// option changes. The admin UI renders locale tabs for labels, not values.
    /// </summary>
    /// <param name="name">Property name</param>
    /// <param name="title">Display title</param>
    /// <param name="options">Static option values (keys)</param>
    /// <param name="labels">Default-locale display label for each option value</param>
    /// <param name="description">Optional description</param>
    /// <param name="required">Whether required</param>
    /// <param name="defaultValue">Default selected value</param>
    public SchemaBuilder LocalizableEnumProperty(
        string name,
        string title,
        string[] options,
        Dictionary<string, string>? labels = null,
        string? description = null,
        bool required = false,
        string? defaultValue = null)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = "string",
            ["title"] = title,
            ["enum"] = options,
            ["x-localizable"] = true,
            ["x-localizableLabels"] = true
        };
        if (labels != null) prop["x-enumLabels"] = labels;
        if (description != null) prop["description"] = description;
        if (defaultValue != null) prop["default"] = defaultValue;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public Dictionary<string, object> Build()
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = _properties
        };

        if (_title != null) schema["title"] = _title;
        if (_description != null) schema["description"] = _description;
        if (_required.Count > 0) schema["required"] = _required;

        return schema;
    }
}

/// <summary>
/// Result of step configuration validation
/// </summary>
public class StepConfigurationValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Non-fatal issues — the step is structurally valid but the admin
    /// likely wants to know (e.g. ExternalIdp with no providers configured
    /// will fall back to all enabled providers; that's a warning, not a
    /// save-blocking error).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static StepConfigurationValidationResult Valid() =>
        new() { IsValid = true };

    public static StepConfigurationValidationResult Valid(params string[] warnings) =>
        new() { IsValid = true, Warnings = warnings };

    public static StepConfigurationValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors };

    public static StepConfigurationValidationResult Invalid(IEnumerable<string> errors) =>
        new() { IsValid = false, Errors = errors.ToList() };

    /// <summary>
    /// Build a result with both errors and warnings. <c>IsValid</c> is true
    /// when there are no errors, regardless of warnings.
    /// </summary>
    public static StepConfigurationValidationResult Build(IEnumerable<string> errors, IEnumerable<string> warnings)
    {
        var errorList = errors.ToList();
        return new StepConfigurationValidationResult
        {
            IsValid = errorList.Count == 0,
            Errors = errorList,
            Warnings = warnings.ToList()
        };
    }
}

/// <summary>
/// Shared helpers for step config validators. The config dict arrives from
/// the controller as <c>IDictionary&lt;string, object&gt;</c> with values
/// deserialized by System.Text.Json — which means nested arrays/objects
/// land as <see cref="System.Text.Json.JsonElement"/>, not native CLR
/// collections. Validators must handle both shapes; these helpers
/// encapsulate that so each handler doesn't reimplement the same check.
/// </summary>
public static class StepConfigurationHelpers
{
    /// <summary>
    /// Returns true when <paramref name="value"/> represents a non-empty
    /// list. Treats strings as non-lists. Recognises:
    /// <list type="bullet">
    ///   <item><see cref="System.Text.Json.JsonElement"/> with
    ///     <c>ValueKind = Array</c></item>
    ///   <item>Any <see cref="System.Collections.IEnumerable"/> (List, Array,
    ///     etc.) excluding <see cref="string"/></item>
    /// </list>
    /// </summary>
    public static bool IsNonEmptyList(object? value)
    {
        if (value is null || value is string) return false;

        if (value is System.Text.Json.JsonElement element)
        {
            return element.ValueKind == System.Text.Json.JsonValueKind.Array
                && element.GetArrayLength() > 0;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (var _ in enumerable) return true;
        }

        return false;
    }

    /// <summary>
    /// Pulls a string from a config dict, normalising
    /// <see cref="System.Text.Json.JsonElement"/> string values to plain
    /// strings.
    /// </summary>
    public static string? GetString(IDictionary<string, object>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value)) return null;
        if (value is System.Text.Json.JsonElement element)
        {
            return element.ValueKind == System.Text.Json.JsonValueKind.String
                ? element.GetString()
                : null;
        }
        return value?.ToString();
    }
}

/// <summary>
/// Interface for modules to configure their step handlers.
/// Implement this to register custom step handlers during startup.
/// </summary>
public interface IConfigureStepHandlers
{
    /// <summary>
    /// Configure step handlers by registering them with the registry
    /// </summary>
    void Configure(IExtendedStepHandlerRegistry registry);
}

/// <summary>
/// Extended step handler registry with registration capabilities
/// </summary>
public interface IExtendedStepHandlerRegistry : IStepHandlerRegistry
{
    /// <summary>
    /// Registers a handler type for a step
    /// </summary>
    void Register(string stepType, Type handlerType);

    /// <summary>
    /// Registers a handler type with metadata
    /// </summary>
    void Register<THandler>(string stepType, Action<StepTypeBuilder>? configure = null) where THandler : IStepHandler;
}
