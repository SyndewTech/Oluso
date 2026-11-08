using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.EntityFramework.Identity;

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
    private readonly ITenantSettingsProvider _tenantSettingsProvider;
    private readonly OlusoDbContext _dbContext;
    private readonly ILogger<IdentityUserService> _logger;
    private readonly IReadOnlyList<IUserDeletionParticipant> _deletionParticipants;
    private readonly IServiceProvider? _services;

    public IdentityUserService(
        UserManager<OlusoUser> userManager,
        SignInManager<OlusoUser> signInManager,
        ITenantContext tenantContext,
        ITenantSettingsProvider tenantSettingsProvider,
        OlusoDbContext dbContext,
        ILogger<IdentityUserService> logger,
        IEnumerable<IUserDeletionParticipant>? deletionParticipants = null,
        IServiceProvider? services = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tenantContext = tenantContext;
        _tenantSettingsProvider = tenantSettingsProvider;
        _dbContext = dbContext;
        _logger = logger;
        _deletionParticipants = deletionParticipants?.ToList() ?? new List<IUserDeletionParticipant>();
        _services = services;
    }

    /// <summary>
    /// Whether the address an external provider returned is proven (see <see cref="ExternalEmailTrust"/>). Without the
    /// email domain store it is not.
    /// </summary>
    private async Task<bool> IsExternalEmailTrustedAsync(
        string provider, ClaimsPrincipal externalUser, string? email, CancellationToken cancellationToken)
    {
        var emailDomains = _services?.GetService<ITenantEmailDomainStore>();
        if (emailDomains == null || string.IsNullOrEmpty(email))
        {
            return false;
        }

        return await ExternalEmailTrust.IsTrustedAsync(
            provider,
            type => externalUser.FindFirst(type)?.Value,
            email,
            _tenantContext.TenantId,
            _services?.GetService<IExternalProviderConfigStore>(),
            emailDomains,
            cancellationToken);
    }

    /// <summary>
    /// Resolved per call rather than injected: the event service constructs
    /// every sink, and the audit and SCIM sinks depend on this service, so a
    /// constructor dependency is a cycle.
    /// </summary>
    private Task RaiseAsync(OlusoEvent evt, CancellationToken cancellationToken)
    {
        var events = _services?.GetService<IOlusoEventService>();
        return events?.RaiseAsync(evt, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Every account a journey step creates runs inside the journey's origin
    /// scope, so the policy is known here even when the caller doesn't pass it.
    /// </summary>
    private Task RaiseCreatedAsync(OlusoUser user, string? clientId, string? policyId, CancellationToken cancellationToken) =>
        RaiseAsync(new UserRegisteredEvent
        {
            TenantId = user.TenantId,
            SubjectId = user.Id,
            Email = user.Email ?? string.Empty,
            Username = user.UserName,
            ClientId = clientId,
            PolicyId = policyId ?? (EventOriginScope.Current is { Kind: EventOriginKinds.Journey } journey ? journey.Id : null)
        }, cancellationToken);

    /// <summary>
    /// Whether the account's own holder is the one acting, judged the way the
    /// event will be attributed: the ambient origin first, then the request.
    /// </summary>
    private bool ActorIsSubject(string userId)
    {
        var origin = EventOriginScope.Current ?? _services?.GetService<IEventOriginResolver>()?.ResolveFromRequest();
        return origin is { Kind: EventOriginKinds.User } && origin.Id == userId;
    }

    /// <summary>
    /// Has at least one linked external login (Google, Azure AD, SAML, etc.).
    /// Orthogonal to <c>HasPasswordAsync</c> — a user can have both, just
    /// one, or neither.
    /// </summary>
    private async Task<bool> IsFederatedAsync(OlusoUser user)
    {
        var logins = await _userManager.GetLoginsAsync(user);
        return logins.Count > 0;
    }

    /// <summary>
    /// Gate for local-password change / reset operations. The discriminator
    /// is <see cref="UserManager{TUser}.HasPasswordAsync"/>, not "has any
    /// linked login":
    ///
    /// <list type="bullet">
    ///   <item><b>User has a password</b> — they own a credential they set
    ///         themselves; mixed-mode users (password + linked SSO) keep
    ///         the ability to manage it. Allow.</item>
    ///   <item><b>User has no password and no logins</b> — about to be set
    ///         up; CreateUserAsync seeds the first password. Allow.</item>
    ///   <item><b>User has no password but has external logins</b> — pure
    ///         SSO user. Block unless the tenant has explicitly opted into
    ///         break-glass / mixed-mode via
    ///         <see cref="TenantPasswordSettings.AllowLocalPasswordForFederatedUsers"/>.</item>
    /// </list>
    ///
    /// Sign-in path (<see cref="ValidateCredentialsAsync"/>) does not call
    /// this gate — if a user has a password, they can use it, full stop.
    /// </summary>
    private async Task<bool> IsLocalPasswordAllowedAsync(OlusoUser user, CancellationToken cancellationToken)
    {
        if (await _userManager.HasPasswordAsync(user))
            return true;

        if (!await IsFederatedAsync(user))
            return true;

        var pwdSettings = await _tenantSettingsProvider.GetPasswordSettingsAsync(cancellationToken);
        return pwdSettings.AllowLocalPasswordForFederatedUsers;
    }

    public async Task<UserValidationResult> ValidateCredentialsAsync(
        string username,
        string password,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // If tenantId is provided, use it; otherwise use current context
        var effectiveTenantId = tenantId ?? _tenantContext.TenantId;

        var (identifier, organizationSlug) = ParseOrganizationQualifier(username);

        // On a tenant host the account is already unambiguous, and honouring a
        // qualifier there would let someone aim at a different organization.
        var tenantIsKnown = !string.IsNullOrEmpty(effectiveTenantId)
            && effectiveTenantId != Core.PlatformConstants.TenantId;

        if (tenantIsKnown)
        {
            if (organizationSlug != null)
            {
                _logger.LogWarning("Organization qualifier supplied on a tenant-scoped host; ignoring it");
            }

            return await ValidateSingleUserAsync(
                await FindUserByUsernameOrEmailAsync(identifier, effectiveTenantId), password);
        }

        // Platform host: the same address may exist in several organizations,
        // each its own row with its own password.
        var candidates = await FindCandidateUsersAsync(identifier, organizationSlug, cancellationToken);

        if (candidates.Count == 0)
        {
            return UserValidationResult.Failed("invalid_credentials", "Invalid username or password");
        }

        if (candidates.Count == 1)
        {
            return await ValidateSingleUserAsync(candidates[0], password);
        }

        // No lockout on the probe — otherwise one wrong guess counts a failure
        // against every account sharing the address, locking out strangers.
        var matched = new List<OlusoUser>();
        foreach (var candidate in candidates)
        {
            if (!candidate.IsActive) continue;
            if (!await _userManager.HasPasswordAsync(candidate)) continue;

            var probe = await _signInManager.CheckPasswordSignInAsync(candidate, password, lockoutOnFailure: false);
            if (probe.Succeeded || probe.RequiresTwoFactor)
            {
                matched.Add(candidate);
            }
        }

        if (matched.Count == 0)
        {
            // Re-run one with lockout on, so wrong guesses still cost something
            // and locked/federated states surface.
            return await ValidateSingleUserAsync(candidates[0], password);
        }

        if (matched.Count == 1)
        {
            return await ValidateSingleUserAsync(matched[0], password);
        }

        var organizations = await DescribeOrganizationsAsync(matched, cancellationToken);
        _logger.LogInformation(
            "Credentials matched accounts in {Count} organizations — organization choice required", organizations.Count);

        return UserValidationResult.MultipleAccountsFound(organizations);
    }

    /// <summary>
    /// Runs the full credential check (active, lockout, federated, password, MFA)
    /// against one resolved account.
    /// </summary>
    private async Task<UserValidationResult> ValidateSingleUserAsync(OlusoUser? user, string password)
    {
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

        // Sign-in gate: only block if the user has NO local password.
        // Mixed users (have a password AND linked SSO) keep the right to
        // sign in with their password — they own that credential. Pure
        // SSO users (no password) couldn't authenticate here anyway since
        // PasswordHash is null, but we surface a clear error so the UI
        // can redirect to the IdP instead of showing "invalid credentials".
        if (!await _userManager.HasPasswordAsync(user))
        {
            return UserValidationResult.Failed("federated_user_local_password_disallowed",
                "This account signs in via an external identity provider.");
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

        if (await IsPasswordExpiredAsync(user))
        {
            _logger.LogInformation("Password for user {UserId} has expired; a change is required before sign-in", user.Id);
            return UserValidationResult.PasswordChangeRequired(MapToValidatedUser(user));
        }

        return UserValidationResult.Successful(MapToValidatedUser(user));
    }

    /// <summary>The user's password is older than their tenant's expiry policy allows.</summary>
    private async Task<bool> IsPasswordExpiredAsync(OlusoUser user)
    {
        var settings = string.IsNullOrEmpty(user.TenantId)
            ? await _tenantSettingsProvider.GetPasswordSettingsAsync()
            : await _tenantSettingsProvider.GetPasswordSettingsAsync(user.TenantId);

        return settings.IsPasswordExpired(user.PasswordChangedAt, user.CreatedAt, DateTime.UtcNow);
    }

    public async Task<OlusoUserInfo?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null) return MapToUserInfo(user);

        // Miss can mean "no such user" or "their record lives in a sibling tenant
        // and the ambient filter hid it". Users granted access through a
        // TenantMembership belong to this tenant as much as its home users do, so
        // resolve them too — bounded to the ambient tenant, never any tenant.
        //
        // Second query only on a miss, so the common path is unchanged.
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId)) return null;

        return await FindByIdInTenantAsync(userId, tenantId, cancellationToken);
    }

    /// <summary>
    /// Batched id resolution — a single <c>WHERE Id IN (…)</c> query instead of
    /// N round-trips, and (critically) one operation on the shared context
    /// rather than concurrent ones. Callers annotating collections with user
    /// info should use this, never <c>Task.WhenAll(FindByIdAsync)</c>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, OlusoUserInfo>> FindByIdsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, OlusoUserInfo>();

        var users = await _userManager.Users
            .Where(u => ids.Contains(u.Id))
            .ToListAsync(cancellationToken);

        return users.ToDictionary(u => u.Id, MapToUserInfo);
    }

    public async Task<IReadOnlyDictionary<string, OlusoUserInfo>> FindByIdsAcrossTenantsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, OlusoUserInfo>();

        // Callers pass ids already scoped by server-side state (see the interface).
        var users = await _dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToListAsync(cancellationToken);

        return users.ToDictionary(u => u.Id, MapToUserInfo);
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
        var user = await FindUserInScopeAsync(userId, cancellationToken);
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
        var user = await FindUserInScopeAsync(userId, cancellationToken);
        if (user == null)
            return Enumerable.Empty<string>();

        return await _userManager.GetRolesAsync(user);
    }

    public async Task<UserOperationResult> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolvedTenantId = request.TenantId ?? _tenantContext.TenantId
            ?? throw new InvalidOperationException(
                "Cannot create user without a tenant. Set request.TenantId or call within a tenant context.");

        // The platform tenant is a meta-tenant for system-level identities (SuperAdmin).
        // Those are bootstrapped via seed/operational scripts; runtime user creation
        // (registration, admin UI, journey signup) must never target it.
        if (resolvedTenantId == Core.PlatformConstants.TenantId)
        {
            return UserOperationResult.Failed("platform_tenant_reserved",
                "The platform tenant does not accept user registration.");
        }

        // Enforce the tenant's AllowedEmailDomains as a centralised floor —
        // call sites already validate, but having the gate here means a
        // future code path can't bypass the tenant policy by skipping the
        // pre-validation. Only checks when the resolved tenant matches the
        // current context (the common case); explicit cross-tenant calls
        // are responsible for their own validation.
        if (_tenantContext.HasTenant
            && _tenantContext.TenantId == resolvedTenantId
            && !IsEmailDomainAllowedForTenant(_tenantContext.Tenant, request.Email))
        {
            _logger.LogWarning(
                "User creation rejected for tenant {TenantId}: email domain not in AllowedEmailDomains",
                resolvedTenantId);
            return UserOperationResult.Failed("email_domain_not_allowed",
                "Registrations from this email domain are not allowed.");
        }

        var user = new OlusoUser
        {
            // Without a username of its own the account signs in with its email.
            UserName = string.IsNullOrWhiteSpace(request.Username) ? request.Email : request.Username.Trim(),
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            TenantId = resolvedTenantId,
            DisplayName = $"{request.FirstName} {request.LastName}".Trim(),
            IsActive = true,
            CustomPropertiesJson = SerializeCustomProperties(request.CustomProperties)
        };

        var result = string.IsNullOrEmpty(request.Password)
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        _logger.LogInformation("Created user {UserId} in tenant {TenantId}", user.Id, user.TenantId);

        await RaiseCreatedAsync(user, request.ClientId, request.PolicyId, cancellationToken);

        return UserOperationResult.Success(user.Id, MapToUserInfo(user));
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

        var before = new
        {
            user.Email, user.EmailConfirmed, user.FirstName, user.LastName, user.PhoneNumber,
            user.ProfilePictureUrl,
            CustomProperties = DeserializeCustomProperties(user.CustomPropertiesJson) ?? new Dictionary<string, string>()
        };

        if (request.Email != null)
        {
            // A new address hasn't been proven by this user, so verification
            // can't carry over: password resets and verified-email checks would
            // otherwise trust an inbox nobody confirmed. A casing-only edit is
            // the same mailbox and keeps it.
            if (!string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                user.EmailConfirmed = false;
            }

            user.Email = request.Email;

            if (request.EmailVerified)
            {
                user.EmailConfirmed = true;
            }
        }

        if (request.FirstName != null) user.FirstName = request.FirstName;
        if (request.LastName != null) user.LastName = request.LastName;
        if (request.PhoneNumber != null) user.PhoneNumber = request.PhoneNumber;
        if (request.Picture != null) user.ProfilePictureUrl = request.Picture;

        if (request.FirstName != null || request.LastName != null)
        {
            user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
        }

        // CustomProperties uses replace-when-set semantics: a non-null
        // dict on the request fully replaces the stored map (lets the
        // admin UI delete keys by omission). Null dict = no change.
        // Step handlers that only want to add/update a few keys must
        // fetch the existing map and merge before calling — see
        // TermsAcceptanceStepHandler / UpdateUserStepHandler.
        if (request.CustomProperties != null)
        {
            user.CustomPropertiesJson = SerializeCustomProperties(request.CustomProperties);
        }

        var changed = new Dictionary<string, string>();
        void Compare(string field, string? previous, string? current)
        {
            if (!string.Equals(previous, current, StringComparison.Ordinal))
                changed[field] = current ?? string.Empty;
        }
        Compare("email", before.Email, user.Email);
        Compare("firstName", before.FirstName, user.FirstName);
        Compare("lastName", before.LastName, user.LastName);
        Compare("phoneNumber", before.PhoneNumber, user.PhoneNumber);
        Compare("picture", before.ProfilePictureUrl, user.ProfilePictureUrl);
        if (request.CustomProperties != null)
        {
            var current = DeserializeCustomProperties(user.CustomPropertiesJson) ?? new Dictionary<string, string>();
            foreach (var key in before.CustomProperties.Keys.Union(current.Keys))
            {
                Compare($"customProperties.{key}",
                    before.CustomProperties.TryGetValue(key, out var was) ? was : null,
                    current.TryGetValue(key, out var now) ? now : null);
            }
        }

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        if (changed.Count > 0)
        {
            await RaiseAsync(new UserUpdatedEvent
            {
                TenantId = user.TenantId,
                SubjectId = user.Id,
                Email = user.Email,
                ChangedFields = changed
            }, cancellationToken);
        }

        // A confirmed address that is new, or was unconfirmed, has just been
        // proven; re-confirming the same verified address proves nothing new.
        if (request.EmailVerified && user.EmailConfirmed
            && (!before.EmailConfirmed || changed.ContainsKey("email")))
        {
            await RaiseAsync(new UserEmailVerifiedEvent
            {
                TenantId = user.TenantId,
                SubjectId = user.Id,
                Email = user.Email ?? string.Empty
            }, cancellationToken);
        }

        return UserOperationResult.Success(userId);
    }

    public async Task<UserOperationResult> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return UserOperationResult.Failed(UserDeletionErrors.NotFound, "User not found");
        }

        var holdsGlobalRole = await _dbContext.Roles
            .IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == null
                && (r.NormalizedName == GlobalRoles.SuperAdminNormalized || r.NormalizedName == GlobalRoles.SystemAdminNormalized)
                && _dbContext.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == r.Id),
                cancellationToken);
        if (user.TenantId == Core.PlatformConstants.TenantId || holdsGlobalRole)
        {
            return UserOperationResult.Failed(UserDeletionErrors.ProtectedIdentity,
                "Platform administrator accounts can't be deleted.");
        }

        var ownedOrganizations = await _dbContext.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.Role == OrganizationRole.Owner)
            .Select(m => m.OrganizationId)
            .ToListAsync(cancellationToken);
        if (ownedOrganizations.Count > 0)
        {
            var coOwned = await _dbContext.OrganizationMemberships
                .IgnoreQueryFilters()
                .Where(m => ownedOrganizations.Contains(m.OrganizationId) && m.UserId != userId && m.Role == OrganizationRole.Owner)
                .Select(m => m.OrganizationId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (ownedOrganizations.Except(coOwned).Any())
            {
                return UserOperationResult.Failed(UserDeletionErrors.LastOrganizationOwner,
                    "This user is the only owner of an organization. Assign another owner first.");
            }
        }

        // One SaveChanges, so the account and everything keyed to it go together
        // or not at all. Rows are matched across tenants: memberships put the
        // user's id in tenants other than their home one.
        _dbContext.UserRoles.RemoveRange(await _dbContext.UserRoles.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.UserClaims.RemoveRange(await _dbContext.UserClaims.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.UserLogins.RemoveRange(await _dbContext.UserLogins.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.UserTokens.RemoveRange(await _dbContext.UserTokens.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.PasswordHistory.RemoveRange(await _dbContext.PasswordHistory.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.TenantMemberships.RemoveRange(await _dbContext.TenantMemberships.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.OrganizationMemberships.RemoveRange(await _dbContext.OrganizationMemberships.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.TeamMemberships.RemoveRange(await _dbContext.TeamMemberships.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.UserModuleAccess.RemoveRange(await _dbContext.UserModuleAccess.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.UserNotificationProfiles.RemoveRange(await _dbContext.UserNotificationProfiles.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.PersistedGrants.RemoveRange(await _dbContext.PersistedGrants.IgnoreQueryFilters().Where(x => x.SubjectId == userId).ToListAsync(cancellationToken));
        _dbContext.ServerSideSessions.RemoveRange(await _dbContext.ServerSideSessions.IgnoreQueryFilters().Where(x => x.SubjectId == userId).ToListAsync(cancellationToken));
        _dbContext.Consents.RemoveRange(await _dbContext.Consents.IgnoreQueryFilters().Where(x => x.SubjectId == userId).ToListAsync(cancellationToken));
        _dbContext.DeviceFlowCodes.RemoveRange(await _dbContext.DeviceFlowCodes.IgnoreQueryFilters().Where(x => x.SubjectId == userId).ToListAsync(cancellationToken));
        _dbContext.CibaRequests.RemoveRange(await _dbContext.CibaRequests.IgnoreQueryFilters().Where(x => x.SubjectId == userId).ToListAsync(cancellationToken));
        _dbContext.ActionTokens.RemoveRange(await _dbContext.ActionTokens.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));
        _dbContext.JourneyStates.RemoveRange(await _dbContext.JourneyStates.IgnoreQueryFilters().Where(x => x.UserId == userId || x.AuthenticatedUserId == userId).ToListAsync(cancellationToken));
        _dbContext.ClientAllowedUsers.RemoveRange(await _dbContext.ClientAllowedUsers.IgnoreQueryFilters().Where(x => x.SubjectId == userId).ToListAsync(cancellationToken));
        _dbContext.ConversationMentionReads.RemoveRange(await _dbContext.ConversationMentionReads.IgnoreQueryFilters().Where(x => x.UserId == userId).ToListAsync(cancellationToken));

        foreach (var team in await _dbContext.Teams.IgnoreQueryFilters().Where(t => t.OwnerUserId == userId).ToListAsync(cancellationToken))
        {
            team.OwnerUserId = null;
        }

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted user {UserId} from tenant {TenantId}", userId, user.TenantId);

        await RaiseAsync(new UserDeletedEvent
        {
            TenantId = user.TenantId,
            SubjectId = userId,
            Email = user.Email,
            DeletedByAdmin = !ActorIsSubject(userId)
        }, cancellationToken);

        foreach (var participant in _deletionParticipants)
        {
            try
            {
                await participant.DeleteUserDataAsync(userId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "{Participant} failed to remove its data for deleted user {UserId}",
                    participant.GetType().Name, userId);
            }
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

        if (!await IsLocalPasswordAllowedAsync(user, cancellationToken))
        {
            _logger.LogWarning(
                "Refused password change for federated user {UserId} on tenant {TenantId}",
                user.Id, user.TenantId);
            return UserOperationResult.Failed("federated_user_local_password_disallowed",
                "This account is managed by an external identity provider. Password cannot be changed here.");
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

        if (!await IsLocalPasswordAllowedAsync(user, cancellationToken))
        {
            _logger.LogWarning(
                "Refused password reset for federated user {UserId} on tenant {TenantId}",
                user.Id, user.TenantId);
            return UserOperationResult.Failed("federated_user_local_password_disallowed",
                "This account is managed by an external identity provider. Password cannot be reset here.");
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
            // A bad or expired reset link is its own failure, so callers don't confuse it with a
            // password the policy rejected.
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                return UserOperationResult.Failed("invalid_token", "This password reset link is invalid or has expired.");
            }
            return UserOperationResult.Failed(result.Errors.Select(e => e.Description).ToArray());
        }

        return UserOperationResult.Success(userId);
    }

    public async Task<string?> GeneratePasswordResetTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        // Same gate as ResetPasswordAsync — don't even MINT a token for a federated
        // user (managed by their IdP, no local password) unless the tenant opted into
        // mixed-mode. Covers every caller: forgot-password page, journeys, API.
        if (!await IsLocalPasswordAllowedAsync(user, cancellationToken))
        {
            _logger.LogWarning(
                "Refused password-reset token for federated user {UserId} on tenant {TenantId}",
                user.Id, user.TenantId);
            return null;
        }

        return await _userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<bool> HasMfaEnabledAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserInScopeAsync(userId, cancellationToken);
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

    public async Task<bool> UnlockAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        // Identity refuses a lockout end for users without lockout; they can't be locked anyway.
        if (user.LockoutEnabled)
        {
            var ended = await _userManager.SetLockoutEndDateAsync(user, null);
            if (!ended.Succeeded) return false;
        }

        return (await _userManager.ResetAccessFailedCountAsync(user)).Succeeded;
    }

    public async Task<bool> LockAsync(string userId, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        if (!user.LockoutEnabled && !(await _userManager.SetLockoutEnabledAsync(user, true)).Succeeded)
        {
            return false;
        }

        return (await _userManager.SetLockoutEndDateAsync(user, lockoutEnd ?? DateTimeOffset.MaxValue)).Succeeded;
    }

    public async Task<ProcessExternalLoginResult> ProcessExternalLoginAsync(
        string provider,
        ClaimsPrincipal externalUser,
        bool createIfNotExists = true,
        string? clientId = null,
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

        var emailTrusted = await IsExternalEmailTrustedAsync(provider, externalUser, email, cancellationToken);

        if (!string.IsNullOrEmpty(email))
        {
            user = await FindUserInTenantByEmailAsync(email);
        }

        if (user != null)
        {
            // Only an address the provider can vouch for, on an account whose own address was proven, joins the
            // account by email. Otherwise anyone a provider lets type that address would sign in as its owner; they
            // prove the account is theirs first (a code sent to it).
            if (!emailTrusted || !user.EmailConfirmed)
            {
                _logger.LogInformation(
                    "External login from {Provider} matches account {UserId} by email but can't be linked unproven (trusted={Trusted}, accountVerified={Verified})",
                    provider, user.Id, emailTrusted, user.EmailConfirmed);
                return ProcessExternalLoginResult.LinkRequired(user.Id);
            }

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

        var resolvedTenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException(
                "Cannot provision external user without a tenant context.");

        if (resolvedTenantId == Core.PlatformConstants.TenantId)
        {
            return ProcessExternalLoginResult.Failed("platform_tenant_reserved",
                "The platform tenant does not accept user registration.");
        }

        // Tenant registration policy — same gates the password-based and
        // journey-based signup paths enforce, so external SSO can't be the
        // hole that bypasses AllowSelfRegistration or AllowedEmailDomains.
        var policyResult = CheckExternalRegistrationPolicy(_tenantContext.Tenant, email);
        if (policyResult is { } policyError)
        {
            _logger.LogWarning(
                "External login auto-provision rejected for tenant {TenantId}: {Code}",
                resolvedTenantId, policyError.Code);
            return ProcessExternalLoginResult.Failed(policyError.Code, policyError.Message);
        }

        var newUser = new OlusoUser
        {
            UserName = email ?? $"{provider}_{providerKey}",
            Email = email,
            EmailConfirmed = emailTrusted,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = name ?? $"{firstName} {lastName}".Trim(),
            TenantId = resolvedTenantId,
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
        await RaiseCreatedAsync(newUser, clientId, null, cancellationToken);
        return ProcessExternalLoginResult.Success(newUser.Id, MapToUserInfo(newUser), isNewUser: true);
    }

    async Task<string?> IOlusoUserService.GeneratePasswordResetTokenAsync(string userId, CancellationToken cancellationToken)
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
        var user = await FindUserInScopeAsync(userId, cancellationToken);
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
        var user = await FindUserInScopeAsync(userId, cancellationToken);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        var scopeTenantId = ResolveRoleScopeTenantId(user);
        if (scopeTenantId == null)
        {
            return UserOperationResult.Failed("No tenant to assign the role in");
        }

        var role = await ResolveRoleAsync(roleName, scopeTenantId, cancellationToken);
        if (role == null)
        {
            return UserOperationResult.Failed($"Role '{roleName}' does not exist");
        }

        await RoleAssignments.GrantAsync(_dbContext.UserRoles, role, user.Id, scopeTenantId, null, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added user {UserId} to role {RoleName} ({RoleId})", userId, roleName, role.Id);
        return UserOperationResult.Success(userId);
    }

    public async Task<UserOperationResult> RemoveFromRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await FindUserInScopeAsync(userId, cancellationToken);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        var scopeTenantId = ResolveRoleScopeTenantId(user);
        var normalizedName = _userManager.NormalizeName(roleName);
        var held = (await GetHeldRolesInScopeAsync(user.Id, scopeTenantId, cancellationToken))
            .Where(r => r.NormalizedName == normalizedName)
            .ToList();
        if (held.Count == 0)
        {
            return UserOperationResult.Failed($"User is not in role '{roleName}'");
        }

        foreach (var role in held)
        {
            await RoleAssignments.RevokeAsync(_dbContext.UserRoles, role, user.Id, scopeTenantId, cancellationToken);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Removed user {UserId} from role {RoleName}", userId, roleName);
        return UserOperationResult.Success(userId);
    }

    public async Task<UserOperationResult> SetRolesAsync(string userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        var user = await FindUserInScopeAsync(userId, cancellationToken);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        // Roles are per-tenant assignments: only the rows visible in this scope
        // (the tenant's own plus platform-wide) are replaced. Roles the user
        // holds in other tenants are untouched.
        var scopeTenantId = ResolveRoleScopeTenantId(user);
        var held = await GetHeldRolesInScopeAsync(user.Id, scopeTenantId, cancellationToken);

        var requested = roleNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var target = new Dictionary<string, OlusoRole>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var name in requested)
        {
            var normalizedName = _userManager.NormalizeName(name);

            // A row the user already holds under this name is kept as-is.
            var heldMatch = PreferRow(held.Where(r => r.NormalizedName == normalizedName).ToList(), name);
            var role = heldMatch ?? await ResolveRoleAsync(name, scopeTenantId, cancellationToken);
            if (role == null)
            {
                missing.Add(name);
                continue;
            }

            target[role.Id] = role;
        }

        if (missing.Count > 0)
        {
            return UserOperationResult.Failed($"Role(s) not found: {string.Join(", ", missing)}");
        }

        if (scopeTenantId == null)
        {
            return UserOperationResult.Failed("No tenant to assign roles in");
        }

        await ReplaceRolesAsync(user.Id, scopeTenantId, held, target.Values, cancellationToken);

        _logger.LogInformation("Set roles for user {UserId} in tenant {TenantId}: {Roles}",
            userId, scopeTenantId ?? "(platform)", string.Join(", ", requested));
        return UserOperationResult.Success(userId);
    }

    public async Task<UserOperationResult> SetRolesByIdAsync(string userId, IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var user = await FindUserInScopeAsync(userId, cancellationToken);
        if (user == null)
        {
            return UserOperationResult.Failed("User not found");
        }

        var scopeTenantId = ResolveRoleScopeTenantId(user);
        var held = await GetHeldRolesInScopeAsync(user.Id, scopeTenantId, cancellationToken);

        var requestedIds = roleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var rows = await _dbContext.Roles
            .IgnoreQueryFilters()
            .Where(r => requestedIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        // A row from another tenant is reported as missing rather than named,
        // so the caller learns nothing about that tenant's roles.
        var inScope = rows.Where(r => r.TenantId == scopeTenantId || r.TenantId == null).ToList();
        var missing = requestedIds.Except(inScope.Select(r => r.Id), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            return UserOperationResult.Failed($"Role(s) not found in this tenant: {string.Join(", ", missing)}");
        }

        if (scopeTenantId == null)
        {
            return UserOperationResult.Failed("No tenant to assign roles in");
        }

        await ReplaceRolesAsync(user.Id, scopeTenantId, held, inScope, cancellationToken);

        _logger.LogInformation("Set roles for user {UserId} in tenant {TenantId}: {Roles}",
            userId, scopeTenantId ?? "(platform)", string.Join(", ", inScope.Select(r => r.Name)));
        return UserOperationResult.Success(userId);
    }

    /// <summary>
    /// Makes <paramref name="target"/> the exact set of rows the user holds in
    /// scope: revokes held rows outside the target, grants target rows not held.
    /// </summary>
    private async Task ReplaceRolesAsync(
        string userId,
        string tenantId,
        IReadOnlyCollection<OlusoRole> held,
        IEnumerable<OlusoRole> target,
        CancellationToken cancellationToken)
    {
        var targetRoles = target.ToList();
        var targetIds = targetRoles.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var role in held.Where(r => !targetIds.Contains(r.Id)))
        {
            await RoleAssignments.RevokeAsync(_dbContext.UserRoles, role, userId, tenantId, cancellationToken);
        }

        var heldIds = held.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var role in targetRoles.Where(r => !heldIds.Contains(r.Id)))
        {
            await RoleAssignments.GrantAsync(_dbContext.UserRoles, role, userId, tenantId, null, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Role assignments are scoped to the tenant the request runs in; outside a
    /// tenant (startup, the platform meta-tenant) the user's home tenant is used.
    /// </summary>
    private string? ResolveRoleScopeTenantId(OlusoUser user) =>
        RoleAssignmentTenant.Resolve(_tenantContext.TenantId, user.TenantId);

    /// <summary>
    /// Resolves a role name to a row in scope.
    /// </summary>
    private async Task<OlusoRole?> ResolveRoleAsync(string roleName, string? tenantId, CancellationToken cancellationToken)
    {
        var normalizedName = _userManager.NormalizeName(roleName);
        var candidates = await _dbContext.Roles
            .IgnoreQueryFilters()
            .Where(r => r.NormalizedName == normalizedName && (r.TenantId == tenantId || r.TenantId == null))
            .ToListAsync(cancellationToken);

        return PreferRow(candidates, roleName);
    }

    /// <summary>
    /// Picks between a tenant row and a platform-wide row of the same name.
    /// Registry-defined module roles (HRAdmin, ...) are platform-governed, so
    /// their platform row wins over a leftover tenant copy; for every other
    /// name the tenant's own row wins.
    /// </summary>
    private static OlusoRole? PreferRow(IReadOnlyList<OlusoRole> candidates, string roleName)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var platformGoverned = PermissionRegistry.Instance.GetModuleRoles()
            .Any(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));

        return platformGoverned
            ? candidates.FirstOrDefault(r => r.TenantId == null) ?? candidates[0]
            : candidates.FirstOrDefault(r => r.TenantId != null) ?? candidates[0];
    }

    private async Task<List<OlusoRole>> GetHeldRolesInScopeAsync(string userId, string? tenantId, CancellationToken cancellationToken)
    {
        return await _dbContext.Roles
            .HeldIn(_dbContext.UserRoles, userId, tenantId)
            .ToListAsync(cancellationToken);
    }

    public async Task<OlusoUserInfo?> FindByIdInTenantAsync(
        string userId, string tenantId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserEntityInTenantAsync(userId, tenantId, cancellationToken);
        return user == null ? null : MapToUserInfo(user);
    }

    public async Task<OlusoUserInfo?> FindByEmailInTenantAsync(
        string email, string tenantId, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = _userManager.NormalizeEmail(email);
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.NormalizedEmail == normalizedEmail
                && (u.TenantId == tenantId
                    || _dbContext.TenantMemberships.Any(tm => tm.UserId == u.Id && tm.TenantId == tenantId)))
            .OrderBy(u => u.TenantId == tenantId ? 0 : 1)
            .ThenBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return user == null ? null : MapToUserInfo(user);
    }

    /// <summary>
    /// The user when they belong to <paramref name="tenantId"/>, as their home
    /// tenant or through a TenantMembership; otherwise null.
    /// </summary>
    private async Task<OlusoUser?> FindUserEntityInTenantAsync(
        string userId, string tenantId, CancellationToken cancellationToken)
    {
        // Filters are bypassed because the point is to reach users whose home
        // tenant is elsewhere; the predicate re-applies the scope. It is applied
        // in the query rather than after loading, so a user outside the tenant is
        // never materialized — a tracked entity would be handed back by the next
        // FindAsync on this context, past the tenant filter.
        return await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId
                && (u.TenantId == tenantId
                    || _dbContext.TenantMemberships.Any(tm => tm.UserId == u.Id && tm.TenantId == tenantId)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The user as the request's tenant sees them: its home users and the members
    /// it was granted through a TenantMembership. The ambient filter alone only
    /// sees home users, so a member acting here would be "not found". At platform
    /// scope or outside a request the filter is off and every user resolves, as
    /// before. Same resolution as <see cref="FindByIdAsync"/>.
    /// </summary>
    private async Task<OlusoUser?> FindUserInScopeAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null) return user;

        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId)) return null;

        return await FindUserEntityInTenantAsync(userId, tenantId, cancellationToken);
    }

    /// <summary>
    /// The tenant whose roles a listing reports and filters on, or null when it's
    /// each user's home tenant. A caller naming a tenant gets that tenant's roles;
    /// a multi-tenant listing, or one outside a real tenant, uses each user's home.
    /// </summary>
    private string? RoleListingTenant(UsersQuery query) =>
        query.TenantIds != null
            ? null
            : !string.IsNullOrEmpty(query.TenantId)
                ? query.TenantId
                : RoleAssignmentTenant.Resolve(_tenantContext.TenantId, null);

    /// <summary>
    /// Users holding <paramref name="role"/> (a role id, or a name) in
    /// <paramref name="tenantId"/>, or in each user's home tenant when null.
    /// Matched on role rows and assignments, the same rule as
    /// <see cref="RoleAssignments.HeldIn"/>, so it runs before counting and paging.
    /// </summary>
    private IQueryable<OlusoUser> FilterByRole(IQueryable<OlusoUser> users, string role, string? tenantId)
    {
        var normalizedRole = _userManager.NormalizeName(role);
        var roles = _dbContext.Roles
            .IgnoreQueryFilters()
            .Where(r => r.Id == role || r.NormalizedName == normalizedRole);
        var userRoles = _dbContext.UserRoles;

        if (tenantId != null)
        {
            return users.Where(u => userRoles.Any(ur => ur.UserId == u.Id
                && roles.Any(r => r.Id == ur.RoleId
                    && (r.TenantId == null || r.TenantId == tenantId)
                    && (ur.TenantId == tenantId
                        || (r.TenantId == null
                            && (r.NormalizedName == GlobalRoles.SuperAdminNormalized
                                || r.NormalizedName == GlobalRoles.SystemAdminNormalized))))));
        }

        return users.Where(u => userRoles.Any(ur => ur.UserId == u.Id
            && roles.Any(r => r.Id == ur.RoleId
                && (r.TenantId == null || r.TenantId == u.TenantId)
                && (ur.TenantId == u.TenantId
                    || (r.TenantId == null
                        && (r.NormalizedName == GlobalRoles.SuperAdminNormalized
                            || r.NormalizedName == GlobalRoles.SystemAdminNormalized))))));
    }

    public async Task<UsersQueryResult> GetUsersAsync(
        UsersQuery query,
        CancellationToken cancellationToken = default)
    {
        var effectiveTenantId = query.TenantId ?? _tenantContext.TenantId;

        // Start with all users
        IQueryable<OlusoUser> usersQuery = _userManager.Users;

        // Filter by tenant. A caller that named a tenant set is scoped to it —
        // and to nothing at all when that set is empty, so an empty scope can't
        // widen into a platform-wide listing.
        if (query.TenantIds != null)
        {
            var tenantIds = query.TenantIds.ToList();
            usersQuery = tenantIds.Count == 0
                ? usersQuery.Where(_ => false)
                : usersQuery.IgnoreQueryFilters().Where(u => u.TenantId != null && tenantIds.Contains(u.TenantId));
        }
        else if (!string.IsNullOrEmpty(effectiveTenantId))
        {
            // A user belongs to a tenant either as their home tenant or through a
            // TenantMembership granted afterwards. Matching on TenantId alone hid
            // everyone added to the tenant later — they were granted access and
            // then didn't appear in its user list.
            //
            // Filters are bypassed because they clamp to the home tenant, which is
            // the very set being widened. effectiveTenantId is the caller's own
            // resolved tenant and the predicate re-applies that scope.
            usersQuery = usersQuery
                .IgnoreQueryFilters()
                .Where(u => u.TenantId == effectiveTenantId
                    || _dbContext.TenantMemberships.Any(tm =>
                        tm.UserId == u.Id && tm.TenantId == effectiveTenantId));
        }

        // Filter by active status
        if (query.IsActive.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.IsActive == query.IsActive.Value);
        }

        // Filter by module access (seat)
        if (!string.IsNullOrEmpty(query.FeatureKey))
        {
            var moduleUserIds = _dbContext.UserModuleAccess
                .Where(a => a.TenantId == effectiveTenantId && a.FeatureKey == query.FeatureKey)
                .Select(a => a.UserId);
            usersQuery = usersQuery.Where(u => moduleUserIds.Contains(u.Id));
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

        if (!string.IsNullOrEmpty(query.Role))
        {
            usersQuery = FilterByRole(usersQuery, query.Role, RoleListingTenant(query));
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

        // Map to user info
        var userInfos = new List<OlusoUserInfo>();
        foreach (var user in users)
        {
            var roles = await _dbContext.Roles
                .HeldIn(_dbContext.UserRoles, user.Id, RoleListingTenant(query) ?? user.TenantId)
                .Select(r => r.Name!)
                .ToListAsync(cancellationToken);

            // Per-user logins lookup — N+1 acceptable here, matches the
            // existing per-user GetRolesAsync. The admin user-list page uses
            // IsFederated to render a badge and gate the reset-password
            // control, so each row needs its own answer.
            var logins = await _userManager.GetLoginsAsync(user);
            var linkedProviders = logins.Count == 0
                ? Array.Empty<string>()
                : logins.Select(l => l.LoginProvider).ToArray();

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
                LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd,
                AccessFailedCount = user.AccessFailedCount,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                LastLoginAt = user.LastLoginAt,
                PasswordChangedAt = user.PasswordChangedAt,
                Roles = roles,
                IsFederated = linkedProviders.Length > 0,
                LinkedProviders = linkedProviders,
                HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
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

    /// <summary>
    /// <c>alice@example.com@acme</c> → (<c>alice@example.com</c>, <c>acme</c>).
    /// The qualifier is the organization slug, since the organization is the
    /// identity boundary — one account spans every tenant within it.
    /// </summary>
    internal static (string identifier, string? organizationSlug) ParseOrganizationQualifier(string input)
    {
        if (string.IsNullOrEmpty(input)) return (input, null);

        var lastAt = input.LastIndexOf('@');
        if (lastAt <= 0) return (input, null);

        var suffix = input[(lastAt + 1)..];
        if (suffix.Length == 0) return (input, null);

        var prefix = input[..lastAt];

        // One '@' means the suffix is a mail domain, unless it has no dot —
        // then it's "username@slug".
        if (!prefix.Contains('@'))
        {
            return suffix.Contains('.') ? (input, null) : (prefix, suffix.ToLowerInvariant());
        }

        return (prefix, suffix.ToLowerInvariant());
    }

    /// <summary>
    /// Rows matching the identifier across organizations, optionally narrowed to
    /// one slug. Filters are bypassed because the platform host has no tenant scope.
    /// </summary>
    private async Task<List<OlusoUser>> FindCandidateUsersAsync(
        string identifier, string? organizationSlug, CancellationToken cancellationToken)
    {
        var normalized = identifier.ToUpperInvariant();

        var query = _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.NormalizedEmail == normalized || u.NormalizedUserName == normalized);

        if (organizationSlug != null)
        {
            var slug = organizationSlug.ToLowerInvariant();
            var orgTenantIds = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Organization != null && t.Organization.Slug == slug)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            if (orgTenantIds.Count == 0)
            {
                return new List<OlusoUser>();
            }

            query = query.Where(u => u.TenantId != null && orgTenantIds.Contains(u.TenantId));
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Maps matched accounts to the organizations that own their home tenants.
    /// </summary>
    private async Task<List<OrganizationOption>> DescribeOrganizationsAsync(
        List<OlusoUser> users, CancellationToken cancellationToken)
    {
        var tenantIds = users
            .Select(u => u.TenantId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        var organizations = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id) && t.Organization != null)
            .Select(t => new OrganizationOption
            {
                Slug = t.Organization!.Slug,
                Name = t.Organization.Name
            })
            .ToListAsync(cancellationToken);

        return organizations
            .GroupBy(o => o.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(o => o.Name)
            .ToList();
    }

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
        var user = await _userManager.FindByNameAsync(username);

        if (user != null && _tenantContext.HasTenant)
        {
            // Platform tenant serves all tenants — skip scoping
            if (_tenantContext.TenantId == Core.PlatformConstants.TenantId)
                return user;

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
            // Platform tenant serves all tenants — skip scoping so any user can authenticate
            // via platform clients (admin-portal, workspace-portal).
            if (_tenantContext.TenantId == Core.PlatformConstants.TenantId)
                return user;

            if (user.TenantId != _tenantContext.TenantId && user.TenantId != null)
            {
                return null;
            }
        }

        return user;
    }

    private OlusoUserInfo MapToUserInfo(OlusoUser user)
    {
        var linkedProviders = GetLinkedProvidersSync(user);
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
            LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd,
            AccessFailedCount = user.AccessFailedCount,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastLoginAt = user.LastLoginAt,
            PasswordChangedAt = user.PasswordChangedAt,
            Roles = GetRolesSync(user),
            CustomProperties = DeserializeCustomProperties(user.CustomPropertiesJson),
            IsFederated = linkedProviders.Count > 0,
            LinkedProviders = linkedProviders,
            HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
        };
    }

    private IReadOnlyList<string> GetLinkedProvidersSync(OlusoUser user)
    {
        // Sync over async — same pattern as GetRolesSync above. Used inside
        // synchronous mapping; the admin user-list endpoint accepts the
        // tradeoff. UserManager.GetLoginsAsync hits the AspNetUserLogins
        // table, indexed on UserId.
        var logins = _userManager.GetLoginsAsync(user).GetAwaiter().GetResult();
        if (logins.Count == 0) return Array.Empty<string>();
        return logins.Select(l => l.LoginProvider).ToArray();
    }

    /// <summary>
    /// Tenant gate applied during external-SSO auto-provisioning. By the
    /// time we get here the call site has already decided whether the user
    /// should be created — typically <c>ExternalCallback.cshtml.cs</c>'s
    /// <c>shouldCreateUser = isRegistration || autoProvision</c>, which
    /// honours the provider's <c>AutoProvisionUsers</c> as an explicit
    /// admin opt-in for that IdP. So we do NOT re-check
    /// <c>AllowSelfRegistration</c> here: that would override the
    /// admin's per-provider auto-provision intent (the canonical use
    /// case is enterprise SSO where the tenant disables open
    /// self-registration but JIT-provisions users coming from their
    /// corporate Okta/Azure AD).
    ///
    /// <c>AllowedEmailDomains</c> is enforced because it's a who-not-how
    /// constraint — even an admin-trusted IdP shouldn't be able to mint
    /// accounts from domains the tenant has barred.
    ///
    /// <c>RequireTermsAcceptance</c> needs a terms-acceptance interstitial
    /// page (no in-flow checkbox in an SSO redirect) — that's a UX change
    /// to add, not a policy gap to enforce silently. <c>RequireEmailVerification</c>
    /// is moot because external IdPs verify the address by definition;
    /// we already set <c>EmailConfirmed = true</c> when present.
    /// </summary>
    private static (string Code, string Message)? CheckExternalRegistrationPolicy(
        Core.Domain.Entities.Tenant? tenant, string? email)
    {
        if (tenant == null) return null;

        if (!string.IsNullOrEmpty(email)
            && !IsEmailDomainAllowedForTenant(tenant, email))
        {
            return ("email_domain_not_allowed",
                "Registrations from this email domain are not allowed.");
        }

        return null;
    }

    /// <summary>
    /// Centralised <c>AllowedEmailDomains</c> check used by
    /// <see cref="CreateUserAsync"/>. Empty / unset list means "any";
    /// the matching is case-insensitive on the comma / whitespace /
    /// semicolon-separated host list. Returns true when no tenant is
    /// supplied — caller is responsible for handling that case.
    /// </summary>
    private static bool IsEmailDomainAllowedForTenant(Core.Domain.Entities.Tenant? tenant, string email)
    {
        var allowed = tenant?.AllowedEmailDomains;
        if (string.IsNullOrWhiteSpace(allowed)) return true;
        if (string.IsNullOrEmpty(email)) return false;

        var atIndex = email.IndexOf('@');
        if (atIndex < 0 || atIndex == email.Length - 1) return false;
        var domain = email[(atIndex + 1)..].ToLowerInvariant();

        var domains = allowed
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant());

        return domains.Contains(domain);
    }

    private static string? SerializeCustomProperties(IDictionary<string, string>? properties)
    {
        if (properties == null || properties.Count == 0) return null;
        return JsonSerializer.Serialize(properties);
    }

    private static IDictionary<string, string>? DeserializeCustomProperties(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            // Corrupted or schema-drift payload — surface null rather than
            // failing the whole user read. Admin can repair via the UI.
            return null;
        }
    }

    private ValidatedUser MapToValidatedUser(OlusoUser user)
    {
        return new ValidatedUser
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            Email = user.Email ?? "",
            EmailVerified = user.EmailConfirmed,
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

        var newlyVerified = verified && !user.EmailConfirmed;
        user.EmailConfirmed = verified;
        var result = await _userManager.UpdateAsync(user);
        _logger.LogInformation("Set email verified to {Verified} for user {UserId}", verified, userId);

        if (newlyVerified && result.Succeeded)
        {
            await RaiseAsync(new UserEmailVerifiedEvent
            {
                TenantId = user.TenantId,
                SubjectId = user.Id,
                Email = user.Email ?? string.Empty
            }, cancellationToken);
        }
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

        var previous = user.Email;
        var result = await _userManager.SetEmailAsync(user, email);
        if (result.Succeeded)
        {
            _logger.LogInformation("Updated email for user {UserId}", userId);
            if (!string.Equals(previous, user.Email, StringComparison.Ordinal))
            {
                await RaiseAsync(new UserUpdatedEvent
                {
                    TenantId = user.TenantId,
                    SubjectId = user.Id,
                    Email = user.Email,
                    ChangedFields = new Dictionary<string, string> { ["email"] = user.Email ?? string.Empty }
                }, cancellationToken);
            }
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
        var previous = user.PhoneNumber;
        var result = await _userManager.SetPhoneNumberAsync(user, normalizedPhone);
        if (result.Succeeded)
        {
            _logger.LogInformation("Updated phone number for user {UserId}", userId);
            if (!string.Equals(previous, user.PhoneNumber, StringComparison.Ordinal))
            {
                await RaiseAsync(new UserUpdatedEvent
                {
                    TenantId = user.TenantId,
                    SubjectId = user.Id,
                    Email = user.Email,
                    ChangedFields = new Dictionary<string, string> { ["phoneNumber"] = user.PhoneNumber ?? string.Empty }
                }, cancellationToken);
            }
        }
        else
        {
            _logger.LogWarning("Failed to update phone number for user {UserId}: {Errors}",
                userId, string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    public async Task SetUserActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found for SetUserActiveAsync: {UserId}", userId);
            return;
        }

        if (user.IsActive == isActive)
        {
            return;
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _userManager.UpdateAsync(user);
        _logger.LogInformation("Set active status to {IsActive} for user {UserId}", isActive, userId);

        if (result.Succeeded)
        {
            await RaiseAsync(isActive
                ? new UserReactivatedEvent { TenantId = user.TenantId, SubjectId = user.Id, Email = user.Email }
                : new UserDeactivatedEvent { TenantId = user.TenantId, SubjectId = user.Id, Email = user.Email },
                cancellationToken);
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

        var resolvedTenantId = request.TenantId ?? _tenantContext.TenantId
            ?? throw new InvalidOperationException(
                "Cannot create external user without a tenant. Set request.TenantId or call within a tenant context.");

        if (resolvedTenantId == Core.PlatformConstants.TenantId)
        {
            return UserOperationResult.Failed("platform_tenant_reserved",
                "The platform tenant does not accept user registration.");
        }

        // Same tenant gate as ProcessExternalLoginAsync — this entry point
        // is reached from ExternalRegister.cshtml when the external IdP
        // didn't supply an email, but the tenant policy still applies.
        if (_tenantContext.HasTenant && _tenantContext.TenantId == resolvedTenantId)
        {
            var policyResult = CheckExternalRegistrationPolicy(_tenantContext.Tenant, request.Email);
            if (policyResult is { } policyError)
            {
                _logger.LogWarning(
                    "External register rejected for tenant {TenantId}: {Code}",
                    resolvedTenantId, policyError.Code);
                return UserOperationResult.Failed(policyError.Code, policyError.Message);
            }
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
            TenantId = resolvedTenantId,
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
        await RaiseCreatedAsync(newUser, request.ClientId, null, cancellationToken);
        return UserOperationResult.Success(newUser.Id, MapToUserInfo(newUser));
    }

    public async Task<IEnumerable<Core.Services.ExternalLoginInfo>> GetExternalLoginsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await FindUserInScopeAsync(userId, cancellationToken);
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
