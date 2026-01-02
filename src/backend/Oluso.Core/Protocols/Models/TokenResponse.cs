using System.Text.Json.Serialization;

namespace Oluso.Core.Protocols.Models;

/// <summary>
/// OAuth 2.0/OIDC token response
/// </summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    [JsonPropertyName("id_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdToken { get; set; }

    // Token exchange specific
    [JsonPropertyName("issued_token_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IssuedTokenType { get; set; }

    // DPoP
    [JsonPropertyName("dpop_nonce")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DPoPNonce { get; set; }

    // Custom properties
    [JsonExtensionData]
    public Dictionary<string, object>? Custom { get; set; }
}

/// <summary>
/// OAuth 2.0 error response
/// </summary>
public class TokenErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = default!;

    [JsonPropertyName("error_description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("error_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorUri { get; set; }
}

/// <summary>
/// Device authorization response
/// </summary>
public class DeviceAuthorizationResponse
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = default!;

    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = default!;

    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; set; } = default!;

    [JsonPropertyName("verification_uri_complete")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerificationUriComplete { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 5;
}

/// <summary>
/// Introspection response
/// </summary>
public class IntrospectionResponse
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    [JsonPropertyName("client_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; set; }

    [JsonPropertyName("username")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Username { get; set; }

    [JsonPropertyName("token_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TokenType { get; set; }

    [JsonPropertyName("exp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Exp { get; set; }

    [JsonPropertyName("iat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Iat { get; set; }

    [JsonPropertyName("nbf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Nbf { get; set; }

    [JsonPropertyName("sub")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sub { get; set; }

    [JsonPropertyName("aud")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Aud { get; set; }

    [JsonPropertyName("iss")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Iss { get; set; }

    [JsonPropertyName("jti")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Jti { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? Claims { get; set; }
}

/// <summary>
/// Pushed Authorization Request (PAR) response - RFC 9126
/// </summary>
public class ParResponse
{
    [JsonPropertyName("request_uri")]
    public string RequestUri { get; set; } = default!;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// Pushed Authorization Request (PAR) request DTO for JSON body - RFC 9126
/// </summary>
public class ParRequestDto
{
    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("redirect_uri")]
    public string? RedirectUri { get; set; }

    [JsonPropertyName("response_type")]
    public string? ResponseType { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }

    [JsonPropertyName("code_challenge")]
    public string? CodeChallenge { get; set; }

    [JsonPropertyName("code_challenge_method")]
    public string? CodeChallengeMethod { get; set; }

    [JsonPropertyName("response_mode")]
    public string? ResponseMode { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("max_age")]
    public string? MaxAge { get; set; }

    [JsonPropertyName("id_token_hint")]
    public string? IdTokenHint { get; set; }

    [JsonPropertyName("login_hint")]
    public string? LoginHint { get; set; }

    [JsonPropertyName("acr_values")]
    public string? AcrValues { get; set; }

    [JsonPropertyName("ui_locales")]
    public string? UiLocales { get; set; }

    [JsonPropertyName("request")]
    public string? Request { get; set; }

    [JsonPropertyName("request_uri")]
    public string? RequestUri { get; set; }

    /// <summary>
    /// Convert to AuthorizeRequest
    /// </summary>
    public AuthorizeRequest ToAuthorizeRequest()
    {
        return new AuthorizeRequest
        {
            ClientId = ClientId,
            RedirectUri = RedirectUri,
            ResponseType = ResponseType,
            Scope = Scope,
            State = State,
            Nonce = Nonce,
            CodeChallenge = CodeChallenge,
            CodeChallengeMethod = CodeChallengeMethod,
            ResponseMode = ResponseMode,
            Prompt = Prompt,
            MaxAge = MaxAge,
            IdTokenHint = IdTokenHint,
            LoginHint = LoginHint,
            AcrValues = AcrValues,
            UiLocales = UiLocales,
            Request = Request,
            RequestUri = RequestUri
        };
    }
}
