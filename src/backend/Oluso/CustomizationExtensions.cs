using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso;

/// <summary>
/// Extension methods for customizing Oluso behavior
/// </summary>
public static class CustomizationExtensions
{
    /// <summary>
    /// Adds a custom profile service for populating token claims.
    /// Only one profile service can be registered.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddProfileService&lt;CustomProfileService&gt;();
    /// </code>
    /// </example>
    public static OlusoBuilder AddProfileService<TService>(this OlusoBuilder builder)
        where TService : class, IProfileService
    {
        builder.Services.AddScoped<IProfileService, TService>();
        return builder;
    }

    /// <summary>
    /// Adds a custom resource owner password validator.
    /// Use this for custom authentication backends (LDAP, legacy systems, etc.)
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddResourceOwnerValidator&lt;LdapPasswordValidator&gt;();
    /// </code>
    /// </example>
    public static OlusoBuilder AddResourceOwnerValidator<TValidator>(this OlusoBuilder builder)
        where TValidator : class, IResourceOwnerPasswordValidator
    {
        builder.Services.AddScoped<IResourceOwnerPasswordValidator, TValidator>();
        return builder;
    }

    /// <summary>
    /// Adds a custom extension grant validator.
    /// Use this to support custom OAuth grant types.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddExtensionGrantValidator&lt;SmsGrantValidator&gt;()
    ///     .AddExtensionGrantValidator&lt;BiometricGrantValidator&gt;();
    /// </code>
    /// </example>
    public static OlusoBuilder AddExtensionGrantValidator<TValidator>(this OlusoBuilder builder)
        where TValidator : class, IExtensionGrantValidator
    {
        builder.Services.AddScoped<IExtensionGrantValidator, TValidator>();
        return builder;
    }

    /// <summary>
    /// Adds an event sink for receiving authentication events.
    /// Multiple event sinks can be registered.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddEventSink&lt;AuditLogEventSink&gt;()
    ///     .AddEventSink&lt;SecurityAlertEventSink&gt;();
    /// </code>
    /// </example>
    public static OlusoBuilder AddEventSink<TSink>(this OlusoBuilder builder)
        where TSink : class, IOlusoEventSink
    {
        builder.Services.AddScoped<IOlusoEventSink, TSink>();
        return builder;
    }

    /// <summary>
    /// Adds event handling services. Called automatically by AddOluso().
    /// </summary>
    public static OlusoBuilder AddEvents(this OlusoBuilder builder)
    {
        builder.Services.AddScoped<IOlusoEventService, OlusoEventService>();
        return builder;
    }
}

/// <summary>
/// Extension methods for UI customization
/// </summary>
public static class UiCustomizationExtensions
{
    /// <summary>
    /// Configures UI options for the authentication pages
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .ConfigureUi(ui =>
    ///     {
    ///         ui.LoginPageTitle = "Welcome to MyApp";
    ///         ui.LogoUrl = "/images/logo.png";
    ///         ui.PrimaryColor = "#007bff";
    ///     });
    /// </code>
    /// </example>
    public static OlusoBuilder ConfigureUi(this OlusoBuilder builder, Action<OlusoUiOptions> configure)
    {
        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Configures the paths for authentication pages.
    /// Use this to override default page locations with your own Razor Pages.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .ConfigurePages(pages =>
    ///     {
    ///         pages.LoginPage = "/Auth/Login";      // Your custom login page
    ///         pages.ConsentPage = "/Auth/Consent";  // Your custom consent page
    ///         pages.ErrorPage = "/Auth/Error";      // Your custom error page
    ///     });
    /// </code>
    /// </example>
    public static OlusoBuilder ConfigurePages(this OlusoBuilder builder, Action<OlusoPageOptions> configure)
    {
        builder.Services.Configure(configure);
        return builder;
    }
}

/// <summary>
/// UI customization options
/// </summary>
public class OlusoUiOptions
{
    /// <summary>
    /// Application name shown on login page
    /// </summary>
    public string ApplicationName { get; set; } = "Sign In";

    /// <summary>
    /// Logo URL for login page
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Primary brand color (hex)
    /// </summary>
    public string PrimaryColor { get; set; } = "#0d6efd";

    /// <summary>
    /// Background color for login page
    /// </summary>
    public string BackgroundColor { get; set; } = "#f8f9fa";

    /// <summary>
    /// Custom CSS to inject into pages
    /// </summary>
    public string? CustomCss { get; set; }

    /// <summary>
    /// Custom JavaScript to inject into pages
    /// </summary>
    public string? CustomJs { get; set; }

    /// <summary>
    /// Footer text
    /// </summary>
    public string? FooterText { get; set; }

    /// <summary>
    /// Show "Remember me" checkbox on login
    /// </summary>
    public bool ShowRememberMe { get; set; } = true;

    /// <summary>
    /// Show "Forgot password" link
    /// </summary>
    public bool ShowForgotPassword { get; set; } = true;

    /// <summary>
    /// Show registration link
    /// </summary>
    public bool ShowRegistration { get; set; } = true;
}

/// <summary>
/// Page path configuration options
/// </summary>
public class OlusoPageOptions
{
    /// <summary>
    /// Path to custom login page (default: /Account/Login)
    /// </summary>
    public string LoginPage { get; set; } = "/Account/Login";

    /// <summary>
    /// Path to custom logout page (default: /Account/Logout)
    /// </summary>
    public string LogoutPage { get; set; } = "/Account/Logout";

    /// <summary>
    /// Path to custom consent page (default: /Consent)
    /// </summary>
    public string ConsentPage { get; set; } = "/Consent";

    /// <summary>
    /// Path to custom error page (default: /Error)
    /// </summary>
    public string ErrorPage { get; set; } = "/Error";

    /// <summary>
    /// Path to custom device authorization page (default: /Device)
    /// </summary>
    public string DevicePage { get; set; } = "/Device";

    /// <summary>
    /// Path to custom registration page (default: /Account/Register)
    /// </summary>
    public string RegisterPage { get; set; } = "/Account/Register";

    /// <summary>
    /// Path to custom MFA setup page (default: /Account/TwoFactor)
    /// </summary>
    public string MfaPage { get; set; } = "/Account/TwoFactor";
}
