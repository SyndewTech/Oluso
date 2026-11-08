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
/// The service owns the user lifecycle events: whatever creates, updates,
/// deactivates, verifies or deletes an account through it gets
/// <c>user.created</c>, <c>user.updated</c>, <c>user.deactivated</c>,
/// <c>user.reactivated</c>, <c>user.email_verified</c> or <c>user.deleted</c>
/// raised once, so callers don't raise them and a new path can't forget them.
/// A custom implementation must do the same, or webhooks, audit and identity
/// sync never hear about its users.
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
    /// Finds a user who belongs to <paramref name="tenantId"/>, either as their
    /// home tenant or through a TenantMembership. Returns null when they don't.
    /// <para>
    /// <see cref="FindByIdAsync"/> resolves under the ambient tenant filter, so
    /// it can't see someone whose home tenant is elsewhere — even when they were
    /// deliberately granted access to this one. Admin screens that list tenant
    /// members and then act on the selection need this instead.
    /// </para>
    /// </summary>
    Task<OlusoUserInfo?> FindByIdInTenantAsync(
        string userId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-resolve users by id, keyed by id, in a single store query
    /// (<c>WHERE Id IN (…)</c>). Use this whenever you need to annotate a
    /// collection with user info (e.g. approver names) — never loop or
    /// <c>Task.WhenAll</c> over <see cref="FindByIdAsync"/>, which would run
    /// concurrent queries on the shared scoped DbContext and trip EF Core's
    /// concurrency detector.
    /// </summary>
    Task<IReadOnlyDictionary<string, OlusoUserInfo>> FindByIdsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-resolve users by id regardless of their home tenant. Only for ids
    /// that server-side state has already scoped (role holders in a tenant,
    /// organization admins) — never for ids taken from a request, since it
    /// bypasses the tenant boundary <see cref="FindByIdsAsync"/> applies.
    /// </summary>
    async Task<IReadOnlyDictionary<string, OlusoUserInfo>> FindByIdsAcrossTenantsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, OlusoUserInfo>(StringComparer.Ordinal);
        foreach (var id in userIds.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal))
        {
            var user = await FindByIdAsync(id, cancellationToken);
            if (user != null) result[id] = user;
        }
        return result;
    }

    /// <summary>
    /// Finds a user by email address
    /// </summary>
    Task<OlusoUserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the user with <paramref name="email"/> who belongs to
    /// <paramref name="tenantId"/>, as their home tenant or through a
    /// TenantMembership. The tenant's own account wins when a member from another
    /// tenant shares the address; among members only, the oldest account.
    /// Returns null when no account of the tenant has the address.
    /// </summary>
    Task<OlusoUserInfo?> FindByEmailInTenantAsync(
        string email, string tenantId, CancellationToken cancellationToken = default);

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
    /// Creates a new user and raises <c>user.created</c>.
    /// </summary>
    Task<UserOperationResult> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing user. Changing the email (other than its casing)
    /// marks it unverified. Raises <c>user.updated</c> naming the fields that
    /// changed, nothing when none did, and <c>user.email_verified</c> when
    /// <see cref="UpdateUserRequest.EmailVerified"/> confirms an address.
    /// </summary>
    Task<UserOperationResult> UpdateUserAsync(
        string userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes the account with everything the identity store keeps
    /// for it: role assignments, claims, external logins, MFA tokens, password
    /// history, tenant/organization/team memberships, module seats, grants,
    /// refresh tokens, sessions and consents. Audit history is kept. Every
    /// registered <see cref="IUserDeletionParticipant"/> then removes what its
    /// module stores.
    /// <para>
    /// Refused with <see cref="UserDeletionErrors.ProtectedIdentity"/> for a
    /// platform identity (home tenant is the platform tenant, or holds
    /// SuperAdmin/SystemAdmin), and with
    /// <see cref="UserDeletionErrors.LastOrganizationOwner"/> while the user is
    /// the only owner of an organization. Callers decide who may delete whom.
    /// </para>
    /// Raises <c>user.deleted</c> once the account is gone.
    /// </summary>
    Task<UserOperationResult> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);

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
    /// Ends the user's lockout now and clears their failed-attempt count. False if the user
    /// doesn't exist or the change was rejected.
    /// </summary>
    Task<bool> UnlockAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the user out until <paramref name="lockoutEnd"/>, or until unlocked when null.
    /// False if the user doesn't exist or the change was rejected.
    /// </summary>
    Task<bool> LockAsync(string userId, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an external login (creates user if needed, links accounts).
    /// A created user raises <c>user.created</c> naming <paramref name="clientId"/>,
    /// the client the sign-in is for.
    /// </summary>
    Task<ProcessExternalLoginResult> ProcessExternalLoginAsync(
        string provider,
        System.Security.Claims.ClaimsPrincipal externalUser,
        bool createIfNotExists = true,
        string? clientId = null,
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
    /// Replaces the roles the user holds within the current tenant scope with
    /// exactly the given role rows. Every id must be a row of that tenant or a
    /// platform-wide row; roles held in other tenants are left untouched.
    /// </summary>
    Task<UserOperationResult> SetRolesByIdAsync(string userId, IEnumerable<string> roleIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets email verified status for a user. Raises <c>user.email_verified</c>
    /// when an unverified address becomes verified, not when it already was.
    /// </summary>
    Task SetEmailVerifiedAsync(string userId, bool verified, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets phone number verified status for a user
    /// </summary>
    Task SetPhoneNumberVerifiedAsync(string userId, bool verified, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates user's email address, raising <c>user.updated</c> when it changes.
    /// </summary>
    Task SetEmailAsync(string userId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates user's phone number, raising <c>user.updated</c> when it changes.
    /// </summary>
    Task SetPhoneNumberAsync(string userId, string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the user's active status. When set to false, the user cannot log in.
    /// Raises <c>user.deactivated</c> or <c>user.reactivated</c> when the status
    /// actually changes.
    /// </summary>
    Task SetUserActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default);

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

    /// <summary>Whether the account's email address has been proven. A password says nothing about it.</summary>
    public bool EmailVerified { get; set; }
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
    public bool LockoutEnabled { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public int AccessFailedCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }

    /// <summary>When the local password was last set; null when never recorded.</summary>
    public DateTime? PasswordChangedAt { get; init; }
    public IEnumerable<string>? Roles { get; init; }
    public IDictionary<string, string>? CustomProperties { get; init; }

    /// <summary>
    /// True when the user has at least one linked external login (e.g. Google,
    /// Azure AD, SAML). Orthogonal to <see cref="HasPassword"/> — a user can
    /// have both (mixed-mode), just one, or neither.
    /// </summary>
    public bool IsFederated { get; init; }

    /// <summary>
    /// Provider names of every external login linked to this user (e.g.
    /// <c>["Google","AzureAD"]</c>). Empty when <see cref="IsFederated"/> is
    /// false. Useful for the admin UI to surface which IdP(s) own the
    /// account.
    /// </summary>
    public IReadOnlyList<string> LinkedProviders { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when the user has a local password set. Users with a password
    /// own a credential they manage themselves — linking SSO later doesn't
    /// revoke ownership. The admin UI uses this (combined with
    /// <see cref="IsFederated"/>) to decide whether the "Reset Password"
    /// control should be shown.
    /// </summary>
    public bool HasPassword { get; init; }
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
    /// <summary>
    /// The email matched a usable account in more than one organization, so the
    /// caller must ask which one before a token can be issued.
    /// </summary>
    public bool RequiresOrganizationChoice { get; init; }

    /// <summary>
    /// Organizations the supplied credentials unlocked. Only populated alongside
    /// <see cref="RequiresOrganizationChoice"/> — never disclosed before the
    /// password has been proven, or the endpoint becomes an org-membership oracle
    /// for any address.
    /// </summary>
    public List<OrganizationOption>? AvailableOrganizations { get; init; }

    /// <summary>
    /// The password was correct but is older than the tenant's expiry allows. Not a success:
    /// no session or token may be issued until the user sets a new password. <see cref="User"/>
    /// is populated so the caller can route the user through a password change.
    /// </summary>
    public bool PasswordExpired { get; init; }

    /// <summary>
    /// True when the password matched, including an expired one. For re-verifying a signed-in
    /// user's password (sensitive account changes, current-password checks) — never as a
    /// sign-in decision; use <see cref="Succeeded"/> for that.
    /// </summary>
    public bool CredentialsVerified => Success || PasswordExpired;

    public static UserValidationResult Successful(ValidatedUser user) =>
        new() { Success = true, User = user };

    public static UserValidationResult PasswordChangeRequired(ValidatedUser user) =>
        new()
        {
            Success = false,
            PasswordExpired = true,
            User = user,
            Error = "password_expired",
            ErrorDescription = Locale.UiStringDefaults.Resolve("password_expired_signin_error")
        };

    public static UserValidationResult Failed(string error, string? description = null) =>
        new() { Success = false, Error = error, ErrorDescription = description };

    public static UserValidationResult LockedOut() =>
        new() { Success = false, IsLockedOut = true, Error = "account_locked" };

    public static UserValidationResult MfaRequired(ValidatedUser user) =>
        new() { Success = false, RequiresMfa = true, User = user };

    public static UserValidationResult MultipleAccountsFound(List<OrganizationOption> organizations) =>
        new()
        {
            Success = false,
            RequiresOrganizationChoice = true,
            AvailableOrganizations = organizations,
            Error = "multiple_accounts"
        };
}

/// <summary>
/// An organization the user can sign in to. The organization is the identity
/// boundary — one account row per (organization, email) — so this is what
/// distinguishes the accounts behind a shared email address.
/// </summary>
public class OrganizationOption
{
    /// <summary>Platform-unique slug, also usable as the <c>email@slug</c> login qualifier.</summary>
    public string Slug { get; set; } = null!;

    public string Name { get; set; } = null!;
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

    /// <summary>
    /// What to tell a person about the failure: the description of a coded failure, else the
    /// identity errors, else the error itself. <see cref="Error"/> alone may be a code
    /// (e.g. <c>email_domain_not_allowed</c>), so show this instead.
    /// </summary>
    public string? Message =>
        !string.IsNullOrWhiteSpace(ErrorDescription) ? ErrorDescription
        : Errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() is { Count: > 0 } list ? string.Join(" ", list)
        : Error;

    /// <summary><see cref="Message"/>, or <paramref name="fallback"/> when there is none.</summary>
    public string MessageOr(string fallback) => string.IsNullOrWhiteSpace(Message) ? fallback : Message!;

    /// <summary>
    /// The failure as a list for API responses: each identity error, else <see cref="Message"/>,
    /// else <paramref name="fallback"/>.
    /// </summary>
    public IReadOnlyList<string> MessagesOr(string fallback) =>
        Errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() is { Count: > 0 } list && string.IsNullOrWhiteSpace(ErrorDescription)
            ? list
            : new[] { MessageOr(fallback) };

    public static UserOperationResult Success(string userId, OlusoUserInfo? user = null) =>
        new() { Succeeded = true, UserId = userId, User = user };

    public static UserOperationResult Failed(string error, string? description = null) =>
        new() { Succeeded = false, Error = error, ErrorDescription = description };

    public static UserOperationResult Failed(params string[] errors) =>
        new() { Succeeded = false, Errors = errors, Error = errors.FirstOrDefault() };
}

/// <summary>
/// <see cref="UserOperationResult.Error"/> codes returned by
/// <see cref="IOlusoUserService.DeleteUserAsync"/>.
/// </summary>
public static class UserDeletionErrors
{
    public const string NotFound = "user_not_found";
    public const string ProtectedIdentity = "protected_identity";
    public const string LastOrganizationOwner = "last_organization_owner";
}

/// <summary>
/// Removes what a module stores about a user outside the identity store (passkeys,
/// provisioning mappings) once <see cref="IOlusoUserService.DeleteUserAsync"/> has
/// deleted the account.
/// </summary>
public interface IUserDeletionParticipant
{
    Task DeleteUserDataAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create a new user
/// </summary>
public class CreateUserRequest
{
    public string? Username { get; init; }
    public required string Email { get; init; }
    public string? Password { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? TenantId { get; init; }
    public bool RequireEmailVerification { get; init; } = true;
    public IDictionary<string, string>? CustomProperties { get; init; }

    /// <summary>The OAuth client the user signed up through, carried on <c>user.created</c>.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// The user journey policy the user signed up through, carried on
    /// <c>user.created</c>. When unset, a create inside a running journey takes
    /// the journey's policy from the event origin.
    /// </summary>
    public string? PolicyId { get; init; }
}

/// <summary>
/// Request to update a user
/// </summary>
public class UpdateUserRequest
{
    public string? Email { get; init; }

    /// <summary>
    /// Records <see cref="Email"/> as verified. Only for a caller that has just had the
    /// user prove they own the new address; without it a changed email is unverified.
    /// </summary>
    public bool EmailVerified { get; init; }

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

    /// <summary>The OAuth client the user signed up through, carried on <c>user.created</c>.</summary>
    public string? ClientId { get; init; }
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

    /// <summary>
    /// <see cref="AccountLinkRequired"/>: the account the sign-in's email belongs to, which the person must prove is
    /// theirs (a code sent to its address) before the external login is linked to it.
    /// </summary>
    public string? ExistingUserId { get; init; }

    /// <summary>The error when an account has the sign-in's email but the provider can't vouch for it.</summary>
    public const string AccountLinkRequired = "account_link_required";

    public static ProcessExternalLoginResult LinkRequired(string existingUserId) =>
        new()
        {
            Succeeded = false,
            Error = AccountLinkRequired,
            ErrorDescription = "An account with this email already exists. Confirm it's yours to link this sign-in.",
            ExistingUserId = existingUserId
        };

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
    public string? FeatureKey { get; init; }
    public bool? IsActive { get; init; }
    public string? TenantId { get; init; }

    /// <summary>
    /// Restricts the search to a specific set of tenants, for callers whose
    /// scope spans more than one — an organization admin picking from every
    /// tenant they administer, for example. Takes precedence over
    /// <see cref="TenantId"/>. An empty list matches nothing rather than
    /// everything, so a caller with no tenants can't fall through to a
    /// platform-wide search.
    /// </summary>
    public IReadOnlyCollection<string>? TenantIds { get; init; }

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
