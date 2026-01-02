namespace Oluso.Core.UserJourneys;

/// <summary>
/// Store for journey policies
/// </summary>
public interface IJourneyPolicyStore
{
    /// <summary>
    /// Gets a policy by ID (alias for GetByIdAsync)
    /// </summary>
    Task<JourneyPolicy?> GetAsync(string policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a policy by ID
    /// </summary>
    Task<JourneyPolicy?> GetByIdAsync(string policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a policy by type (returns first matching enabled policy)
    /// </summary>
    Task<JourneyPolicy?> GetByTypeAsync(JourneyType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all policies for a tenant
    /// </summary>
    Task<IEnumerable<JourneyPolicy>> GetByTenantAsync(string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a matching policy based on context
    /// </summary>
    Task<JourneyPolicy?> FindMatchingAsync(JourneyPolicyMatchContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a policy
    /// </summary>
    Task SaveAsync(JourneyPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a policy
    /// </summary>
    Task DeleteAsync(string policyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A journey policy defining steps and conditions
/// </summary>
public class JourneyPolicy
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string? TenantId { get; init; }
    public JourneyType Type { get; set; } = JourneyType.SignIn;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public string? Description { get; set; }

    /// <summary>
    /// Version for optimistic concurrency and change tracking
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Steps in execution order
    /// </summary>
    public IList<JourneyPolicyStep> Steps { get; set; } = new List<JourneyPolicyStep>();

    /// <summary>
    /// Conditions for policy matching (when to use this policy)
    /// </summary>
    public IList<JourneyPolicyCondition>? Conditions { get; set; }

    /// <summary>
    /// Output claims to include in the token after journey completion
    /// </summary>
    public IList<ClaimMapping>? OutputClaims { get; set; }

    /// <summary>
    /// Session configuration for this journey
    /// </summary>
    public SessionConfiguration? Session { get; set; }

    /// <summary>
    /// UI customization for the journey
    /// </summary>
    public JourneyUiConfiguration? Ui { get; set; }

    /// <summary>
    /// Default timeout for steps in this policy (seconds)
    /// </summary>
    public int DefaultStepTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum time allowed for the entire journey (minutes)
    /// </summary>
    public int MaxJourneyDurationMinutes { get; set; } = 30;

    /// <summary>
    /// Tags for categorization and filtering
    /// </summary>
    public IList<string>? Tags { get; set; }

    /// <summary>
    /// Whether this journey requires authentication (false for waitlists, surveys, etc.)
    /// </summary>
    public bool RequiresAuthentication { get; set; } = true;

    /// <summary>
    /// Whether to persist collected data as submissions (for data collection journeys)
    /// </summary>
    public bool PersistSubmissions { get; set; } = false;

    /// <summary>
    /// Collection name for storing submissions (defaults to policy ID)
    /// </summary>
    public string? SubmissionCollection { get; set; }

    /// <summary>
    /// Maximum submissions allowed (0 = unlimited)
    /// </summary>
    public int MaxSubmissions { get; set; } = 0;

    /// <summary>
    /// Whether to allow duplicate submissions from same IP/email
    /// </summary>
    public bool AllowDuplicates { get; set; } = false;

    /// <summary>
    /// Fields to use for duplicate detection (e.g., "email", "phone")
    /// </summary>
    public IList<string>? DuplicateCheckFields { get; set; }

    /// <summary>
    /// Redirect URL after successful submission (for data collection)
    /// </summary>
    public string? SuccessRedirectUrl { get; set; }

    /// <summary>
    /// Success message to display after submission
    /// </summary>
    public string? SuccessMessage { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// UI customization for the journey
/// </summary>
public class JourneyUiConfiguration
{
    public string? Title { get; set; }
    public string? LogoUrl { get; set; }
    public string? BackgroundColor { get; set; }
    public string? PrimaryColor { get; set; }
    public string? CustomCss { get; set; }
    public IDictionary<string, string>? Localization { get; set; }

    /// <summary>
    /// Layout width for the journey container.
    /// Options: "narrow" (460px), "medium" (600px), "wide" (900px), "full" (100%)
    /// Default is "narrow".
    /// </summary>
    public string? Layout { get; set; }
}

/// <summary>
/// Common layout width options for journey UI.
/// </summary>
public static class JourneyLayouts
{
    /// <summary>Default narrow layout (460px) - good for login forms</summary>
    public const string Narrow = "narrow";

    /// <summary>Medium layout (600px) - good for forms with more fields</summary>
    public const string Medium = "medium";

    /// <summary>Wide layout (900px) - good for plan selection, multi-column content</summary>
    public const string Wide = "wide";

    /// <summary>Full width (100%) - for complex dashboards or wide tables</summary>
    public const string Full = "full";
}

/// <summary>
/// A step defined in a journey policy
/// </summary>
public class JourneyPolicyStep
{
    public required string Id { get; init; }

    /// <summary>
    /// Step type (e.g., "LocalLogin", "Mfa", "CustomPlugin")
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Human-readable name for the step
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Whether this step can be skipped
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Execution order (lower = earlier)
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Step-specific configuration
    /// </summary>
    public IDictionary<string, object>? Configuration { get; set; }

    /// <summary>
    /// Conditions that must be met for this step to execute
    /// </summary>
    public IList<StepCondition>? Conditions { get; set; }

    /// <summary>
    /// Next step ID on success (null = next in order)
    /// </summary>
    public string? OnSuccess { get; set; }

    /// <summary>
    /// Next step ID on failure (null = end journey with error)
    /// </summary>
    public string? OnFailure { get; set; }

    /// <summary>
    /// Named branches for conditional navigation (e.g., "mfa_required" -> "step_mfa")
    /// </summary>
    public IDictionary<string, string>? Branches { get; set; }

    /// <summary>
    /// Plugin name for CustomPlugin step type
    /// </summary>
    public string? PluginName { get; set; }

    /// <summary>
    /// Timeout in seconds for this step (overrides policy default)
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Number of retry attempts allowed for this step
    /// </summary>
    public int MaxRetries { get; set; } = 0;

    /// <summary>
    /// Whether to skip this step if it has already been completed in this session
    /// </summary>
    public bool SkipIfCompleted { get; set; } = false;

    /// <summary>
    /// Custom error message template for failures
    /// </summary>
    public string? ErrorMessageTemplate { get; set; }

    /// <summary>
    /// Claims required in context to execute this step
    /// </summary>
    public IList<string>? RequiredClaims { get; set; }

    /// <summary>
    /// Claims this step outputs to the journey context
    /// </summary>
    public IList<string>? OutputClaims { get; set; }
}

/// <summary>
/// Step types for user journeys
/// </summary>
public enum StepType
{
    LocalLogin,
    ExternalIdp,
    Mfa,
    PasswordlessEmail,
    PasswordlessSms,
    WebAuthn,
    Ldap,
    Saml,
    Consent,
    ClaimsCollection,
    TermsAcceptance,
    CaptchaVerification,
    Condition,
    Branch,
    Transform,
    ApiCall,
    Webhook,
    CreateUser,
    UpdateUser,
    LinkAccount,
    PasswordChange,
    PasswordReset,
    CustomPlugin,
    CustomPage
}

/// <summary>
/// A condition for policy matching
/// </summary>
public class JourneyPolicyCondition
{
    public required string Type { get; init; }
    public required string Operator { get; init; }
    public required string Value { get; init; }
}

/// <summary>
/// Context for matching journey policies
/// </summary>
public class JourneyPolicyMatchContext
{
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public JourneyType Type { get; init; }
    public IReadOnlyCollection<string>? Scopes { get; init; }
    public string? AcrValues { get; init; }
    public IDictionary<string, string>? AdditionalParameters { get; init; }
}

/// <summary>
/// Condition for step execution
/// </summary>
public class StepCondition
{
    /// <summary>
    /// Type of condition (e.g., "claim", "context", "expression")
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Field or path to evaluate
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// Comparison operator (eq, ne, gt, lt, contains, exists, regex)
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// Value to compare against
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Logical operator for combining with other conditions (and, or)
    /// </summary>
    public string LogicalOperator { get; set; } = "and";

    /// <summary>
    /// Whether to negate the condition result
    /// </summary>
    public bool Negate { get; set; } = false;
}

/// <summary>
/// Session configuration for user journeys
/// </summary>
public class SessionConfiguration
{
    /// <summary>
    /// Session lifetime in minutes
    /// </summary>
    public int SessionLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum session lifetime regardless of activity
    /// </summary>
    public int MaxSessionLifetimeMinutes { get; set; } = 1440;

    /// <summary>
    /// Whether to extend session on activity
    /// </summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>
    /// Whether session survives browser close
    /// </summary>
    public bool PersistentSession { get; set; } = false;

    /// <summary>
    /// Require re-authentication after this many minutes
    /// </summary>
    public int? RequireReauthAfterMinutes { get; set; }

    /// <summary>
    /// Single sign-on mode for this journey
    /// </summary>
    public SsoMode SsoMode { get; set; } = SsoMode.Enabled;

    /// <summary>
    /// Cookie name for this journey's session
    /// </summary>
    public string? CookieName { get; set; }

    /// <summary>
    /// Cookie same-site mode
    /// </summary>
    public string CookieSameSite { get; set; } = "Lax";
}

/// <summary>
/// Single sign-on mode
/// </summary>
public enum SsoMode
{
    /// <summary>SSO is enabled - user stays logged in across clients</summary>
    Enabled,
    /// <summary>SSO is disabled - each client requires separate login</summary>
    Disabled,
    /// <summary>SSO only within the same tenant</summary>
    TenantScoped,
    /// <summary>SSO only within the same client</summary>
    ClientScoped
}

/// <summary>
/// Claim mapping for output claims
/// </summary>
public class ClaimMapping
{
    public string SourceType { get; set; } = null!;
    public string SourcePath { get; set; } = null!;
    public string TargetClaimType { get; set; } = null!;
    public string? DefaultValue { get; set; }
}

