namespace Oluso.Core.Protocols;

/// <summary>
/// Base configuration for protocol endpoints
/// </summary>
public abstract class ProtocolEndpointConfiguration
{
    /// <summary>
    /// Protocol identifier
    /// </summary>
    public abstract string ProtocolName { get; }

    /// <summary>
    /// Default expiration for protocol state (default: 10 minutes)
    /// </summary>
    public TimeSpan StateExpiration { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// OIDC/OAuth 2.0 endpoint configuration
/// </summary>
public class OidcEndpointConfiguration : ProtocolEndpointConfiguration
{
    public override string ProtocolName => "oidc";

    /// <summary>
    /// Authorization endpoint path
    /// </summary>
    public string AuthorizeEndpoint { get; set; } = "/connect/authorize";

    /// <summary>
    /// Token endpoint path
    /// </summary>
    public string TokenEndpoint { get; set; } = "/connect/token";

    /// <summary>
    /// UserInfo endpoint path
    /// </summary>
    public string UserInfoEndpoint { get; set; } = "/connect/userinfo";

    /// <summary>
    /// Token revocation endpoint path
    /// </summary>
    public string RevocationEndpoint { get; set; } = "/connect/revocation";

    /// <summary>
    /// Token introspection endpoint path
    /// </summary>
    public string IntrospectionEndpoint { get; set; } = "/connect/introspect";

    /// <summary>
    /// End session (logout) endpoint path
    /// </summary>
    public string EndSessionEndpoint { get; set; } = "/connect/endsession";

    /// <summary>
    /// Device authorization endpoint path
    /// </summary>
    public string DeviceAuthorizationEndpoint { get; set; } = "/connect/deviceauthorization";

    /// <summary>
    /// Pushed Authorization Request endpoint path
    /// </summary>
    public string PushedAuthorizationEndpoint { get; set; } = "/connect/par";

    /// <summary>
    /// CIBA (Client Initiated Backchannel Authentication) endpoint path
    /// </summary>
    public string BackchannelAuthenticationEndpoint { get; set; } = "/connect/ciba";

    /// <summary>
    /// OpenID Connect discovery endpoint path
    /// </summary>
    public string DiscoveryEndpoint { get; set; } = "/.well-known/openid-configuration";

    /// <summary>
    /// JSON Web Key Set endpoint path
    /// </summary>
    public string JwksEndpoint { get; set; } = "/.well-known/jwks";

    /// <summary>
    /// Enable Pushed Authorization Requests (RFC 9126)
    /// </summary>
    public bool EnablePar { get; set; } = true;

    /// <summary>
    /// Enable DPoP (RFC 9449)
    /// </summary>
    public bool EnableDPoP { get; set; } = true;

    /// <summary>
    /// Query parameter name for policy ID
    /// </summary>
    public string PolicyQueryParam { get; set; } = "policy";

    /// <summary>
    /// Alternative query parameter name for policy ID (e.g., "p" for Azure B2C compatibility)
    /// </summary>
    public string? PolicyQueryParamAlternate { get; set; } = "p";

    /// <summary>
    /// Query parameter name for UI mode
    /// </summary>
    public string UiModeQueryParam { get; set; } = "ui_mode";
}

/// <summary>
/// Route information for protocol endpoints
/// </summary>
public class ProtocolRouteInfo
{
    /// <summary>
    /// Endpoint path
    /// </summary>
    public string Path { get; init; } = null!;

    /// <summary>
    /// HTTP methods supported
    /// </summary>
    public string[] Methods { get; init; } = ["GET"];

    /// <summary>
    /// Endpoint type
    /// </summary>
    public EndpointType EndpointType { get; init; }

    /// <summary>
    /// Whether this endpoint supports policy query parameter
    /// </summary>
    public bool SupportsPolicyParam { get; init; }

    public static ProtocolRouteInfo Create(
        string path,
        EndpointType type,
        bool supportsPolicyParam = false,
        params string[] methods) => new()
    {
        Path = path,
        EndpointType = type,
        SupportsPolicyParam = supportsPolicyParam,
        Methods = methods.Length > 0 ? methods : ["GET"]
    };
}
