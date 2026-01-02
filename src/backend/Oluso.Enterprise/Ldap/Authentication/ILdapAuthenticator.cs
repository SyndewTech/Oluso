using System.Security.Claims;

namespace Oluso.Enterprise.Ldap.Authentication;

/// <summary>
/// Result of an LDAP authentication attempt
/// </summary>
public class LdapAuthenticationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public LdapUser? User { get; set; }
    public IReadOnlyList<Claim> Claims { get; set; } = Array.Empty<Claim>();

    public static LdapAuthenticationResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };

    public static LdapAuthenticationResult Succeeded(LdapUser user, IReadOnlyList<Claim> claims) => new()
    {
        Success = true,
        User = user,
        Claims = claims
    };
}

/// <summary>
/// Represents a user from LDAP directory
/// </summary>
public class LdapUser
{
    public string DistinguishedName { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public IReadOnlyList<string> Groups { get; set; } = Array.Empty<string>();
    public Dictionary<string, string[]> Attributes { get; set; } = new();
}

/// <summary>
/// Service for authenticating users against LDAP directory
/// </summary>
public interface ILdapAuthenticator
{
    /// <summary>
    /// Authenticates a user with username and password
    /// </summary>
    Task<LdapAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a user by username without authentication
    /// </summary>
    Task<LdapUser?> FindUserAsync(
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a user by distinguished name
    /// </summary>
    Task<LdapUser?> FindUserByDnAsync(
        string distinguishedName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a user exists and is active
    /// </summary>
    Task<bool> ValidateUserAsync(
        string username,
        CancellationToken cancellationToken = default);
}
