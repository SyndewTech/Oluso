namespace Oluso.Core.UserJourneys;

/// <summary>
/// Options for the User Journey Engine
/// </summary>
public class UserJourneyOptions
{
    /// <summary>Configuration section these options are read from.</summary>
    public const string SectionName = "Oluso:UserJourneys";

    /// <summary>The standard identity claims a journey hands on by default.</summary>
    public static readonly IReadOnlyList<string> StandardIdentityClaims =
    [
        "sub", "name", "given_name", "family_name", "middle_name", "nickname", "preferred_username",
        "email", "email_verified", "phone_number", "phone_number_verified", "picture", "locale", "zoneinfo",
        "amr", "acr", "idp",
    ];

    /// <summary>
    /// Journey data a completed journey hands on as claims when its policy maps no output
    /// claims; <see cref="StandardIdentityClaims"/> when not set. Journey data also holds
    /// request details, form answers, codes and internal state, so only these names leave
    /// the journey. A policy's own output claims mapping replaces the list.
    /// </summary>
    public List<string>? DefaultOutputClaims { get; set; }

    public IReadOnlyList<string> EffectiveDefaultOutputClaims => DefaultOutputClaims ?? StandardIdentityClaims;

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
