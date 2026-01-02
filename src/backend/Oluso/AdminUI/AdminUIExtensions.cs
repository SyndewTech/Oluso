using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Oluso.AdminUI;

/// <summary>
/// Extension methods for adding Admin UI functionality
/// </summary>
public static class AdminUIExtensions
{
    /// <summary>
    /// Adds Admin UI services to the DI container.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddOlusoAdminUI(
        this IServiceCollection services,
        Action<AdminUIOptions>? configure = null)
    {
        var options = new AdminUIOptions();
        configure?.Invoke(options);

        services.AddSingleton(Options.Create(options));

        return services;
    }

    /// <summary>
    /// Adds the Admin UI middleware to the pipeline.
    /// This should be called after authentication/authorization middleware.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    /// <example>
    /// <code>
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseOlusoAdminUI(); // Serve admin UI with auth
    /// </code>
    /// </example>
    public static IApplicationBuilder UseOlusoAdminUI(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AdminUIMiddleware>();
    }

    /// <summary>
    /// Adds the Admin UI middleware with custom options.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="configure">Configuration action for options</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseOlusoAdminUI(
        this IApplicationBuilder app,
        Action<AdminUIOptions> configure)
    {
        var options = new AdminUIOptions();
        configure(options);
      
        // Use the middleware with inline options
        return app.UseMiddleware<AdminUIMiddleware>(Options.Create(options));
    }

    /// <summary>
    /// Configures Admin UI to be disabled.
    /// Useful for conditional deployment scenarios.
    /// </summary>
    public static IServiceCollection AddOlusoAdminUIDisabled(this IServiceCollection services)
    {
        return services.AddOlusoAdminUI(options => options.Enabled = false);
    }

    /// <summary>
    /// Extension method for OlusoBuilder to add Admin UI
    /// </summary>
    public static OlusoBuilder AddAdminUI(
        this OlusoBuilder builder,
        Action<AdminUIOptions>? configure = null)
    {
        builder.Services.AddOlusoAdminUI(configure);
        return builder;
    }

    /// <summary>
    /// Extension method for OlusoBuilder to add disabled Admin UI
    /// </summary>
    public static OlusoBuilder AddAdminUIDisabled(this OlusoBuilder builder)
    {
        builder.Services.AddOlusoAdminUIDisabled();
        return builder;
    }
}
