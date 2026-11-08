namespace Oluso.Core.Protocols.Models;

/// <summary>
/// Represents a validated token request with all OAuth 2.0/OIDC parameters
/// </summary>
public class TokenRequest
{
    // Required
    public string GrantType { get; set; } = default!;

    // Client authentication (multiple methods supported)
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ClientAssertion { get; set; }
    public string? ClientAssertionType { get; set; }

    // Authorization code grant
    public string? Code { get; set; }
    public string? RedirectUri { get; set; }
    public string? CodeVerifier { get; set; }  // PKCE

    // Refresh token grant
    public string? RefreshToken { get; set; }

    /// <summary>
    /// The <c>tenant</c> parameter of a refresh: the tenant the new tokens are for. The user must be entitled
    /// to it (a member, an owner or admin of its organization, or a SuperAdmin), and the rotated refresh token
    /// keeps it, so later refreshes stay there. Null keeps the refresh token's tenant.
    /// </summary>
    public string? Tenant { get; set; }

    // Resource owner password grant (deprecated but supported)
    public string? UserName { get; set; }
    public string? Password { get; set; }

    // Device code grant
    public string? DeviceCode { get; set; }

    // CIBA grant
    public string? AuthReqId { get; set; }

    // Token exchange (RFC 8693)
    public string? SubjectToken { get; set; }
    public string? SubjectTokenType { get; set; }
    public string? ActorToken { get; set; }
    public string? ActorTokenType { get; set; }
    public string? RequestedTokenType { get; set; }

    // JWT Bearer grant (RFC 7523)
    public string? Assertion { get; set; }

    // Common optional parameters
    public string? Scope { get; set; }
    public ICollection<string> Resource { get; set; } = new List<string>();

    // DPoP (RFC 9449)
    public string? DPoP { get; set; }
    public string? DPoPNonce { get; set; }

    /// <summary>
    /// Validated DPoP key thumbprint (jkt) from the current proof
    /// </summary>
    public string? DPoPKeyThumbprint { get; set; }

    /// <summary>
    /// Previously bound DPoP key thumbprint (for refresh token validation)
    /// </summary>
    public string? BoundDPoPJkt { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of the mTLS client certificate (x5t#S256), set during
    /// mTLS client authentication for certificate-bound access tokens (RFC 8705 §3).
    /// </summary>
    public string? MtlsCertificateThumbprint { get; set; }

    // Parsed properties

    /// <summary>
    /// The requested scopes the server will grant: the <c>scope</c> parameter less any the client isn't allowed,
    /// that don't exist, or that the tenant doesn't offer. Empty when no scope was named.
    /// </summary>
    public ICollection<string> RequestedScopes { get; set; } = new List<string>();

    /// <summary>
    /// The <c>scope</c> parameter as sent. Grants that narrow an earlier grant (refresh, token exchange) check
    /// this against it, so asking for a scope beyond the original grant fails even if it would have been left out.
    /// </summary>
    public ICollection<string> ScopesAsRequested { get; set; } = new List<string>();

    /// <summary>The scopes the client asked for: <see cref="ScopesAsRequested"/>, else <see cref="RequestedScopes"/>.</summary>
    public IEnumerable<string> ScopesAsked => ScopesAsRequested.Count > 0 ? ScopesAsRequested : RequestedScopes;

    // Validated client (populated after authentication)
    public ValidatedClient? Client { get; set; }

    // Authorization code grant data (populated after code validation)
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public AuthorizationCodeData? AuthorizationCodeData { get; set; }

    // Raw parameters
    public IDictionary<string, string> Raw { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// Result of validating an authorization code grant
/// </summary>
public class AuthorizationCodeValidationResult
{
    /// <summary>
    /// The authorization code from the request
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>
    /// The redirect_uri from the request (if provided)
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// The code_verifier from the request (if provided)
    /// </summary>
    public string? CodeVerifier { get; set; }

    /// <summary>
    /// The subject ID from the authorization code grant
    /// </summary>
    public string? SubjectId { get; set; }

    /// <summary>
    /// The session ID from the authorization code grant
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// DPoP key thumbprint bound to this authorization (if any)
    /// </summary>
    public string? BoundDPoPJkt { get; set; }

    /// <summary>
    /// The parsed authorization code data
    /// </summary>
    public AuthorizationCodeData? CodeData { get; set; }
}

/// <summary>
/// Data stored with an authorization code for validation at token exchange
/// </summary>
public class AuthorizationCodeData
{
    /// <summary>
    /// The redirect_uri used in the authorization request
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// PKCE code_challenge from the authorization request
    /// </summary>
    public string? CodeChallenge { get; set; }

    /// <summary>
    /// PKCE code_challenge_method (plain or S256)
    /// </summary>
    public string? CodeChallengeMethod { get; set; }

    /// <summary>
    /// DPoP key thumbprint bound to this authorization
    /// </summary>
    public string? DPoPJkt { get; set; }

    /// <summary>
    /// The nonce from the authorization request (for ID token)
    /// </summary>
    public string? Nonce { get; set; }

    /// <summary>
    /// Requested scopes from the authorization request
    /// </summary>
    public ICollection<string>? RequestedScopes { get; set; }

    /// <summary>
    /// Granted scopes after consent
    /// </summary>
    public ICollection<string>? GrantedScopes { get; set; }

    /// <summary>
    /// Additional claims to include in tokens
    /// </summary>
    public IDictionary<string, object>? Claims { get; set; }

    /// <summary>
    /// Authentication time (auth_time claim)
    /// </summary>
    public DateTime? AuthTime { get; set; }

    /// <summary>
    /// Authentication method references (amr claim)
    /// </summary>
    public ICollection<string>? Amr { get; set; }

    /// <summary>
    /// Authentication context class reference (acr claim)
    /// </summary>
    public string? Acr { get; set; }
}

/// <summary>
/// Validated client information
/// </summary>
public class ValidatedClient
{
    public string ClientId { get; set; } = default!;
    public string? ClientName { get; set; }
    public string? TenantId { get; set; }
    public string? Description { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public ClientAuthenticationMethod AuthenticationMethod { get; set; }
    public ICollection<string> AllowedGrantTypes { get; set; } = new List<string>();
    public ICollection<string> AllowedScopes { get; set; } = new List<string>();
    public bool RequirePkce { get; set; }
    public bool AllowPlainTextPkce { get; set; }
    public bool RequireRequestObject { get; set; }
    public bool AllowAccessTokensViaBrowser { get; set; }
    public bool AllowOfflineAccess { get; set; }

    // Token lifetime settings
    public int IdentityTokenLifetime { get; set; }
    public int AccessTokenLifetime { get; set; }
    public int AuthorizationCodeLifetime { get; set; }
    public int DeviceCodeLifetime { get; set; }

    // Token settings
    public int AccessTokenType { get; set; }
    public string? AllowedIdentityTokenSigningAlgorithms { get; set; }
    public bool AlwaysIncludeUserClaimsInIdToken { get; set; }
    public bool UpdateAccessTokenClaimsOnRefresh { get; set; }
    public bool IncludeJwtId { get; set; }

    // Refresh token settings
    public int RefreshTokenLifetime { get; set; }
    public int AbsoluteRefreshTokenLifetime { get; set; }
    public int SlidingRefreshTokenLifetime { get; set; }
    public int RefreshTokenExpiration { get; set; }
    public int RefreshTokenUsage { get; set; }

    // DPoP
    public bool RequireDPoP { get; set; }

    // Subject identifier type (OpenID Connect Core Section 8)
    public string? PairWiseSubjectSalt { get; set; }

    // PAR (Pushed Authorization Request)
    public bool RequirePushedAuthorization { get; set; }
    public int PushedAuthorizationLifetime { get; set; }

    // Consent settings
    public bool RequireConsent { get; set; }
    public bool AllowRememberConsent { get; set; }
    public int? ConsentLifetime { get; set; }

    // Login settings
    public bool EnableLocalLogin { get; set; }
    public int? UserSsoLifetime { get; set; }

    // Client claims
    public bool AlwaysSendClientClaims { get; set; }
    public string ClientClaimsPrefix { get; set; } = "client_";
    public ICollection<ValidatedClientClaim> Claims { get; set; } = new List<ValidatedClientClaim>();

    // IdP restrictions
    public ICollection<string> IdentityProviderRestrictions { get; set; } = new List<string>();

    // Access restrictions
    public string? RequiredPermission { get; set; }
    public ICollection<string> AllowedRoles { get; set; } = new List<string>();
    public ICollection<string> AllowedUsers { get; set; } = new List<string>();
    public bool RequireVerifiedEmail { get; set; }

    /// <summary>Resource indicators the client may request; empty means any.</summary>
    public ICollection<string> AllowedResources { get; set; } = new List<string>();

    // Custom properties (includes domain_hint and other custom settings)
    public IDictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

    // Logout settings
    public string? FrontChannelLogoutUri { get; set; }
    public bool FrontChannelLogoutSessionRequired { get; set; }
    public string? BackChannelLogoutUri { get; set; }
    public bool BackChannelLogoutSessionRequired { get; set; }

    // CIBA settings
    public bool CibaEnabled { get; set; }
    public string CibaTokenDeliveryMode { get; set; } = "poll";
    public string? CibaClientNotificationEndpoint { get; set; }
    public int CibaRequestLifetime { get; set; } = 120;
    public int CibaPollingInterval { get; set; } = 5;
    public bool CibaRequireUserCode { get; set; }
}

/// <summary>
/// Validated client claim
/// </summary>
public class ValidatedClientClaim
{
    public string Type { get; set; } = default!;
    public string Value { get; set; } = default!;
}

/// <summary>
/// Client authentication methods
/// </summary>
public enum ClientAuthenticationMethod
{
    None,
    ClientSecretBasic,
    ClientSecretPost,
    ClientSecretJwt,
    PrivateKeyJwt,
    TlsClientAuth,
    SelfSignedTlsClientAuth
}

/// <summary>
/// Client assertion types
/// </summary>
public static class ClientAssertionTypes
{
    public const string JwtBearer = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
    public const string SamlBearer = "urn:ietf:params:oauth:client-assertion-type:saml2-bearer";
}
