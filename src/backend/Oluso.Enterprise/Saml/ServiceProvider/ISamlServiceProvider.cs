using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Oluso.Enterprise.Saml.ServiceProvider;

/// <summary>
/// Result of processing a SAML response
/// </summary>
public class SamlAuthenticationResult
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public string? SubjectId { get; set; }
    public string? SessionIndex { get; set; }
    public ClaimsPrincipal? Principal { get; set; }
    public string? RelayState { get; set; }
    public DateTime? AuthnInstant { get; set; }
    public DateTime? SessionNotOnOrAfter { get; set; }

    public static SamlAuthenticationResult Success(
        string subjectId,
        ClaimsPrincipal principal,
        string? sessionIndex = null,
        string? relayState = null)
    {
        return new SamlAuthenticationResult
        {
            Succeeded = true,
            SubjectId = subjectId,
            Principal = principal,
            SessionIndex = sessionIndex,
            RelayState = relayState
        };
    }

    public static SamlAuthenticationResult Failure(string error)
    {
        return new SamlAuthenticationResult
        {
            Succeeded = false,
            Error = error
        };
    }
}

/// <summary>
/// Parameters for initiating SAML authentication
/// </summary>
public class SamlAuthnRequestParams
{
    /// <summary>
    /// The IdP to authenticate with
    /// </summary>
    public string IdpName { get; set; } = string.Empty;

    /// <summary>
    /// Return URL after authentication
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Force re-authentication
    /// </summary>
    public bool ForceAuthn { get; set; }

    /// <summary>
    /// Passive authentication (no UI)
    /// </summary>
    public bool IsPassive { get; set; }

    /// <summary>
    /// Requested authentication context classes
    /// </summary>
    public List<string>? RequestedAuthnContextClasses { get; set; }

    /// <summary>
    /// Custom relay state
    /// </summary>
    public string? RelayState { get; set; }
}

/// <summary>
/// SAML Service Provider for consuming external SAML IdPs.
/// Supports both static configuration and database-backed IdP configurations.
/// </summary>
public interface ISamlServiceProvider
{
    /// <summary>
    /// Creates a SAML AuthnRequest for the specified IdP
    /// </summary>
    Task<SamlAuthnRequest> CreateAuthnRequestAsync(
        SamlAuthnRequestParams parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a SAML Response from an IdP
    /// </summary>
    Task<SamlAuthenticationResult> ProcessResponseAsync(
        HttpContext httpContext,
        string samlResponse,
        string? relayState = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates Single Logout
    /// </summary>
    Task<SamlLogoutRequest> CreateLogoutRequestAsync(
        string idpName,
        string nameId,
        string? sessionIndex = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a SAML Logout Response
    /// </summary>
    Task<bool> ProcessLogoutResponseAsync(
        string samlResponse,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets configured IdPs from database and static config
    /// </summary>
    Task<IReadOnlyList<SamlIdpInfo>> GetConfiguredIdpsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific IdP configuration by name
    /// </summary>
    Task<SamlIdpInfo?> GetIdpByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes IdP configuration (clears cache)
    /// </summary>
    Task RefreshIdpConfigurationAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates SP metadata
    /// </summary>
    Task<string> GenerateMetadataAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// SAML authentication request
/// </summary>
public class SamlAuthnRequest
{
    public string Url { get; set; } = string.Empty;
    public string? SamlRequest { get; set; }
    public string? RelayState { get; set; }
    public string Binding { get; set; } = "Redirect";
}

/// <summary>
/// SAML logout request
/// </summary>
public class SamlLogoutRequest
{
    public string Url { get; set; } = string.Empty;
    public string? SamlRequest { get; set; }
    public string? RelayState { get; set; }
    public string Binding { get; set; } = "Redirect";
}

/// <summary>
/// Information about a configured IdP
/// </summary>
public class SamlIdpInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool ProxyMode { get; set; }
    public bool StoreUserLocally { get; set; } = true;
}
