using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using System.Security.Claims;
using System.Text.Json;

namespace Oluso.Core.Authentication;

/// <summary>
/// Claims principal factory for cookie authentication (ASP.NET Identity Sign-In).
/// Plugin claims and the derived authorization claims (super_admin, is_org_admin,
/// org_ids, org_roles, allowed_tenants) are sourced from IProfileService — the
/// same service the OIDC token path uses — so cookie and bearer sessions emit
/// identical claim shapes.
/// </summary>
public class OlusoClaimsPrincipalFactory : UserClaimsPrincipalFactory<OlusoUser, OlusoRole>
{
    private readonly ITenantContext _tenantContext;
    private readonly IServiceProvider _serviceProvider;

    // Resolve IProfileService lazily — direct injection creates a cycle:
    // IProfileService -> IOlusoUserService -> SignInManager -> IUserClaimsPrincipalFactory -> back here.
    public OlusoClaimsPrincipalFactory(
        UserManager<OlusoUser> userManager,
        RoleManager<OlusoRole> roleManager,
        IOptions<IdentityOptions> options,
        ITenantContext tenantContext,
        IServiceProvider serviceProvider)
        : base(userManager, roleManager, options)
    {
        _tenantContext = tenantContext;
        _serviceProvider = serviceProvider;
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

        // Plugin + authorization claims (super_admin, is_org_admin, org_ids,
        // org_roles, allowed_tenants) come from IProfileService — same source the
        // OIDC token path uses. Anything not already on the identity gets added.
        await AddProfileServiceClaimsAsync(identity, user);

        return identity;
    }

    private async Task AddProfileServiceClaimsAsync(ClaimsIdentity identity, OlusoUser user)
    {
        try
        {
            var profileService = _serviceProvider.GetRequiredService<IProfileService>();
            var context = new ProfileDataRequestContext
            {
                SubjectId = user.Id,
                Client = new Client { ClientId = "internal" },
                RequestedScopes = new[] { "openid", "profile", "email" },
                Caller = "CookieAuthentication"
            };

            await profileService.GetProfileDataAsync(context);

            foreach (var claim in context.IssuedClaims)
            {
                if (!identity.HasClaim(c => c.Type == claim.Type && c.Value == claim.Value))
                {
                    identity.AddClaim(claim);
                }
            }

            // Permissions: cookies have no OIDC scope concept and the in-app UI
            // relies on the permissions claim for live render-time gates, so we
            // always include them (no scope-gating like the token path does).
            var permissions = await profileService.GetUserPermissionsAsync(user.Id, user.TenantId);
            if (permissions.Count > 0)
            {
                var permissionsJson = JsonSerializer.Serialize(permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                identity.AddClaim(new Claim("permissions", permissionsJson));
            }
        }
        catch
        {
            // Don't fail cookie authentication if profile service errors —
            // the user can still sign in with the base claims set.
        }
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
        // Non-permission role claims only. The "permissions" claim comes from
        // IProfileService.GetUserPermissionsAsync (AddProfileServiceClaimsAsync),
        // which includes the organization-admin grant derived from membership.
        var roles = await UserManager.GetRolesAsync(user);

        foreach (var roleName in roles)
        {
            var role = await RoleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                var roleClaims = await RoleManager.GetClaimsAsync(role);
                foreach (var claim in roleClaims)
                {
                    if (claim.Type == OlusoRole.PermissionClaimType)
                    {
                        continue;
                    }
                    else if (ReservedClaimKeys.IsReserved(claim.Type))
                    {
                        // ReservedAuthInterceptor blocks writing these, but a row
                        // predating that guard must not reach the identity —
                        // super_admin / allowed_tenants here would be escalation.
                        continue;
                    }
                    else if (claim.Type == OlusoRole.EntitlementClaimType)
                    {
                        // Entitlements are for the tenant's own applications and
                        // reach them through the scope-gated token path. This is
                        // Oluso's session cookie — nothing here reads them, so
                        // including them would only inflate the cookie.
                        continue;
                    }
                    else if (!identity.HasClaim(c => c.Type == claim.Type && c.Value == claim.Value))
                    {
                        identity.AddClaim(claim);
                    }
                }
            }
        }
    }

}
