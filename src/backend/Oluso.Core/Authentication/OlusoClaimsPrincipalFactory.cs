using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using System.Security.Claims;
using System.Text.Json;

namespace Oluso.Core.Authentication;

/// <summary>
/// Claims principal factory that adds tenant claims, organization claims, and plugin-provided claims to the user identity.
/// This is used for cookie authentication (ASP.NET Identity Sign-In) flows.
///
/// For token-based authentication (OIDC), claims are collected via IClaimsProviderRegistry in TokenService.
/// This factory ensures cookie sessions also have plugin claims available.
/// </summary>
public class OlusoClaimsPrincipalFactory : UserClaimsPrincipalFactory<OlusoUser, OlusoRole>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClaimsProviderRegistry _claimsProviderRegistry;
    private readonly IOrganizationMembershipStore? _organizationMembershipStore;

    public OlusoClaimsPrincipalFactory(
        UserManager<OlusoUser> userManager,
        RoleManager<OlusoRole> roleManager,
        IOptions<IdentityOptions> options,
        ITenantContext tenantContext,
        IClaimsProviderRegistry claimsProviderRegistry,
        IOrganizationMembershipStore? organizationMembershipStore = null)
        : base(userManager, roleManager, options)
    {
        _tenantContext = tenantContext;
        _claimsProviderRegistry = claimsProviderRegistry;
        _organizationMembershipStore = organizationMembershipStore;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(OlusoUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // Add tenant claim from user or current context
        var tenantId = user.TenantId ?? _tenantContext.TenantId;
        if (!string.IsNullOrEmpty(tenantId))
        {
            identity.AddClaim(new Claim("tenant_id", tenantId));
        }

        // Add user's tenant name if available
        if (_tenantContext.HasTenant && _tenantContext.Tenant != null)
        {
            identity.AddClaim(new Claim("tenant_name", _tenantContext.Tenant.Name));
        }

        // Add standard user claims
        AddUserProfileClaims(identity, user);

        // Add email claims
        AddEmailClaims(identity, user);

        // Add phone claims
        AddPhoneClaims(identity, user);

        // Add custom user claims from the Identity Claims table
        await AddUserClaimsAsync(identity, user);

        // Add role claims from the roles the user belongs to
        await AddRoleClaimsAsync(identity, user);

        // Add claims from plugins via IClaimsProviderRegistry
        await AddPluginClaimsAsync(identity, user, tenantId);

        // Add organization claims
        await AddOrganizationClaimsAsync(identity, user);

        return identity;
    }

    /// <summary>
    /// Adds OIDC-standard profile claims.
    /// Uses short claim names per OIDC Core spec (given_name, family_name, etc.)
    /// SAML assertions use their own claim mapping in the SAML module.
    /// </summary>
    private static void AddUserProfileClaims(ClaimsIdentity identity, OlusoUser user)
    {
        // OIDC standard claims - https://openid.net/specs/openid-connect-core-1_0.html#StandardClaims
        if (!string.IsNullOrEmpty(user.FirstName))
        {
            identity.AddClaim(new Claim("given_name", user.FirstName));
        }

        if (!string.IsNullOrEmpty(user.LastName))
        {
            identity.AddClaim(new Claim("family_name", user.LastName));
        }

        if (!string.IsNullOrEmpty(user.DisplayName))
        {
            identity.AddClaim(new Claim("name", user.DisplayName));
        }
        else if (!string.IsNullOrEmpty(user.FirstName) || !string.IsNullOrEmpty(user.LastName))
        {
            identity.AddClaim(new Claim("name", $"{user.FirstName} {user.LastName}".Trim()));
        }

        if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
        {
            identity.AddClaim(new Claim("picture", user.ProfilePictureUrl));
        }

        if (!string.IsNullOrEmpty(user.Locale))
        {
            identity.AddClaim(new Claim("locale", user.Locale));
        }

        if (!string.IsNullOrEmpty(user.TimeZone))
        {
            identity.AddClaim(new Claim("zoneinfo", user.TimeZone));
        }
    }

    private static void AddEmailClaims(ClaimsIdentity identity, OlusoUser user)
    {
        if (!string.IsNullOrEmpty(user.Email) && !identity.HasClaim(c => c.Type == "email"))
        {
            identity.AddClaim(new Claim("email", user.Email));
            identity.AddClaim(new Claim("email_verified", user.EmailConfirmed.ToString().ToLowerInvariant()));
        }
    }

    private static void AddPhoneClaims(ClaimsIdentity identity, OlusoUser user)
    {
        if (!string.IsNullOrEmpty(user.PhoneNumber))
        {
            identity.AddClaim(new Claim("phone_number", user.PhoneNumber));
            identity.AddClaim(new Claim("phone_number_verified", user.PhoneNumberConfirmed.ToString().ToLowerInvariant()));
        }
    }

    private async Task AddUserClaimsAsync(ClaimsIdentity identity, OlusoUser user)
    {
        var userClaims = await UserManager.GetClaimsAsync(user);
        foreach (var claim in userClaims)
        {
            // Avoid duplicating claims already in the identity
            if (!identity.HasClaim(c => c.Type == claim.Type && c.Value == claim.Value))
            {
                identity.AddClaim(claim);
            }
        }
    }

    private async Task AddRoleClaimsAsync(ClaimsIdentity identity, OlusoUser user)
    {
        var roles = await UserManager.GetRolesAsync(user);
        foreach (var roleName in roles)
        {
            var role = await RoleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                var roleClaims = await RoleManager.GetClaimsAsync(role);
                foreach (var claim in roleClaims)
                {
                    if (!identity.HasClaim(c => c.Type == claim.Type && c.Value == claim.Value))
                    {
                        identity.AddClaim(claim);
                    }
                }
            }
        }
    }

    private async Task AddPluginClaimsAsync(ClaimsIdentity identity, OlusoUser user, string? tenantId)
    {
        try
        {
            var context = new ClaimsProviderContext
            {
                SubjectId = user.Id,
                TenantId = tenantId,
                Caller = "CookieAuthentication",
                // For cookie auth, we don't have specific scopes - plugins should provide all claims they have
                Scopes = new[] { "openid", "profile", "email" }
            };

            var pluginClaims = await _claimsProviderRegistry.GetAllClaimsAsync(context);

            foreach (var claim in pluginClaims)
            {
                var claimValue = claim.Value?.ToString() ?? "";
                if (!identity.HasClaim(c => c.Type == claim.Key && c.Value == claimValue))
                {
                    identity.AddClaim(new Claim(claim.Key, claimValue));
                }
            }
        }
        catch
        {
            // Don't fail authentication if plugin claims fail
            // The core authentication should still work
        }
    }

    /// <summary>
    /// Adds organization-related claims to the identity.
    /// These claims enable organization-based access control and tenant switching.
    /// </summary>
    private async Task AddOrganizationClaimsAsync(ClaimsIdentity identity, OlusoUser user)
    {
        if (_organizationMembershipStore == null)
        {
            return;
        }

        try
        {
            // Get all organizations the user is a member of
            var memberships = await _organizationMembershipStore.GetByUserAsync(user.Id);
            var membershipList = memberships.ToList();

            if (membershipList.Count == 0)
            {
                return;
            }

            // Add organization IDs as a claim (multiple values if member of multiple orgs)
            foreach (var membership in membershipList)
            {
                identity.AddClaim(new Claim("org_id", membership.OrganizationId));
            }

            // Add organization roles (format: org_id:role)
            foreach (var membership in membershipList)
            {
                var roleValue = $"{membership.OrganizationId}:{membership.Role.ToString().ToLowerInvariant()}";
                identity.AddClaim(new Claim("org_role", roleValue));
            }

            // Add organization names for display purposes
            foreach (var membership in membershipList)
            {
                if (membership.Organization != null)
                {
                    identity.AddClaim(new Claim("org_name", membership.Organization.Name));
                }
            }

            // Add allowed tenant IDs (aggregated from all memberships)
            var allowedTenantIds = new HashSet<string>();
            foreach (var membership in membershipList)
            {
                var tenantIds = await _organizationMembershipStore.GetAllowedTenantIdsAsync(
                    user.Id, membership.OrganizationId);
                foreach (var tenantId in tenantIds)
                {
                    allowedTenantIds.Add(tenantId);
                }
            }

            if (allowedTenantIds.Count > 0)
            {
                // Add as JSON array for structured data
                var tenantsJson = JsonSerializer.Serialize(allowedTenantIds);
                identity.AddClaim(new Claim("allowed_tenants", tenantsJson));
            }

            // Check if user is an org admin (owner or admin in any org)
            var isOrgAdmin = membershipList.Any(m =>
                m.Role == OrganizationRole.Owner || m.Role == OrganizationRole.Admin);
            if (isOrgAdmin)
            {
                identity.AddClaim(new Claim("is_org_admin", "true"));
            }
        }
        catch
        {
            // Don't fail authentication if organization claims fail
            // The core authentication should still work
        }
    }
}
