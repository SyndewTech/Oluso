using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using System.Security.Claims;
using System.Text.Json;

namespace Oluso.Middleware;

/// <summary>
/// Role classification for the authenticated user in the tenant resolution context.
/// </summary>
internal enum TenantUserRole
{
    RegularUser,
    OrgAdmin,
    SuperAdmin
}

/// <summary>
/// Layer 3 of 3 in the tenant-context pipeline. Finalises tenant
/// resolution <b>after</b> authentication has populated claims, and
/// enforces tenant access on authenticated API / hub paths.
///
/// <para>Picks up where Layers 1 + 2 left off:
/// <list type="bullet">
///   <item><see cref="EarlyTenantHostMiddleware"/> (Layer 1) may have
///     set context from a tenant subdomain or custom domain — if so,
///     this middleware still validates and may override (e.g. an org
///     admin sending <c>X-Tenant-Id</c>).</item>
///   <item><see cref="BearerTokenTenantResolverMiddleware"/> (Layer 2)
///     may have set context from a subdomain-issued bearer's
///     <c>tenant_id</c>. Same story — validated here.</item>
///   <item>Otherwise this is the first chance to set context, e.g. for
///     a platform-host API call where the token's <c>iss</c> is the
///     bare base URL and Layer 2 intentionally left context untouched.</item>
/// </list></para>
///
/// <para>Two-path resolution:
/// <list type="bullet">
///   <item><b>Unauthenticated / non-API paths:</b> subdomain →
///     custom-domain → header → query (host signals first because a
///     browser following a link can't set headers).</item>
///   <item><b>Authenticated API / hub requests:</b> all roles may use
///     <c>X-Tenant-Id</c> to switch; the JWT <c>tenant_id</c> claim is
///     the fallback. Access is then gated against
///     <c>allowed_tenants</c>.</item>
/// </list></para>
///
/// <para>Access validation is purely JWT-claim-based (no DB lookups):
/// <list type="bullet">
///   <item><b>SuperAdmin</b> (<c>super_admin</c> claim, or SuperAdmin
///     role with no <c>tenant_id</c> claim) — granted any tenant.
///     Inside a non-platform tenant the SuperAdmin claim is scoped down
///     (<see cref="ScopeSuperAdminToTenant"/>) so admin UIs treat them
///     as a regular tenant admin for visibility.</item>
///   <item><b>OrgAdmin</b> — granted any tenant in
///     <c>allowed_tenants</c>, plus any tenant whose
///     <c>OrganizationId</c> they hold an owner/admin role for in
///     <c>org_roles</c>. Not <c>org_ids</c>: that lists every
///     organization they belong to at any role.</item>
///   <item><b>Regular user</b> — tenant must be in
///     <c>allowed_tenants</c> (which always includes the user's home
///     tenant by construction in
///     <c>TokenService.GetOrganizationClaimsAsync</c>).</item>
/// </list></para>
///
/// <para>Pipeline position: <b>AFTER <c>UseAuthentication()</c></b>
/// (so <c>context.User</c> is populated) and <b>BEFORE
/// <c>UseAuthorization()</c></b> (so the org_id / org_role claims this
/// adds are visible to policy handlers).</para>
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = EffectiveOptions(context);

        var tenantAccessor = context.RequestServices.GetService<ITenantContextAccessor>();

        if (tenantAccessor == null)
        {
            // Multi-tenancy not enabled, continue without tenant context
            await _next(context);
            return;
        }

        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var path = context.Request.Path.Value ?? "";
        var isApiRequest = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
        // SignalR hub paths use the same JWT-based tenant resolution as
        // /api/. Without this, the WS upgrade leaves _tenantContext.TenantId
        // null in RealtimeHub.OnConnectedAsync, so the connection never joins
        // its user-group and BroadcastToUserAsync drops every event silently.
        var isHubRequest = path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);

        // Step 3: Resolve tenant identifier based on auth/path context
        var userRole = TenantUserRole.RegularUser;
        var isMachineClient = isAuthenticated && IsMachineClient(context.User);
        string? tenantIdentifier;

        if (isAuthenticated && isMachineClient && (isApiRequest || isHubRequest))
        {
            // Machine-to-machine: the tenant is baked into the client
            // registration and stamped on the token at issuance. The header is
            // deliberately not consulted — a service client cannot switch tenant.
            tenantIdentifier = ResolveFromToken(context);
        }
        else if (isAuthenticated && (isApiRequest || isHubRequest))
        {
            // Authenticated API/hub path: resolve based on user role
            userRole = DetermineUserRole(context.User);
            tenantIdentifier = ResolveAuthenticatedApiTenant(context, options);
        }
        else
        {
            // Unauthenticated OR non-API path (OIDC, static files, Razor pages, etc.)
            tenantIdentifier = await ResolveUnauthenticatedTenantAsync(context, options);
        }

        // Step 4: Look up tenant. OIDC protocol endpoints (discovery, token,
        // userinfo, etc.) skip the hard 404 — an OAuth client doing discovery
        // against an unfamiliar host should still get the platform-level
        // response, not a tenant-not-found error.
        Tenant? resolvedTenant = null;
        var tenantStore = context.RequestServices.GetService<ITenantStore>();
        var isOidcEndpoint = OidcCorsMiddleware.IsOidcEndpoint(context, path);

        if (!string.IsNullOrEmpty(tenantIdentifier) && tenantStore != null)
        {
            resolvedTenant = await LookupTenant(context, tenantStore, tenantIdentifier, softFail: isOidcEndpoint);
            if (context.Response.HasStarted)
                return; // LookupTenant wrote an error response (404 or 403)
        }

        // Fall through to the configured default if no tenant resolved (either
        // no signal in the request, or the signal didn't match any tenant on
        // an OIDC endpoint that opted to soft-fail).
        if (resolvedTenant == null && !string.IsNullOrEmpty(options.DefaultTenantId) && tenantStore != null)
        {
            resolvedTenant = await LookupTenant(context, tenantStore, options.DefaultTenantId, softFail: false);
            if (context.Response.HasStarted)
                return;
        }

        // Step 5: Validate access for authenticated API/hub requests.
        // Machine clients are exempt: allowed_tenants is a user-token claim
        // (issued by DefaultProfileService from TenantMembership rows), so a
        // client-credentials token can never carry one. Its tenant came from
        // the signed token itself, which is the same trust the gate provides.
        if (isAuthenticated && !isMachineClient && resolvedTenant != null && (isApiRequest || isHubRequest))
        {
            if (!await ValidateAuthenticatedAccessAsync(context, resolvedTenant, userRole))
            {
                return; // ValidateAuthenticatedAccessAsync wrote a 403 response
            }

            // Set organization context from JWT claims (no DB lookup)
            AddOrganizationContext(context, resolvedTenant);
        }

        // Step 6: Enforce tenant requirement for authenticated API/hub paths.
        // SuperAdmin and OrgAdmin are system-level roles that legitimately
        // operate across tenants — they don't need a specific tenant context
        // for org-level operations (discovery, listing tenants, etc.). They
        // explicitly opt into tenant scope via X-Tenant-Id header for any
        // tenant-scoped operation.
        var isSystemLevel = userRole == TenantUserRole.SuperAdmin || userRole == TenantUserRole.OrgAdmin;
        if (resolvedTenant == null && (isApiRequest || isHubRequest) && isAuthenticated && !isSystemLevel)
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\": \"tenant_required\", \"error_description\": \"Tenant context is required for API requests\"}");
            return;
        }

        // Step 7: Set context and continue
        if (resolvedTenant != null)
        {
            tenantAccessor.SetTenant(resolvedTenant);
            _logger.LogDebug("Resolved tenant: {TenantId} ({TenantIdentifier})", resolvedTenant.Id, resolvedTenant.Identifier);

            // Step 7a: Scope SuperAdmin's effective identity to the resolved tenant.
            // When a SuperAdmin operates inside a non-platform tenant via X-Tenant-Id,
            // strip the super_admin claim so downstream visibility (RolesController's
            // reserved-role filter, AdminBaseController.IsSuperAdmin, etc.) treats
            // them as a regular tenant admin in that scope. Their roles and the *
            // permission wildcard remain — capability is preserved, only visibility
            // of privileged things is scoped.
            ScopeSuperAdminToTenant(context, resolvedTenant);
        }

        try
        {
            await _next(context);
        }
        finally
        {
            tenantAccessor.ClearTenant();
        }
    }

    /// <summary>
    /// Scopes a SuperAdmin's effective identity to the resolved tenant when
    /// operating inside a non-platform tenant.
    ///
    /// Drops the <c>super_admin</c> claim so visibility checks (e.g.
    /// AdminBaseController.IsSuperAdmin, RolesController's reserved-role
    /// filter) treat them as a regular tenant admin. Adds a <c>TenantAdmin</c>
    /// role so role-based filters that look for "TenantAdmin" or "Admin"
    /// (which the user's only role — "SuperAdmin" — wouldn't satisfy) can
    /// match, granting them the tenant-admin visibility bucket instead of
    /// the strictest unprivileged bucket.
    ///
    /// Keeps the original <c>SuperAdmin</c> role and permission claims so
    /// capability gates (e.g. TenantAdminAuthorizationHandler's
    /// <c>userRoles.Contains("SuperAdmin")</c> system-admin check) still
    /// succeed — they need the role to authorise SuperAdmin-only operations.
    /// The JWT itself is untouched; this only adjusts
    /// <see cref="HttpContext.User"/> for the duration of the request.
    /// </summary>
    private static void ScopeSuperAdminToTenant(HttpContext context, Tenant resolvedTenant)
    {
        if (resolvedTenant.Id == Core.PlatformConstants.TenantId) return;
        if (context.User.Identity?.IsAuthenticated != true) return;

        if (context.User.FindFirst("super_admin") == null) return;

        var sourceIdentity = (ClaimsIdentity)context.User.Identity;
        var roleClaimType = sourceIdentity.RoleClaimType ?? ClaimTypes.Role;

        var alreadyHasTenantAdmin = sourceIdentity.Claims.Any(c =>
            (c.Type == "role" || c.Type == roleClaimType)
            && (c.Value.Equals("TenantAdmin", StringComparison.OrdinalIgnoreCase)
                || c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase)));

        var keptClaims = sourceIdentity.Claims
            .Where(c => c.Type != "super_admin")
            .ToList();

        if (!alreadyHasTenantAdmin)
        {
            keptClaims.Add(new Claim(roleClaimType, "TenantAdmin"));
            keptClaims.Add(new Claim("role", "TenantAdmin"));
        }

        var scoped = new ClaimsIdentity(
            keptClaims,
            sourceIdentity.AuthenticationType,
            sourceIdentity.NameClaimType,
            roleClaimType);
        context.User = new ClaimsPrincipal(scoped);
    }

    /// <summary>
    /// Determines the user's role for tenant access decisions.
    /// Checks explicit claims first, then falls back to role-based checks.
    /// </summary>
    /// <summary>
    /// True for a client-credentials access token — a client_id claim with no
    /// subject behind it. There is no user, so none of the user-scoped tenant
    /// machinery (roles, allowed_tenants, org claims) applies.
    /// </summary>
    internal static bool IsMachineClient(ClaimsPrincipal user)
    {
        var clientId = user.FindFirst("client_id")?.Value;
        if (string.IsNullOrEmpty(clientId))
        {
            return false;
        }

        var subject = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrEmpty(subject);
    }

    internal static TenantUserRole DetermineUserRole(ClaimsPrincipal user)
    {
        // 1. Check for explicit super_admin claim
        var superAdminClaim = user.FindFirst("super_admin")?.Value;
        if (string.Equals(superAdminClaim, "true", StringComparison.OrdinalIgnoreCase) || superAdminClaim == "1")
            return TenantUserRole.SuperAdmin;

        // 2. Check for SuperAdmin/SystemAdmin role ONLY if tenant_id is absent
        //    (users scoped to a tenant cannot be platform-level super admins)
        var userTenantId = user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst("tid")?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            if (user.IsInRole("SuperAdmin") || user.IsInRole("SystemAdmin") ||
                user.IsInRole("super_admin") || user.IsInRole("platform_admin"))
            {
                return TenantUserRole.SuperAdmin;
            }
        }

        // 3. Check for org admin: from organization membership (is_org_admin is
        // emitted for an Owner/Admin membership), never from the OrgAdmin role.
        var isOrgAdminClaim = user.FindFirst("is_org_admin")?.Value;
        if (string.Equals(isOrgAdminClaim, "true", StringComparison.OrdinalIgnoreCase) || isOrgAdminClaim == "1")
            return TenantUserRole.OrgAdmin;

        // 4. Default: regular user
        return TenantUserRole.RegularUser;
    }

    /// <summary>
    /// Resolves tenant for authenticated API requests.
    /// All roles can use the header to switch tenants — access is enforced in
    /// ValidateAuthenticatedAccess via the allowed_tenants claim.
    /// Falls back to JWT tenant_id when no header is present.
    /// </summary>
    private static string? ResolveAuthenticatedApiTenant(
        HttpContext context, MultiTenancyOptions options)
    {
        // All authenticated users can specify tenant via header;
        // access control is enforced later against allowed_tenants.
        var headerValue = ResolveFromHeader(context, options.TenantHeaderName);
        if (!string.IsNullOrEmpty(headerValue))
            return headerValue;

        // Fall back to JWT tenant_id
        return ResolveFromToken(context);
    }

    /// <summary>
    /// Resolves tenant for unauthenticated requests or non-API paths.
    /// Tries every signal the host might carry — subdomain, then custom
    /// domain — before giving up to header/query.
    ///
    /// Strategy is treated as a *preference* not an *exclusive gate*: a
    /// browser visiting tenant.example.com via a link click has no way
    /// to set X-Tenant-Id, so if we only consulted the configured
    /// Strategy=Header, the request would drop to DefaultTenantId and
    /// silently hijack the tenant-scoped URL into platform context.
    /// Whenever the host itself carries the tenant signal, prefer it.
    /// </summary>
    /// <summary>
    /// The multi-tenancy options, with BaseDomain derived from the issuer when not set.
    /// </summary>
    internal static MultiTenancyOptions EffectiveOptions(HttpContext context)
    {
        var options = context.RequestServices.GetService<IOptions<MultiTenancyOptions>>()?.Value
            ?? new MultiTenancyOptions();

        // Auto-derive BaseDomain from IssuerUri if not explicitly set
        if (string.IsNullOrEmpty(options.BaseDomain))
        {
            var configuration = context.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var issuerUri = configuration?["Oluso:IssuerUri"];
            if (!string.IsNullOrEmpty(issuerUri) && Uri.TryCreate(issuerUri, UriKind.Absolute, out var uri))
            {
                options.BaseDomain = uri.Host;
            }
        }
        return options;
    }

    /// <summary>
    /// The tenant the request's host belongs to (its subdomain or custom domain), or null for a
    /// host that names no tenant. Also used by CORS, which runs before this middleware.
    /// </summary>
    internal static async Task<string?> ResolveHostTenantIdentifierAsync(HttpContext context, MultiTenancyOptions options)
    {
        // 1. Subdomain — always tried when BaseDomain is configured.
        //    BaseDomain is the toggle that says "tenant subdomains are a
        //    thing for this deployment", so use it regardless of Strategy.
        if (!string.IsNullOrEmpty(options.BaseDomain))
        {
            var subdomain = ResolveFromSubdomain(context, options.ReservedSubdomains, options.BaseDomain);
            if (!string.IsNullOrEmpty(subdomain))
                return subdomain;
        }

        // 2. CustomDomain — try the host against the Tenants.CustomDomain
        //    column. Always tried (no Strategy gate) for the same reason
        //    subdomain is: the visitor can't set X-Tenant-Id from a link.
        //    Returns the matched tenant's identifier silently when no row
        //    matches, so we fall through to header/query rather than 404.
        var host = context.Request.Host.Host;
        if (!string.IsNullOrEmpty(host) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            var tenantStore = context.RequestServices.GetService<ITenantStore>();
            if (tenantStore != null)
            {
                var customDomainTenant = await tenantStore.GetByCustomDomainAsync(host);
                if (customDomainTenant != null)
                {
                    return customDomainTenant.Identifier ?? customDomainTenant.Id;
                }
            }
        }

        return null;
    }

    private static async Task<string?> ResolveUnauthenticatedTenantAsync(
        HttpContext context, MultiTenancyOptions options)
    {
        // 1–2. The host: tenant subdomain or custom domain.
        var fromHost = await ResolveHostTenantIdentifierAsync(context, options);
        if (!string.IsNullOrEmpty(fromHost))
            return fromHost;

        // 3. Header (admin UI tenant switcher, programmatic clients)
        var header = ResolveFromHeader(context, options.TenantHeaderName);
        if (!string.IsNullOrEmpty(header))
            return header;

        // 4. Query string (last-resort signal, e.g. shared static-link tests)
        var query = ResolveFromQueryString(context, options.TenantQueryParameterName);
        if (!string.IsNullOrEmpty(query))
            return query;

        return null;
    }

    /// <summary>
    /// Looks up a tenant by identifier or ID. Writes 404/403 for not-found
    /// and disabled tenants unless <paramref name="softFail"/> is true, in
    /// which case the call returns null silently and lets the caller fall
    /// through (used by OIDC protocol endpoints, where an unrecognised host
    /// should serve the platform-level discovery doc, not a tenant-not-found).
    /// </summary>
    private async Task<Tenant?> LookupTenant(HttpContext context, ITenantStore tenantStore, string identifier, bool softFail)
    {
        var tenant = await tenantStore.GetByIdentifierAsync(identifier);
        if (tenant == null)
        {
            // Try by ID as fallback
            tenant = await tenantStore.GetByIdAsync(identifier);
        }
        if (tenant == null)
        {
            // Try by custom domain as fallback
            tenant = await tenantStore.GetByCustomDomainAsync(identifier);
        }

        if (tenant == null)
        {
            if (softFail)
            {
                _logger.LogDebug("Tenant not found (soft-fail): {TenantIdentifier}", identifier);
                return null;
            }

            _logger.LogWarning("Tenant not found: {TenantIdentifier}", identifier);
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\": \"tenant_not_found\", \"error_description\": \"The specified tenant does not exist\"}");
            return null;
        }

        // softFail applies to "not found" only — a tenant that exists but
        // is disabled is a deliberate admin action; respect it everywhere
        // (including OIDC endpoints) so disabled tenants can't authenticate.
        if (!tenant.Enabled)
        {
            _logger.LogWarning("Tenant is disabled: {TenantId}", tenant.Id);
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\": \"tenant_disabled\", \"error_description\": \"The specified tenant is disabled\"}");
            return null;
        }

        return tenant;
    }

    /// <summary>
    /// Validates that the authenticated user has access to the resolved tenant.
    /// Uses JWT claims only -- no database lookups.
    /// - SuperAdmin: always granted
    /// - OrgAdmin / RegularUser: tenant.Id must be in JWT allowed_tenants claim.
    ///   The user's home tenant is included in allowed_tenants by construction at
    ///   token-issue time (TokenService.GetOrganizationClaimsAsync).
    /// </summary>
    private async Task<bool> ValidateAuthenticatedAccessAsync(HttpContext context, Tenant tenant, TenantUserRole userRole)
    {
        if (userRole == TenantUserRole.SuperAdmin)
        {
            _logger.LogDebug("Super admin granted access to tenant {TenantId}", tenant.Id);
            return true;
        }

        // Parse allowed_tenants from JWT claims. The user's home tenant is
        // included by construction in DefaultProfileService.AddAuthorizationClaims,
        // so the legacy jwt-tenant_id-matches-requested-tenant fallback is no
        // longer needed and intentionally omitted to keep this gate strict.
        var allowedTenants = GetAllowedTenantsFromClaims(context.User);

        if (allowedTenants.Contains(tenant.Id))
        {
            _logger.LogDebug("User granted access to tenant {TenantId} (role: {Role})", tenant.Id, userRole);
            return true;
        }

        // OrgAdmin grant: every tenant under an organization the user administers,
        // without needing a TenantMembership row per tenant.
        //
        // Scoped by org_roles, not org_ids. is_org_admin and the OrgAdmin role are
        // both platform-wide booleans, and org_ids lists every organization the
        // user belongs to at ANY role — so matching on org_ids handed an admin of
        // one organization the run of every tenant in another where they are only
        // a member.
        if (userRole == TenantUserRole.OrgAdmin && !string.IsNullOrEmpty(tenant.OrganizationId))
        {
            var orgRole = GetOrgRoleFromClaims(context.User, tenant.OrganizationId);
            if (IsOrgAdminRole(orgRole))
            {
                _logger.LogDebug(
                    "Org admin granted access to tenant {TenantId} via organization {OrgId} (role: {OrgRole})",
                    tenant.Id, tenant.OrganizationId, orgRole);
                return true;
            }
        }

        var userId = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";

        _logger.LogWarning(
            "User {UserId} denied access to tenant {TenantId}: not in allowed_tenants (role: {Role})",
            userId, tenant.Id, userRole);

        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            "{\"error\": \"access_denied\", \"error_description\": \"You do not have access to this tenant\"}");
        return false;
    }

    /// <summary>
    /// Parses the allowed_tenants claim from the JWT.
    /// Handles both JSON array format (from JWT tokens) and multiple claim values (from cookies).
    /// </summary>
    private static HashSet<string> GetAllowedTenantsFromClaims(ClaimsPrincipal user)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var claims = user.FindAll("allowed_tenants");
        foreach (var claim in claims)
        {
            var value = claim.Value;
            if (string.IsNullOrEmpty(value))
                continue;

            // Try JSON array format: ["tenant1", "tenant2"]
            if (value.StartsWith('['))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(value);
                    if (parsed != null)
                    {
                        foreach (var id in parsed)
                            result.Add(id);
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // Not valid JSON, treat as plain value
                }
            }

            // Plain string value (single tenant ID or comma-separated)
            if (value.Contains(','))
            {
                foreach (var id in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    result.Add(id);
            }
            else
            {
                result.Add(value);
            }
        }

        return result;
    }

    /// <summary>
    /// The user's role in one specific organization, or null if they hold none.
    /// Handles both the JWT dictionary form and the cookie's "org_id:role" pairs.
    /// </summary>
    private static string? GetOrgRoleFromClaims(ClaimsPrincipal user, string organizationId)
    {
        var orgRolesClaim = user.FindFirst("org_roles")?.Value;
        if (!string.IsNullOrEmpty(orgRolesClaim))
        {
            try
            {
                var orgRoles = JsonSerializer.Deserialize<Dictionary<string, string>>(orgRolesClaim);
                if (orgRoles != null && orgRoles.TryGetValue(organizationId, out var role))
                {
                    return role;
                }
            }
            catch (JsonException)
            {
                // Invalid JSON, fall through to cookie format
            }
        }

        foreach (var claim in user.FindAll("org_role"))
        {
            var parts = claim.Value.Split(':', 2);
            if (parts.Length == 2 && parts[0] == organizationId)
            {
                return parts[1];
            }
        }

        return null;
    }

    private static bool IsOrgAdminRole(string? orgRole) =>
        orgRole != null
        && (orgRole.Equals("owner", StringComparison.OrdinalIgnoreCase)
            || orgRole.Equals("admin", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sets organization context from JWT claims (no database lookup).
    /// - current_org_id: from the resolved tenant's OrganizationId
    /// - current_org_role: parsed from JWT org_roles claim
    /// </summary>
    private static void AddOrganizationContext(HttpContext context, Tenant tenant)
    {
        if (string.IsNullOrEmpty(tenant.OrganizationId))
            return;

        // Set HttpContext.Items for downstream use
        context.Items["CurrentOrgId"] = tenant.OrganizationId;

        var orgRole = GetOrgRoleFromClaims(context.User, tenant.OrganizationId);

        if (orgRole != null)
        {
            context.Items["CurrentOrgRole"] = orgRole;
        }

        // Add claims to the current identity for controller access
        if (context.User.Identity is ClaimsIdentity identity)
        {
            var existingOrgIdClaim = identity.FindFirst("current_org_id");
            var existingOrgRoleClaim = identity.FindFirst("current_org_role");
            if (existingOrgIdClaim != null) identity.RemoveClaim(existingOrgIdClaim);
            if (existingOrgRoleClaim != null) identity.RemoveClaim(existingOrgRoleClaim);

            identity.AddClaim(new Claim("current_org_id", tenant.OrganizationId));
            if (orgRole != null)
            {
                identity.AddClaim(new Claim("current_org_role", orgRole));
            }
        }
    }

    private static string? ResolveFromToken(HttpContext context)
    {
        return context.User.FindFirst("tenant_id")?.Value
            ?? context.User.FindFirst("tid")?.Value
            ?? context.User.FindFirst("http://schemas.oluso.io/claims/tenant")?.Value;
    }

    private static string? ResolveFromSubdomain(HttpContext context, HashSet<string> reservedSubdomains, string? baseDomain)
    {
        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
            return null;

        // If baseDomain is configured (e.g., "localhost" or "auth.example.com"),
        // extract the subdomain by stripping the base domain suffix.
        // This handles both "acme.localhost" (2 parts) and "acme.auth.example.com" (4 parts).
        if (!string.IsNullOrEmpty(baseDomain))
        {
            var suffix = "." + baseDomain;
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length)
            {
                var subdomain = host[..^suffix.Length];
                // Subdomain must be a single label (no dots) and not reserved
                if (!subdomain.Contains('.') && !reservedSubdomains.Contains(subdomain))
                {
                    return subdomain;
                }
            }
            // BaseDomain is authoritative once configured. Falling through to
            // the label heuristic below would claim any 3+ label host as a
            // subdomain — a custom domain like "identity.acme.com" resolves to
            // "identity", which matches no tenant and, because it is non-empty,
            // shadows the custom-domain lookup the caller tries next.
            return null;
        }

        // Fallback: legacy 3+ part hostname heuristic (e.g., "acme.auth.example.com").
        // Skip IP literals — an IPv4 like 192.0.2.1 has dotted parts but its
        // first octet is not a tenant identifier.
        if (System.Net.IPAddress.TryParse(host, out _))
            return null;

        var parts = host.Split('.');
        if (parts.Length >= 3)
        {
            var subdomain = parts[0];
            if (!reservedSubdomains.Contains(subdomain))
                return subdomain;
        }

        return null;
    }

    private static string? ResolveFromHeader(HttpContext context, string headerName)
    {
        if (context.Request.Headers.TryGetValue(headerName, out var value))
        {
            return value.FirstOrDefault();
        }
        return null;
    }

    private static string? ResolveFromQueryString(HttpContext context, string parameterName)
    {
        return context.Request.Query[parameterName].FirstOrDefault();
    }
}

/// <summary>
/// Middleware for handling CORS for OIDC endpoints
/// </summary>
public class OidcCorsMiddleware
{
    private readonly RequestDelegate _next;

    public OidcCorsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // For OIDC endpoints that may receive cross-origin requests
        // (e.g., token endpoint from SPAs)
        var path = context.Request.Path.Value ?? "";

        if (IsOidcEndpoint(context, path))
        {
            // Allow CORS for these endpoints
            // The actual CORS policy is configured elsewhere,
            // this just ensures OPTIONS requests are handled
            if (context.Request.Method == "OPTIONS")
            {
                context.Response.StatusCode = 204;
                return;
            }
        }

        await _next(context);
    }

    internal static bool IsOidcEndpoint(HttpContext context, string path)
    {
        var config = context.RequestServices
            .GetService<IOptions<Oluso.Core.Protocols.OidcEndpointConfiguration>>()?.Value;

        if (config == null)
        {
            // Fallback to default paths if config not available
            return path.Contains("/connect/") || path.Contains("/.well-known/");
        }

        // Check against all configured OIDC endpoints
        return path.StartsWith(config.AuthorizeEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.TokenEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.UserInfoEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.RevocationEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.IntrospectionEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.EndSessionEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.DeviceAuthorizationEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.PushedAuthorizationEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.DiscoveryEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.JwksEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.BackchannelAuthenticationEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.RegistrationEndpoint, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Options for host validation middleware
/// </summary>
public class HostValidationOptions
{
    /// <summary>
    /// Whether to enable host validation. Default is false.
    /// When enabled, requests from unknown hosts will be rejected.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Additional allowed hosts beyond the server's IssuerUri and tenant custom domains.
    /// Useful for allowing localhost during development or load balancer health checks.
    /// </summary>
    public List<string> AdditionalAllowedHosts { get; set; } = new();

    /// <summary>
    /// Whether to allow tenant subdomains of the IssuerUri host.
    /// When true, {identifier}.{issuerHost} is validated by checking the identifier
    /// against the tenant store. Default is true.
    /// </summary>
    public bool AllowTenantSubdomains { get; set; } = true;

    /// <summary>
    /// Whether to allow localhost in development environments. Default is true.
    /// </summary>
    public bool AllowLocalhostInDevelopment { get; set; } = true;

    /// <summary>
    /// Cache duration for allowed hosts. Default is 5 minutes.
    /// Set to TimeSpan.Zero to disable caching.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Middleware that validates request host against known hosts.
/// Rejects requests from unknown/unexpected hosts for security.
/// Known hosts include:
/// - Server's configured IssuerUri
/// - All tenant custom domains
/// - Additional allowed hosts from configuration
/// Uses distributed cache to support multi-instance deployments.
/// </summary>
public class HostValidationMiddleware
{
    private const string CacheKey = "oluso:hosts:allowed";
    private readonly RequestDelegate _next;
    private readonly ILogger<HostValidationMiddleware> _logger;

    public HostValidationMiddleware(
        RequestDelegate next,
        ILogger<HostValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = context.RequestServices.GetService<IOptions<HostValidationOptions>>()?.Value
            ?? new HostValidationOptions();

        // Skip validation if not enabled
        if (!options.Enabled)
        {
            await _next(context);
            return;
        }

        var requestHost = context.Request.Host.Host;
        if (string.IsNullOrEmpty(requestHost))
        {
            _logger.LogWarning("Request missing Host header, rejecting");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Bad Request: Missing Host header");
            return;
        }

        // Check if localhost (or subdomain of localhost) is allowed in development
        if (options.AllowLocalhostInDevelopment)
        {
            var isLocalhost = requestHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || requestHost.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || requestHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
            if (isLocalhost)
            {
                var env = context.RequestServices.GetService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
                if (env?.EnvironmentName == "Development")
                {
                    await _next(context);
                    return;
                }
            }
        }

        // Get cached allowed hosts or build from sources
        var allowedHosts = await GetAllowedHostsAsync(context, options);

        // Check if request host is in allowed list (exact match)
        if (allowedHosts.Contains(requestHost.ToLowerInvariant()))
        {
            await _next(context);
            return;
        }

        // Check if request host is a valid tenant subdomain of the issuer base domain.
        // e.g., "acme.auth.example.com" where "auth.example.com" is the issuer host.
        if (options.AllowTenantSubdomains)
        {
            var configuration = context.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var issuerUri = configuration?["Oluso:IssuerUri"];
            if (!string.IsNullOrEmpty(issuerUri) && Uri.TryCreate(issuerUri, UriKind.Absolute, out var uri))
            {
                var issuerHost = uri.Host;
                var suffix = "." + issuerHost;
                if (requestHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && requestHost.Length > suffix.Length)
                {
                    var subdomain = requestHost[..^suffix.Length];
                    if (!subdomain.Contains('.'))
                    {
                        // Verify this subdomain maps to a real tenant
                        var tenantStore = context.RequestServices.GetService<ITenantStore>();
                        if (tenantStore != null)
                        {
                            var tenant = await tenantStore.GetByIdentifierAsync(subdomain);
                            if (tenant != null && tenant.Enabled)
                            {
                                await _next(context);
                                return;
                            }
                        }
                    }
                }
            }
        }

        // Host not recognized
        _logger.LogWarning("Request from unknown host '{Host}', rejecting", requestHost);
        context.Response.StatusCode = 421; // Misdirected Request
        await context.Response.WriteAsync("Misdirected Request: Unknown host");
    }

    private async Task<HashSet<string>> GetAllowedHostsAsync(HttpContext context, HostValidationOptions options)
    {
        var cache = context.RequestServices.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();

        // Try to get from cache first
        if (cache != null && options.CacheDuration > TimeSpan.Zero)
        {
            var cachedJson = await cache.GetStringAsync(CacheKey);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                try
                {
                    var cachedHosts = System.Text.Json.JsonSerializer.Deserialize<List<string>>(cachedJson);
                    if (cachedHosts != null)
                    {
                        return new HashSet<string>(cachedHosts, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // Cache corrupted, will re-fetch
                }
            }
        }

        // Build allowed hosts set
        var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add additional allowed hosts from options
        foreach (var host in options.AdditionalAllowedHosts)
        {
            allowedHosts.Add(host.ToLowerInvariant());
        }

        // Add server's configured IssuerUri host
        var configuration = context.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        var issuerUri = configuration?["Oluso:IssuerUri"];
        if (!string.IsNullOrEmpty(issuerUri))
        {
            try
            {
                var issuerHost = new Uri(issuerUri).Host;
                allowedHosts.Add(issuerHost.ToLowerInvariant());
            }
            catch (UriFormatException)
            {
                // Invalid URI, skip
            }
        }

        // Add tenant custom domains
        var tenantStore = context.RequestServices.GetService<ITenantStore>();
        if (tenantStore != null)
        {
            var tenants = await tenantStore.GetAllAsync();
            foreach (var tenant in tenants.Where(t => t.Enabled && !string.IsNullOrEmpty(t.CustomDomain)))
            {
                allowedHosts.Add(tenant.CustomDomain!.ToLowerInvariant());
            }
        }

        // Add domains straight from their verification records.
        //
        // Tenants.CustomDomain is a denormalised copy written during activation,
        // so any path that flips a domain to Active without also updating the
        // tenant row leaves it Active in the UI but unknown here — a 421 on a
        // domain the operator can see is live. Reading the records themselves
        // removes that dependency.
        //
        // Verified counts as well as Active: the host has to answer before a
        // certificate can be issued for it, and Active is what issuing one
        // produces. Gating on Active alone can never complete. Ownership is
        // already proven by the TXT record at Verified.
        var domainStore = context.RequestServices.GetService<ICustomDomainVerificationStore>();
        if (domainStore != null)
        {
            foreach (var domain in await domainStore.GetServableDomainsAsync())
            {
                if (!string.IsNullOrEmpty(domain.Domain))
                {
                    allowedHosts.Add(domain.Domain.ToLowerInvariant());
                }
            }
        }

        // Cache the result
        if (cache != null && options.CacheDuration > TimeSpan.Zero)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(allowedHosts.ToList());
            var cacheOptions = new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.CacheDuration
            };
            await cache.SetStringAsync(CacheKey, json, cacheOptions);
            _logger.LogDebug("Cached {Count} allowed hosts for {Duration}",
                allowedHosts.Count, options.CacheDuration);
        }

        return allowedHosts;
    }
}

/// <summary>
/// Default implementation of host validation cache invalidator
/// </summary>
public class HostValidationCacheInvalidator : IHostValidationCacheInvalidator
{
    private const string CacheKey = "oluso:hosts:allowed";
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache? _cache;

    public HostValidationCacheInvalidator(Microsoft.Extensions.Caching.Distributed.IDistributedCache? cache = null)
    {
        _cache = cache;
    }

    public async Task InvalidateCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKey, cancellationToken);
        }
    }
}
