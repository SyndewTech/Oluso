using System.Security.Claims;

namespace Oluso.Enterprise.Saml.IdentityProvider;

/// <summary>
/// Parameters for creating a SAML assertion
/// </summary>
public class SamlAssertionParams
{
    /// <summary>
    /// The Service Provider entity ID
    /// </summary>
    public string SpEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Subject/user identifier
    /// </summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Claims to include in assertion
    /// </summary>
    public IEnumerable<Claim> Claims { get; set; } = Array.Empty<Claim>();

    /// <summary>
    /// Name ID format
    /// </summary>
    public string? NameIdFormat { get; set; }

    /// <summary>
    /// Authentication context class
    /// </summary>
    public string? AuthnContextClassRef { get; set; }

    /// <summary>
    /// The original AuthnRequest ID (for InResponseTo)
    /// </summary>
    public string? InResponseTo { get; set; }

    /// <summary>
    /// Destination URL (ACS)
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>
    /// Session index
    /// </summary>
    public string? SessionIndex { get; set; }
}

/// <summary>
/// Result of parsing an AuthnRequest
/// </summary>
public class SamlAuthnRequestResult
{
    public bool Valid { get; set; }
    public string? Error { get; set; }
    public string? Id { get; set; }
    public string? Issuer { get; set; }
    public string? AssertionConsumerServiceUrl { get; set; }
    public string? RelayState { get; set; }
    public bool ForceAuthn { get; set; }
    public bool IsPassive { get; set; }
    public string? NameIdFormat { get; set; }
    public List<string>? RequestedAuthnContextClasses { get; set; }
}

/// <summary>
/// SAML Response for sending to SP
/// </summary>
public class SamlResponseResult
{
    public string Destination { get; set; } = string.Empty;
    public string SamlResponse { get; set; } = string.Empty;
    public string? RelayState { get; set; }
}

/// <summary>
/// SAML Identity Provider for issuing assertions
/// </summary>
public interface ISamlIdentityProvider
{
    /// <summary>
    /// Whether IdP functionality is enabled
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Parses an incoming AuthnRequest (assumes HTTP-Redirect binding)
    /// </summary>
    Task<SamlAuthnRequestResult> ParseAuthnRequestAsync(
        string samlRequest,
        string? relayState = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses an incoming AuthnRequest with explicit binding type
    /// </summary>
    Task<SamlAuthnRequestResult> ParseAuthnRequestAsync(
        string samlRequest,
        string? relayState,
        bool isPostBinding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a SAML Response with assertion
    /// </summary>
    Task<SamlResponseResult> CreateResponseAsync(
        SamlAssertionParams parameters,
        string? relayState = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an error response
    /// </summary>
    Task<SamlResponseResult> CreateErrorResponseAsync(
        string spEntityId,
        string? inResponseTo,
        string statusCode,
        string? message = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an incoming logout request
    /// </summary>
    Task<SamlLogoutRequestResult> ParseLogoutRequestAsync(
        string samlRequest,
        string? relayState = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a logout response
    /// </summary>
    Task<SamlResponseResult> CreateLogoutResponseAsync(
        string spEntityId,
        string? inResponseTo,
        bool success,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets registered Service Providers
    /// </summary>
    IReadOnlyList<SamlSpInfo> GetRegisteredServiceProviders();

    /// <summary>
    /// Generates IdP metadata
    /// </summary>
    Task<string> GenerateMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates IdP metadata for a specific tenant using tenant-specific settings.
    /// Uses IIssuerResolver for tenant-specific URLs and ISamlTenantSettingsService for certificates.
    /// </summary>
    Task<string> GenerateMetadataForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of parsing a logout request
/// </summary>
public class SamlLogoutRequestResult
{
    public bool Valid { get; set; }
    public string? Error { get; set; }
    public string? Id { get; set; }
    public string? Issuer { get; set; }
    public string? NameId { get; set; }
    public string? SessionIndex { get; set; }
    public string? RelayState { get; set; }
}

/// <summary>
/// Information about a registered Service Provider
/// </summary>
public class SamlSpInfo
{
    public string EntityId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; }
}
