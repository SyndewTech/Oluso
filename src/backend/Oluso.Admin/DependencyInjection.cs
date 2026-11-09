using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Events;
using Oluso.Admin.Controllers;
using Oluso.Admin.Controllers.Service;
using Oluso.Admin.Search;
using Oluso.Admin.Services;
using Oluso.Core.Protocols.Models;
using Oluso.Imaging;

using Oluso.Admin.Services.ProviderIcons;

namespace Oluso.Admin;

/// <summary>
/// Extension methods for adding Oluso Admin API
/// </summary>
public static class OlusoAdminExtensions
{
    /// <summary>
    /// Authentication scheme name for Admin API — uses Oluso OIDC access tokens
    /// </summary>
    public const string AdminApiScheme = OidcConstants.AccessTokenAuthenticationScheme;

    /// <summary>
    /// Add Oluso Admin API services and controllers
    /// </summary>
    public static IMvcBuilder AddOlusoAdmin(this IMvcBuilder mvcBuilder, Action<AdminApiOptions>? configure = null)
    {
        var options = new AdminApiOptions();
        configure?.Invoke(options);

        // Backfill core admin permissions into the singleton PermissionRegistry
        // so the registry is the canonical universe of valid permissions — used
        // by RolesController.ValidatePermissionsAsync, by the reserved-auth
        // SaveChanges interceptor, and by anywhere else that needs to ask
        // "is this a known permission?". Before this, core perms lived in
        // AdminPermissions.GetCorePermissions() as a separate source and
        // callers had to remember to merge both, which the interceptor
        // can't do (it can't reference Oluso.Admin).
        PermissionRegistry.Instance.RegisterPermissions(
            AdminPermissions.GetCorePermissions());

        // OrgAdmin is a reserved platform role that authorization keys off
        // (ReservedClaimTypes.AdminDashboardRoles, TenantAdminAuthorizationHandler),
        // but nothing outside the demo seeders ever created the row — so granting
        // it threw "Role ORGADMIN does not exist" on any real deployment, and it
        // can't be created through RolesController because reserved names are
        // rejected. Registering it here means SeedModuleRolesAsync() creates it at
        // startup like every other system role. No FeatureKey: it isn't gated.
        PermissionRegistry.Instance.RegisterModuleRole(new ModuleRoleDefinition(
            Name: "OrgAdmin",
            DisplayName: "Organization Administrator",
            Description: "Manages an organization: its tenants, members and their roles",
            Category: "system",
            Permissions: new[]
            {
                AdminPermissions.AdminAccess,
                "organizations.*",
                "tenants.*",
                "users.*",
                "roles.*"
            }));

        // Add admin controllers from this assembly
        mvcBuilder.AddApplicationPart(typeof(UsersController).Assembly);

        // Image resizer for the anonymous public-image shim (PublicFilesController).
        // Default is SkiaSharp (MIT); only hit in local/dev — prod resizes at the
        // Cloudflare edge.
        mvcBuilder.Services.AddImageResizing();

        // Provider icon discovery: a dedicated client with redirects off and
        // connections pinned to the address that passed the public-IP check.
        mvcBuilder.Services.AddHttpClient(SafeIconFetcher.ClientName)
            .ConfigurePrimaryHttpMessageHandler(SafeIconFetcher.CreateHandler);
        mvcBuilder.Services.TryAddSingleton<IProviderIconFetcher, SafeIconFetcher>();
        mvcBuilder.Services.TryAddScoped<IProviderIconDiscoveryService, ProviderIconDiscoveryService>();

        // Register auto-generated event property source for admin events
        mvcBuilder.Services.AddEventPropertySource<Oluso.Core.Events.AdminEventPropertySource>();

        // Global search: aggregator plus the core record providers; modules add theirs.
        mvcBuilder.Services.AddAdminSearch();

        // Configure MVC options for admin API
        mvcBuilder.AddMvcOptions(mvcOptions =>
        {
            // Add SensitiveDataFilter globally to strip unauthorized sensitive fields from responses
            mvcOptions.Filters.Add<SensitiveDataFilter>();
        });

        // Register authorization handlers
        mvcBuilder.Services.AddScoped<IAuthorizationHandler, TenantAdminAuthorizationHandler>();
        mvcBuilder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        mvcBuilder.Services.AddScoped<IAuthorizationHandler, ServiceApiAuthorizationHandler>();

        // A scope-restricted client's user token never reaches the admin API.
        mvcBuilder.Services.AddScopeRestrictedClients();

        // Provisions the ApiScope rows behind the Service API when a client is
        // granted service access — see ServiceApiScopeProvisioner.
        mvcBuilder.Services.AddScoped<IServiceApiScopeProvisioner, ServiceApiScopeProvisioner>();

        // Provisions the identity resource that releases the entitlements claim
        // — see EntitlementScopeProvisioner.
        mvcBuilder.Services.AddScoped<IEntitlementScopeProvisioner, EntitlementScopeProvisioner>();

        // Register dynamic policy provider for permission-based authorization
        mvcBuilder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        // Include required permission name in 403 responses
        mvcBuilder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, PermissionDeniedResultHandler>();

        // Register authorization policies using Oluso OIDC access token scheme
        mvcBuilder.Services.AddAuthorization(authOptions =>
        {
            // AdminApi policy: the admin.access permission in the tenant the request landed in, the same
            // permission the admin-portal client requires at token issuance. Endpoints guard themselves
            // with their own permission or the SuperAdmin / SuperAdminOrOrgAdmin policies.
            authOptions.AddPolicy("AdminApi", policy =>
            {
                policy.AuthenticationSchemes.Add(AdminApiScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new PermissionRequirement(AdminPermissions.AdminAccess));
                policy.AddRequirements(new ScopeRestrictedClientRequirement());
            });

            // SuperAdmin policy: Cross-tenant system-wide access
            // Only users with SuperAdmin role and TenantId = null can pass
            authOptions.AddPolicy("SuperAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add(AdminApiScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new TenantAdminRequirement());
                policy.AddRequirements(new ScopeRestrictedClientRequirement());
            });

            // SuperAdminOrOrgAdmin policy: Cross-tenant access for SuperAdmin or OrgAdmin
            // Allows both system-level admins and organization-level admins
            authOptions.AddPolicy("SuperAdminOrOrgAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add(AdminApiScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new TenantAdminRequirement(allowOrgAdmin: true));
                policy.AddRequirements(new ScopeRestrictedClientRequirement());
            });

            // TenantAdmin policy: alias for AdminApi
            authOptions.AddPolicy("TenantAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add(AdminApiScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new PermissionRequirement(AdminPermissions.AdminAccess));
                policy.AddRequirements(new ScopeRestrictedClientRequirement());
            });

            // ServiceApi policy: Machine-to-machine access via client_credentials grant
            // Used by resource servers and external applications for automated operations
            // Requires the base service scope (ServiceScopes.ServiceApi) in the access token
            authOptions.AddPolicy("ServiceApi", policy =>
            {
                policy.AuthenticationSchemes.Add(OidcConstants.AccessTokenAuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ServiceApiRequirement(ServiceScopes.ServiceApi));
            });
        });

        return mvcBuilder;
    }
}

/// <summary>
/// Options for configuring the Admin API
/// </summary>
public class AdminApiOptions
{
    /// <summary>
    /// Base path for admin API endpoints (default: /api/admin)
    /// </summary>
    public string BasePath { get; set; } = "/api/admin";

    /// <summary>
    /// Whether to require admin role for access
    /// </summary>
    public bool RequireAdminRole { get; set; } = true;

    /// <summary>
    /// Role names that grant admin access
    /// </summary>
    public string[] AdminRoles { get; set; } = new[] { "Admin", "TenantAdmin", "SuperAdmin" };

    /// <summary>
    /// Optional claim type required for admin access
    /// </summary>
    public string? RequireClaim { get; set; }

    /// <summary>
    /// Optional claim value required for admin access
    /// </summary>
    public string? RequireClaimValue { get; set; }

    /// <summary>
    /// Enable audit logging for admin actions
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// Rate limit per minute for admin API calls (0 = disabled)
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 0;
}
