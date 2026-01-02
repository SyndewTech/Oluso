using Microsoft.AspNetCore.Http;

namespace Oluso.Core.Protocols;

/// <summary>
/// Context for protocol request processing, bridges protocol layer to authentication
/// </summary>
public class ProtocolContext
{
    /// <summary>
    /// The HTTP context
    /// </summary>
    public HttpContext HttpContext { get; init; } = null!;

    /// <summary>
    /// Protocol name (e.g., "oidc", "saml")
    /// </summary>
    public string ProtocolName { get; init; } = null!;

    /// <summary>
    /// Endpoint type being processed
    /// </summary>
    public EndpointType EndpointType { get; init; }

    /// <summary>
    /// Parsed and validated protocol request
    /// </summary>
    public IProtocolRequest? Request { get; set; }

    /// <summary>
    /// Validated client/relying party
    /// </summary>
    public object? Client { get; set; }

    /// <summary>
    /// Current tenant (if multi-tenant)
    /// </summary>
    public object? Tenant { get; set; }

    /// <summary>
    /// Resolved UI mode for this request
    /// </summary>
    public UiMode UiMode { get; set; } = UiMode.Journey;

    /// <summary>
    /// Journey ID if using journey flow
    /// </summary>
    public string? JourneyId { get; set; }

    /// <summary>
    /// Resolved policy ID to use
    /// </summary>
    public string? PolicyId { get; set; }

    /// <summary>
    /// Correlation ID for state management across redirects
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Additional protocol-specific state
    /// </summary>
    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}

/// <summary>
/// Type of protocol endpoint
/// </summary>
public enum EndpointType
{
    /// <summary>
    /// Authorization endpoint - requires user interaction
    /// </summary>
    Authorize,

    /// <summary>
    /// Token endpoint - machine-to-machine
    /// </summary>
    Token,

    /// <summary>
    /// Metadata/discovery endpoint
    /// </summary>
    Metadata,

    /// <summary>
    /// Token-protected user info endpoint
    /// </summary>
    UserInfo,

    /// <summary>
    /// Logout/session termination endpoint
    /// </summary>
    Logout,

    /// <summary>
    /// Token introspection endpoint
    /// </summary>
    Introspection,

    /// <summary>
    /// Token revocation endpoint
    /// </summary>
    Revocation,

    /// <summary>
    /// Device authorization endpoint
    /// </summary>
    DeviceAuthorization,

    /// <summary>
    /// Pushed authorization request endpoint
    /// </summary>
    PushedAuthorization,

    /// <summary>
    /// CIBA backchannel authentication endpoint
    /// </summary>
    BackchannelAuthentication,

    /// <summary>
    /// Custom protocol-specific endpoint
    /// </summary>
    Custom
}

/// <summary>
/// UI mode for authentication flow
/// </summary>
public enum UiMode
{
    /// <summary>
    /// Use journey engine for authentication UI
    /// </summary>
    Journey,

    /// <summary>
    /// Use standalone Razor pages
    /// </summary>
    Standalone,

    /// <summary>
    /// API-only mode (no UI redirects)
    /// </summary>
    Headless
}
