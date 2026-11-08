namespace Oluso.Core.UserJourneys;

/// <summary>
/// Store for journey policies
/// </summary>
public interface IJourneyPolicyStore
{
    /// <summary>
    /// Gets a policy by ID (alias for GetByIdAsync). IDs are what the platform stores;
    /// clients name a policy by <see cref="JourneyPolicy.Slug"/> (<see cref="GetBySlugAsync"/>).
    /// </summary>
    Task<JourneyPolicy?> GetAsync(string policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a policy by ID
    /// </summary>
    Task<JourneyPolicy?> GetByIdAsync(string policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the policy a client names by slug (<c>?policy=</c>, share links, the public
    /// API): the current tenant's, else a global one. Slugs are unique per tenant, so
    /// another tenant's policy with the same slug is never returned.
    /// </summary>
    Task<JourneyPolicy?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this slug is unavailable to a policy in the current tenant: the tenant or
    /// a global policy already uses it (other than <paramref name="exceptPolicyId"/>, the
    /// policy being renamed). Global policies are seen by every tenant, so a global slug
    /// must be free in all of them.
    /// </summary>
    Task<bool> IsSlugTakenAsync(string slug, string? exceptPolicyId = null, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// The handle clients use to name this policy (<c>?policy=</c>, share links, the public
    /// API). Unique within a tenant and against global policies; for policies that predate
    /// slugs it equals <see cref="Id"/>.
    /// </summary>
    public required string Slug { get; set; }

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
    /// The module that owns this journey (its module key), or null for a platform journey. A module journey's
    /// answers go only to that module (<see cref="IJourneyResultSink"/>); the platform keeps no copy of them.
    /// </summary>
    public string? OwnerModule { get; set; }

    /// <summary>Whether other modules may use this module journey. Ignored for a platform journey.</summary>
    public JourneySharing Sharing { get; set; } = JourneySharing.Private;

    /// <summary>True when a module owns this journey, so its answers never reach the platform.</summary>
    public bool IsModuleOwned => !string.IsNullOrEmpty(OwnerModule);

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

    /// <summary>
    /// Comma-separated list of origins allowed to embed this journey
    /// </summary>
    public string? AllowedOrigins { get; set; }

    /// <summary>
    /// Public API key required for accessing this journey
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Whether API key is required for public access
    /// </summary>
    public bool RequireApiKey { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}


/// <summary>Who may use a module-owned journey.</summary>
public enum JourneySharing
{
    /// <summary>Only the owning module.</summary>
    Private = 0,

    /// <summary>The owning module and any other module; each run's answers go to the module that started it.</summary>
    Shared = 1,
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
    /// Optional shell width for this step (narrow / medium / wide / full).
    /// Overrides the policy-level <see cref="JourneyUiConfiguration.Layout"/>
    /// for this step only. Null = use the policy default.
    /// </summary>
    public string? Layout { get; set; }

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
    /// Failed attempts allowed at this step (wrong codes or passwords) before the journey fails.
    /// 0 uses the platform default (<see cref="DefaultMaxFailedAttempts"/>).
    /// </summary>
    public int MaxRetries { get; set; } = 0;

    public const int DefaultMaxFailedAttempts = 5;

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



