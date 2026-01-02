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
/// Registry for collecting claims from all enabled plugins.
/// Automatically discovers claims providers from plugins registered in IManagedPluginRegistry.
/// </summary>
public interface IClaimsProviderRegistry
{
    /// <summary>
    /// Get claims from all enabled plugins that have claims providers.
    /// Only collects from plugins that are enabled and whose trigger scopes match.
    /// </summary>
    Task<IDictionary<string, object>> GetAllClaimsAsync(
        ClaimsProviderContext context,
        CancellationToken cancellationToken = default);
}
