using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Default implementation of IProfileService that collects claims from plugins.
/// Uses IClaimsProviderRegistry to gather claims from all enabled plugins.
/// Implements IExtendedProfileService for role and permission retrieval.
///
/// Single source of truth for token AND cookie claim emission — including the
/// derived authorization claims (super_admin, is_org_admin, org_ids, org_roles,
/// allowed_tenants). TokenService and OlusoClaimsPrincipalFactory both call
/// here, so cookie sessions and bearer tokens stay in sync by construction.
/// </summary>
public class DefaultProfileService : IExtendedProfileService
{
    private readonly IOlusoUserService _userService;
    private readonly IRoleStore _roleStore;
    private readonly IClaimsProviderRegistry _claimsProviderRegistry;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantStore _tenantStore;
    private readonly IResourceStore _resourceStore;
    private readonly IOrganizationMembershipStore? _organizationMembershipStore;
    private readonly ITenantMembershipStore? _tenantMembershipStore;
    private readonly IUserModuleAccessStore? _moduleAccessStore;
    private readonly IEntitlementStore? _entitlementStore;
    private readonly IIdentityScopeClaimResolver _identityScopeClaims;
    private readonly IEffectiveRoleResolver _effectiveRoles;
    private readonly ITokenTenantResolver _tokenTenants;
    private readonly ITenantSettingsProvider? _tenantSettings;
    private readonly ILogger<DefaultProfileService> _logger;

    public DefaultProfileService(
        IOlusoUserService userService,
        IRoleStore roleStore,
        IClaimsProviderRegistry claimsProviderRegistry,
        ITenantContext tenantContext,
        ITenantStore tenantStore,
        IResourceStore resourceStore,
        ILogger<DefaultProfileService> logger,
        IOrganizationMembershipStore? organizationMembershipStore = null,
        ITenantMembershipStore? tenantMembershipStore = null,
        IUserModuleAccessStore? moduleAccessStore = null,
        IEntitlementStore? entitlementStore = null,
        IIdentityScopeClaimResolver? identityScopeClaims = null,
        IEffectiveRoleResolver? effectiveRoles = null,
        ITokenTenantResolver? tokenTenants = null,
        ITenantSettingsProvider? tenantSettings = null)
    {
        _userService = userService;
        _roleStore = roleStore;
        _claimsProviderRegistry = claimsProviderRegistry;
        _tenantContext = tenantContext;
        _tenantStore = tenantStore;
        _resourceStore = resourceStore;
        _organizationMembershipStore = organizationMembershipStore;
        _tenantMembershipStore = tenantMembershipStore;
        _moduleAccessStore = moduleAccessStore;
        _entitlementStore = entitlementStore;
        _identityScopeClaims = identityScopeClaims ?? new IdentityScopeClaimResolver(resourceStore);
        _effectiveRoles = effectiveRoles ?? new EffectiveRoleResolver(roleStore, tenantStore, organizationMembershipStore);
        _tokenTenants = tokenTenants ?? new TokenTenantResolver(
            userService, tenantContext, tenantStore, logger, tenantMembershipStore, organizationMembershipStore);
        _tenantSettings = tenantSettings;
        _logger = logger;
    }

    public async Task GetProfileDataAsync(ProfileDataRequestContext context)
    {
        _logger.LogDebug(
            "Getting profile data for subject {SubjectId}, caller: {Caller}",
            context.SubjectId, context.Caller);

        var user = await FindSubjectAsync(context.SubjectId);
        if (user == null)
        {
            _logger.LogWarning("User {SubjectId} not found", context.SubjectId);
            return;
        }

        var scopes = context.RequestedScopes.ToList();

        // Emit user claims gated by the granted scopes/resources. Honours the
        // configured IdentityResource / ApiScope / Resource UserClaims, falling
        // back to the well-known OIDC defaults for standard scopes that have no
        // DB row. See AddScopedUserClaimsAsync for the resolution rules.
        await AddScopedUserClaimsAsync(context, user, scopes);

        // Add org/authz claims (super_admin, is_org_admin, org_ids, org_roles,
        // allowed_tenants). Single source of truth — cookie path and token path
        // both call here, so they stay in sync.
        await AddAuthorizationClaimsAsync(context, user);

        // Get claims from all enabled plugins
        var pluginClaims = await _claimsProviderRegistry.GetAllClaimsAsync(new ClaimsProviderContext
        {
            SubjectId = context.SubjectId,
            TenantId = context.TenantId ?? _tenantContext.TenantId,
            ClientId = context.Client.ClientId,
            Scopes = scopes,
            Caller = context.Caller,
            Protocol = context.Protocol,
            AdditionalData = context.AdditionalData
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
        var user = await FindSubjectAsync(context.SubjectId);
        if (user == null || !user.IsActive)
        {
            context.IsActive = false;
            return;
        }

        // Defense in depth: a user whose home tenant has been disabled by an
        // admin must not pass IsActive checks even if their token's other
        // signals (signature, expiry, audience) are still valid. Token
        // refresh and userinfo calls hit this path, so flipping a tenant to
        // disabled invalidates its users' sessions on the next refresh.
        if (!string.IsNullOrEmpty(user.TenantId))
        {
            var tenant = await _tenantStore.GetByIdAsync(user.TenantId);
            if (tenant != null && !tenant.Enabled)
            {
                _logger.LogWarning(
                    "IsActive=false for user {SubjectId}: tenant {TenantId} is disabled",
                    context.SubjectId, user.TenantId);
                context.IsActive = false;
                return;
            }
        }

        context.IsActive = true;
    }

    /// <summary>
    /// The tenant a token is being minted for: the tenant the caller names
    /// (<see cref="ProfileDataRequestContext.TenantId"/>), else the ambient tenant,
    /// when the user is entitled to it — SuperAdmin, a TenantMembership there, or Owner/Admin of
    /// the organization that owns it (the same rule TenantResolutionMiddleware
    /// admits requests by) — otherwise their home tenant. See <see cref="ITokenTenantResolver"/>.
    /// </summary>
    private Task<string?> ResolveTokenTenantAsync(OlusoUserInfo user, string? requestedTenantId = null)
        => _tokenTenants.ResolveAsync(user, requestedTenantId);

    /// <summary>
    /// The user a subject id names, whatever their home tenant. The ambient tenant filter only sees the tenant's
    /// home users and members, so an organization owner or admin, or a SuperAdmin, acting in another tenant was
    /// "not found" and got a token with no claims. Subject ids here come from an authenticated grant or token,
    /// never from request input; whether the user may act in the ambient tenant is
    /// <see cref="ResolveTokenTenantAsync"/>'s decision, the same rule TenantResolutionMiddleware applies.
    /// </summary>
    private async Task<OlusoUserInfo?> FindSubjectAsync(string subjectId, CancellationToken cancellationToken = default) =>
        (await _userService.FindByIdsAcrossTenantsAsync(new[] { subjectId }, cancellationToken)).GetValueOrDefault(subjectId);

    public async Task<ICollection<string>> GetUserRolesAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        var user = await FindSubjectAsync(subjectId, cancellationToken);
        if (user == null)
        {
            return Array.Empty<string>();
        }

        // user.Roles comes from UserManager, which joins the Roles set and so
        // inherits its query filter — and the platform meta-tenant switches that
        // filter off. Minting from a platform client (admin-portal,
        // workspace-portal) therefore yielded the union of the user's roles
        // across every tenant. Scope to the tenant this token is for, plus
        // global roles.
        var tenantId = await ResolveTokenTenantAsync(user);

        var roles = await _roleStore.GetUserRolesInTenantAsync(subjectId, tenantId, cancellationToken);
        return roles.Select(r => r.Name!).Where(n => n != null).ToList();
    }

    public async Task<ICollection<string>> GetUserPermissionsAsync(string subjectId, string? tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GetUserPermissionsAsync called for user {SubjectId}, tenantId={TenantId}", subjectId, tenantId ?? "(null)");

        // Role ROWS held in this tenant (never names, which are ambiguous across
        // tenants), plus the organization-admin grant derived from membership.
        var permissions = await _effectiveRoles.GetEffectivePermissionsAsync(subjectId, tenantId, cancellationToken);

        _logger.LogInformation("Collected {PermissionCount} permissions for user {SubjectId} in tenant {TenantId}",
            permissions.Count, subjectId, tenantId ?? "(none)");

        return permissions.ToList();
    }


    /// <summary>
    /// Emits the user's identity claims gated by the granted scopes and requested
    /// resources. The allowed claim types come from configured IdentityResource /
    /// ApiScope / Resource UserClaims (see <see cref="ResolveAllowedClaimTypesAsync"/>),
    /// with the standard OIDC defaults as a fallback. tenant_id and role are NOT
    /// scope-gated — they are platform claims required on every user token.
    /// </summary>
    private async Task AddScopedUserClaimsAsync(
        ProfileDataRequestContext context,
        OlusoUserInfo user,
        List<string> scopes)
    {
        var allowedClaimTypes = await ResolveAllowedClaimTypesAsync(scopes, context.RequestedResources);
        if (allowedClaimTypes.Count > 0)
        {
            foreach (var (type, value) in BuildUserClaimPool(user))
            {
                if (allowedClaimTypes.Contains(type))
                {
                    context.IssuedClaims.Add(new Claim(type, value));
                }
            }

            await AddScopedRoleClaimsAsync(context, user, allowedClaimTypes);
        }

        // tenant_id and role are platform claims, not gated by identity scopes:
        // TenantResolutionMiddleware and permission checks depend on them being
        // present on every user token regardless of the requested scope set.
        //
        // tenant_id names the tenant the token is FOR, which is not always the
        // user's home tenant — a token minted after a tenant switch is for the
        // target.
        var tokenTenantId = await ResolveTokenTenantAsync(user, context.TenantId);

        if (!string.IsNullOrEmpty(tokenTenantId))
        {
            context.IssuedClaims.Add(new Claim("tenant_id", tokenTenantId));
        }

        // Roles assigned in the token's tenant (SuperAdmin/SystemAdmin apply in
        // every tenant). user.Roles is read for the ambient tenant, which is not
        // always the tenant this token is for.
        var tokenRoles = await _roleStore.GetUserRolesInTenantAsync(user.Id, tokenTenantId);
        foreach (var roleName in tokenRoles.Select(r => r.Name).OfType<string>())
        {
            context.IssuedClaims.Add(new Claim("role", roleName));
        }
    }

    /// <summary>
    /// Emits the non-permission claims carried by the user's roles, gated by the
    /// same scope/resource rules as user claims. This is how a tenant models its
    /// own application's authorization (e.g. an <c>app_permissions</c> claim on a
    /// role): the value only reaches a token when a client requests a scope whose
    /// UserClaims list the claim type.
    ///
    /// Permission claims are excluded — those are aggregated into the
    /// <c>permissions</c> claim under the permissions scope, and platform
    /// authorization matches them with wildcards. Reserved keys are excluded as
    /// defense in depth: ReservedAuthInterceptor blocks writing them, but a row
    /// predating that guard must not become an escalation path.
    /// </summary>
    private async Task AddScopedRoleClaimsAsync(
        ProfileDataRequestContext context,
        OlusoUserInfo user,
        HashSet<string> allowedClaimTypes)
    {
        // Role ROWS the user holds in the token's tenant, not names — same
        // resolution as GetUserRolesAsync and the role claim.
        var tenantId = await ResolveTokenTenantAsync(user, context.TenantId);

        var roles = (await _roleStore.GetUserRolesInTenantAsync(user.Id, tenantId)).ToList();
        if (roles.Count == 0)
        {
            return;
        }

        var emitted = new HashSet<(string Type, string Value)>();
        var candidates = new List<RoleClaim>();

        foreach (var role in roles)
        {
            foreach (var claim in await _roleStore.GetRoleClaimsAsync(role.Id))
            {
                if (claim.Type == OlusoRole.PermissionClaimType
                    || ReservedClaimKeys.IsReserved(claim.Type)
                    || !allowedClaimTypes.Contains(claim.Type))
                {
                    continue;
                }

                if (!emitted.Add((claim.Type, claim.Value)))
                {
                    continue;
                }

                candidates.Add(claim);
            }
        }

        var withheld = await ResolveWithheldEntitlementsAsync(candidates, tenantId);

        foreach (var claim in candidates)
        {
            if (claim.Type == OlusoRole.EntitlementClaimType && withheld.Contains(claim.Value))
            {
                continue;
            }

            context.IssuedClaims.Add(new Claim(claim.Type, claim.Value));
        }
    }

    /// <summary>
    /// Entitlement values that must not be issued: retired from the tenant's
    /// catalog, or still listed but disabled. Lets an admin switch one off
    /// without editing every role that holds it, and stops a deleted one
    /// lingering in tokens via a stale role claim.
    ///
    /// Costs one indexed lookup, and only when a granted scope actually released
    /// the entitlements claim — token issuance is otherwise untouched.
    /// </summary>
    private async Task<HashSet<string>> ResolveWithheldEntitlementsAsync(
        IReadOnlyCollection<RoleClaim> candidates,
        string? tenantId)
    {
        var assigned = candidates
            .Where(c => c.Type == OlusoRole.EntitlementClaimType)
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (assigned.Count == 0 || _entitlementStore == null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var live = (await _entitlementStore.FindByNamesAsync(tenantId, assigned))
            .Where(e => e.Enabled)
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        return assigned.Where(a => !live.Contains(a)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolves the set of user-claim types allowed for the granted scopes and
    /// requested resources. Per scope: a configured IdentityResource row
    /// contributes its UserClaims and overrides the standard default; a standard
    /// OIDC scope with no row falls back to <see cref="IdentityScopeClaimResolver.StandardScopeClaimTypes"/>;
    /// an ApiScope row contributes its UserClaims additively. Each requested
    /// resource (RFC 8707) contributes its Resource.UserClaims.
    /// </summary>
    private async Task<HashSet<string>> ResolveAllowedClaimTypesAsync(
        IReadOnlyCollection<string> scopes,
        IEnumerable<string>? resources)
    {
        // Identity half comes from the shared resolver — the same one the ID
        // token filters on, so the two can't disagree about what an identity
        // claim is.
        var allowed = await _identityScopeClaims.ResolveAsync(scopes);

        if (scopes.Count > 0)
        {
            // Keyed by the scope value, so an instance of a prefix scope takes the prefix scope's claims.
            var defaultSuffixMaxLength = await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(_tenantSettings);
            var apiScopes = (await ApiScopePrefixes.ResolveAsync(_resourceStore, scopes, default, defaultSuffixMaxLength))
                .ToDictionary(kv => kv.Key, kv => kv.Value.UserClaims.Select(c => c.Type).ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var scope in scopes)
            {
                if (apiScopes.TryGetValue(scope, out var apiClaims))
                {
                    allowed.UnionWith(apiClaims);
                }
            }
        }

        var resourceList = resources?.Where(r => !string.IsNullOrEmpty(r)).ToList();
        if (resourceList is { Count: > 0 })
        {
            foreach (var resource in await _resourceStore.FindResourcesByUrisAsync(resourceList))
            {
                allowed.UnionWith(resource.UserClaims.Select(c => c.Type));
            }
        }

        return allowed;
    }

    /// <summary>
    /// The pool of claim type/value pairs available for this user — standard
    /// profile fields plus any custom properties. Scope/resource filtering in
    /// <see cref="ResolveAllowedClaimTypesAsync"/> decides which are emitted.
    /// </summary>
    private static IEnumerable<(string Type, string Value)> BuildUserClaimPool(OlusoUserInfo user)
    {
        if (!string.IsNullOrEmpty(user.DisplayName))
        {
            yield return ("name", user.DisplayName);
        }
        if (!string.IsNullOrEmpty(user.FirstName))
        {
            yield return ("given_name", user.FirstName);
        }
        if (!string.IsNullOrEmpty(user.LastName))
        {
            yield return ("family_name", user.LastName);
        }
        // The account's username (the email when it has no other): relying parties that make their own handle, such as
        // a Matrix homeserver building its user ID, take it from here.
        if (!string.IsNullOrEmpty(user.Username))
        {
            yield return ("preferred_username", user.Username);
        }
        if (!string.IsNullOrEmpty(user.Picture))
        {
            yield return ("picture", user.Picture);
        }
        if (!string.IsNullOrEmpty(user.Email))
        {
            yield return ("email", user.Email);
            yield return ("email_verified", user.EmailVerified.ToString().ToLower());
        }
        if (!string.IsNullOrEmpty(user.PhoneNumber))
        {
            yield return ("phone_number", user.PhoneNumber);
            yield return ("phone_number_verified", user.PhoneNumberVerified.ToString().ToLower());
        }

        if (user.CustomProperties != null)
        {
            foreach (var kvp in user.CustomProperties)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    yield return (kvp.Key, kvp.Value);
                }
            }
        }
    }

    /// <summary>
    /// Emits the derived authorization claims that drive TenantResolutionMiddleware
    /// and downstream guards: super_admin, is_org_admin, org_ids, org_roles,
    /// allowed_tenants. Centralized here so token issuance and cookie sign-in
    /// produce the same shape — no drift between bearer and cookie sessions.
    /// </summary>
    private async Task AddAuthorizationClaimsAsync(ProfileDataRequestContext context, OlusoUserInfo user)
    {
        // super_admin: any user with the SuperAdmin or SystemAdmin role. The
        // role itself is system-scoped (seeded with TenantId=null), so the role
        // name acts as the platform-wide privilege gate.
        if (user.Roles != null)
        {
            var hasSuperAdminRole = user.Roles.Any(r =>
                r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase));
            if (hasSuperAdminRole)
            {
                context.IssuedClaims.Add(new Claim("super_admin", "true"));
            }
        }

        // Organization claims drive OrgAdmin tenant access. We emit org_ids as
        // multi-value claims (each org_id is its own Claim entry) so they
        // collapse into a List<string> at the dictionary stage and TokenService
        // emits them as a typed JsonArray claim in the JWT. org_roles stays a
        // pre-serialized JSON dict because key/value mappings don't fit the
        // multi-Claim model.
        var memberships = _organizationMembershipStore != null
            ? (await _organizationMembershipStore.GetByUserAsync(user.Id)).ToList()
            : new List<OrganizationMembership>();
        if (memberships.Count > 0)
        {
            foreach (var orgId in memberships.Select(m => m.OrganizationId).Distinct())
            {
                context.IssuedClaims.Add(new Claim("org_ids", orgId));
            }

            var orgRoles = memberships
                .GroupBy(m => m.OrganizationId)
                .ToDictionary(g => g.Key, g => g.First().Role.ToString().ToLowerInvariant());
            // Tagged as JSON so it reaches the token as an object rather than
            // a quoted string that clients have to unwrap by hand.
            context.IssuedClaims.Add(new Claim(
                "org_roles", JsonSerializer.Serialize(orgRoles), ProfileClaimValueTypes.Json));

            var isOrgAdmin = memberships.Any(m =>
                m.Role == OrganizationRole.Owner || m.Role == OrganizationRole.Admin);
            if (isOrgAdmin)
            {
                context.IssuedClaims.Add(new Claim("is_org_admin", "true"));
            }
        }

        // allowed_tenants: TenantMembership rows ∪ user.TenantId ∪ the tenants of
        // organizations the user owns or administers. The home tenant is included
        // by construction so the middleware's allowed_tenants gate works without
        // the legacy jwt-tenant_id-matches-requested-tenant fallback. Organization
        // owners and admins reach every tenant of their organization without a
        // membership row in each (the middleware admits them there), so those
        // tenants are listed too; otherwise everything that reads this claim
        // treats them as outsiders in tenants they run.
        // Emitted as multi-value claims (one per tenant id) for the same JsonArray
        // semantics as org_ids.
        var allowedTenantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var organizationId in memberships
            .Where(m => m.Role == OrganizationRole.Owner || m.Role == OrganizationRole.Admin)
            .Select(m => m.OrganizationId)
            .Distinct())
        {
            foreach (var tenant in await _tenantStore.GetByOrganizationAsync(organizationId))
            {
                allowedTenantIds.Add(tenant.Id);
            }
        }
        if (_tenantMembershipStore != null)
        {
            var tenantMemberships = await _tenantMembershipStore.GetByUserAsync(user.Id);
            foreach (var tm in tenantMemberships)
            {
                allowedTenantIds.Add(tm.TenantId);
            }
        }
        if (!string.IsNullOrEmpty(user.TenantId))
        {
            allowedTenantIds.Add(user.TenantId);
        }
        foreach (var tenantId in allowedTenantIds)
        {
            context.IssuedClaims.Add(new Claim("allowed_tenants", tenantId));
        }


        // module_seats: per-(tenant, user) module access rows. Drives the
        // workspace app switcher / nav visibility (requiresSeat on the
        // frontend) without a DB call per request — same pattern as
        // allowed_tenants.
        //
        // Seats belong to the tenant the token is being minted FOR, not to the
        // user's home tenant. Emitting home-tenant seats meant a token minted
        // for another tenant advertised the wrong modules — and because platform
        // clients mint under the meta-tenant, "home" was the only answer anyone
        // ever got.
        var seatTenantId = await ResolveTokenTenantAsync(user, context.TenantId);

        if (_moduleAccessStore != null && !string.IsNullOrEmpty(seatTenantId))
        {
            var seats = await _moduleAccessStore.GetUserModulesAsync(seatTenantId, user.Id);
            foreach (var seat in seats)
            {
                if (!string.IsNullOrEmpty(seat.FeatureKey))
                {
                    context.IssuedClaims.Add(new Claim("module_seats", seat.FeatureKey));
                }
            }
        }
    }
}
