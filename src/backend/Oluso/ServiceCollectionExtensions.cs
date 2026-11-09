using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Oluso.Certificates;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.ActionCenter;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Keys;
using Oluso.Core.Licensing;
using Oluso.Middleware;

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
        services.AddSignInRateLimiting(configuration);

        // SignalR + realtime broadcaster primitive. Modules push events
        // through IRealtimeBroadcaster — they never depend on SignalR
        // directly — and the consumer app exposes the hub by calling
        // app.MapOlusoRealtime() alongside MapControllers().
        // CamelCase JSON so the wire shape (channel/name/payload) matches
        // what the workspace-ui RealtimeEvent interface declares; without
        // this, default System.Text.Json sends PascalCase, the frontend's
        // `evt.channel === '...'` filter never matches, and every
        // subscriber silently drops every event.
        var signalR = services.AddSignalR().AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
            options.PayloadSerializerOptions.DictionaryKeyPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        // Scale-out backplane (optional). SignalR keeps connection→group mappings
        // in-memory PER server, so tenant/user/channel broadcasts only reach the
        // server a connection landed on. With more than one app instance behind a
        // load balancer, a Redis backplane fans every broadcast across all servers
        // so group targeting works cluster-wide — the hub/broadcaster code is
        // unchanged. No connection table is needed (and would only go stale).
        //
        // Opt-in: set ConnectionStrings:RealtimeRedis (preferred) or
        // ConnectionStrings:Redis. Absent (single instance / dev) → in-memory,
        // exactly as before. Safe no-op when not configured.
        var redisConn = configuration.GetConnectionString("RealtimeRedis")
            ?? configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            signalR.AddStackExchangeRedis(redisConn, options =>
            {
                // Namespace the channel so multiple Oluso deployments can share a
                // Redis instance without cross-talk.
                options.Configuration.ChannelPrefix =
                    StackExchange.Redis.RedisChannel.Literal("oluso:realtime");
            });
        }

        // Realtime hub tickets live where the hub's connections do: in Redis alongside the backplane when
        // there are several instances, in this server's memory otherwise.
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.TryAddSingleton<Oluso.Realtime.IHubTicketStore>(_ => new Oluso.Realtime.RedisHubTicketStore(redisConn));
        }
        else
        {
            services.AddMemoryCache();
            services.TryAddSingleton<Oluso.Realtime.IHubTicketStore, Oluso.Realtime.MemoryHubTicketStore>();
        }
        // Central catalogue of realtime channels (scope + permission). Singleton
        // built from every IRealtimeChannelGate at startup; the hub gates
        // Subscribe against it and the broadcaster targets channel groups. An
        // unregistered channel is rejected on subscribe and dropped on broadcast
        // (fail-closed).
        services.TryAddSingleton<Oluso.Core.Realtime.IRealtimeChannelRegistry, Oluso.Core.Realtime.RealtimeChannelRegistry>();
        services.TryAddSingleton<Oluso.Core.Realtime.IRealtimeBroadcaster, Oluso.Realtime.SignalRRealtimeBroadcaster>();

        // Terminology — the platform's single engine (contracts + engine in
        // Oluso.Core, override table on the platform DbContext). Modules only
        // contribute keys/bundles via ITerminologyContributor; nothing about
        // terminology depends on any particular module being enabled.
        services.TryAddScoped<Oluso.Core.Terminology.ITerminologyService, Oluso.Core.Terminology.TerminologyService>();
        services.TryAddScoped<Oluso.Core.Terminology.ITenantProfileService, Oluso.Core.Terminology.TenantProfileService>();
        services.AddScoped<Oluso.Core.Events.IOlusoEventSink, Oluso.Realtime.TerminologyRealtimeSink>();
        services.AddSingleton<Oluso.Core.Realtime.IRealtimeChannelGate, Oluso.Realtime.PlatformChannelGate>();

        // nosniff on every response; SVG/XML responses are sandboxed.
        services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter,
            Oluso.Middleware.ActiveContentHeadersStartupFilter>();

        // Auto-injects the early host-based tenant resolver at the start
        // of the pipeline so OAuth callbacks (/signin-{scheme}), the
        // dynamic external-scheme provider, and Stripe webhook signature
        // verification can read ITenantContext when they fire — those
        // sit inside UseAuthentication and would otherwise short-circuit
        // before any post-auth middleware could populate the context.
        // Using IStartupFilter means consumers don't have to remember to
        // place a Use*() call at the right point in their pipeline.
        services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter,
            Oluso.Middleware.EarlyTenantHostStartupFilter>();

        // Bearer token tenant resolver — runs after EarlyTenantHost (so a
        // subdomain match still wins) but before UseAuthentication, so the
        // JWT bearer's signing-key lookup and issuer validation see the
        // tenant the token claims rather than the request host's tenant.
        services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter,
            Oluso.Middleware.BearerTokenTenantResolverStartupFilter>();

        // Configure OIDC options from configuration
        services.Configure<OlusoOptions>(configuration.GetSection("Oluso"));

        // Bind HostValidationOptions from Oluso:HostValidation so consumers can
        // set Enabled / AdditionalAllowedHosts / etc. via env vars and appsettings
        // without writing inline Configure() lambdas. Multiple Configure() calls
        // compose, so any consumer-side .Configure<HostValidationOptions>(...)
        // still applies on top of the values bound here.
        services.Configure<Oluso.Middleware.HostValidationOptions>(
            configuration.GetSection("Oluso:HostValidation"));

        // Register event service, core event property source, and webhook event registry
        services.AddOlusoEvents();

        // Register tenant seeding event sink (seeds module data on tenant creation)
        services.AddTenantSeeding();

        // Register action center aggregation service + platform-level
        // providers. Module providers are
        // registered from each module's descriptor.
        services.AddActionCenter();
        services.AddActionCenterProvider<Oluso.ActionCenter.MentionsActionCenterProvider>();

        // Conversation threads: shared by the workspace and Service API controllers
        services.TryAddScoped<Oluso.Core.Notifications.IConversationService, Oluso.Core.Notifications.ConversationService>();

        // Register feature gate (always available — returns Allowed when no license/providers configured)
        services.AddFeatureGate();

        // Register HTML sanitization service for XSS protection in rich text content
        services.TryAddSingleton<IHtmlSanitizationService, HtmlSanitizationService>();

        // Register key encryption service (shared between keys and certificates)
        services.TryAddSingleton<IKeyEncryptionService, DataProtectionKeyEncryptionService>();

        // Register tenant configuration encryption options (modules add their section keys)
        services.AddOptions<Oluso.Core.Services.TenantConfigurationOptions>();

        // Register certificate services (local provider by default)
        services.TryAddSingleton<ICertificateMaterialProvider, LocalCertificateMaterialProvider>();
        services.TryAddSingleton<ICertificateMaterialProviderRegistry>(sp =>
        {
            var providers = sp.GetServices<ICertificateMaterialProvider>();
            var certOptions = sp.GetService<Certificates.CertificateOptions>();
            var defaultProvider = certOptions?.DefaultStorageProvider ?? Core.Domain.Entities.KeyStorageProvider.Local;
            return new Certificates.CertificateMaterialProviderRegistry(providers, defaultProvider);
        });
        services.TryAddScoped<ICertificateService, Certificates.CertificateService>();

        // Register authentication method registry for pluggable authentication providers
        // Providers are discovered via IAuthenticationMethodProvider registrations from packages like Fido2
        services.TryAddScoped<IAuthenticationMethodRegistry, DefaultAuthenticationMethodRegistry>();

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
        // Register for both the Oluso.Middleware and Oluso.Core.Services interfaces
        builder.Services.AddSingleton<Middleware.HostValidationCacheInvalidator>();
        builder.Services.AddSingleton<Middleware.IHostValidationCacheInvalidator>(sp => sp.GetRequiredService<Middleware.HostValidationCacheInvalidator>());
        builder.Services.AddSingleton<Core.Services.IHostValidationCacheInvalidator>(sp => sp.GetRequiredService<Middleware.HostValidationCacheInvalidator>());

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
    /// Default tenant ID when no tenant is resolved from request.
    /// Defaults to the platform tenant — platform clients (admin-portal, workspace-portal)
    /// serve users from all tenants, and the platform tenant enables unscoped user lookup.
    /// </summary>
    public string? DefaultTenantId { get; set; } = Core.PlatformConstants.TenantId;

    /// <summary>
    /// Subdomains that are reserved and should not be treated as tenant identifiers.
    /// Case-insensitive comparison.
    /// </summary>
    public HashSet<string> ReservedSubdomains { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "auth", "login", "mail", "smtp", "ftp",
        "cdn", "static", "assets", "status", "health", "docs"
    };

    /// <summary>
    /// Query parameter name used for tenant resolution (default: "tenant").
    /// </summary>
    public string TenantQueryParameterName { get; set; } = "tenant";

    /// <summary>
    /// Base domain for subdomain-based tenant resolution.
    /// Auto-derived from IssuerUri host if not explicitly set.
    /// Tenant subdomains are resolved as {identifier}.{BaseDomain}.
    /// Example: "auth.example.com" enables "acme.auth.example.com" → tenant "acme".
    /// Example: "localhost" enables "acme.localhost" → tenant "acme".
    /// </summary>
    public string? BaseDomain { get; set; }
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
        builder.Configuration.GetSection(UserJourneyOptions.SectionName).Bind(options);
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

        // Files posted to journey steps: validated by content, stored as attachments
        // readable through the generic attachment endpoint by the uploader and by
        // administrators who can read submissions.
        builder.Services.AddScoped<Oluso.Core.Storage.IJourneyFileUploadService, Oluso.Storage.JourneyFileUploadService>();
        builder.Services.AddScoped<Oluso.Core.Storage.IFileAccessProvider, Oluso.Storage.JourneyUploadFileAccessProvider>();
        builder.Services.AddScoped<IJourneyUserAttachedHandler, Oluso.Storage.JourneyUploadUserAttachedHandler>();

        // Register all built-in step handlers via the canonical builder
        // pattern.
        // Discovered by DefaultStepHandlerRegistry via
        // IServiceProvider.GetServices<IStepHandler>().
        var journeyBuilder = new Oluso.UserJourneys.UserJourneyBuilder(builder);
        journeyBuilder.AddAllBuiltInSteps();

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
        // Register the concrete type AND expose it as ICustomStepHandler so
        // DefaultStepHandlerRegistry.GetServices<ICustomStepHandler>() can
        // discover it. Without the second registration the handler goes
        // into DI but is unreachable from the journey runtime.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<THandler, THandler>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICustomStepHandler>(
                sp => sp.GetRequiredService<THandler>()));

        // Apply the StepTypeBuilder configure callback through the canonical
        // IConfigureStepHandlers path the registry constructor invokes.
        // Resolves THandler at Configure-time to read its StepType, then
        // calls registry.Register<THandler>(stepType, configure) which
        // populates _handlerTypes and _typeInfo so the step shows up in
        // the journey-builder UI with admin-supplied metadata.
        builder.Services.AddSingleton<IConfigureStepHandlers>(sp =>
            new CustomJourneyStepConfigurator<THandler>(sp, configure));

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
/// Carries the <see cref="StepTypeBuilder"/> configure callback supplied
/// to <c>AddCustomJourneyStep&lt;T&gt;</c> through to the step handler
/// registry. The registry constructor invokes every
/// <see cref="IConfigureStepHandlers"/> contributor at startup; this one
/// resolves the handler instance to read its <c>StepType</c> and then
/// calls <c>registry.Register&lt;THandler&gt;(stepType, configure)</c> so
/// the step appears in the journey-builder UI with admin-supplied
/// metadata.
/// </summary>
internal class CustomJourneyStepConfigurator<THandler> : IConfigureStepHandlers
    where THandler : class, ICustomStepHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Action<StepTypeBuilder>? _configure;

    public CustomJourneyStepConfigurator(IServiceProvider serviceProvider, Action<StepTypeBuilder>? configure)
    {
        _serviceProvider = serviceProvider;
        _configure = configure;
    }

    public void Configure(IExtendedStepHandlerRegistry registry)
    {
        var handler = _serviceProvider.GetRequiredService<THandler>();
        registry.Register<THandler>(handler.StepType, _configure);
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
