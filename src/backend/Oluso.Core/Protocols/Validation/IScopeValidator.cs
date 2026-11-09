using Oluso.Core.Common;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Result of scope validation including categorized scopes
/// </summary>
public class ScopeValidationResult : ValidationResult
{
    /// <summary>The scopes that will be granted: the request less any <see cref="DroppedScopes"/>.</summary>
    public ICollection<string> ValidScopes { get; set; } = new List<string>();
    public ICollection<string> IdentityScopes { get; set; } = new List<string>();
    public ICollection<string> ApiScopes { get; set; } = new List<string>();
    public ICollection<string> ApiResources { get; set; } = new List<string>();

    /// <summary>
    /// Requested scopes left out of the grant (RFC 6749 §3.3: the server may partially ignore the requested
    /// scope), with the reason for each. Empty unless the grant is narrower than the request.
    /// </summary>
    public IList<DroppedScope> DroppedScopes { get; set; } = new List<DroppedScope>();

    public bool ContainsOpenIdScope => IdentityScopes.Contains(OidcConstants.Scopes.OpenId);
    public bool ContainsOfflineAccessScope => ValidScopes.Contains(OidcConstants.Scopes.OfflineAccess);
}

/// <summary>A requested scope the server left out of the grant, and why.</summary>
public sealed record DroppedScope(string Scope, string Reason)
{
    /// <summary>The client is not allowed to request the scope.</summary>
    public const string NotAllowed = "not_allowed";

    /// <summary>No identity resource or API scope has the name.</summary>
    public const string Unknown = "unknown";

    /// <summary>The scope needs a tenant feature the routed tenant does not have on.</summary>
    public const string FeatureUnavailable = "feature_unavailable";

    /// <summary>The scope starts with a prefix scope's name but the rest is not a suffix it accepts.</summary>
    public const string InvalidValue = "invalid_value";
}

/// <summary>
/// A scope check: the request, the client's allowed scopes, and who is asking where, so scopes the server
/// leaves out can be logged and audited against the client.
/// </summary>
public sealed class ScopeValidationContext
{
    /// <summary>
    /// Client property that turns partial grants off for one client: a requested scope that would be left out
    /// fails the whole request with <c>invalid_scope</c> instead. Off unless set to <c>true</c>.
    /// </summary>
    public const string StrictScopesProperty = "strict_scopes";

    public required IEnumerable<string> RequestedScopes { get; init; }
    public required IEnumerable<string> ClientAllowedScopes { get; init; }

    /// <summary>The requesting client, for logs and the audit trail.</summary>
    public string? ClientId { get; init; }

    /// <summary>Where the scopes were requested (authorize, par, device_authorization, token, direct).</summary>
    public string? Endpoint { get; init; }

    /// <summary>Refuse the request instead of granting part of it (the client's <see cref="StrictScopesProperty"/>).</summary>
    public bool Strict { get; init; }

    public static bool IsStrict(IEnumerable<KeyValuePair<string, string>>? properties) =>
        properties != null && properties.Any(p => p.Key == StrictScopesProperty && IsTrue(p.Value));

    public static bool IsStrict(IEnumerable<ClientProperty>? properties) =>
        properties != null && properties.Any(p => p.Key == StrictScopesProperty && IsTrue(p.Value));

    private static bool IsTrue(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) || value?.Trim() == "1";
}

/// <summary>
/// Validates OAuth 2.0 scopes
/// </summary>
public interface IScopeValidator
{
    /// <summary>
    /// Validates requested scopes against the client's allowed scopes. A scope the client isn't allowed, that
    /// isn't known, or that the routed tenant doesn't offer is left out of the grant (and listed in
    /// <see cref="ScopeValidationResult.DroppedScopes"/>) unless the request is strict. The request fails with
    /// <c>invalid_scope</c> when a scope token is malformed, when nothing requested is left, or when
    /// <c>openid</c> was asked for and left out.
    /// </summary>
    Task<ScopeValidationResult> ValidateAsync(ScopeValidationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates requested scopes against allowed scopes for the client (no client to audit against, not strict).
    /// </summary>
    Task<ScopeValidationResult> ValidateAsync(
        IEnumerable<string> requestedScopes,
        IEnumerable<string> clientAllowedScopes,
        CancellationToken cancellationToken = default)
        => ValidateAsync(new ScopeValidationContext
        {
            RequestedScopes = requestedScopes,
            ClientAllowedScopes = clientAllowedScopes
        }, cancellationToken);

    /// <summary>
    /// Parses a space-delimited scope string
    /// </summary>
    IEnumerable<string> ParseScopes(string? scopeString);
}
