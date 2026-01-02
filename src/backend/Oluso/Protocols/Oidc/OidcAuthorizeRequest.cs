using Oluso.Core.Protocols;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OIDC authorization request
/// </summary>
public class OidcAuthorizeRequest : IProtocolRequest
{
    public string? ClientId { get; set; }
    public string? PolicyId { get; set; }
    public string? UiMode { get; set; }

    public string? RedirectUri { get; set; }
    public string? ResponseType { get; set; }
    public string? Scope { get; set; }
    public string? State { get; set; }
    public string? Nonce { get; set; }
    public string? ResponseMode { get; set; }

    // OIDC parameters
    public string? Prompt { get; set; }
    public string? MaxAge { get; set; }
    public string? UiLocales { get; set; }
    public string? IdTokenHint { get; set; }
    public string? LoginHint { get; set; }
    public string? AcrValues { get; set; }

    // Domain hint for IdP selection (used with Azure AD, Google Workspace, etc.)
    public string? DomainHint { get; set; }

    // PKCE
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }

    // Request objects (JAR)
    public string? Request { get; set; }
    public string? RequestUri { get; set; }

    // Resource indicators
    public IList<string>? Resource { get; set; }

    // Parsed values
    public IList<string> RequestedScopes { get; set; } = new List<string>();
    public IList<string> RequestedResponseTypes { get; set; } = new List<string>();

    // Raw parameters
    public IDictionary<string, string>? Raw { get; set; }
}
