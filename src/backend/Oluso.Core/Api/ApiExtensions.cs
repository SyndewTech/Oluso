using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Oluso.Core.Api;

/// <summary>
/// Extension methods for configuring Oluso API authorization policies
/// </summary>
public static class ApiExtensions
{
    /// <summary>
    /// Adds Oluso API authorization policies for Admin, Account, and Service APIs.
    /// Call this after AddAuthentication and AddAuthorization.
    /// </summary>
    public static IServiceCollection AddOlusoApiPolicies(
        this IServiceCollection services,
        Action<OlusoApiOptions>? configure = null)
    {
        var options = new OlusoApiOptions();
        configure?.Invoke(options);

        services.AddAuthorization(authOptions =>
        {
            // Admin API policy - requires admin scope or role
            authOptions.AddPolicy("AdminApi", policy =>
            {
                policy.RequireAuthenticatedUser();

                if (!string.IsNullOrEmpty(options.AdminApiScope))
                {
                    policy.RequireClaim("scope", options.AdminApiScope);
                }

                if (options.AdminApiRoles.Count > 0)
                {
                    policy.RequireRole(options.AdminApiRoles.ToArray());
                }
            });

            // Account API policy - requires account scope (user self-service)
            authOptions.AddPolicy("AccountApi", policy =>
            {
                policy.RequireAuthenticatedUser();

                if (!string.IsNullOrEmpty(options.AccountApiScope))
                {
                    policy.RequireClaim("scope", options.AccountApiScope);
                }
            });

            // Service API policy - requires service scope (machine-to-machine)
            authOptions.AddPolicy("ServiceApi", policy =>
            {
                policy.RequireAuthenticatedUser();

                if (!string.IsNullOrEmpty(options.ServiceApiScope))
                {
                    policy.RequireClaim("scope", options.ServiceApiScope);
                }
            });
        });

        return services;
    }

    /// <summary>
    /// Configures JWT Bearer authentication for Oluso APIs
    /// </summary>
    public static IServiceCollection AddOlusoApiAuthentication(
        this IServiceCollection services,
        string authority,
        string audience,
        Action<JwtBearerOptions>? configureJwt = null)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = !authority.Contains("localhost");

                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };

                configureJwt?.Invoke(options);
            });

        return services;
    }
}

/// <summary>
/// Options for Oluso API authorization policies
/// </summary>
public class OlusoApiOptions
{
    /// <summary>
    /// Required scope for Admin API access. Default is "admin".
    /// Set to null to not require a specific scope.
    /// </summary>
    public string? AdminApiScope { get; set; } = "admin";

    /// <summary>
    /// Required roles for Admin API access.
    /// Leave empty to not require specific roles (scope-only auth).
    /// </summary>
    public List<string> AdminApiRoles { get; set; } = new();

    /// <summary>
    /// Required scope for Account API access. Default is "account".
    /// Set to null to not require a specific scope.
    /// </summary>
    public string? AccountApiScope { get; set; } = "account";

    /// <summary>
    /// Required scope for Service API access. Default is "service".
    /// Set to null to not require a specific scope.
    /// </summary>
    public string? ServiceApiScope { get; set; } = "service";

    /// <summary>
    /// Whether to require tenant context for Admin API calls.
    /// Default is true (tenant is required).
    /// </summary>
    public bool RequireTenantForAdmin { get; set; } = true;

    /// <summary>
    /// Whether to require tenant context for Account API calls.
    /// Default is false (tenant is optional for single-tenant deployments).
    /// </summary>
    public bool RequireTenantForAccount { get; set; } = false;

    /// <summary>
    /// Whether to require tenant context for Service API calls.
    /// Default is true (tenant comes from client registration).
    /// </summary>
    public bool RequireTenantForService { get; set; } = true;
}
