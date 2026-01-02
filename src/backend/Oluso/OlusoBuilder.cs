using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.DPoP;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Storage;
using Oluso.Core.UserJourneys;
using Oluso.Core.Services;
using Oluso.EntityFramework.Stores;
using Oluso.Protocols;
using Oluso.Protocols.DPoP;
using Oluso.Protocols.Grants;
using Oluso.Protocols.Oidc;
using Oluso.Protocols.Routing;
using Oluso.Protocols.Services;
using Oluso.Protocols.Validation;
using Oluso.Storage;

namespace Oluso;

/// <summary>
/// Builder for configuring Oluso identity services
/// </summary>
public class OlusoBuilder
{
    public IServiceCollection Services { get; }
    public IConfiguration Configuration { get; }
    internal OlusoOptions Options { get; } = new();

    internal List<Action<IMvcBuilder>> MvcConfigurations { get; } = new();
    internal List<Action<MvcOptions>> MvcOptionConfigurations { get; } = new();

    internal OlusoBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;

        // Core protocol services
        Services.AddScoped<IAuthenticationCoordinator, AuthenticationCoordinator>();
        Services.AddSingleton<IProtocolStateStore, InMemoryProtocolStateStore>();

        // Plugin registry - collects all managed plugins
        Services.TryAddSingleton<IManagedPluginRegistry, DefaultManagedPluginRegistry>();

        // Tenant settings provider (can be overridden by user)
        Services.TryAddScoped<ITenantSettingsProvider, DefaultTenantSettingsProvider>();

        // Issuer resolver - centralized issuer URI resolution for multi-tenancy
        Services.TryAddScoped<IIssuerResolver, Services.IssuerResolver>();

        // Claims provider registry - automatically collects claims from enabled plugins
        Services.TryAddScoped<IClaimsProviderRegistry, ClaimsProviderRegistry>();

        // Profile service - uses claims provider registry
        Services.TryAddScoped<IProfileService, DefaultProfileService>();

        // Dynamic CORS policy provider - checks client origins + app config
        Services.TryAddSingleton<ICorsPolicyProvider, Services.OlusoCorsPolicyProvider>();
        Services.TryAddSingleton<Services.ICorsOriginsCacheInvalidator, Services.CorsOriginsCacheInvalidator>();
    }

    /// <summary>
    /// Configures Oluso options
    /// </summary>
    public OlusoBuilder Configure(Action<OlusoOptions> configure)
    {
        configure(Options);
        return this;
    }

    /// <summary>
    /// Register a managed plugin.
    /// Plugins that implement IPluginClaimsProvider will automatically contribute claims.
    /// </summary>
    public OlusoBuilder AddPlugin<TPlugin>() where TPlugin : class, IManagedPlugin
    {
        Services.AddSingleton<IManagedPlugin, TPlugin>();
        // Auto-register with the registry
        Services.AddSingleton<IConfigureOptions<object>>(sp =>
        {
            var plugin = sp.GetRequiredService<IEnumerable<IManagedPlugin>>()
                .OfType<TPlugin>()
                .FirstOrDefault();
            if (plugin != null)
            {
                var registry = sp.GetRequiredService<IManagedPluginRegistry>();
                registry.Register(plugin.Name, plugin);
            }
            return new ConfigureOptions<object>(_ => { });
        });
        return this;
    }

    /// <summary>
    /// Register a managed plugin with a factory.
    /// </summary>
    public OlusoBuilder AddPlugin<TPlugin>(Func<IServiceProvider, TPlugin> factory)
        where TPlugin : class, IManagedPlugin
    {
        Services.AddSingleton<IManagedPlugin>(factory);
        return this;
    }

    /// <summary>
    /// Register a custom tenant settings provider.
    /// Use this to override the default tenant settings resolution.
    /// </summary>
    public OlusoBuilder UseTenantSettingsProvider<TProvider>() where TProvider : class, ITenantSettingsProvider
    {
        Services.AddScoped<ITenantSettingsProvider, TProvider>();
        return this;
    }

    /// <summary>
    /// Register a custom profile service.
    /// Use this to override how user claims are collected.
    /// </summary>
    public OlusoBuilder UseProfileService<TService>() where TService : class, IProfileService
    {
        Services.AddScoped<IProfileService, TService>();
        return this;
    }

    /// <summary>
    /// Add Admin API endpoints for user, client, and settings management.
    /// Requires the Oluso.Admin package. The configuration action receives the MVC builder
    /// and should call AddOlusoAdmin() to register controllers and authorization policies.
    /// </summary>
    /// <param name="configure">Action to configure Admin API (typically call mvcBuilder.AddOlusoAdmin())</param>
    public OlusoBuilder AddAdminApi(Action<IMvcBuilder>? configure = null)
    {
        Options.AdminApiEnabled = true;
        AdminApiConfiguration = configure;
        return this;
    }

    /// <summary>
    /// Add file system based plugin storage for WASM plugins.
    /// Plugins are stored in the specified directory with separate folders for global and tenant plugins.
    /// Also registers the Extism plugin executor for WASM plugin execution with hot-reload support.
    /// </summary>
    /// <param name="baseDirectory">Base directory for plugin storage</param>
    /// <param name="enableHotReload">Enable hot-reload when plugin files change (default: true)</param>
    public OlusoBuilder AddFileSystemPluginStore(string baseDirectory, bool enableHotReload = true)
    {
        // Register plugin store
        Services.AddSingleton<IPluginStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileSystemPluginStore>>();
            return new FileSystemPluginStore(baseDirectory, logger);
        });

        // Register plugin executor options
        var executorOptions = new PluginExecutorOptions
        {
            PluginDirectory = baseDirectory,
            EnableHotReload = enableHotReload
        };
        Services.AddSingleton(executorOptions);

        // Register plugin watcher (enabled or disabled based on hot-reload setting)
        if (enableHotReload)
        {
            Services.AddSingleton<UserJourneys.Plugins.IPluginWatcher, UserJourneys.Plugins.FileSystemPluginWatcher>();
        }
        else
        {
            Services.AddSingleton<UserJourneys.Plugins.IPluginWatcher, UserJourneys.Plugins.NullPluginWatcher>();
        }

        // Register plugin executor with factory to start watcher if hot-reload is enabled
        Services.AddSingleton<IPluginExecutor>(sp =>
        {
            var executor = new UserJourneys.Plugins.ExtismPluginExecutor(
                sp.GetRequiredService<IManagedPluginRegistry>(),
                sp.GetRequiredService<UserJourneys.Plugins.IPluginWatcher>(),
                sp.GetRequiredService<ILogger<UserJourneys.Plugins.ExtismPluginExecutor>>(),
                sp.GetService<IPluginStore>(),
                sp.GetService<PluginExecutorOptions>()
            );

            // Start watching for plugin changes if hot-reload is enabled
            if (enableHotReload && !string.IsNullOrEmpty(baseDirectory))
            {
                var watcher = sp.GetRequiredService<UserJourneys.Plugins.IPluginWatcher>();
                watcher.StartWatching(baseDirectory);
            }

            return executor;
        });

        return this;
    }

    /// <summary>
    /// Add local file system storage for plugins and files.
    /// Use this for development or single-server deployments.
    /// </summary>
    /// <param name="baseDirectory">Base directory for file storage</param>
    /// <param name="configure">Optional additional configuration</param>
    public OlusoBuilder AddLocalFileStorage(string baseDirectory, Action<LocalFileOptions>? configure = null)
    {
        Services.Configure<LocalFileOptions>(options =>
        {
            options.BaseDirectory = baseDirectory;
            options.CreateDirectoryIfNotExists = true;
            configure?.Invoke(options);
        });

        Services.AddSingleton<IFileUploader, LocalFileUploader>();

        return this;
    }

    /// <summary>
    /// Add database-backed plugin store with file storage.
    /// Metadata is stored in the database for queryable access,
    /// plugin bytes are stored using the registered IFileUploader.
    /// Requires IFileUploader to be registered (use AddLocalFileStorage or AddAzureBlobStorage).
    /// </summary>
    public OlusoBuilder AddDatabasePluginStore()
    {
        Services.AddScoped<IPluginStore, DatabasePluginStore>();
        return this;
    }

    /// <summary>
    /// Internal callback for Admin API configuration, invoked by ApplyOlusoConventions
    /// </summary>
    internal Action<IMvcBuilder>? AdminApiConfiguration { get; private set; }

    /// <summary>
    /// Add OIDC/OAuth 2.0 protocol support
    /// </summary>
    public OlusoBuilder AddOidc(Action<OidcEndpointConfiguration>? configure = null)
    {
        var config = new OidcEndpointConfiguration();
        configure?.Invoke(config);

        Services.Configure<OidcEndpointConfiguration>(opt =>
        {
            opt.AuthorizeEndpoint = config.AuthorizeEndpoint;
            opt.TokenEndpoint = config.TokenEndpoint;
            opt.UserInfoEndpoint = config.UserInfoEndpoint;
            opt.RevocationEndpoint = config.RevocationEndpoint;
            opt.IntrospectionEndpoint = config.IntrospectionEndpoint;
            opt.EndSessionEndpoint = config.EndSessionEndpoint;
            opt.DeviceAuthorizationEndpoint = config.DeviceAuthorizationEndpoint;
            opt.PushedAuthorizationEndpoint = config.PushedAuthorizationEndpoint;
            opt.DiscoveryEndpoint = config.DiscoveryEndpoint;
            opt.JwksEndpoint = config.JwksEndpoint;
            opt.EnablePar = config.EnablePar;
            opt.EnableDPoP = config.EnableDPoP;
            opt.PolicyQueryParam = config.PolicyQueryParam;
            opt.PolicyQueryParamAlternate = config.PolicyQueryParamAlternate;
            opt.UiModeQueryParam = config.UiModeQueryParam;
            opt.StateExpiration = config.StateExpiration;
        });

        Services.AddScoped<IOidcProtocolService, OidcProtocolService>();

        // Register validators
        Services.AddScoped<IPkceValidator, PkceValidator>();
        Services.AddScoped<IScopeValidator, ScopeValidator>();
        Services.AddScoped<IRedirectUriValidator, RedirectUriValidator>();
        Services.AddScoped<IClientAuthenticator, ClientAuthenticator>();
        Services.AddScoped<IAuthorizeRequestValidator, AuthorizeRequestValidator>();
        Services.AddScoped<ITokenRequestValidator, TokenRequestValidator>();

        // Register DPoP services
        Services.AddSingleton<IDPoPNonceStore, InMemoryDPoPNonceStore>();
        Services.AddScoped<IDPoPProofValidator, DPoPProofValidator>();

        // Register grant handlers
        Services.AddScoped<IGrantHandler, AuthorizationCodeGrantHandler>();
        Services.AddScoped<IGrantHandler, RefreshTokenGrantHandler>();
        Services.AddScoped<IGrantHandler, ClientCredentialsGrantHandler>();
        Services.AddScoped<IGrantHandler, DeviceCodeGrantHandler>();
        Services.AddScoped<IGrantHandler, TokenExchangeGrantHandler>();
        Services.AddScoped<IGrantHandler, ResourceOwnerPasswordGrantHandler>();
        Services.AddScoped<IGrantHandler, CibaGrantHandler>();
        Services.AddScoped<IGrantHandlerRegistry, GrantHandlerRegistry>();

        // Register token service
        Services.AddScoped<ITokenService, TokenService>();

        // Register CIBA services
        Services.AddScoped<ICibaService, Protocols.Ciba.CibaService>();
        Services.TryAddScoped<ICibaUserNotificationService, Services.DefaultCibaUserNotificationService>();

        // Register backchannel logout services
        Services.AddScoped<IBackchannelLogoutService, Services.BackchannelLogoutService>();
        Services.AddScoped<ILogoutTokenGenerator, Services.LogoutTokenGenerator>();

        // Register JWT bearer authentication for API endpoints (UserInfo, etc.)
        // This uses dynamic key resolution from ISigningCredentialStore and validates
        // issuer/audience dynamically based on configuration and tenant settings
        Services.AddAuthentication()
            .AddJwtBearer(OidcConstants.AccessTokenAuthenticationScheme, options =>
            {
                // Disable inbound claim type mapping to preserve original JWT claim names (e.g., "sub" stays "sub")
                options.MapInboundClaims = false;

                // Configure token validation - issuer and audience validated dynamically
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Issuer validation - uses custom validator that checks against configuration
                    ValidateIssuer = true,
                    IssuerValidator = (issuer, token, parameters) =>
                    {
                        // Issuer is validated in OnTokenValidated where we have HttpContext
                        return issuer;
                    },
                    // Audience validation - validated in OnTokenValidated against client store
                    ValidateAudience = true,
                    AudienceValidator = (audiences, token, parameters) =>
                    {
                        // Audience is validated in OnTokenValidated where we have access to client store
                        return true;
                    },
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                // Use events to dynamically resolve signing keys and validate issuer/audience
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var issuerResolver = context.HttpContext.RequestServices
                            .GetService<IIssuerResolver>();
                        var clientStore = context.HttpContext.RequestServices
                            .GetService<IClientStore>();
                        var loggerFactory = context.HttpContext.RequestServices
                            .GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
                        var logger = loggerFactory?.CreateLogger("Oluso.AccessToken");

                        // Validate issuer using centralized resolver
                        var expectedIssuer = issuerResolver != null
                            ? await issuerResolver.GetIssuerAsync()
                            : $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');

                        var tokenIssuer = context.Principal?.FindFirst("iss")?.Value;
                        if (!string.Equals(tokenIssuer, expectedIssuer, StringComparison.OrdinalIgnoreCase))
                        {
                            logger?.LogWarning("Issuer mismatch: expected '{Expected}', got '{Actual}'",
                                expectedIssuer, tokenIssuer);
                            context.Fail($"Invalid issuer: expected '{expectedIssuer}'");
                            return;
                        }

                        // Validate audience - the token's aud claim should match a valid client
                        var tokenAudience = context.Principal?.FindFirst("aud")?.Value
                            ?? context.Principal?.FindFirst("client_id")?.Value;

                        if (string.IsNullOrEmpty(tokenAudience))
                        {
                            logger?.LogWarning("Token missing audience claim");
                            context.Fail("Token missing audience claim");
                            return;
                        }

                        // Verify the audience is a valid client in the system
                        if (clientStore != null)
                        {
                            var client = await clientStore.FindClientByIdAsync(tokenAudience);
                            if (client == null || !client.Enabled)
                            {
                                logger?.LogWarning("Invalid audience: client '{ClientId}' not found or disabled",
                                    tokenAudience);
                                context.Fail($"Invalid audience: client '{tokenAudience}' not found or disabled");
                                return;
                            }
                        }
                    },
                    OnMessageReceived = async context =>
                    {
                        // Get signing keys from the credential store
                        var credentialStore = context.HttpContext.RequestServices
                            .GetService<ISigningCredentialStore>();

                        if (credentialStore != null)
                        {
                            var keys = await credentialStore.GetValidationKeysAsync();
                            context.Options.TokenValidationParameters.IssuerSigningKeys =
                                keys.Select(k => k.Key);
                        }
                    },
                    OnAuthenticationFailed = context =>
                    {
                        var loggerFactory = context.HttpContext.RequestServices
                            .GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
                        loggerFactory?.CreateLogger("Oluso.AccessToken")
                            .LogWarning(context.Exception,
                                "Access token authentication failed: {Message}",
                                context.Exception.Message);
                        return Task.CompletedTask;
                    }
                };
            });

        // Register route convention
        MvcOptionConfigurations.Add(options =>
        {
            options.Conventions.Add(new OidcEndpointRouteConvention(config));
        });

        // Register controllers from this assembly
        MvcConfigurations.Add(mvc =>
        {
            mvc.AddApplicationPart(typeof(OidcAuthorizeController).Assembly);
        });

        return this;
    }
}

/// <summary>
/// Extension methods for applying Oluso MVC conventions
/// </summary>
public static class OlusoMvcExtensions
{
    /// <summary>
    /// Apply Oluso MVC conventions to the MVC builder.
    /// If AddAdminApi() was called, you must also call mvcBuilder.AddOlusoAdmin() from Oluso.Admin package.
    /// </summary>
    public static IMvcBuilder ApplyOlusoConventions(this IMvcBuilder mvcBuilder, OlusoBuilder olusoBuilder)
    {
        // Apply MVC option configurations (route conventions)
        foreach (var config in olusoBuilder.MvcOptionConfigurations)
        {
            mvcBuilder.AddMvcOptions(config);
        }

        // Apply MVC configurations (application parts, etc.)
        foreach (var config in olusoBuilder.MvcConfigurations)
        {
            config(mvcBuilder);
        }

        // Apply Admin API configuration if one was registered
        olusoBuilder.AdminApiConfiguration?.Invoke(mvcBuilder);

        return mvcBuilder;
    }
}

/// <summary>
/// Core options for Oluso
/// </summary>
public class OlusoOptions
{
    /// <summary>
    /// The issuer URI for tokens (e.g., https://auth.myapp.com)
    /// </summary>
    public string? IssuerUri { get; set; }

    /// <summary>
    /// Enable automatic database migrations on startup
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Token lifetimes
    /// </summary>
    public TokenOptions Tokens { get; set; } = new();

    /// <summary>
    /// Password policy (deprecated - use tenant-specific password policies instead).
    /// Password validation is now handled by TenantPasswordValidator which reads
    /// settings from ITenantSettingsProvider.GetPasswordSettingsAsync().
    /// </summary>
    [Obsolete("Use tenant-specific password policies via ITenantSettingsProvider instead. This property is ignored.")]
    public PasswordOptions Password { get; set; } = new();

    /// <summary>
    /// Whether multi-tenancy is enabled (set by AddMultiTenancy())
    /// </summary>
    internal bool MultiTenancyEnabled { get; set; }

    /// <summary>
    /// Whether User Journey Engine is enabled (set by AddUserJourneyEngine())
    /// </summary>
    internal bool UserJourneyEngineEnabled { get; set; }

    /// <summary>
    /// Whether a custom user service was registered (set by AddUserService())
    /// </summary>
    internal bool CustomUserServiceRegistered { get; set; }

    /// <summary>
    /// Whether to skip ASP.NET Identity registration (set by SkipIdentity())
    /// </summary>
    internal bool SkipIdentityRegistration { get; set; }

    /// <summary>
    /// Whether Admin API is enabled (set by AddAdminApi())
    /// </summary>
    internal bool AdminApiEnabled { get; set; }
}

public class TokenOptions
{
    public int AccessTokenLifetimeSeconds { get; set; } = 3600;
    public int IdentityTokenLifetimeSeconds { get; set; } = 300;
    public int RefreshTokenLifetimeSeconds { get; set; } = 2592000;
}

/// <summary>
/// Password options (deprecated - use TenantPasswordSettings instead).
/// Password validation is now tenant-specific via TenantPasswordValidator.
/// </summary>
[Obsolete("Use tenant-specific password policies via ITenantSettingsProvider instead.")]
public class PasswordOptions
{
    public int RequiredLength { get; set; } = 8;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; } = true;
}
