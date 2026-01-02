using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers;
using Oluso.Core.Protocols.Models;

namespace Oluso.Admin;

/// <summary>
/// Extension methods for adding Oluso Admin API
/// </summary>
public static class OlusoAdminExtensions
{
    /// <summary>
    /// Authentication scheme name for Admin API JWT tokens
    /// </summary>
    public const string AdminApiScheme = "AdminApiJwt";

    /// <summary>
    /// Add Oluso Admin API services and controllers
    /// </summary>
    public static IMvcBuilder AddOlusoAdmin(this IMvcBuilder mvcBuilder, Action<AdminApiOptions>? configure = null)
    {
        var options = new AdminApiOptions();
        configure?.Invoke(options);

        // Add admin controllers from this assembly
        mvcBuilder.AddApplicationPart(typeof(UsersController).Assembly);

        // Configure MVC options for admin API
        mvcBuilder.AddMvcOptions(mvcOptions =>
        {
            // Add custom conventions if needed
        });

        // Register authorization handlers
        mvcBuilder.Services.AddScoped<IAuthorizationHandler, TenantAdminAuthorizationHandler>();

        // Add JWT Bearer authentication for Admin API
        // Configuration is read at runtime when the authentication handler is invoked
        mvcBuilder.Services.AddAuthentication()
            .AddJwtBearer(AdminApiScheme, options =>
            {
                // Defer token validation parameter configuration to runtime
                // so we can read from IConfiguration
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Configuration is available here
                        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                        var jwtKey = config["Jwt:Key"] ?? config["Oluso:AdminJwtKey"];

                        if (!string.IsNullOrEmpty(jwtKey))
                        {
                            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
                            context.Options.TokenValidationParameters.IssuerSigningKey = key;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        // Return 401 JSON instead of redirect for API endpoints
                        if (context.Request.Path.StartsWithSegments("/api"))
                        {
                            context.HandleResponse();
                            context.Response.StatusCode = 401;
                            context.Response.ContentType = "application/json";
                            return context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
                        }
                        return Task.CompletedTask;
                    },
                    OnForbidden = context =>
                    {
                        // Return 403 JSON instead of redirect for API endpoints
                        if (context.Request.Path.StartsWithSegments("/api"))
                        {
                            context.Response.StatusCode = 403;
                            context.Response.ContentType = "application/json";
                            return context.Response.WriteAsync("{\"error\":\"Forbidden\"}");
                        }
                        return Task.CompletedTask;
                    }
                };

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false, // Issuer validation can be added if needed
                    ValidateAudience = false, // Audience validation can be added if needed
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        // Register authorization policies with the Admin API authentication scheme
        mvcBuilder.Services.AddAuthorization(authOptions =>
        {
            // AdminApi policy: Tenant-scoped admin access
            // Requires authenticated user with Admin/TenantAdmin/SuperAdmin role
            // SuperAdmin can access any tenant, others only their own
            authOptions.AddPolicy("AdminApi", policy =>
            {
                policy.AuthenticationSchemes.Add(AdminApiScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new TenantAdminRequirement(requireSuperAdmin: false));
            });

            // SuperAdmin policy: Cross-tenant system-wide access
            // Only users with SuperAdmin role and TenantId = null can pass
            authOptions.AddPolicy("SuperAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add(AdminApiScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new TenantAdminRequirement(requireSuperAdmin: true));
            });

            // TenantAdmin policy: Explicit tenant-scoped admin (alias for AdminApi)
            authOptions.AddPolicy("TenantAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add(AdminApiScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new TenantAdminRequirement(requireSuperAdmin: false));
            });

            // AccountApi policy: End-user self-service access
            // Uses OIDC access tokens (not admin JWT) - for end users authenticated via the identity server
            // Requires authenticated user (no admin role needed)
            // Used for account management endpoints like profile, sessions, passkeys
            authOptions.AddPolicy("AccountApi", policy =>
            {
                policy.AuthenticationSchemes.Add(OidcConstants.AccessTokenAuthenticationScheme);
                policy.RequireAuthenticatedUser();
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
