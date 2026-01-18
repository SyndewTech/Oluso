using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Default implementation of IProfileService that collects claims from plugins.
/// Uses IClaimsProviderRegistry to gather claims from all enabled plugins.
/// Implements IExtendedProfileService for role and permission retrieval.
/// </summary>
public class DefaultProfileService : IExtendedProfileService
{
    private readonly IOlusoUserService _userService;
    private readonly IRoleStore _roleStore;
    private readonly IClaimsProviderRegistry _claimsProviderRegistry;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<DefaultProfileService> _logger;

    public DefaultProfileService(
        IOlusoUserService userService,
        IRoleStore roleStore,
        IClaimsProviderRegistry claimsProviderRegistry,
        ITenantContext tenantContext,
        ILogger<DefaultProfileService> logger)
    {
        _userService = userService;
        _roleStore = roleStore;
        _claimsProviderRegistry = claimsProviderRegistry;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task GetProfileDataAsync(ProfileDataRequestContext context)
    {
        _logger.LogDebug(
            "Getting profile data for subject {SubjectId}, caller: {Caller}",
            context.SubjectId, context.Caller);

        // Get base user info
        var user = await _userService.FindByIdAsync(context.SubjectId);
        if (user == null)
        {
            _logger.LogWarning("User {SubjectId} not found", context.SubjectId);
            return;
        }

        var scopes = context.RequestedScopes.ToList();

        // Add standard claims based on scopes
        AddStandardClaims(context, user, scopes);

        // Get claims from all enabled plugins
        var pluginClaims = await _claimsProviderRegistry.GetAllClaimsAsync(new ClaimsProviderContext
        {
            SubjectId = context.SubjectId,
            TenantId = _tenantContext.TenantId,
            ClientId = context.Client.ClientId,
            Scopes = scopes,
            Caller = context.Caller,
            Protocol = context.Protocol
        });

        // Add plugin claims
        foreach (var claim in pluginClaims)
        {
            var value = claim.Value is string s ? s : System.Text.Json.JsonSerializer.Serialize(claim.Value);
            context.IssuedClaims.Add(new Claim(claim.Key, value));
        }

        _logger.LogDebug(
            "Issued {ClaimCount} claims for subject {SubjectId}",
            context.IssuedClaims.Count, context.SubjectId);
    }

    public async Task IsActiveAsync(IsActiveContext context)
    {
        var user = await _userService.FindByIdAsync(context.SubjectId);
        context.IsActive = user?.IsActive ?? false;
    }

    public async Task<ICollection<string>> GetUserRolesAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        var user = await _userService.FindByIdAsync(subjectId);
        if (user?.Roles == null)
        {
            return Array.Empty<string>();
        }

        return user.Roles.ToList();
    }

    public async Task<ICollection<string>> GetUserPermissionsAsync(string subjectId, string? tenantId, CancellationToken cancellationToken = default)
    {
        var roles = await GetUserRolesAsync(subjectId, cancellationToken);
        if (roles.Count == 0)
        {
            return Array.Empty<string>();
        }

        var allPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in roles)
        {
            // Try tenant-specific role first
            var role = await _roleStore.GetByNameAsync(roleName, tenantId);
            if (role == null)
            {
                // Try global role
                role = await _roleStore.GetByNameAsync(roleName, null);
            }

            if (role != null)
            {
                var permissions = role.GetPermissions();
                foreach (var permission in permissions)
                {
                    allPermissions.Add(permission);
                }
            }
        }

        _logger.LogDebug(
            "Collected {PermissionCount} permissions for user {SubjectId} from {RoleCount} roles",
            allPermissions.Count, subjectId, roles.Count);

        return allPermissions.ToList();
    }

    private static void AddStandardClaims(
        ProfileDataRequestContext context,
        OlusoUserInfo user,
        List<string> scopes)
    {
        // profile scope
        if (scopes.Contains("profile"))
        {
            if (!string.IsNullOrEmpty(user.DisplayName))
            {
                context.IssuedClaims.Add(new Claim("name", user.DisplayName));
            }

            if (!string.IsNullOrEmpty(user.FirstName))
            {
                context.IssuedClaims.Add(new Claim("given_name", user.FirstName));
            }

            if (!string.IsNullOrEmpty(user.LastName))
            {
                context.IssuedClaims.Add(new Claim("family_name", user.LastName));
            }

            if (!string.IsNullOrEmpty(user.Picture))
            {
                context.IssuedClaims.Add(new Claim("picture", user.Picture));
            }
        }

        // email scope
        if (scopes.Contains("email"))
        {
            if (!string.IsNullOrEmpty(user.Email))
            {
                context.IssuedClaims.Add(new Claim("email", user.Email));
                context.IssuedClaims.Add(new Claim("email_verified", user.EmailVerified.ToString().ToLower()));
            }
        }

        // phone scope
        if (scopes.Contains("phone"))
        {
            if (!string.IsNullOrEmpty(user.PhoneNumber))
            {
                context.IssuedClaims.Add(new Claim("phone_number", user.PhoneNumber));
                context.IssuedClaims.Add(new Claim("phone_number_verified", user.PhoneNumberVerified.ToString().ToLower()));
            }
        }

        // Add roles if available
        if (user.Roles != null)
        {
            foreach (var role in user.Roles)
            {
                context.IssuedClaims.Add(new Claim("role", role));
            }
        }
    }
}
