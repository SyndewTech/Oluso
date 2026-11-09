using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.UI.Localization;

namespace Oluso.UI;

/// <summary>
/// Extension methods for adding Oluso UI to your application
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds Oluso UI pages (standalone login, register, consent, error pages)
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration for UI options</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddOlusoUI(
        this IServiceCollection services,
        Action<OlusoUIOptions>? configure = null)
    {
        var options = new OlusoUIOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Configure Razor Pages to find our pages
        services.Configure<RazorPagesOptions>(opts =>
        {
            // Add our pages area
            opts.Conventions.AddAreaPageRoute("OlusoUI", "/Account/Login", "/account/login");
            opts.Conventions.AddAreaPageRoute("OlusoUI", "/Account/Register", "/account/register");
            opts.Conventions.AddAreaPageRoute("OlusoUI", "/Account/Consent", "/account/consent");
            opts.Conventions.AddAreaPageRoute("OlusoUI", "/Account/ForgotPassword", "/account/forgot-password");
            opts.Conventions.AddAreaPageRoute("OlusoUI", "/Error", "/error");
        });

        // JSON-based localization for journey/account views
        services.AddLocalization();
        services.AddHttpContextAccessor();
        services.AddSingleton<HostFileLocaleOverrides>();
        services.AddSingleton<JsonUiLocalizer>();
        services.Replace(ServiceDescriptor.Singleton<IStringLocalizerFactory, JsonUiLocalizerFactory>());
        services.AddScoped<ITenantLocaleOverrideProvider, TenantConfigLocaleOverrideProvider>();

        // Headless clients (the SDK) draw the hosted views from their descriptions, or from the view's HTML.
        services.AddJourneyStepFormDescribers(typeof(DependencyInjection).Assembly);
        services.TryAddScoped<IJourneyStepHtmlRenderer, Headless.RazorStepHtmlRenderer>();

        return services;
    }

    /// <summary>
    /// Adds Oluso UI pages to the MVC builder
    /// </summary>
    public static IMvcBuilder AddOlusoUIPages(this IMvcBuilder builder)
    {
        // Add the assembly containing our Razor pages
        builder.AddApplicationPart(typeof(DependencyInjection).Assembly);

        return builder;
    }

    /// <summary>
    /// Uses Oluso UI static files and endpoints
    /// </summary>
    public static IApplicationBuilder UseOlusoUI(this IApplicationBuilder app)
    {
        // Serve static files from this assembly if any
        var assembly = typeof(DependencyInjection).Assembly;
        var embeddedProvider = new EmbeddedFileProvider(assembly, "Oluso.UI.wwwroot");

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = embeddedProvider,
            RequestPath = "/oluso"
        });

        var regionCodes = Oluso.Core.Locale.SupportedLocales.All.Select(l => l.Code);
        var translationCodes = Oluso.Core.Locale.UiStringDefaults.AvailableLocales;
        var supportedCodes = regionCodes.Concat(translationCodes).Distinct().ToArray();
        var locOptions = new RequestLocalizationOptions()
            .AddSupportedCultures(supportedCodes)
            .AddSupportedUICultures(supportedCodes);

        // Read ui_locales from query string (standalone pages get it from
        // the coordinator redirect, same pattern as domain_hint/loginHint).
        locOptions.RequestCultureProviders.Insert(0, new CustomRequestCultureProvider(ctx =>
        {
            var uiLocales = ctx.Request.Query["ui_locales"].FirstOrDefault();
            if (string.IsNullOrEmpty(uiLocales)) return Task.FromResult<ProviderCultureResult?>(null);
            var locale = uiLocales.Split(' ', ',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            return Task.FromResult<ProviderCultureResult?>(
                string.IsNullOrEmpty(locale) ? null : new ProviderCultureResult(locale));
        }));

        app.UseRequestLocalization(locOptions);

        return app;
    }
}

/// <summary>
/// Options for Oluso UI configuration
/// </summary>
public class OlusoUIOptions
{
    /// <summary>
    /// Primary color for the UI theme (CSS color value)
    /// </summary>
    public string? PrimaryColor { get; set; }

    /// <summary>
    /// Background color/gradient start color
    /// </summary>
    public string? BackgroundColor { get; set; }

    /// <summary>
    /// URL to the logo image
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Application title shown in the UI
    /// </summary>
    public string? ApplicationTitle { get; set; }

    /// <summary>
    /// Custom CSS to inject into pages
    /// </summary>
    public string? CustomCss { get; set; }

    /// <summary>
    /// Enable local registration (default: true)
    /// </summary>
    public bool EnableLocalRegistration { get; set; } = true;

    /// <summary>
    /// Enable forgot password functionality (default: true)
    /// </summary>
    public bool EnableForgotPassword { get; set; } = true;

    /// <summary>
    /// Require terms acceptance during registration (default: true)
    /// </summary>
    public bool RequireTermsAcceptance { get; set; } = true;

    /// <summary>
    /// URL to terms of service page
    /// </summary>
    public string? TermsOfServiceUrl { get; set; } = "/terms";

    /// <summary>
    /// URL to privacy policy page
    /// </summary>
    public string? PrivacyPolicyUrl { get; set; } = "/privacy";
}
