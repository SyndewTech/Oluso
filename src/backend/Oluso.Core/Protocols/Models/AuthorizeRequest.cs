namespace Oluso.Core.Protocols.Models;

/// <summary>
/// Represents a validated authorization request with all OAuth 2.0/OIDC parameters
/// </summary>
public class AuthorizeRequest
{
    // Required OAuth 2.0 parameters
    public string ClientId { get; set; } = default!;
    public string ResponseType { get; set; } = default!;
    public string? RedirectUri { get; set; }

    // Optional OAuth 2.0 parameters
    public string? Scope { get; set; }
    public string? State { get; set; }

    // PKCE parameters (RFC 7636)
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }

    // OpenID Connect parameters
    public string? Nonce { get; set; }
    public string? ResponseMode { get; set; }
    public string? Display { get; set; }
    public string? Prompt { get; set; }
    public string? MaxAge { get; set; }
    public string? UiLocales { get; set; }
    public string? IdTokenHint { get; set; }
    public string? LoginHint { get; set; }
    public string? AcrValues { get; set; }

    // Domain hint for IdP selection (used with Azure AD, Google Workspace, etc.)
    public string? DomainHint { get; set; }

    // Request object (JAR - RFC 9101)
    public string? Request { get; set; }
    public string? RequestUri { get; set; }

    // Custom extension: UI mode override
    // Values: "journey" or "standalone"
    public string? UiMode { get; set; }

    // Custom extension: Specific journey policy ID
    // Allows clients to request a specific journey (e.g., "signup", "signin-mfa")
    public string? Policy { get; set; }

    // Resource indicators (RFC 8707)
    public ICollection<string> Resource { get; set; } = new List<string>();

    // DPoP (RFC 9449)
    public string? DPoPKeyThumbprint { get; set; }

    // PAR reference (RFC 9126)
    public string? RequestUriReference { get; set; }

    // Parsed/validated properties
    public ICollection<string> RequestedScopes { get; set; } = new List<string>();
    public ICollection<string> RequestedResponseTypes { get; set; } = new List<string>();
    public bool IsOpenIdRequest => RequestedScopes.Contains(OidcConstants.Scopes.OpenId);

    // Session binding
    public string? SessionId { get; set; }
    public string? SubjectId { get; set; }

    // Raw parameters for signature validation
    public IDictionary<string, string> Raw { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// Response types for authorization endpoint
/// </summary>
public static class ResponseTypes
{
    public const string Code = "code";
    public const string Token = "token";
    public const string IdToken = "id_token";

    // Hybrid flows
    public const string CodeIdToken = "code id_token";
    public const string CodeToken = "code token";
    public const string CodeIdTokenToken = "code id_token token";
    public const string IdTokenToken = "id_token token";
}

/// <summary>
/// Response modes
/// </summary>
public static class ResponseModes
{
    public const string Query = "query";
    public const string Fragment = "fragment";
    public const string FormPost = "form_post";
    public const string Jwt = "jwt";
    public const string QueryJwt = "query.jwt";
    public const string FragmentJwt = "fragment.jwt";
    public const string FormPostJwt = "form_post.jwt";
}

/// <summary>
/// Code challenge methods (PKCE)
/// </summary>
public static class CodeChallengeMethods
{
    public const string Plain = "plain";
    public const string Sha256 = "S256";
}

/// <summary>
/// Prompt values
/// </summary>
public static class PromptModes
{
    public const string None = "none";
    public const string Login = "login";
    public const string Consent = "consent";
    public const string SelectAccount = "select_account";
    public const string Create = "create";
}

/// <summary>
/// UI mode values for custom ui_mode parameter
/// </summary>
public static class UiModes
{
    /// <summary>
    /// Use journey-based authentication flow with step handlers
    /// </summary>
    public const string Journey = "journey";

    /// <summary>
    /// Use standalone Razor pages for authentication
    /// </summary>
    public const string Standalone = "standalone";
}
