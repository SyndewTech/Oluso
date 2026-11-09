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
    /// The launch link the journey was opened with, when there was one. A module's journey starts only with a launch
    /// from a module that may use it; its context and module are written into the journey's data.
    /// </summary>
    public JourneyLaunchTicket? Launch { get; init; }

    /// <summary>
    /// Journey data key holding the user signed in by an existing session. Login steps
    /// complete for this user instead of asking for credentials again.
    /// </summary>
    public const string SessionUserIdDataKey = "session_user_id";

    /// <summary>
    /// Journey data key holding when the existing session signed in (Unix seconds), so a
    /// login step can ask again when that sign-in is older than it accepts.
    /// </summary>
    public const string SessionAuthTimeDataKey = "session_auth_time";

    /// <summary>
    /// User already signed in by an existing session, when a requested policy runs for them
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// When the existing session signed in, for a journey started for <see cref="UserId"/>
    /// </summary>
    public DateTimeOffset? SessionAuthTime { get; init; }

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
    PasswordChange,
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

    /// <summary>
    /// Files posted with the step's form. Only the request that posted them can read
    /// them; they are never written to journey state, so a step that keeps a file
    /// stores it (see <see cref="Oluso.Core.Storage.IJourneyFileUploadService"/>).
    /// </summary>
    public IReadOnlyList<JourneyInputFile>? Files { get; init; }
}

/// <summary>A file posted to a journey step, read through <see cref="OpenReadStream"/>.</summary>
public sealed class JourneyInputFile
{
    private readonly Func<Stream> _open;

    public JourneyInputFile(string fieldName, string fileName, string? contentType, long length, Func<Stream> open)
    {
        FieldName = fieldName;
        FileName = fileName;
        ContentType = contentType;
        Length = length;
        _open = open;
    }

    /// <summary>The form field the file was posted under.</summary>
    public string FieldName { get; }

    /// <summary>The name the browser sent; untrusted.</summary>
    public string FileName { get; }

    /// <summary>The type the browser claimed; untrusted (uploads are sniffed instead).</summary>
    public string? ContentType { get; }

    public long Length { get; }

    public Stream OpenReadStream() => _open();
}

/// <summary>
/// Told when a journey that started without a user learns who the user is (a sign-up or
/// sign-in step set it), so records made earlier in the same journey can be tied to them.
/// </summary>
public interface IJourneyUserAttachedHandler
{
    Task OnUserAttachedAsync(JourneyUserAttached attached, CancellationToken cancellationToken = default);
}

public sealed record JourneyUserAttached(string JourneyId, string TenantId, string UserId, IReadOnlyDictionary<string, object> JourneyData);

/// <summary>
/// A counter in journey data that waiting pages poll (through the journey status
/// endpoint) to learn that something outside the browser changed what the current step
/// would show, e.g. a provider's webhook deciding a pending check.
/// </summary>
public static class JourneyStatusRevision
{
    /// <summary>Journey data key; underscored so it never reaches submissions or claims.</summary>
    public const string DataKey = "_status_revision";

    public static long Read(IDictionary<string, object>? data)
    {
        if (data == null || !data.TryGetValue(DataKey, out var value) || value == null) return 0;
        return long.TryParse(value is System.Text.Json.JsonElement e ? e.ToString() : value.ToString(), out var n) ? n : 0;
    }

    /// <summary>Increments the counter on a journey that is still running; false when it's gone or finished.</summary>
    public static async Task<bool> BumpAsync(IJourneyStateStore store, string journeyId, CancellationToken cancellationToken = default)
    {
        var state = await store.GetAsync(journeyId, cancellationToken);
        if (state == null || state.Status != JourneyStatus.InProgress) return false;
        var data = state.Data != null
            ? new Dictionary<string, object>(state.Data)
            : new Dictionary<string, object>();
        data[DataKey] = Read(data) + 1;
        await store.SaveAsync(state with { Data = data }, cancellationToken);
        return true;
    }
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
