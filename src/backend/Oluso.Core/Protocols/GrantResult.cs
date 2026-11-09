using Oluso.Core.Common;

namespace Oluso.Core.Protocols;

/// <summary>
/// Result of grant processing
/// </summary>
public class GrantResult : ValidationResult
{
    /// <summary>
    /// Subject identifier (user ID) for the grant
    /// </summary>
    public string? SubjectId { get; set; }

    /// <summary>
    /// Session ID for the grant
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Claims to include in tokens
    /// </summary>
    public IDictionary<string, object> Claims { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// How the user signed in (<c>amr</c>), for the ID token. Set by the authorization code grant from the code.
    /// </summary>
    public ICollection<string>? Amr { get; set; }

    /// <summary>
    /// Authentication context class reference (<c>acr</c>) of the sign-in, for the ID token.
    /// </summary>
    public string? Acr { get; set; }

    /// <summary>
    /// Scopes granted
    /// </summary>
    public ICollection<string> Scopes { get; set; } = new List<string>();

    /// <summary>
    /// Resource indicators this token is for (RFC 8707): the request's, narrowed from the grant's.
    /// Null when the grant type doesn't track resources; the token request's own then apply.
    /// </summary>
    public ICollection<string>? Resources { get; set; }

    /// <summary>
    /// Resource indicators of the whole grant, kept on a refresh token so later refreshes can
    /// ask for any of them (RFC 8707 §2.2). Null when the grant type doesn't track resources.
    /// </summary>
    public ICollection<string>? GrantedResources { get; set; }

    /// <summary>
    /// Scopes of the whole grant, kept on a rotated refresh token when a refresh asked for fewer (RFC 6749 §6: a
    /// downscoped refresh narrows the access token, never the grant), so later refreshes can still ask for any of them.
    /// Null: the token's own scopes are the grant's.
    /// </summary>
    public ICollection<string>? GrantedScopes { get; set; }

    /// <summary>
    /// Permissions granted to the user based on their roles.
    /// These are separate from claims and represent what the user can do.
    /// Added to tokens as a dedicated "permissions" claim.
    /// </summary>
    public ICollection<string> Permissions { get; set; } = new List<string>();

    /// <summary>
    /// Custom data for the grant
    /// </summary>
    public IDictionary<string, object> CustomData { get; set; } = new Dictionary<string, object>();

    public static GrantResult Success(string? subjectId = null, IEnumerable<string>? scopes = null)
    {
        return new GrantResult
        {
            SubjectId = subjectId,
            Scopes = scopes?.ToList() ?? new List<string>()
        };
    }

    public new static GrantResult Failure(string error, string? description = null) => new()
    {
        Error = error,
        ErrorDescription = description
    };
}
