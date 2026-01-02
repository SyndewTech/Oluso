namespace Oluso.Telemetry;

/// <summary>
/// Combined telemetry service providing access to metrics and tracing.
/// This is the main entry point for telemetry in Oluso.
/// </summary>
public interface IOlusoTelemetry
{
    /// <summary>
    /// Metrics collection interface
    /// </summary>
    IOlusoMetrics Metrics { get; }

    /// <summary>
    /// Distributed tracing interface
    /// </summary>
    IOlusoTracing Tracing { get; }

    /// <summary>
    /// Whether telemetry collection is enabled
    /// </summary>
    bool IsEnabled { get; }
}

/// <summary>
/// Default no-op telemetry implementation
/// </summary>
public class NullOlusoTelemetry : IOlusoTelemetry
{
    public static readonly NullOlusoTelemetry Instance = new();

    public IOlusoMetrics Metrics => NullOlusoMetrics.Instance;
    public IOlusoTracing Tracing => NullOlusoTracing.Instance;
    public bool IsEnabled => false;
}

/// <summary>
/// Telemetry events that can be subscribed to
/// </summary>
public static class TelemetryEvents
{
    // Token events
    public const string TokenIssued = "Oluso.TokenIssued";
    public const string TokenIssueFailed = "Oluso.TokenIssueFailed";
    public const string TokenIntrospected = "Oluso.TokenIntrospected";
    public const string TokenRevoked = "Oluso.TokenRevoked";

    // Authentication events
    public const string UserAuthenticated = "Oluso.UserAuthenticated";
    public const string UserAuthenticationFailed = "Oluso.UserAuthenticationFailed";
    public const string ExternalLoginSuccess = "Oluso.ExternalLoginSuccess";
    public const string ExternalLoginFailed = "Oluso.ExternalLoginFailed";
    public const string MfaChallengeSuccess = "Oluso.MfaChallengeSuccess";
    public const string MfaChallengeFailed = "Oluso.MfaChallengeFailed";

    // Authorization events
    public const string AuthorizationSuccess = "Oluso.AuthorizationSuccess";
    public const string AuthorizationFailed = "Oluso.AuthorizationFailed";
    public const string ConsentGranted = "Oluso.ConsentGranted";
    public const string ConsentDenied = "Oluso.ConsentDenied";

    // Session events
    public const string SessionCreated = "Oluso.SessionCreated";
    public const string SessionEnded = "Oluso.SessionEnded";
    public const string SessionExpired = "Oluso.SessionExpired";

    // Client events
    public const string ClientAuthenticated = "Oluso.ClientAuthenticated";
    public const string ClientAuthenticationFailed = "Oluso.ClientAuthenticationFailed";

    // Journey events
    public const string JourneyStarted = "Oluso.JourneyStarted";
    public const string JourneyCompleted = "Oluso.JourneyCompleted";
    public const string JourneyFailed = "Oluso.JourneyFailed";
    public const string JourneyStepCompleted = "Oluso.JourneyStepCompleted";
}
