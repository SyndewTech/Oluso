using Oluso.Core.Common;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Result of scope validation including categorized scopes
/// </summary>
public class ScopeValidationResult : ValidationResult
{
    public ICollection<string> ValidScopes { get; set; } = new List<string>();
    public ICollection<string> IdentityScopes { get; set; } = new List<string>();
    public ICollection<string> ApiScopes { get; set; } = new List<string>();
    public ICollection<string> ApiResources { get; set; } = new List<string>();
    public bool ContainsOpenIdScope => IdentityScopes.Contains(OidcConstants.Scopes.OpenId);
    public bool ContainsOfflineAccessScope => ValidScopes.Contains(OidcConstants.Scopes.OfflineAccess);
}

/// <summary>
/// Validates OAuth 2.0 scopes
/// </summary>
public interface IScopeValidator
{
    /// <summary>
    /// Validates requested scopes against allowed scopes for the client
    /// </summary>
    Task<ScopeValidationResult> ValidateAsync(
        IEnumerable<string> requestedScopes,
        IEnumerable<string> clientAllowedScopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a space-delimited scope string
    /// </summary>
    IEnumerable<string> ParseScopes(string? scopeString);
}
