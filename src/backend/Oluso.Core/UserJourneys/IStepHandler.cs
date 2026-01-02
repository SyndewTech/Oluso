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
/// Marker interface for custom step handlers that can be registered by external modules
/// </summary>
public interface ICustomStepHandler : IStepHandler
{
}

/// <summary>
/// Interface for custom step handlers that provide their own metadata for Admin UI discovery.
/// Implement this alongside ICustomStepHandler to have your step appear in the journey builder.
/// </summary>
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
    /// Category for grouping in the UI (e.g., "Billing", "Authentication")
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Optional module name (e.g., "Oluso.Billing")
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

        if (value is T typed) return typed;

        try
        {
            if (value is System.Text.Json.JsonElement element)
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(element.GetRawText());
            }
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

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
        if (value is T typed) return typed;
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
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
    public string? Module { get; init; }
    public Type? HandlerType { get; init; }

    /// <summary>
    /// JSON Schema for the step's configuration options.
    /// This schema describes what settings are available when configuring this step type.
    /// </summary>
    public Dictionary<string, object>? ConfigurationSchema { get; init; }
}

/// <summary>
/// Builder for configuring step type metadata
/// </summary>
public class StepTypeBuilder
{
    internal string? Description { get; private set; }
    internal string? Category { get; private set; }
    internal string? Module { get; private set; }
    internal string? RequiredFeature { get; private set; }
    internal Dictionary<string, object>? ConfigurationSchema { get; private set; }

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

    public static StepConfigurationValidationResult Valid() =>
        new() { IsValid = true };

    public static StepConfigurationValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors };

    public static StepConfigurationValidationResult Invalid(IEnumerable<string> errors) =>
        new() { IsValid = false, Errors = errors.ToList() };
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
