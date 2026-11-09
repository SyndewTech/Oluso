using Oluso.Core.UserJourneys;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Context passed to claims collection
/// </summary>
public class ClaimsProviderContext
{
    /// <summary>
    /// The user's subject ID (user ID)
    /// </summary>
    public required string SubjectId { get; init; }

    /// <summary>
    /// The tenant ID (if multi-tenant)
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// The client ID requesting the token
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// The scopes being requested
    /// </summary>
    public IEnumerable<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The caller (e.g., "TokenEndpoint", "UserInfoEndpoint")
    /// </summary>
    public string? Caller { get; init; }

    /// <summary>
    /// Session ID for the current authentication session
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// The protocol being used (e.g., "oidc", "saml", "wsfed").
    /// Claims providers can use this to only respond to specific protocols.
    /// </summary>
    public string? Protocol { get; init; }

    /// <summary>
    /// Additional context data
    /// </summary>
    public IDictionary<string, object>? AdditionalData { get; init; }

    /// <summary>
    /// Convert to PluginClaimsContext for plugin invocation
    /// </summary>
    public PluginClaimsContext ToPluginContext() => new()
    {
        SubjectId = SubjectId,
        TenantId = TenantId,
        ClientId = ClientId,
        Scopes = Scopes,
        Caller = Caller,
        SessionId = SessionId,
        Protocol = Protocol
    };
}

/// <summary>
/// Interface for module-level claims providers registered via DI.
/// Modules implement this to inject custom claims into tokens.
/// </summary>
public interface IClaimsProvider
{
    /// <summary>
    /// Unique identifier for this provider (the module's key)
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Priority for claim resolution. Higher priority providers are processed first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// The scopes that trigger this claims provider.
    /// If null or empty, provider is always invoked.
    /// </summary>
    IEnumerable<string>? TriggerScopes { get; }

    /// <summary>
    /// Whether this provider should run for the given context.
    /// </summary>
    bool ShouldProvide(ClaimsProviderContext context);

    /// <summary>
    /// Get claims to add to the token.
    /// </summary>
    Task<ClaimsProviderResult> GetClaimsAsync(
        ClaimsProviderContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a claims provider invocation
/// </summary>
public class ClaimsProviderResult
{
    public bool Success { get; init; }
    public IDictionary<string, object> Claims { get; init; } = new Dictionary<string, object>();
    public string? Error { get; init; }

    public static ClaimsProviderResult Succeeded(IDictionary<string, object> claims) =>
        new() { Success = true, Claims = claims };

    public static ClaimsProviderResult Failed(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Registry for collecting claims from all sources: plugins and DI-registered IClaimsProvider instances.
/// </summary>
public interface IClaimsProviderRegistry
{
    /// <summary>
    /// Get claims from all enabled providers (plugins + DI-registered module providers).
    /// </summary>
    Task<IDictionary<string, object>> GetAllClaimsAsync(
        ClaimsProviderContext context,
        CancellationToken cancellationToken = default);
}
