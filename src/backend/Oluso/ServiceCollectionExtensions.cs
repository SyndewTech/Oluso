using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Oluso.Certificates;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Keys;

namespace Oluso;

/// <summary>
/// Extension methods for adding Oluso to your application
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Oluso identity services to the application
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(builder.Configuration)
    ///     .AddMultiTenancy()
    ///     .AddUserJourneyEngine()
    ///     .AddEntityFrameworkStores&lt;AppDbContext&gt;();
    /// </code>
    /// </example>
    public static OlusoBuilder AddOluso(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = new OlusoBuilder(services, configuration);

        // Register core services that are always needed
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        // Configure OIDC options from configuration
        services.Configure<OlusoOptions>(configuration.GetSection("Oluso"));

        // Register event service for authentication hooks
        services.AddScoped<IOlusoEventService, OlusoEventService>();

        // Register key encryption service (shared between keys and certificates)
        services.TryAddSingleton<IKeyEncryptionService, DataProtectionKeyEncryptionService>();

        // Register certificate services (local provider by default)
        services.TryAddSingleton<ICertificateMaterialProvider, LocalCertificateMaterialProvider>();
        services.TryAddSingleton<ICertificateMaterialProviderRegistry>(sp =>
        {
            var providers = sp.GetServices<ICertificateMaterialProvider>();
            return new CertificateMaterialProviderRegistry(providers);
        });
        services.TryAddScoped<ICertificateService, CertificateService>();

        // Oluso is an OIDC-compliant identity server - enable OIDC and dynamic providers by default
        builder.AddOidc();
        builder.AddDynamicExternalProviders();

        return builder;
    }

    /// <summary>
    /// Adds Oluso with configuration action
    /// </summary>
    public static OlusoBuilder AddOluso(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OlusoOptions> configure)
    {
        var builder = services.AddOluso(configuration);
        configure(builder.Options);
        return builder;
    }
}

/// <summary>
/// Extension methods for adding multi-tenancy support
/// </summary>
public static class MultiTenancyExtensions
{
    /// <summary>
    /// Adds multi-tenancy support to Oluso.
    /// Enables tenant isolation for users, clients, and resources.
    /// Also enables tenant-aware cookie authentication to prevent cross-tenant session leakage.
    /// </summary>
    public static OlusoBuilder AddMultiTenancy(this OlusoBuilder builder)
    {
        // Register tenant context (AsyncLocal-based for request scope)
        builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        builder.Services.AddSingleton<ITenantContext>(sp =>
            sp.GetRequiredService<ITenantContextAccessor>());

        // Register tenant-aware cookie authentication to scope cookies per tenant
        // This ensures users authenticated in one tenant cannot access other tenants
        builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>,
            TenantCookieAuthenticationOptions>();

        // Register host validation cache invalidator for when tenants change
        builder.Services.AddSingleton<Middleware.IHostValidationCacheInvalidator, Middleware.HostValidationCacheInvalidator>();

        // Tenant resolution will be added via middleware
        builder.Options.MultiTenancyEnabled = true;

        return builder;
    }

    /// <summary>
    /// Adds multi-tenancy with options
    /// </summary>
    public static OlusoBuilder AddMultiTenancy(
        this OlusoBuilder builder,
        Action<MultiTenancyOptions> configure)
    {
        builder.AddMultiTenancy();

        var options = new MultiTenancyOptions();
        configure(options);
        builder.Services.Configure(configure);

        return builder;
    }
}

/// <summary>
/// Options for multi-tenancy
/// </summary>
public class MultiTenancyOptions
{
    /// <summary>
    /// Primary strategy for resolving tenant from request.
    /// Header (X-Tenant-Id) and query string (?tenant=) are always checked first.
    /// Set to Subdomain to also check subdomain after header/query.
    /// </summary>
    public TenantResolutionStrategy ResolutionStrategy { get; set; } = TenantResolutionStrategy.Header;

    /// <summary>
    /// Header name for tenant resolution (default: X-Tenant-Id)
    /// </summary>
    public string TenantHeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    /// Default tenant ID when no tenant is resolved from request
    /// </summary>
    public string? DefaultTenantId { get; set; } = "default";
}

public enum TenantResolutionStrategy
{
    /// <summary>
    /// Resolve from HTTP header (X-Tenant-Id) or query string (?tenant=)
    /// This is the default and recommended strategy.
    /// </summary>
    Header,

    /// <summary>
    /// Resolve from subdomain: tenant-id.auth.example.com
    /// Also checks header and query string first.
    /// </summary>
    Subdomain,

    /// <summary>
    /// Resolve from full domain: tenant-id.example.com
    /// Maps full domains to tenants. Also checks header and query string first.
    /// </summary>
    Domain
}

/// <summary>
/// Extension methods for adding User Journey Engine
/// </summary>
public static class UserJourneyExtensions
{
    /// <summary>
    /// Adds the User Journey Engine for customizable authentication flows.
    /// Enables features like MFA, passwordless login, progressive profiling, etc.
    /// </summary>
    /// <remarks>
    /// The User Journey Engine provides:
    /// - **Authentication steps**: LocalLogin, ExternalIdP, MFA, Passwordless (Email/SMS)
    /// - **User management**: SignUp, PasswordReset, PasswordChange, UpdateUser, LinkAccount
    /// - **Flow control**: Condition, Branch, ApiCall, Webhook, Transform
    /// - **User interaction**: Consent, ClaimsCollection, CAPTCHA, TermsAcceptance
    /// - **Plugin system**: WASM plugins via Extism with hot reload support
    /// </remarks>
    public static OlusoBuilder AddUserJourneyEngine(this OlusoBuilder builder)
    {
        return builder.AddUserJourneyEngine(_ => { });
    }

    /// <summary>
    /// Adds User Journey Engine with options
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddUserJourneyEngine(options =>
    ///     {
    ///         options.PluginDirectory = "/plugins";
    ///         options.EnablePluginHotReload = true;
    ///         options.DefaultJourneyTimeoutMinutes = 30;
    ///     });
    /// </code>
    /// </example>
    public static OlusoBuilder AddUserJourneyEngine(
        this OlusoBuilder builder,
        Action<UserJourneyOptions> configure)
    {
        builder.Options.UserJourneyEngineEnabled = true;

        var options = new UserJourneyOptions();
        configure(options);

        // Register options
        builder.Services.AddSingleton(options);

        // Register core journey services
        builder.Services.AddScoped<IJourneyOrchestrator, DefaultJourneyOrchestrator>();
        builder.Services.AddScoped<DefaultStepHandlerRegistry>();
        builder.Services.AddScoped<IStepHandlerRegistry>(sp => sp.GetRequiredService<DefaultStepHandlerRegistry>());
        builder.Services.AddScoped<IExtendedStepHandlerRegistry>(sp => sp.GetRequiredService<DefaultStepHandlerRegistry>());
        builder.Services.AddScoped<IJourneyPolicyStore, InMemoryJourneyPolicyStore>();
        // State store must be Singleton for in-memory implementation to persist across requests
        builder.Services.AddSingleton<IJourneyStateStore, InMemoryJourneyStateStore>();
        builder.Services.AddSingleton<IConditionEvaluator, DefaultConditionEvaluator>();

        // Register built-in step handlers
        // These are discovered by DefaultStepHandlerRegistry via IServiceProvider.GetServices<IStepHandler>()
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.LocalLoginStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.CompositeLoginStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.ExternalLoginStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.MfaStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.SignUpStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.CreateUserStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.ConsentStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.PasswordResetStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.PasswordChangeStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.UpdateUserStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.TermsAcceptanceStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.LinkAccountStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.PasswordlessEmailStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.PasswordlessSmsStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.CaptchaStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.DynamicFormStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.ClaimsCollectionStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.ConditionStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.BranchStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.TransformStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.ApiCallStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.WebhookStepHandler>();
        builder.Services.AddScoped<IStepHandler, Oluso.UserJourneys.Steps.CustomPluginStepHandler>();

        // Register plugin system
        if (!string.IsNullOrEmpty(options.PluginDirectory))
        {
            builder.Services.AddSingleton<IManagedPluginRegistry, DefaultManagedPluginRegistry>();
        }

        return builder;
    }

    /// <summary>
    /// Registers a custom journey step handler.
    /// Use this to add custom authentication or validation steps to user journeys.
    /// </summary>
    /// <typeparam name="THandler">The custom step handler type</typeparam>
    /// <param name="builder">The Oluso builder</param>
    /// <param name="configure">Optional configuration for step metadata</param>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddUserJourneyEngine()
    ///     .AddCustomJourneyStep&lt;BiometricVerificationStepHandler&gt;(step => step
    ///         .WithDescription("Verify user with biometrics")
    ///         .InCategory("Authentication")
    ///         .FromModule("MyCompany.Biometrics"));
    /// </code>
    /// </example>
    public static OlusoBuilder AddCustomJourneyStep<THandler>(
        this OlusoBuilder builder,
        Action<StepTypeBuilder>? configure = null)
        where THandler : class, ICustomStepHandler
    {
        // Register the handler
        builder.Services.AddScoped<THandler>();

        // Register step type configuration
        builder.Services.AddSingleton<ICustomStepHandlerConfiguration>(
            new CustomStepHandlerConfiguration<THandler>(configure));

        return builder;
    }

    /// <summary>
    /// Registers a managed plugin for custom step handling.
    /// </summary>
    /// <typeparam name="TPlugin">The managed plugin type</typeparam>
    /// <param name="builder">The Oluso builder</param>
    /// <param name="name">The plugin name used in journey policies</param>
    public static OlusoBuilder AddJourneyPlugin<TPlugin>(
        this OlusoBuilder builder,
        string name)
        where TPlugin : class, IManagedPlugin
    {
        builder.Services.AddScoped<TPlugin>();
        builder.Services.AddScoped<IManagedPlugin>(sp =>
        {
            var plugin = sp.GetRequiredService<TPlugin>();
            var registry = sp.GetService<IManagedPluginRegistry>();
            registry?.Register(name, plugin);
            return plugin;
        });

        return builder;
    }
}

/// <summary>
/// Configuration wrapper for custom step handlers
/// </summary>
internal class CustomStepHandlerConfiguration<THandler> : ICustomStepHandlerConfiguration
    where THandler : class, ICustomStepHandler
{
    private readonly Action<StepTypeBuilder>? _configure;

    public CustomStepHandlerConfiguration(Action<StepTypeBuilder>? configure)
    {
        _configure = configure;
    }

    public void Configure(IStepHandlerRegistry registry)
    {
        // Configuration will be applied when handler is resolved
    }
}

/// <summary>
/// Extension methods for user service configuration
/// </summary>
public static class UserServiceExtensions
{
    /// <summary>
    /// Registers a custom user service implementation.
    /// Use this to bring your own user store (LDAP, external API, custom database, etc.)
    /// </summary>
    /// <typeparam name="TService">Your IOlusoUserService implementation</typeparam>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddEntityFrameworkStores&lt;AppDbContext&gt;()
    ///     .AddUserService&lt;LdapUserService&gt;();
    /// </code>
    /// </example>
    public static OlusoBuilder AddUserService<TService>(this OlusoBuilder builder)
        where TService : class, IOlusoUserService
    {
        // Remove any existing IOlusoUserService registration
        var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IOlusoUserService));
        if (descriptor != null)
        {
            builder.Services.Remove(descriptor);
        }

        builder.Services.AddScoped<IOlusoUserService, TService>();
        builder.Options.CustomUserServiceRegistered = true;

        return builder;
    }

    /// <summary>
    /// Registers a custom user service with a factory.
    /// Use this when your user service needs complex configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddEntityFrameworkStores&lt;AppDbContext&gt;()
    ///     .AddUserService(sp => new ExternalApiUserService(
    ///         sp.GetRequiredService&lt;HttpClient&gt;(),
    ///         "https://users.mycompany.com/api"
    ///     ));
    /// </code>
    /// </example>
    public static OlusoBuilder AddUserService(
        this OlusoBuilder builder,
        Func<IServiceProvider, IOlusoUserService> factory)
    {
        var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IOlusoUserService));
        if (descriptor != null)
        {
            builder.Services.Remove(descriptor);
        }

        builder.Services.AddScoped(factory);
        builder.Options.CustomUserServiceRegistered = true;

        return builder;
    }

    /// <summary>
    /// Skips ASP.NET Identity registration entirely.
    /// Use this when you want full control over user management
    /// and will register your own IOlusoUserService.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddEntityFrameworkStores&lt;AppDbContext&gt;()
    ///     .SkipIdentity()
    ///     .AddUserService&lt;MyCustomUserService&gt;();
    /// </code>
    /// </example>
    public static OlusoBuilder SkipIdentity(this OlusoBuilder builder)
    {
        builder.Options.SkipIdentityRegistration = true;
        return builder;
    }
}
