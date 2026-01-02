namespace Oluso.Core.Protocols.Models;

/// <summary>
/// Central constants for OAuth 2.0 and OpenID Connect protocols
/// </summary>
public static class OidcConstants
{
    /// <summary>
    /// Authentication scheme name for Oluso access token bearer authentication
    /// </summary>
    public const string AccessTokenAuthenticationScheme = "OlusoAccessToken";

    public static class GrantTypes
    {
        public const string AuthorizationCode = "authorization_code";
        public const string ClientCredentials = "client_credentials";
        public const string RefreshToken = "refresh_token";
        public const string Implicit = "implicit";
        public const string Hybrid = "hybrid";
        public const string Password = "password";
        public const string DeviceCode = "urn:ietf:params:oauth:grant-type:device_code";
        public const string Ciba = "urn:openid:params:grant-type:ciba";
        public const string TokenExchange = "urn:ietf:params:oauth:grant-type:token-exchange";
        public const string JwtBearer = "urn:ietf:params:oauth:grant-type:jwt-bearer";
        public const string Saml2Bearer = "urn:ietf:params:oauth:grant-type:saml2-bearer";
    }

    public static class Scopes
    {
        public const string OpenId = "openid";
        public const string Profile = "profile";
        public const string Email = "email";
        public const string Address = "address";
        public const string Phone = "phone";
        public const string OfflineAccess = "offline_access";
    }

    public static class TokenTypes
    {
        public const string AccessToken = "urn:ietf:params:oauth:token-type:access_token";
        public const string RefreshToken = "urn:ietf:params:oauth:token-type:refresh_token";
        public const string IdToken = "urn:ietf:params:oauth:token-type:id_token";
        public const string Saml1 = "urn:ietf:params:oauth:token-type:saml1";
        public const string Saml2 = "urn:ietf:params:oauth:token-type:saml2";
        public const string Jwt = "urn:ietf:params:oauth:token-type:jwt";
    }

    public static class Errors
    {
        public const string InvalidRequest = "invalid_request";
        public const string InvalidClient = "invalid_client";
        public const string InvalidGrant = "invalid_grant";
        public const string UnauthorizedClient = "unauthorized_client";
        public const string UnsupportedGrantType = "unsupported_grant_type";
        public const string InvalidScope = "invalid_scope";
        public const string AccessDenied = "access_denied";
        public const string UnsupportedResponseType = "unsupported_response_type";
        public const string ServerError = "server_error";
        public const string TemporarilyUnavailable = "temporarily_unavailable";
        public const string InvalidToken = "invalid_token";
        public const string InsufficientScope = "insufficient_scope";

        // Device flow
        public const string AuthorizationPending = "authorization_pending";
        public const string SlowDown = "slow_down";
        public const string ExpiredToken = "expired_token";

        // OIDC specific
        public const string InteractionRequired = "interaction_required";
        public const string LoginRequired = "login_required";
        public const string ConsentRequired = "consent_required";
        public const string RequestNotSupported = "request_not_supported";
        public const string RequestUriNotSupported = "request_uri_not_supported";
        public const string RegistrationNotSupported = "registration_not_supported";

        // DPoP
        public const string UseDPoPNonce = "use_dpop_nonce";
        public const string InvalidDPoPProof = "invalid_dpop_proof";
    }

    public static class EndpointNames
    {
        public const string Authorize = "authorize";
        public const string Token = "token";
        public const string UserInfo = "userinfo";
        public const string Revocation = "revocation";
        public const string Introspection = "introspect";
        public const string DeviceAuthorization = "device_authorization";
        public const string EndSession = "end_session";
        public const string CheckSession = "checksession";
        public const string Discovery = ".well-known/openid-configuration";
        public const string Jwks = ".well-known/jwks";
        public const string PushedAuthorization = "par";
        public const string BackchannelAuthentication = "ciba";
    }

    public static class StandardClaims
    {
        // ID Token claims
        public const string Subject = "sub";
        public const string Issuer = "iss";
        public const string Audience = "aud";
        public const string Expiration = "exp";
        public const string IssuedAt = "iat";
        public const string NotBefore = "nbf";
        public const string AuthTime = "auth_time";
        public const string Nonce = "nonce";
        public const string Acr = "acr";
        public const string Amr = "amr";
        public const string Azp = "azp";
        public const string AtHash = "at_hash";
        public const string CHash = "c_hash";
        public const string SessionId = "sid";

        // Profile claims
        public const string Name = "name";
        public const string GivenName = "given_name";
        public const string FamilyName = "family_name";
        public const string MiddleName = "middle_name";
        public const string Nickname = "nickname";
        public const string PreferredUsername = "preferred_username";
        public const string Profile = "profile";
        public const string Picture = "picture";
        public const string Website = "website";
        public const string Gender = "gender";
        public const string Birthdate = "birthdate";
        public const string Zoneinfo = "zoneinfo";
        public const string Locale = "locale";
        public const string UpdatedAt = "updated_at";

        // Email claims
        public const string Email = "email";
        public const string EmailVerified = "email_verified";

        // Phone claims
        public const string PhoneNumber = "phone_number";
        public const string PhoneNumberVerified = "phone_number_verified";

        // Address claims
        public const string Address = "address";

        // Access token claims
        public const string Scope = "scope";
        public const string ClientId = "client_id";
        public const string JwtId = "jti";

        // Custom/extension claims
        public const string TenantId = "tenant_id";
        public const string Role = "role";
    }
}
