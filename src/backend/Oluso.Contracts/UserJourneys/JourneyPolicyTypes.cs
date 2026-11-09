namespace Oluso.Core.UserJourneys;

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

    /// <summary>
    /// Visual theme applied to the journey shell. One of "light", "dark",
    /// "minimal" — anything else (incl. null) renders the default look.
    /// Pure-CSS: maps to a <c>theme-{value}</c> class on the page body.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// How the multi-step progress indicator presents. One of "horizontal"
    /// (default — dots across the top), "vertical" (a step list beside the
    /// card), or "none" (hidden). Anything else falls back to horizontal.
    /// </summary>
    public string? ProgressStyle { get; set; }

    public IDictionary<string, string>? Localization { get; set; }

    /// <summary>
    /// Per-locale string overrides for this journey. Outer key is the locale
    /// code (e.g. "en", "fr"), inner dictionary maps string key → override value.
    /// At runtime, journey overrides layer on top of tenant overrides which layer
    /// on top of system defaults.
    /// </summary>
    public IDictionary<string, IDictionary<string, string>>? LocaleOverrides { get; set; }

    /// <summary>
    /// Layout width for the journey container.
    /// Options: "narrow" (460px), "medium" (600px), "wide" (900px), "full" (100%)
    /// Default is "narrow".
    /// </summary>
    public string? Layout { get; set; }
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
