using System.Security.Claims;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Services;

/// <summary>
/// Abstraction for user operations. Implement this to bring your own user store
/// (LDAP, external API, custom database, etc.)
/// </summary>
/// <remarks>
/// Default implementation uses ASP.NET Core Identity with Entity Framework.
/// To use a custom store, implement this interface and register with:
/// <code>
/// builder.AddUserService&lt;MyCustomUserService&gt;()
/// </code>
/// </remarks>
public interface IOlusoUserService
{
    /// <summary>
    /// Validates user credentials with optional tenant scoping
    /// </summary>
    /// <returns>Result with user info if valid, or error if invalid</returns>
    Task<UserValidationResult> ValidateCredentialsAsync(
        string username,
        string password,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by their unique ID
    /// </summary>
    Task<OlusoUserInfo?> FindByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by email address
    /// </summary>
    Task<OlusoUserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by username
    /// </summary>
    Task<OlusoUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by phone number
    /// </summary>
    Task<OlusoUserInfo?> FindByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if user has a password set (vs. only external logins)
    /// </summary>
    Task<bool> HasPasswordAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets claims for a user (used for token generation)
    /// </summary>
    Task<IEnumerable<Claim>> GetClaimsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles for a user
    /// </summary>
    Task<IEnumerable<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user
    /// </summary>
    Task<UserOperationResult> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing user
    /// </summary>
    Task<UserOperationResult> UpdateUserAsync(
        string userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes user password (requires current password)
    /// </summary>
    Task<UserOperationResult> ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets user password (admin operation or via token)
    /// </summary>
    Task<UserOperationResult> ResetPasswordAsync(
        string userId,
        string newPassword,
        string? resetToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a password reset token
    /// </summary>
    Task<string?> GeneratePasswordResetTokenAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if user has MFA enabled
    /// </summary>
    Task<bool> HasMfaEnabledAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates MFA code
    /// </summary>
    Task<bool> ValidateMfaCodeAsync(string userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful login
    /// </summary>
    Task RecordLoginAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if account is locked
    /// </summary>
    Task<bool> IsLockedOutAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed login attempt (for lockout tracking)
    /// </summary>
    Task RecordFailedLoginAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an external login (creates user if needed, links accounts)
    /// </summary>
    Task<ProcessExternalLoginResult> ProcessExternalLoginAsync(
        string provider,
        System.Security.Claims.ClaimsPrincipal externalUser,
        bool createIfNotExists = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by ID (for admin operations)
    /// </summary>
    Task<OlusoUser?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a paged list of users with optional filtering
    /// </summary>
    Task<UsersQueryResult> GetUsersAsync(
        UsersQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user roles
    /// </summary>
    Task<IEnumerable<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates last login timestamp
    /// </summary>
    Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a user to a role
    /// </summary>
    Task<UserOperationResult> AddToRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a user from a role
    /// </summary>
    Task<UserOperationResult> RemoveFromRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets user roles (replaces existing roles)
    /// </summary>
    Task<UserOperationResult> SetRolesAsync(string userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets email verified status for a user
    /// </summary>
    Task SetEmailVerifiedAsync(string userId, bool verified, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets phone number verified status for a user
    /// </summary>
    Task SetPhoneNumberVerifiedAsync(string userId, bool verified, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates user's email address
    /// </summary>
    Task SetEmailAsync(string userId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates user's phone number
    /// </summary>
    Task SetPhoneNumberAsync(string userId, string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by external login (provider and key)
    /// </summary>
    Task<OlusoUserInfo?> FindByExternalLoginAsync(
        string provider,
        string providerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user from external login with pre-collected information.
    /// Used when auto-provisioning is disabled and user completes registration form.
    /// </summary>
    Task<UserOperationResult> CreateUserFromExternalLoginAsync(
        CreateExternalUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all external logins linked to a user
    /// </summary>
    Task<IEnumerable<ExternalLoginInfo>> GetExternalLoginsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an external login from a user
    /// </summary>
    Task<UserOperationResult> RemoveExternalLoginAsync(
        string userId,
        string provider,
        string providerKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// User entity returned from validation (service layer DTO)
/// </summary>
public class ValidatedUser
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? TenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>
/// User information returned from the user service
/// </summary>
public class OlusoUserInfo
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public bool EmailVerified { get; init; }
    public string? PhoneNumber { get; init; }
    public bool PhoneNumberVerified { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DisplayName { get; init; }
    public string? Picture { get; init; }
    public string? TenantId { get; init; }
    public bool IsActive { get; init; } = true;
    public bool TwoFactorEnabled { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public IEnumerable<string>? Roles { get; init; }
    public IDictionary<string, string>? CustomProperties { get; init; }
}

/// <summary>
/// Result of credential validation
/// </summary>
public class UserValidationResult
{
    public bool Success { get; init; }
    public bool Succeeded => Success;
    public ValidatedUser? User { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public bool RequiresMfa { get; init; }
    public bool IsLockedOut { get; init; }
    public bool IsNotAllowed { get; init; }
    public bool RequiresTenantQualifier { get; init; }
    public List<TenantOption>? AvailableTenants { get; init; }

    public static UserValidationResult Successful(ValidatedUser user) =>
        new() { Success = true, User = user };

    public static UserValidationResult Failed(string error, string? description = null) =>
        new() { Success = false, Error = error, ErrorDescription = description };

    public static UserValidationResult LockedOut() =>
        new() { Success = false, IsLockedOut = true, Error = "account_locked" };

    public static UserValidationResult MfaRequired(ValidatedUser user) =>
        new() { Success = false, RequiresMfa = true, User = user };

    public static UserValidationResult MultipleAccountsFound(List<TenantOption> tenants) =>
        new() { Success = false, RequiresTenantQualifier = true, AvailableTenants = tenants, Error = "multiple_accounts" };
}

public class TenantOption
{
    public string Identifier { get; set; } = null!;
    public string? DisplayName { get; set; }
}

/// <summary>
/// Result of user operations (create, update, etc.)
/// </summary>
public class UserOperationResult
{
    public bool Succeeded { get; init; }
    public string? UserId { get; init; }
    public OlusoUserInfo? User { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public IEnumerable<string>? Errors { get; init; }

    public static UserOperationResult Success(string userId, OlusoUserInfo? user = null) =>
        new() { Succeeded = true, UserId = userId, User = user };

    public static UserOperationResult Failed(string error, string? description = null) =>
        new() { Succeeded = false, Error = error, ErrorDescription = description };

    public static UserOperationResult Failed(params string[] errors) =>
        new() { Succeeded = false, Errors = errors, Error = errors.FirstOrDefault() };
}

/// <summary>
/// Request to create a new user
/// </summary>
public class CreateUserRequest
{
    public string? Username { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? TenantId { get; init; }
    public bool RequireEmailVerification { get; init; } = true;
    public IDictionary<string, string>? CustomProperties { get; init; }
}

/// <summary>
/// Request to update a user
/// </summary>
public class UpdateUserRequest
{
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Picture { get; init; }
    public IDictionary<string, string>? CustomProperties { get; init; }
}

/// <summary>
/// Request to create a user from external login (no password required)
/// </summary>
public class CreateExternalUserRequest
{
    public required string Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public required string Provider { get; init; }
    public required string ProviderKey { get; init; }
    public bool EmailConfirmed { get; init; } = true;
    public string? TenantId { get; init; }
}

/// <summary>
/// Result of processing an external login (finding/creating user)
/// </summary>
public class ProcessExternalLoginResult
{
    public bool Succeeded { get; init; }
    public string? UserId { get; init; }
    public OlusoUserInfo? User { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public bool IsNewUser { get; init; }

    public static ProcessExternalLoginResult Success(string userId, OlusoUserInfo? user = null, bool isNewUser = false) =>
        new() { Succeeded = true, UserId = userId, User = user, IsNewUser = isNewUser };

    public static ProcessExternalLoginResult Failed(string error, string? description = null) =>
        new() { Succeeded = false, Error = error, ErrorDescription = description };
}

/// <summary>
/// Query parameters for listing users
/// </summary>
public class UsersQuery
{
    public string? Search { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
    public string? TenantId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Result of a users query
/// </summary>
public class UsersQueryResult
{
    public IReadOnlyList<OlusoUserInfo> Users { get; init; } = Array.Empty<OlusoUserInfo>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
