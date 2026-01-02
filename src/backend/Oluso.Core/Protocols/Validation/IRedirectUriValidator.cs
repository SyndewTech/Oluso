using Oluso.Core.Common;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Validates redirect URIs for OAuth 2.0/OIDC flows
/// </summary>
public interface IRedirectUriValidator
{
    /// <summary>
    /// Validates that a redirect_uri is allowed for the client
    /// </summary>
    Task<ValidationResult> ValidateAsync(
        string? redirectUri,
        IEnumerable<string> allowedRedirectUris,
        bool isImplicitOrHybridFlow = false);

    /// <summary>
    /// Validates a post_logout_redirect_uri
    /// </summary>
    Task<ValidationResult> ValidatePostLogoutAsync(
        string? postLogoutRedirectUri,
        IEnumerable<string> allowedPostLogoutRedirectUris);

    /// <summary>
    /// Determines if a URI is a native client redirect (custom scheme or loopback)
    /// </summary>
    bool IsNativeClient(string uri);
}
