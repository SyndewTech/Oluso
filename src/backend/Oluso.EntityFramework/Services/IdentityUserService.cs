using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.EntityFramework.Services;

/// <summary>
/// Default IOlusoUserService implementation using ASP.NET Core Identity.
/// This is automatically registered when using AddEntityFrameworkStores().
/// </summary>
public class IdentityUserService : IOlusoUserService
{
    private readonly UserManager<OlusoUser> _userManager;
    private readonly SignInManager<OlusoUser> _signInManager;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<IdentityUserService> _logger;

    public IdentityUserService(
        UserManager<OlusoUser> userManager,
        SignInManager<OlusoUser> signInManager,
        ITenantContext tenantContext,
        ILogger<IdentityUserService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<UserValidationResult> ValidateCredentialsAsync(
        string username,
        string password,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // If tenantId is provided, use it; otherwise use current context
        var effectiveTenantId = tenantId ?? _tenantContext.TenantId;

        var user = await FindUserByUsernameOrEmailAsync(username, effectiveTenantId);
        if (user == null)
        {
            return UserValidationResult.Failed("invalid_credentials", "Invalid username or password");
        }

        if (!user.IsActive)
        {
            return UserValidationResult.Failed("account_disabled", "Account is disabled");
        }

        // Check lockout
        if (await _userManager.IsLockedOutAsync(user))
        {
            return UserValidationResult.LockedOut();
        }

        // Validate password
        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return UserValidationResult.LockedOut();
        }

        if (result.RequiresTwoFactor)
        {
            return UserValidationResult.MfaRequired(MapToValidatedUser(user));
        }

        if (!result.Succeeded)
        {
            return UserValidationResult.Failed("invalid_credentials", "Invalid username or password");
        }

        return UserValidationResult.Successful(MapToValidatedUser(user));
    }

    public async Task<OlusoUserInfo?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user != null ? MapToUserInfo(user) : null;
    }

    public async Task<OlusoUserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await FindUserInTenantByEmailAsync(email);
        return user != null ? MapToUserInfo(user) : null;
    }

    public async Task<OlusoUserInfo?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await FindUserInTenantByUsernameAsync(username);
        return user != null ? MapToUserInfo(user) : null;
    }

    public async Task<IEnumerable<Claim>> GetClaimsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return Enumerable.Empty<Claim>();

        var claims = await _userManager.GetClaimsAsync(user);
        var roles = await _userManager.GetRolesAsync(user);

        // Add role claims
        var allClaims = claims.ToList();
        foreach (var role in roles)
        {
            allClaims.Add(new Claim(ClaimTypes.Role, role));
        }

        // Add user ID claim (required for cookie authentication)
        allClaims.Add(new Claim(ClaimTypes.NameIdentifier, user.Id));
        allClaims.Add(new Claim("sub", user.Id));

        // Add standard claims
        if (!string.IsNullOrEmpty(user.Email))
            allClaims.Add(new Claim(ClaimTypes.Email, user.Email));

        if (!string.IsNullOrEmpty(user.FirstName))
            allClaims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));

        if (!string.IsNullOrEmpty(user.LastName))
            allClaims.Add(new Claim(ClaimTypes.Surname, user.LastName));

        if (!string.IsNullOrEmpty(user.DisplayName))
            allClaims.Add(new Claim("name", user.DisplayName));

        return allClaims;
    }

    public async Task<IEnumerable<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return Enumerable.Empty<string>();

        return await _userManager.GetRolesAsync(user);
    }

    public async Task<UserOperationResult> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = new OlusoUser
        {
            UserName = request.Username,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            TenantId = request.TenantId ?? _tenantContext.TenantId,
            DisplayName = $"{request.FirstName} {request.LastName}".Trim(),
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        _logger.LogInformation("Created user {UserId} in tenant {TenantId}", user.Id, user.TenantId);
        return UserOperationResult.Success(user.Id);
    }

    public async Task<UserOperationResult> UpdateUserAsync(
        string userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        if (request.Email != null) user.Email = request.Email;
        if (request.FirstName != null) user.FirstName = request.FirstName;
        if (request.LastName != null) user.LastName = request.LastName;
        if (request.PhoneNumber != null) user.PhoneNumber = request.PhoneNumber;
        if (request.Picture != null) user.ProfilePictureUrl = request.Picture;

        if (request.FirstName != null || request.LastName != null)
        {
            user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
        }

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        return UserOperationResult.Success(userId);
    }

    public async Task<UserOperationResult> ChangePasswordAsync(
        string userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);

        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        return UserOperationResult.Success(userId);
    }

    public async Task<UserOperationResult> ResetPasswordAsync(
        string userId,
        string newPassword,
        string? resetToken = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        IdentityResult result;

        if (!string.IsNullOrEmpty(resetToken))
        {
            result = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);
        }
        else
        {
            // Admin reset - remove old password and set new one
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        }

        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        return UserOperationResult.Success(userId);
    }

    public async Task<string?> GeneratePasswordResetTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        return await _userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<bool> HasMfaEnabledAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        return await _userManager.GetTwoFactorEnabledAsync(user);
    }

    public async Task<bool> ValidateMfaCodeAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        // Try TOTP first
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);

        return isValid;
    }

    public async Task RecordLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return;

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Reset lockout on successful login
        await _userManager.ResetAccessFailedCountAsync(user);
    }

    public async Task<bool> IsLockedOutAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return true;

        return await _userManager.IsLockedOutAsync(user);
    }

    public async Task<OlusoUserInfo?> FindByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // UserManager doesn't have FindByPhoneAsync, so we need to search manually
        // This requires access to the underlying store
        var normalizedPhone = NormalizePhoneNumber(phoneNumber);

        // Try to find user by phone number - this is a simplified implementation
        // In production, you might want to use a custom UserManager with phone lookup
        var users = _userManager.Users
            .Where(u => u.PhoneNumber != null && u.PhoneNumber.Contains(normalizedPhone.TrimStart('+')));

        var user = users.FirstOrDefault();

        if (user != null && _tenantContext.HasTenant)
        {
            if (user.TenantId != _tenantContext.TenantId && user.TenantId != null)
            {
                return null;
            }
        }

        return user != null ? MapToUserInfo(user) : null;
    }

    public async Task<bool> HasPasswordAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        return await _userManager.HasPasswordAsync(user);
    }

    public async Task RecordFailedLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return;

        await _userManager.AccessFailedAsync(user);
    }

    public async Task<ProcessExternalLoginResult> ProcessExternalLoginAsync(
        string provider,
        ClaimsPrincipal externalUser,
        bool createIfNotExists = true,
        CancellationToken cancellationToken = default)
    {
        var providerKey = externalUser.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(providerKey))
        {
            return ProcessExternalLoginResult.Failed("invalid_external_login", "No provider key found in external claims");
        }

        // Try to find existing login
        var user = await _userManager.FindByLoginAsync(provider, providerKey);

        if (user != null)
        {
            // Existing user with this external login
            _logger.LogDebug("Found existing user {UserId} for provider {Provider}", user.Id, provider);
            return ProcessExternalLoginResult.Success(user.Id, MapToUserInfo(user), isNewUser: false);
        }

        // Try to find by email - check multiple claim types as different IdPs use different formats
        var email = externalUser.FindFirst(ClaimTypes.Email)?.Value
            ?? externalUser.FindFirst("email")?.Value
            ?? externalUser.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value
            ?? externalUser.FindFirst("urn:oid:0.9.2342.19200300.100.1.3")?.Value; // eduPerson mail OID

        if (!string.IsNullOrEmpty(email))
        {
            user = await FindUserInTenantByEmailAsync(email);
        }

        if (user != null)
        {
            // Link the external login to existing account
            var loginInfo = new UserLoginInfo(provider, providerKey, provider);
            var addLoginResult = await _userManager.AddLoginAsync(user, loginInfo);

            if (!addLoginResult.Succeeded)
            {
                _logger.LogWarning("Failed to link external login for user {UserId}: {Errors}",
                    user.Id, string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
                return ProcessExternalLoginResult.Failed("link_failed", "Failed to link external account");
            }

            _logger.LogInformation("Linked provider {Provider} to user {UserId}", provider, user.Id);
            return ProcessExternalLoginResult.Success(user.Id, MapToUserInfo(user), isNewUser: false);
        }

        // No existing user found
        if (!createIfNotExists)
        {
            return ProcessExternalLoginResult.Failed("user_not_found", "No account found for this external login");
        }

        // If email is required but missing, return error to redirect to registration page
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogDebug("External login has no email, redirecting to registration for provider {Provider}", provider);
            return ProcessExternalLoginResult.Failed("email_required", "Email address is required to complete registration");
        }

        // Create new user
        var firstName = externalUser.FindFirst(ClaimTypes.GivenName)?.Value
            ?? externalUser.FindFirst("given_name")?.Value
            ?? externalUser.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname")?.Value;
        var lastName = externalUser.FindFirst(ClaimTypes.Surname)?.Value
            ?? externalUser.FindFirst("family_name")?.Value
            ?? externalUser.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname")?.Value;
        var name = externalUser.FindFirst("name")?.Value ?? externalUser.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(name))
        {
            var nameParts = name.Split(' ', 2);
            firstName = nameParts[0];
            lastName = nameParts.Length > 1 ? nameParts[1] : null;
        }

        var newUser = new OlusoUser
        {
            UserName = email ?? $"{provider}_{providerKey}",
            Email = email,
            EmailConfirmed = !string.IsNullOrEmpty(email), // Trust email from external provider
            FirstName = firstName,
            LastName = lastName,
            DisplayName = name ?? $"{firstName} {lastName}".Trim(),
            TenantId = _tenantContext.TenantId,
            IsActive = true
        };

        var createResult = await _userManager.CreateAsync(newUser);
        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Failed to create user from external login: {Errors}",
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return ProcessExternalLoginResult.Failed("create_failed", "Failed to create account");
        }

        // Link external login
        var externalLoginInfo = new UserLoginInfo(provider, providerKey, provider);
        await _userManager.AddLoginAsync(newUser, externalLoginInfo);

        _logger.LogInformation("Created new user {UserId} from provider {Provider}", newUser.Id, provider);
        return ProcessExternalLoginResult.Success(newUser.Id, MapToUserInfo(newUser), isNewUser: true);
    }

    async Task<string> IOlusoUserService.GeneratePasswordResetTokenAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return string.Empty;

        return await _userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<OlusoUser?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _userManager.FindByIdAsync(userId);
    }

    public async Task<IEnumerable<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return Enumerable.Empty<string>();

        return await _userManager.GetRolesAsync(user);
    }

    public async Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        }
    }

    public async Task<UserOperationResult> AddToRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        var result = await _userManager.AddToRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        _logger.LogInformation("Added user {UserId} to role {RoleName}", userId, roleName);
        return UserOperationResult.Success(userId);
    }

    public async Task<UserOperationResult> RemoveFromRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        var result = await _userManager.RemoveFromRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        _logger.LogInformation("Removed user {UserId} from role {RoleName}", userId, roleName);
        return UserOperationResult.Success(userId);
    }

    public async Task<UserOperationResult> SetRolesAsync(string userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        // Get current roles
        var currentRoles = await _userManager.GetRolesAsync(user);

        // Remove roles that are not in the new list
        var rolesToRemove = currentRoles.Except(roleNames).ToList();
        if (rolesToRemove.Any())
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return UserOperationResult.Failed(removeResult.Errors.Select(e => e.Description).ToArray());
            }
        }

        // Add new roles that are not in the current list
        var rolesToAdd = roleNames.Except(currentRoles).ToList();
        if (rolesToAdd.Any())
        {
            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return UserOperationResult.Failed(addResult.Errors.Select(e => e.Description).ToArray());
            }
        }

        _logger.LogInformation("Set roles for user {UserId}: {Roles}", userId, string.Join(", ", roleNames));
        return UserOperationResult.Success(userId);
    }

    public async Task<UsersQueryResult> GetUsersAsync(
        UsersQuery query,
        CancellationToken cancellationToken = default)
    {
        var effectiveTenantId = query.TenantId ?? _tenantContext.TenantId;

        // Start with all users
        IQueryable<OlusoUser> usersQuery = _userManager.Users;

        // Filter by tenant
        if (!string.IsNullOrEmpty(effectiveTenantId))
        {
            usersQuery = usersQuery.Where(u => u.TenantId == effectiveTenantId);
        }

        // Filter by active status
        if (query.IsActive.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.IsActive == query.IsActive.Value);
        }

        // Search filter (username, email, first name, last name)
        if (!string.IsNullOrEmpty(query.Search))
        {
            var search = query.Search.ToLower();
            usersQuery = usersQuery.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(search)) ||
                (u.Email != null && u.Email.ToLower().Contains(search)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(search)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(search)) ||
                (u.DisplayName != null && u.DisplayName.ToLower().Contains(search)));
        }

        // Get total count before pagination
        var totalCount = usersQuery.Count();

        // Order by last login (most recent first), then by created date
        usersQuery = usersQuery
            .OrderByDescending(u => u.LastLoginAt)
            .ThenByDescending(u => u.Id);

        // Apply pagination
        var skip = (query.Page - 1) * query.PageSize;
        var users = usersQuery
            .Skip(skip)
            .Take(query.PageSize)
            .ToList();

        // Map to user info and filter by role if needed
        var userInfos = new List<OlusoUserInfo>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);

            // Filter by role if specified
            if (!string.IsNullOrEmpty(query.Role) && !roles.Contains(query.Role))
            {
                continue;
            }

            userInfos.Add(new OlusoUserInfo
            {
                Id = user.Id,
                Username = user.UserName ?? "",
                Email = user.Email,
                EmailVerified = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberVerified = user.PhoneNumberConfirmed,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                Picture = user.ProfilePictureUrl,
                TenantId = user.TenantId,
                IsActive = user.IsActive,
                TwoFactorEnabled = user.TwoFactorEnabled,
                LastLoginAt = user.LastLoginAt,
                Roles = roles
            });
        }

        return new UsersQueryResult
        {
            Users = userInfos,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    // Helper methods

    private async Task<OlusoUser?> FindUserByUsernameOrEmailAsync(string usernameOrEmail, string? tenantId = null)
    {
        // Try username first
        var user = await FindUserInTenantByUsernameAsync(usernameOrEmail);
        if (user != null) return user;

        // Try email
        return await FindUserInTenantByEmailAsync(usernameOrEmail);
    }

    private async Task<OlusoUser?> FindUserInTenantByUsernameAsync(string username)
    {
        // UserManager.FindByNameAsync should be tenant-aware if using TenantUserManager
        // Otherwise, we need to filter manually
        var user = await _userManager.FindByNameAsync(username);

        if (user != null && _tenantContext.HasTenant)
        {
            // Verify tenant match (or system user)
            if (user.TenantId != _tenantContext.TenantId && user.TenantId != null)
            {
                return null;
            }
        }

        return user;
    }

    private async Task<OlusoUser?> FindUserInTenantByEmailAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);

        if (user != null && _tenantContext.HasTenant)
        {
            if (user.TenantId != _tenantContext.TenantId && user.TenantId != null)
            {
                return null;
            }
        }

        return user;
    }

    private OlusoUserInfo MapToUserInfo(OlusoUser user)
    {
        return new OlusoUserInfo
        {
            Id = user.Id,
            Username = user.UserName ?? "",
            Email = user.Email,
            EmailVerified = user.EmailConfirmed,
            PhoneNumber = user.PhoneNumber,
            PhoneNumberVerified = user.PhoneNumberConfirmed,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            Picture = user.ProfilePictureUrl,
            TenantId = user.TenantId,
            IsActive = user.IsActive,
            TwoFactorEnabled = user.TwoFactorEnabled,
            LastLoginAt = user.LastLoginAt,
            Roles = GetRolesSync(user)
        };
    }

    private ValidatedUser MapToValidatedUser(OlusoUser user)
    {
        return new ValidatedUser
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            Email = user.Email ?? "",
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            TenantId = user.TenantId,
            IsActive = user.IsActive,
            LastLoginAt = user.LastLoginAt
        };
    }

    private IEnumerable<string> GetRolesSync(OlusoUser user)
    {
        // Note: This is synchronous for the mapping, use GetRolesAsync for full async
        return _userManager.GetRolesAsync(user).GetAwaiter().GetResult();
    }

    private static string NormalizePhoneNumber(string phone)
    {
        var normalized = new string(phone.Where(c => char.IsDigit(c)).ToArray());
        if (phone.StartsWith("+"))
            normalized = "+" + normalized;
        return normalized;
    }

    public async Task SetEmailVerifiedAsync(string userId, bool verified, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found for SetEmailVerifiedAsync: {UserId}", userId);
            return;
        }

        user.EmailConfirmed = verified;
        await _userManager.UpdateAsync(user);
        _logger.LogInformation("Set email verified to {Verified} for user {UserId}", verified, userId);
    }

    public async Task SetPhoneNumberVerifiedAsync(string userId, bool verified, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found for SetPhoneNumberVerifiedAsync: {UserId}", userId);
            return;
        }

        user.PhoneNumberConfirmed = verified;
        await _userManager.UpdateAsync(user);
        _logger.LogInformation("Set phone verified to {Verified} for user {UserId}", verified, userId);
    }

    public async Task SetEmailAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found for SetEmailAsync: {UserId}", userId);
            return;
        }

        var result = await _userManager.SetEmailAsync(user, email);
        if (result.Succeeded)
        {
            _logger.LogInformation("Updated email for user {UserId}", userId);
        }
        else
        {
            _logger.LogWarning("Failed to update email for user {UserId}: {Errors}",
                userId, string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    public async Task SetPhoneNumberAsync(string userId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found for SetPhoneNumberAsync: {UserId}", userId);
            return;
        }

        var normalizedPhone = NormalizePhoneNumber(phoneNumber);
        var result = await _userManager.SetPhoneNumberAsync(user, normalizedPhone);
        if (result.Succeeded)
        {
            _logger.LogInformation("Updated phone number for user {UserId}", userId);
        }
        else
        {
            _logger.LogWarning("Failed to update phone number for user {UserId}: {Errors}",
                userId, string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    public async Task<OlusoUserInfo?> FindByExternalLoginAsync(
        string provider,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByLoginAsync(provider, providerKey);
        if (user == null)
        {
            return null;
        }

        // Verify tenant match
        if (_tenantContext.HasTenant && user.TenantId != _tenantContext.TenantId && user.TenantId != null)
        {
            return null;
        }

        return MapToUserInfo(user);
    }

    public async Task<UserOperationResult> CreateUserFromExternalLoginAsync(
        CreateExternalUserRequest request,
        CancellationToken cancellationToken = default)
    {
        // Check if email already exists
        var existingUser = await FindUserInTenantByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return UserOperationResult.Failed("email_exists", "A user with this email already exists");
        }

        // Check if external login already linked
        var linkedUser = await _userManager.FindByLoginAsync(request.Provider, request.ProviderKey);
        if (linkedUser != null)
        {
            return UserOperationResult.Failed("login_exists", "This external login is already linked to another account");
        }

        // Create the user
        var newUser = new OlusoUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = request.EmailConfirmed,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = $"{request.FirstName} {request.LastName}".Trim(),
            TenantId = request.TenantId ?? _tenantContext.TenantId,
            IsActive = true
        };

        var createResult = await _userManager.CreateAsync(newUser);
        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Failed to create user from external login: {Errors}",
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return UserOperationResult.Failed(createResult.Errors.Select(e => e.Description).ToArray());
        }

        // Link the external login
        var loginInfo = new UserLoginInfo(request.Provider, request.ProviderKey, request.Provider);
        var addLoginResult = await _userManager.AddLoginAsync(newUser, loginInfo);
        if (!addLoginResult.Succeeded)
        {
            _logger.LogWarning("Failed to link external login for new user {UserId}: {Errors}",
                newUser.Id, string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
            // User was created but login wasn't linked - still return success
        }

        _logger.LogInformation("Created user {UserId} from external provider {Provider}", newUser.Id, request.Provider);
        return UserOperationResult.Success(newUser.Id, MapToUserInfo(newUser));
    }

    public async Task<IEnumerable<Core.Services.ExternalLoginInfo>> GetExternalLoginsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Enumerable.Empty<Core.Services.ExternalLoginInfo>();
        }

        var logins = await _userManager.GetLoginsAsync(user);
        return logins.Select(l => new Core.Services.ExternalLoginInfo
        {
            Provider = l.LoginProvider,
            ProviderKey = l.ProviderKey,
            DisplayName = l.ProviderDisplayName
        });
    }

    public async Task<UserOperationResult> RemoveExternalLoginAsync(
        string userId,
        string provider,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        // Verify tenant access
        if (_tenantContext.HasTenant && user.TenantId != _tenantContext.TenantId && user.TenantId != null)
        {
            return UserOperationResult.Failed("Access denied");
        }

        // Check if user has password or other logins - don't allow removing last login method
        var hasPassword = await _userManager.HasPasswordAsync(user);
        var logins = await _userManager.GetLoginsAsync(user);

        if (!hasPassword && logins.Count <= 1)
        {
            return UserOperationResult.Failed("Cannot remove the last login method. Set a password first.");
        }

        var result = await _userManager.RemoveLoginAsync(user, provider, providerKey);
        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        _logger.LogInformation("Removed external login {Provider} from user {UserId}", provider, userId);
        return UserOperationResult.Success(userId);
    }
}
