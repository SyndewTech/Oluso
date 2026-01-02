namespace Oluso.Core.UserJourneys;

/// <summary>
/// Options for the User Journey Engine
/// </summary>
public class UserJourneyOptions
{
    /// <summary>
    /// Use in-memory policy store (development only)
    /// </summary>
    public bool UseInMemoryPolicyStore { get; set; } = true;

    /// <summary>
    /// Directory for WASM plugins
    /// </summary>
    public string? PluginDirectory { get; set; }

    /// <summary>
    /// Enable hot reload for plugins in development
    /// </summary>
    public bool EnablePluginHotReload { get; set; } = true;

    /// <summary>
    /// Default timeout for journey sessions (minutes)
    /// </summary>
    public int DefaultJourneyTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum number of steps allowed in a journey
    /// </summary>
    public int MaxStepsPerJourney { get; set; } = 50;

    /// <summary>
    /// Enable CAPTCHA for sign-up flows
    /// </summary>
    public bool RequireCaptchaForSignUp { get; set; } = false;

    /// <summary>
    /// Default MFA policy
    /// </summary>
    public MfaPolicy DefaultMfaPolicy { get; set; } = MfaPolicy.Optional;
}

/// <summary>
/// MFA enforcement policy
/// </summary>
public enum MfaPolicy
{
    /// <summary>MFA is not required</summary>
    Off,

    /// <summary>User can choose to enable MFA</summary>
    Optional,

    /// <summary>MFA is required for all users</summary>
    Required,

    /// <summary>MFA is required based on risk assessment</summary>
    RiskBased
}
