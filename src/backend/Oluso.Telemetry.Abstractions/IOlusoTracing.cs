using System.Diagnostics;

namespace Oluso.Telemetry;

/// <summary>
/// Interface for Oluso distributed tracing.
/// Provides activity/span management for request tracing.
/// </summary>
public interface IOlusoTracing
{
    /// <summary>
    /// Start a new activity/span for an operation
    /// </summary>
    IDisposable? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

    /// <summary>
    /// Start a token endpoint activity
    /// </summary>
    IDisposable? StartTokenActivity(string grantType, string clientId);

    /// <summary>
    /// Start an authorization endpoint activity
    /// </summary>
    IDisposable? StartAuthorizeActivity(string clientId, string responseType);

    /// <summary>
    /// Start a user authentication activity
    /// </summary>
    IDisposable? StartAuthenticationActivity(string method, string? clientId = null);

    /// <summary>
    /// Start an external IdP activity
    /// </summary>
    IDisposable? StartExternalIdpActivity(string provider);

    /// <summary>
    /// Start a user journey activity
    /// </summary>
    IDisposable? StartJourneyActivity(string journeyId, string policyType);

    /// <summary>
    /// Start a journey step activity
    /// </summary>
    IDisposable? StartJourneyStepActivity(string journeyId, string stepType, string stepId);

    /// <summary>
    /// Add a tag/attribute to the current activity
    /// </summary>
    void SetTag(string key, object? value);

    /// <summary>
    /// Add an event to the current activity
    /// </summary>
    void AddEvent(string name, IDictionary<string, object?>? attributes = null);

    /// <summary>
    /// Record an exception on the current activity
    /// </summary>
    void RecordException(Exception exception);

    /// <summary>
    /// Set the status of the current activity
    /// </summary>
    void SetStatus(ActivityStatusCode status, string? description = null);
}

/// <summary>
/// Activity tags/attributes used by Oluso tracing
/// </summary>
public static class OlusoTracingTags
{
    public const string ClientId = "oluso.client_id";
    public const string GrantType = "oluso.grant_type";
    public const string ResponseType = "oluso.response_type";
    public const string Scope = "oluso.scope";
    public const string SubjectId = "oluso.subject_id";
    public const string TenantId = "oluso.tenant_id";
    public const string TokenType = "oluso.token_type";
    public const string AuthMethod = "oluso.auth_method";
    public const string IdpName = "oluso.idp";
    public const string JourneyId = "oluso.journey_id";
    public const string JourneyPolicy = "oluso.journey_policy";
    public const string StepType = "oluso.step_type";
    public const string StepId = "oluso.step_id";
    public const string Error = "oluso.error";
    public const string ErrorDescription = "oluso.error_description";
}

/// <summary>
/// Activity names used by Oluso
/// </summary>
public static class OlusoActivityNames
{
    public const string SourceName = "Oluso";

    // Endpoints
    public const string TokenEndpoint = "Oluso.Token";
    public const string AuthorizeEndpoint = "Oluso.Authorize";
    public const string UserInfoEndpoint = "Oluso.UserInfo";
    public const string IntrospectionEndpoint = "Oluso.Introspection";
    public const string RevocationEndpoint = "Oluso.Revocation";
    public const string DeviceAuthorizationEndpoint = "Oluso.DeviceAuthorization";
    public const string EndSessionEndpoint = "Oluso.EndSession";

    // Authentication
    public const string LocalLogin = "Oluso.LocalLogin";
    public const string ExternalLogin = "Oluso.ExternalLogin";
    public const string MfaChallenge = "Oluso.MfaChallenge";

    // Journey
    public const string JourneyExecution = "Oluso.Journey";
    public const string JourneyStep = "Oluso.JourneyStep";

    // Token Operations
    public const string TokenCreation = "Oluso.CreateToken";
    public const string TokenValidation = "Oluso.ValidateToken";

    // Database
    public const string DatabaseOperation = "Oluso.Database";
}

/// <summary>
/// No-op implementation of tracing (used when no telemetry provider is configured)
/// </summary>
public class NullOlusoTracing : IOlusoTracing
{
    public static readonly NullOlusoTracing Instance = new();

    public IDisposable? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) => null;
    public IDisposable? StartTokenActivity(string grantType, string clientId) => null;
    public IDisposable? StartAuthorizeActivity(string clientId, string responseType) => null;
    public IDisposable? StartAuthenticationActivity(string method, string? clientId = null) => null;
    public IDisposable? StartExternalIdpActivity(string provider) => null;
    public IDisposable? StartJourneyActivity(string journeyId, string policyType) => null;
    public IDisposable? StartJourneyStepActivity(string journeyId, string stepType, string stepId) => null;
    public void SetTag(string key, object? value) { }
    public void AddEvent(string name, IDictionary<string, object?>? attributes = null) { }
    public void RecordException(Exception exception) { }
    public void SetStatus(ActivityStatusCode status, string? description = null) { }
}
