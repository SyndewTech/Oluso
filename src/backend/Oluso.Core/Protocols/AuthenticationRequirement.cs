using Oluso.Core.UserJourneys;

namespace Oluso.Core.Protocols;

/// <summary>
/// Describes what authentication is required by the protocol
/// </summary>
public class AuthenticationRequirement
{
    /// <summary>
    /// Suggested policy type based on protocol request
    /// </summary>
    public JourneyType SuggestedPolicyType { get; init; } = JourneyType.SignIn;

    /// <summary>
    /// Explicit policy ID if requested
    /// </summary>
    public string? ExplicitPolicyId { get; init; }

    /// <summary>
    /// Whether MFA should be forced (overrides policy)
    /// </summary>
    public bool ForceMfa { get; init; }

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
    public IReadOnlyList<string> RequestedScopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Maximum authentication age in seconds
    /// </summary>
    public int? MaxAge { get; init; }

    /// <summary>
    /// Prompt mode from protocol request
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>
    /// ID token hint for re-authentication
    /// </summary>
    public string? IdTokenHint { get; init; }

    /// <summary>
    /// Force fresh login (ignore existing session)
    /// </summary>
    public bool ForceFreshLogin { get; init; }
}

/// <summary>
/// Result of authentication process
/// </summary>
public class AuthenticationResult
{
    /// <summary>
    /// Whether authentication succeeded
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Authenticated user ID
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Session ID
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Consented/granted scopes
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Claims from authentication
    /// </summary>
    public IDictionary<string, object> Claims { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Authentication time
    /// </summary>
    public DateTime? AuthTime { get; init; }

    /// <summary>
    /// Authentication methods used
    /// </summary>
    public IReadOnlyList<string> Amr { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Authentication context class reference
    /// </summary>
    public string? Acr { get; init; }

    /// <summary>
    /// Error if authentication failed
    /// </summary>
    public ProtocolError? Error { get; init; }

    public static AuthenticationResult Success(
        string userId,
        string? sessionId = null,
        IReadOnlyList<string>? scopes = null,
        IDictionary<string, object>? claims = null) => new()
    {
        Succeeded = true,
        UserId = userId,
        SessionId = sessionId,
        Scopes = scopes ?? Array.Empty<string>(),
        Claims = claims ?? new Dictionary<string, object>(),
        AuthTime = DateTime.UtcNow
    };

    public static AuthenticationResult Failed(string error, string? description = null) => new()
    {
        Succeeded = false,
        Error = new ProtocolError { Code = error, Description = description }
    };

    public static AuthenticationResult Failed(ProtocolError error) => new()
    {
        Succeeded = false,
        Error = error
    };
}
