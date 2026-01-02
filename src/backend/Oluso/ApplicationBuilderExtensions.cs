using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Middleware;

namespace Oluso;

/// <summary>
/// Extension methods for adding Oluso middleware to the pipeline
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds Oluso middleware to the pipeline.
    /// This should be called after UseRouting() and before UseAuthentication().
    /// </summary>
    /// <example>
    /// <code>
    /// app.UseRouting();
    /// app.UseOluso();  // Add this line
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseOluso(this IApplicationBuilder app)
    {
        // Add tenant resolution middleware (if multi-tenancy enabled)
        app.UseMiddleware<TenantResolutionMiddleware>();

        // Add CORS for OIDC endpoints (cross-origin token requests, etc.)
        //app.UseMiddleware<OidcCorsMiddleware>();

        return app;
    }

    /// <summary>
    /// Adds host validation middleware to reject requests from unknown hosts.
    /// This validates that the request Host header matches:
    /// - The server's configured IssuerUri
    /// - Any tenant's CustomDomain
    /// - Additional allowed hosts specified in options
    ///
    /// Call this BEFORE UseOluso() for maximum security.
    /// </summary>
    /// <example>
    /// <code>
    /// // In Program.cs services configuration:
    /// builder.Services.Configure&lt;HostValidationOptions&gt;(options =>
    /// {
    ///     options.Enabled = true;
    ///     options.AdditionalAllowedHosts.Add("health.internal");
    /// });
    ///
    /// // In middleware pipeline:
    /// app.UseHostValidation();
    /// app.UseOluso();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseHostValidation(this IApplicationBuilder app)
    {
        app.UseMiddleware<HostValidationMiddleware>();
        return app;
    }

    /// <summary>
    /// Adds host validation middleware with inline configuration.
    /// </summary>
    public static IApplicationBuilder UseHostValidation(
        this IApplicationBuilder app,
        Action<HostValidationOptions> configure)
    {
        var options = new HostValidationOptions();
        configure(options);
        app.ApplicationServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<HostValidationOptions>>();

        // Configure options in DI if not already done
        var optionsInstance = Microsoft.Extensions.Options.Options.Create(options);
        app.Use(async (context, next) =>
        {
            // This is a simple inline approach - for production use Configure<> in services
            context.RequestServices = new HostValidationServiceProvider(
                context.RequestServices, optionsInstance);
            await next();
        });

        app.UseMiddleware<HostValidationMiddleware>();
        return app;
    }

    // Helper class for inline options configuration
    private class HostValidationServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _inner;
        private readonly Microsoft.Extensions.Options.IOptions<HostValidationOptions> _options;

        public HostValidationServiceProvider(
            IServiceProvider inner,
            Microsoft.Extensions.Options.IOptions<HostValidationOptions> options)
        {
            _inner = inner;
            _options = options;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Microsoft.Extensions.Options.IOptions<HostValidationOptions>))
                return _options;
            return _inner.GetService(serviceType);
        }
    }

    /// <summary>
    /// Maps Oluso endpoints (OIDC protocol endpoints).
    /// Call this after MapControllers() or at the end of your routing config.
    /// </summary>
    public static IEndpointRouteBuilder MapOluso(this IEndpointRouteBuilder endpoints)
    {
        // Map OIDC endpoints:
        // - /.well-known/openid-configuration (discovery)
        // - /.well-known/jwks (JSON Web Key Set)
        // - /connect/authorize (authorization endpoint)
        // - /connect/token (token endpoint)
        // - /connect/userinfo (userinfo endpoint)
        // - /connect/revocation (token revocation)
        // - /connect/introspect (token introspection)
        // - /connect/endsession (logout)
        // - /connect/deviceauthorization (device flow)
        // - /connect/par (pushed authorization requests)

        // These are already mapped via controller routing conventions
        // This method is here for explicit mapping if needed

        return endpoints;
    }
}
