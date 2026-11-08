using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Plugins;

/// <summary>
/// Runtime for executing plugin hooks in response to domain events
/// </summary>
public interface IPluginHookRuntime
{
    /// <summary>
    /// Executes all registered hooks for a domain event
    /// </summary>
    /// <typeparam name="TContext">The domain-specific context type</typeparam>
    /// <typeparam name="TResponse">The domain-specific response type</typeparam>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="domain">The domain (e.g. journey, workflow, or a module domain)</param>
    /// <param name="entityType">The entity type within the domain</param>
    /// <param name="eventType">The event type (before_insert, after_update, etc.)</param>
    /// <param name="context">The execution context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The aggregated result from all hooks</returns>
    Task<HookExecutionResult<TResponse>> ExecuteHooksAsync<TContext, TResponse>(
        string tenantId,
        string domain,
        string entityType,
        string eventType,
        TContext context,
        CancellationToken cancellationToken = default)
        where TContext : BaseHookContext
        where TResponse : BaseHookResponse, new();

    /// <summary>
    /// Gets active hook bindings for a domain event
    /// </summary>
    Task<IReadOnlyList<PluginHookBinding>> GetActiveHooksAsync(
        string tenantId,
        string domain,
        string entityType,
        string eventType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from executing hooks
/// </summary>
public class HookExecutionResult<TResponse> where TResponse : BaseHookResponse, new()
{
    /// <summary>
    /// Whether all hooks executed successfully and allowed continuation
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Whether the operation was rejected by any hook
    /// </summary>
    public bool IsRejected { get; init; }

    /// <summary>
    /// The final response (from the last hook that modified it)
    /// </summary>
    public TResponse Response { get; init; } = new();

    /// <summary>
    /// Individual results from each hook
    /// </summary>
    public IReadOnlyList<SingleHookResult> HookResults { get; init; } = Array.Empty<SingleHookResult>();

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    public static HookExecutionResult<TResponse> Continue(TResponse response, IReadOnlyList<SingleHookResult>? results = null)
        => new()
        {
            Success = true,
            IsRejected = false,
            Response = response,
            HookResults = results ?? Array.Empty<SingleHookResult>()
        };

    public static HookExecutionResult<TResponse> Rejected(TResponse response, IReadOnlyList<SingleHookResult>? results = null)
        => new()
        {
            Success = true,
            IsRejected = true,
            Response = response,
            HookResults = results ?? Array.Empty<SingleHookResult>()
        };

    public static HookExecutionResult<TResponse> Error(string message)
        => new()
        {
            Success = false,
            IsRejected = false,
            ErrorMessage = message
        };
}

/// <summary>
/// Result from a single hook execution
/// </summary>
public class SingleHookResult
{
    public required string PluginId { get; init; }
    public required string HookBindingId { get; init; }
    public required string FunctionName { get; init; }
    public int DurationMs { get; init; }
    public PluginExecutionResult Result { get; init; }
    public string? Action { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Base class for hook execution contexts
/// </summary>
public abstract class BaseHookContext
{
    /// <summary>
    /// Tenant ID
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Current user information
    /// </summary>
    public HookUserContext? User { get; init; }

    /// <summary>
    /// Timestamp of the execution
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Unique execution ID for tracing
    /// </summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Domain-specific context as JSON object
    /// </summary>
    public abstract object GetDomainContext();
}

/// <summary>
/// User context passed to hooks
/// </summary>
public class HookUserContext
{
    public required string Id { get; init; }
    public string? Email { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Base class for hook responses
/// </summary>
public class BaseHookResponse
{
    /// <summary>
    /// Action to take: continue, reject, skip
    /// </summary>
    public string Action { get; set; } = "continue";

    /// <summary>
    /// Message to return (for reject)
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Field-specific errors (for validation rejections)
    /// </summary>
    public IReadOnlyList<HookFieldError>? Errors { get; set; }

    /// <summary>
    /// Side effects to execute (for after_* hooks)
    /// </summary>
    public IReadOnlyList<HookSideEffect>? SideEffects { get; set; }
}

/// <summary>
/// Field-specific error
/// </summary>
public class HookFieldError
{
    public required string Field { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Side effect requested by a hook
/// </summary>
public class HookSideEffect
{
    public required string Type { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = new();
}

// ============================================================================
// Entity-CRUD hook context and response
// ============================================================================

/// <summary>
/// Hook context for entity create/update/delete operations.
/// </summary>
public class EntityHookContext : BaseHookContext
{
    /// <summary>
    /// Entity type (Deal, Account, Contact, etc.)
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Event type (before_insert, after_update, etc.)
    /// </summary>
    public required string Event { get; init; }

    /// <summary>
    /// The record being operated on
    /// </summary>
    public required Dictionary<string, object> Record { get; init; }

    /// <summary>
    /// For updates: the previous state of the record
    /// </summary>
    public Dictionary<string, object>? OldRecord { get; init; }

    /// <summary>
    /// Pre-fetched related records (Account, Contact, etc.)
    /// </summary>
    public Dictionary<string, Dictionary<string, object>>? Related { get; init; }

    public override object GetDomainContext() => new
    {
        entity_type = EntityType,
        @event = Event,
        record = Record,
        old_record = OldRecord,
        related = Related
    };
}

/// <summary>
/// Hook response for entity create/update/delete operations.
/// </summary>
public class EntityHookResponse : BaseHookResponse
{
    /// <summary>
    /// Modified record (for before_* hooks)
    /// </summary>
    public Dictionary<string, object>? Record { get; set; }
}

// ============================================================================
// Journey-specific hook context and response
// ============================================================================

/// <summary>
/// Journey hook context
/// </summary>
public class JourneyHookContext : BaseHookContext
{
    public required string JourneyId { get; init; }
    public string? JourneyName { get; init; }
    public string? PolicyId { get; init; }
    public required string SessionId { get; init; }
    public required string Event { get; init; }

    public JourneyStepInfo? Step { get; init; }
    public Dictionary<string, object> SubmissionData { get; init; } = new();
    public Dictionary<string, object>? StepInput { get; init; }
    public JourneyAuthState? AuthState { get; init; }

    public override object GetDomainContext() => new
    {
        journey_id = JourneyId,
        journey_name = JourneyName,
        policy_id = PolicyId,
        session_id = SessionId,
        @event = Event,
        step = Step,
        submission_data = SubmissionData,
        step_input = StepInput,
        auth_state = AuthState
    };
}

public class JourneyStepInfo
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? Name { get; init; }
}

public class JourneyAuthState
{
    public bool IsAuthenticated { get; init; }
    public string? UserId { get; init; }
    public string? Email { get; init; }
    public Dictionary<string, object> Claims { get; init; } = new();
}

/// <summary>
/// Journey hook response
/// </summary>
public class JourneyHookResponse : BaseHookResponse
{
    /// <summary>
    /// Redirect URL (for redirect action)
    /// </summary>
    public string? RedirectUrl { get; set; }

    /// <summary>
    /// Modified submission data
    /// </summary>
    public Dictionary<string, object>? SubmissionData { get; set; }

    /// <summary>
    /// Modified step input
    /// </summary>
    public Dictionary<string, object>? StepInput { get; set; }

    /// <summary>
    /// Override next step ID (for branching)
    /// </summary>
    public string? NextStepId { get; set; }
}
