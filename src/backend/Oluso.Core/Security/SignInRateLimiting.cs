namespace Oluso.Core.Security;

/// <summary>
/// Rate-limit policies for the surfaces that accept credentials or codes. Applied with
/// <c>[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]</c>; registered by <c>AddOluso</c>.
/// Account lockout stops guessing one account's password; these stop one client from trying
/// many accounts (spraying, credential stuffing) or hammering codes across journeys.
/// </summary>
public static class SignInRateLimitPolicies
{
    /// <summary>
    /// Credential and code submissions: sign-in, registration, password reset, journey steps,
    /// the headless journey API and passkey assertions. Counts POSTs per client IP.
    /// </summary>
    public const string SignIn = "oluso-sign-in";

    /// <summary>The token endpoint, per client IP.</summary>
    public const string Token = "oluso-token";
}

/// <summary>Limits for <see cref="SignInRateLimitPolicies"/>, bound from <c>Oluso:RateLimiting</c>.</summary>
public class SignInRateLimitOptions
{
    public const string SectionName = "Oluso:RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Credential and code submissions allowed per client IP per window.</summary>
    public int SignInPermitLimit { get; set; } = 30;
    public TimeSpan SignInWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Token requests allowed per client IP per window.</summary>
    public int TokenPermitLimit { get; set; } = 300;
    public TimeSpan TokenWindow { get; set; } = TimeSpan.FromMinutes(1);
}
