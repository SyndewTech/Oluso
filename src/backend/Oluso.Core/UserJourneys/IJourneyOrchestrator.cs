using Microsoft.AspNetCore.Http;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Orchestrates user journey execution through defined policies and steps
/// </summary>
public interface IJourneyOrchestrator
{
    /// <summary>
    /// Starts a new journey for the given context
    /// </summary>
    Task<JourneyResult> StartJourneyAsync(JourneyContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a new journey with a specific policy (for protocol integration)
    /// </summary>
    Task<JourneyState> StartJourneyAsync(JourneyPolicy policy, JourneyStartContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continues an existing journey from a specific step
    /// </summary>
    Task<JourneyResult> ContinueJourneyAsync(string journeyId, JourneyStepInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of a journey
    /// </summary>
    Task<JourneyState?> GetStateAsync(string journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an active journey
    /// </summary>
    Task CancelJourneyAsync(string journeyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for starting a journey from protocol layer
/// </summary>
public class JourneyStartContext
{
    /// <summary>
    /// HTTP context
    /// </summary>
    public HttpContext HttpContext { get; init; } = null!;

    /// <summary>
    /// Protocol name (oidc, saml, etc.)
    /// </summary>
    public string? ProtocolName { get; init; }

    /// <summary>
    /// Correlation ID for protocol state
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// URL to redirect after journey completion
    /// </summary>
    public string? CallbackUrl { get; init; }

    /// <summary>
    /// Login hint from protocol request
    /// </summary>
    public string? LoginHint { get; init; }

    /// <summary>
    /// ACR values from protocol request
    /// </summary>
    public string? AcrValues { get; init; }

    /// <summary>
    /// Requested scopes
    /// </summary>
    public IReadOnlyList<string>? RequestedScopes { get; init; }

    /// <summary>
    /// Additional properties
    /// </summary>
    public IDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Context for starting a new user journey
/// </summary>
public class JourneyContext
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public string? UserId { get; init; }
    public JourneyType Type { get; init; } = JourneyType.SignIn;
    public string? PolicyId { get; init; }
    public IReadOnlyCollection<string>? Scopes { get; init; }
    public string? AcrValues { get; init; }
    public string? RedirectUri { get; init; }
    public string? State { get; init; }
    public string? Nonce { get; init; }
    public IDictionary<string, string>? AdditionalParameters { get; init; }
}

/// <summary>
/// Types of user journeys
/// </summary>
public enum JourneyType
{
    // Authentication flows
    SignIn,
    SignUp,
    SignInSignUp,
    PasswordReset,
    ProfileEdit,
    LinkAccount,
    Consent,

    // Data collection (no auth required)
    Waitlist,
    ContactForm,
    Survey,
    Feedback,
    DataCollection,

    // Other
    Custom
}

/// <summary>
/// Input provided when continuing a journey step
/// </summary>
public class JourneyStepInput
{
    public required string StepId { get; init; }
    public IDictionary<string, object>? Values { get; init; }
    public string? Action { get; init; }
}

/// <summary>
/// Result of a journey operation
/// </summary>
public class JourneyResult
{
    public required string JourneyId { get; init; }
    public JourneyStatus Status { get; init; }
    public JourneyStepResult? CurrentStep { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    /// <summary>
    /// When completed successfully, contains the authenticated user info
    /// </summary>
    public JourneyCompletionResult? Completion { get; init; }
}

/// <summary>
/// Status of a journey
/// </summary>
public enum JourneyStatus
{
    InProgress,
    Completed,
    Failed,
    Cancelled,
    Expired
}

/// <summary>
/// Information about the current step requiring user interaction
/// </summary>
public class JourneyStepResult
{
    public required string StepId { get; init; }
    public required string StepType { get; init; }
    public string? DisplayName { get; init; }
    public string? ViewName { get; init; }
    public object? ViewModel { get; init; }
    public IDictionary<string, object>? ViewData { get; init; }
    public IReadOnlyCollection<StepAction>? AvailableActions { get; init; }
}

/// <summary>
/// An action available on a journey step
/// </summary>
public class StepAction
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsCancel { get; init; }
}

/// <summary>
/// Result when a journey completes successfully
/// </summary>
public class JourneyCompletionResult
{
    public required string UserId { get; init; }
    public string? SessionId { get; init; }
    public IReadOnlyDictionary<string, object>? Claims { get; init; }
    public string? RedirectUri { get; init; }
    public string? AuthorizationCode { get; init; }

    /// <summary>
    /// Success message to display (for data collection journeys)
    /// </summary>
    public string? SuccessMessage { get; init; }
}

/// <summary>
/// Current state of a journey
/// </summary>
public record JourneyState
{
    public required string Id { get; init; }
    public string JourneyId => Id;  // Alias for backward compatibility
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public string? UserId { get; init; }
    public required string PolicyId { get; init; }
    public required string CurrentStepId { get; init; }
    public JourneyStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public IDictionary<string, object>? Data { get; init; }

    /// <summary>
    /// Session ID for the authenticated user
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Authenticated user ID (set after successful authentication step)
    /// </summary>
    public string? AuthenticatedUserId { get; init; }

    /// <summary>
    /// Claims collected during the journey
    /// </summary>
    public JourneyClaimsBag ClaimsBag { get; init; } = new();

    /// <summary>
    /// Protocol correlation ID for callback
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Callback URL after journey completion
    /// </summary>
    public string? CallbackUrl { get; init; }
}

/// <summary>
/// Thread-safe claims bag for journey data
/// </summary>
public class JourneyClaimsBag
{
    private readonly Dictionary<string, string> _claims = new();

    public void Set(string key, string value)
    {
        _claims[key] = value;
    }

    public string? Get(string key)
    {
        return _claims.TryGetValue(key, out var value) ? value : null;
    }

    public bool TryGet(string key, out string? value)
    {
        return _claims.TryGetValue(key, out value);
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        return _claims;
    }

    public void Remove(string key)
    {
        _claims.Remove(key);
    }

    public void Clear()
    {
        _claims.Clear();
    }
}
