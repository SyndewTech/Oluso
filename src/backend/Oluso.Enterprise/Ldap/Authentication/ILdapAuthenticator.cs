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

public sealed record LdapConnectionTestResult(bool Success, string Message, IReadOnlyList<string> Details);

public enum LdapPasswordChangeStatus
{
    Changed,
    InvalidCredentials,
    RejectedByPolicy,
    SecureConnectionRequired,
    NotSupported,
    Unavailable
}

public class LdapPasswordChangeResult
{
    public LdapPasswordChangeStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Success => Status == LdapPasswordChangeStatus.Changed;

    public static LdapPasswordChangeResult Changed() => new() { Status = LdapPasswordChangeStatus.Changed };

    public static LdapPasswordChangeResult Failed(LdapPasswordChangeStatus status, string message) =>
        new() { Status = status, ErrorMessage = message };
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
    /// Authenticates a user with username and password using global LDAP configuration.
    /// For per-tenant authentication, use the overload that accepts LdapProviderSettings.
    /// </summary>
    Task<LdapAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a user with username and password using per-tenant LDAP settings.
    /// </summary>
    Task<LdapAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        Configuration.LdapProviderSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a user by username without authentication
    /// </summary>
    Task<LdapUser?> FindUserAsync(
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a user by username without authentication using per-tenant settings
    /// </summary>
    Task<LdapUser?> FindUserAsync(
        string username,
        Configuration.LdapProviderSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a user by distinguished name
    /// </summary>
    Task<LdapUser?> FindUserByDnAsync(
        string distinguishedName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects, binds with the service account (anonymously when none is set) and reads the
    /// base DN. Uses the provider's settings, or the module's directory when null.
    /// </summary>
    Task<LdapConnectionTestResult> TestConnectionAsync(
        Configuration.LdapProviderSettings? settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a user's directory password. The user proves the current password by binding
    /// with it, so no service account needs write access. Uses the provider's settings, or the
    /// module's directory when <paramref name="settings"/> is null.
    /// </summary>
    Task<LdapPasswordChangeResult> ChangePasswordAsync(
        string username,
        string currentPassword,
        string newPassword,
        Configuration.LdapProviderSettings? settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a user exists and is active
    /// </summary>
    Task<bool> ValidateUserAsync(
        string username,
        CancellationToken cancellationToken = default);
}
