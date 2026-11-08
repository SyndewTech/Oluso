using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
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
using Oluso.Services;
using Oluso.Storage;

namespace Oluso;

/// <summary>
/// Builder for configuring Oluso identity services
/// </summary>
public class OlusoBuilder
{
    /// <summary>HttpContext item carrying a failed sender-constraint check from token validation to the 401 challenge.</summary>
    private const string SenderConstraintChallengeItem = "Oluso.SenderConstraintChallenge";

    private static void FailSenderConstraint(TokenValidatedContext context, Protocols.SenderConstraint.SenderConstraintFailure failure)
    {
        context.HttpContext.Items[SenderConstraintChallengeItem] = failure;
        context.Fail(failure.Description);
    }

    public IServiceCollection Services { get; }
    public IConfiguration Configuration { get; }
    internal OlusoOptions Options { get; } = new();

    internal List<Action<IMvcBuilder>> MvcConfigurations { get; } = new();
    internal List<Action<MvcOptions>> MvcOptionConfigurations { get; } = new();

    internal OlusoBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;

        // Public-asset serving/resizing config (provider-agnostic) — bound here so
        // the public file endpoints can resolve it regardless of storage provider.
        Services.AddOptions<Core.Storage.PublicAssetOptions>()
            .BindConfiguration(Core.Storage.PublicAssetOptions.SectionName);

        // Public-asset allowlist — core owns only its own prefixes; modules
        // contribute the rest via IPublicAssetPathProvider (collected here).
        Services.AddSingleton<Core.Storage.IPublicAssetPathRegistry, Core.Storage.PublicAssetPathRegistry>();

        // Core protocol services
        Services.AddScoped<IAuthenticationCoordinator, AuthenticationCoordinator>();
        Services.AddScoped<ISessionHintMatcher, SessionHintMatcher>();
        Services.AddScoped<Protocols.IConsentDecisions, Protocols.ConsentDecisions>();
        Services.AddScoped<Protocols.Device.IDeviceVerificationService, Protocols.Device.DeviceVerificationService>();
        Services.AddSingleton<IProtocolStateStore, InMemoryProtocolStateStore>();

        // Plugin registry - collects all managed plugins
        Services.TryAddSingleton<IManagedPluginRegistry, DefaultManagedPluginRegistry>();

        // Tenant settings provider (can be overridden by user)
        Services.TryAddScoped<ITenantSettingsProvider, DefaultTenantSettingsProvider>();

        // Issuer resolver and custom-domain DNS verification (shared with headless hosts)
        Services.AddOlusoTenantHostServices();

        // Custom domain activation
        Services.TryAddScoped<Core.Services.ICustomDomainActivationService, Core.Services.CustomDomainActivationService>();

        // Redeems organization invitation links (/account/accept-invitation)
        Services.TryAddScoped<Core.Services.IOrganizationInvitationService, Core.Services.OrganizationInvitationService>();

        // Custom domains finalise asynchronously — the edge provider issues the
        // certificate minutes after the hostname is submitted, so activation
        // can't complete on the request that verified DNS. These sweeps are the
        // only thing that moves a domain from Verified to Active unattended.
        Services.AddHostedService<Jobs.DomainVerificationBackgroundService>();
        Services.AddHostedService<Jobs.DomainRevalidationBackgroundService>();

        // Signing keys expire on a schedule nobody watches: this pass rotates
        // ahead of expiry, marks expired keys, and keeps caches coherent so an
        // expired or rotated key never strands a session.
        Services.AddHostedService<Jobs.SigningKeyMaintenanceBackgroundService>();
        // Expired grants, sessions and PAR requests are swept on the same hourly pass
        // (and by the same maintenance callback), so the tables stop growing forever.
        Services.TryAddScoped<Core.Services.IExpiredRecordsPurger, Services.ExpiredRecordsPurger>();
        // Every interactive sign-in path (standalone pages, external callback, passkeys,
        // journey completion) reports through one auditor so the audit log and dashboard
        // see all of them, not just the local-login journey step.
        Services.TryAddScoped<Core.Services.ISignInAuditor, Services.SignInAuditor>();

        // Claims provider registry - automatically collects claims from enabled plugins
        Services.TryAddScoped<IClaimsProviderRegistry, ClaimsProviderRegistry>();

        // Emits token claims from a client's allowlisted custom /authorize parameters
        Services.AddScoped<Core.Domain.Interfaces.IClaimsProvider, Core.Services.AuthorizeParameterClaimsProvider>();

        // Shared by the profile service and the ID-token builder so both agree
        // on which claim types an identity scope releases.
        Services.TryAddScoped<Core.Services.IIdentityScopeClaimResolver, Core.Services.IdentityScopeClaimResolver>();

        // Journey step secrets: the step-type schema marks which config fields
        // are credentials; these encrypt them at rest and redact them on read.
        // Scoped, not singleton: it reads IExtendedStepHandlerRegistry, which is
        // scoped, so a singleton would be a captive-dependency violation.
        Services.TryAddScoped<Core.UserJourneys.IJourneyStepSecretResolver, Core.UserJourneys.JourneyStepSecretResolver>();
        Services.TryAddScoped<Core.UserJourneys.IJourneyStepSecretProtector, Core.UserJourneys.JourneyStepSecretProtector>();

        // Profile service - uses claims provider registry
        Services.TryAddScoped<IProfileService, DefaultProfileService>();

        // Which tenant a token is for, and whether the routed tenant may issue it (authorize and token endpoints).
        Services.TryAddScoped<Core.Services.ITokenTenantResolver, Core.Services.TokenTenantResolver>();
        Services.TryAddScoped<Core.Services.IClientUserAccess, Core.Services.ClientUserAccess>();

        // Scopes that require a tenant feature (ApiScope property required_feature) are issued only while the
        // routed tenant has it on.
        Services.TryAddScoped<Core.Services.IScopeFeatureGate, Core.Services.ScopeFeatureGate>();

        // Dynamic CORS policy provider - checks client origins + app config
        Services.TryAddSingleton<ICorsPolicyProvider, Services.OlusoCorsPolicyProvider>();
        Services.TryAddSingleton<Oluso.Core.Services.ICorsOriginsCacheInvalidator, Services.CorsOriginsCacheInvalidator>();

        // Signed journey launch links: context for the calling app, and a return address
        // limited to the tenant's host and its registered app origins.
        Services.TryAddScoped<Oluso.Core.Services.ITenantAppOrigins, Services.TenantAppOrigins>();
        Services.TryAddScoped<Core.UserJourneys.IJourneyLaunchTokenService, Core.UserJourneys.JourneyLaunchTokenService>();
        Services.TryAddScoped<Core.UserJourneys.IJourneyStepForms, Core.UserJourneys.JourneyStepForms>();
        Services.AddHttpContextAccessor();
        Services.TryAddScoped<Core.UserJourneys.IHeadlessJourneySessions, Core.UserJourneys.HeadlessJourneySessions>();
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
    /// Add local file system storage with sensible defaults.
    /// Uses "./storage" directory relative to the application base path.
    /// Ideal for development scenarios.
    /// </summary>
    public OlusoBuilder AddLocalFileStorage()
    {
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "storage");
        return AddLocalFileStorage(defaultPath);
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
    /// Add plugin hook system for executing WASM plugins in response to domain events.
    /// Requires AddDatabasePluginStore() or AddFileSystemPluginStore() to be called first.
    /// </summary>
    public OlusoBuilder AddPluginHooks()
    {
        // Plugin hook stores
        Services.AddScoped<Core.Plugins.IPluginHookStore, PluginHookStore>();
        Services.AddScoped<Core.Plugins.IPluginExecutionLogger, PluginExecutionLogStore>();
        Services.AddScoped<PluginNetworkConfigStore>();
        Services.AddScoped<PluginConfigOverrideStore>();

        // Plugin hook runtime
        Services.AddScoped<Core.Plugins.IPluginHookRuntime, Core.Plugins.PluginHookRuntime>();

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

        // One issuer for codes returned to a page instead of a redirect (SDK passkey, embedded and headless
        // journeys, FedCM): authorize-endpoint client rules, then the authorize endpoint's code shape.
        Services.AddScoped<Core.Protocols.IAuthorizationCodeIssuer, Protocols.AuthorizationCodeIssuer>();

        // Register validators
        Services.AddScoped<IPkceValidator, PkceValidator>();
        Services.AddScoped<IScopeValidator, ScopeValidator>();
        Services.AddScoped<IRedirectUriValidator, RedirectUriValidator>();
        Services.AddScoped<IClientAuthenticator, ClientAuthenticator>();
        Services.AddScoped<IAuthorizeRequestValidator, AuthorizeRequestValidator>();
        Services.AddScoped<ITokenRequestValidator, TokenRequestValidator>();
        Services.AddScoped<IBearerTokenValidator, BearerTokenValidator>();

        // Register mTLS services (RFC 8705)
        Services.AddSingleton(Options.Mtls);
        Services.AddSingleton<Protocols.Mtls.IClientCertificateExtractor>(sp =>
            new Protocols.Mtls.ClientCertificateExtractor(
                Options.Mtls,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Protocols.Mtls.ClientCertificateExtractor>>()));

        // Register DPoP services
        // Replay cache for proof jti values: atomic in this process, shared through Redis when a Redis
        // connection string is configured (same one the realtime backplane uses).
        Services.TryAddSingleton<IDPoPReplayCache>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var redis = configuration.GetConnectionString("RealtimeRedis") ?? configuration.GetConnectionString("Redis");
            return string.IsNullOrWhiteSpace(redis)
                ? new LocalDPoPReplayCache(sp.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>())
                : new RedisDPoPReplayCache(redis);
        });
        Services.AddSingleton<IDPoPNonceStore, InMemoryDPoPNonceStore>();
        Services.AddScoped<IDPoPProofValidator, DPoPProofValidator>();
        Services.AddScoped<IDPoPResourceValidator, DPoPResourceValidator>();
        Services.AddSingleton<Protocols.Mtls.ICertificateBoundTokenValidator, Protocols.Mtls.CertificateBoundTokenValidator>();

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
        Services.AddScoped<IAudienceResolver, AudienceResolver>();
        Services.AddScoped<IClientUsageRecorder, ClientUsageRecorder>();

        // Register CIBA services
        Services.AddScoped<ICibaService, Protocols.Ciba.CibaService>();
        Services.TryAddScoped<ICibaUserNotificationService, Protocols.Ciba.CibaUserNotificationService>();
        // Delivery order matters: email checks whether push already reached the user.
        Services.AddScoped<ICibaNotificationChannel, Protocols.Ciba.CibaRealtimeNotificationChannel>();
        Services.AddScoped<ICibaNotificationChannel, Protocols.Ciba.CibaPushNotificationChannel>();
        Services.AddScoped<ICibaNotificationChannel, Protocols.Ciba.CibaEmailNotificationChannel>();

        // Register backchannel logout services
        Services.AddScoped<Services.BackchannelLogoutService>();
        Services.AddScoped<IBackchannelLogoutService>(sp => sp.GetRequiredService<Services.BackchannelLogoutService>());
        Services.AddScoped<IResourceServerLogoutNotifier>(sp => sp.GetRequiredService<Services.BackchannelLogoutService>());
        Services.AddOptions<ResourceServerLogoutOptions>();
        Services.AddScoped<ILogoutTokenGenerator, Services.LogoutTokenGenerator>();

        // Register JWT bearer authentication for API endpoints (UserInfo, etc.)
        // This uses dynamic key resolution from ISigningCredentialStore and validates
        // issuer/audience dynamically based on configuration and tenant settings.
        //
        // The "Oluso.Smart" policy scheme is wired up as the default authenticate
        // scheme (set in EntityFrameworkExtensions.RestoreOlusoSmartScheme, after
        // AddIdentity). It forwards each request to either the JWT bearer scheme
        // (if an Authorization header is present) or the Identity cookie scheme
        // (otherwise). Without this, app.UseAuthentication() never populated
        // HttpContext.User for bearer-token requests, so TenantResolutionMiddleware
        // saw isAuthenticated=false and silently skipped ValidateAuthenticatedAccess
        // — letting any user pass an X-Tenant-Id header and switch tenant context.
        Services.AddAuthentication()
            .AddPolicyScheme("Oluso.Smart", "Oluso.Smart", options =>
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    // Realtime hub opened with a ticket rather than an access token.
                    if (Realtime.HubTicketAuthenticationHandler.TicketFrom(ctx.Request) != null)
                        return Realtime.HubTicketAuthenticationHandler.SchemeName;

                    // Bearer header — the regular API path. Forward to JWT.
                    if (ctx.Request.Headers.ContainsKey("Authorization"))
                        return OidcConstants.AccessTokenAuthenticationScheme;

                    // SignalR WS / SSE upgrade — browsers can't set the
                    // Authorization header on a WebSocket upgrade, so the
                    // client puts the bearer in ?access_token=… instead.
                    // Forward to JWT so JwtBearer's OnMessageReceived lifts
                    // the token out of the query string and Context.User
                    // ends up populated for OnConnectedAsync (sub, tenant_id,
                    // etc.). Without this branch the selector would fall
                    // through to the cookie scheme and the hub connection
                    // would join no user-group, silently dropping every
                    // targeted broadcast.
                    if (ctx.Request.Path.StartsWithSegments("/hubs")
                        && !string.IsNullOrEmpty(ctx.Request.Query["access_token"]))
                        return OidcConstants.AccessTokenAuthenticationScheme;

                    return Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme;
                };
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, Realtime.HubTicketAuthenticationHandler>(
                Realtime.HubTicketAuthenticationHandler.SchemeName, configureOptions: null)
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
                    ClockSkew = TimeSpan.FromMinutes(1),
                    // Our signing keys also sign JWTs that are not access tokens (token-exchange tickets,
                    // logout tokens); those are refused here by their typ.
                    TypeValidator = AccessTokenTypes.Validate
                };

                // Use events to dynamically resolve signing keys and validate issuer/audience
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var configuration = context.HttpContext.RequestServices
                            .GetRequiredService<IConfiguration>();
                        var clientStore = context.HttpContext.RequestServices
                            .GetService<IClientStore>();
                        var loggerFactory = context.HttpContext.RequestServices
                            .GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
                        var logger = loggerFactory?.CreateLogger("Oluso.AccessToken");

                        // Accept any iss that's ours: the configured base URL,
                        // or any subdomain of it (per-tenant subdomain auto-
                        // issuer). Pattern-based instead of strict-equality so
                        // a SPA on one host can use a token minted on another
                        // (e.g. SSO on tenant subdomain, API call on platform).
                        // Signature check still gates everything — a forged
                        // iss with the right pattern but wrong signature 401s.
                        // Ours means minted here: the base issuer, a tenant subdomain, or the
                        // token's own tenant's custom domain / issuer override.
                        var services = context.HttpContext.RequestServices;
                        var tokenIssuer = context.Principal?.FindFirst("iss")?.Value;
                        var tokenTenantId = context.Principal?.FindFirst("tenant_id")?.Value;
                        var tenantStore = services.GetService<ITenantStore>();
                        var tokenTenant = string.IsNullOrEmpty(tokenTenantId) || tenantStore == null
                            ? null
                            : await tenantStore.GetByIdAsync(tokenTenantId, context.HttpContext.RequestAborted);
                        var issuerAccepted = await services.GetRequiredService<IIssuerResolver>()
                            .IsOwnIssuerAsync(tokenIssuer, tokenTenant, context.HttpContext.RequestAborted);
                        if (!issuerAccepted)
                        {
                            logger?.LogWarning("Unknown issuer: '{Actual}' (tenant {TenantId})", tokenIssuer, tokenTenantId);
                            // A token exception, so the 401's WWW-Authenticate names the issuer.
                            context.Fail(new SecurityTokenInvalidIssuerException($"The issuer '{tokenIssuer}' isn't one of this server's issuers.")
                            {
                                InvalidIssuer = tokenIssuer
                            });
                            return;
                        }

                        // Validate audience - one of the token's aud values should be a valid client (a token
                        // whose scopes span several APIs lists each one's audience, Oluṣọ's own being the client).
                        var audiences = context.Principal?.FindAll("aud").Select(c => c.Value).Where(v => !string.IsNullOrEmpty(v)).ToList()
                            ?? new List<string>();
                        if (audiences.Count == 0 && context.Principal?.FindFirst("client_id")?.Value is { Length: > 0 } clientIdClaim)
                            audiences.Add(clientIdClaim);
                        var tokenAudience = audiences.FirstOrDefault();

                        if (string.IsNullOrEmpty(tokenAudience))
                        {
                            logger?.LogWarning("Token missing audience claim");
                            context.Fail("Token missing audience claim");
                            return;
                        }

                        // Verify the audience is a valid, enabled client. This runs on
                        // EVERY authenticated API call — keep it cheap. IsClientEnabledAsync
                        // does a one-column projection on the (TenantId, ClientId) unique
                        // index, no Includes, no cartesian explosion.
                        if (clientStore != null)
                        {
                            bool? enabled = null;
                            foreach (var audience in audiences)
                            {
                                enabled = await clientStore.IsClientEnabledAsync(audience);
                                if (enabled == true)
                                {
                                    tokenAudience = audience;
                                    break;
                                }
                            }
                            if (enabled != true)
                            {
                                logger?.LogWarning("Invalid audience: client '{ClientId}' not found or disabled",
                                    tokenAudience);
                                context.Fail(new SecurityTokenInvalidAudienceException(
                                    $"Invalid audience: client '{tokenAudience}' not found or disabled")
                                {
                                    InvalidAudience = tokenAudience
                                });
                                return;
                            }
                        }

                        // A sender-constrained token is usable only by the holder of what it is bound to:
                        // a DPoP key (cnf.jkt, RFC 9449 §7), never as a bearer token, and a client
                        // certificate (cnf.x5t#S256, RFC 8705 §3).
                        var confirmation = Oluso.Core.Protocols.SenderConstraint.TokenConfirmation.Parse(context.Principal?.FindFirst("cnf")?.Value);
                        var presented = PresentedAccessToken.FromAuthorizationHeader(context.Request.Headers.Authorization);
                        var rawToken = context.SecurityToken switch
                        {
                            Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt => jwt.EncodedToken,
                            System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt => jwt.RawData,
                            _ => presented?.Token,
                        };
                        var dpop = await services.GetRequiredService<IDPoPResourceValidator>().ValidateAsync(
                            context.Request, presented?.Scheme, rawToken ?? string.Empty, confirmation.Jkt,
                            context.HttpContext.RequestAborted);
                        if (!dpop.IsValid)
                        {
                            logger?.LogWarning("DPoP check failed: {Error} {Description}", dpop.Error, dpop.ErrorDescription);
                            FailSenderConstraint(context, new Protocols.SenderConstraint.SenderConstraintFailure(
                                PresentedAccessToken.DPoPScheme, dpop.Error ?? "invalid_token",
                                dpop.ErrorDescription ?? "DPoP validation failed", dpop.ServerNonce));
                            return;
                        }

                        var certificateProblem = services.GetRequiredService<Protocols.Mtls.ICertificateBoundTokenValidator>()
                            .Problem(context.HttpContext, confirmation.CertificateThumbprint);
                        if (certificateProblem != null)
                        {
                            logger?.LogWarning("Certificate-bound token check failed: {Description}", certificateProblem);
                            FailSenderConstraint(context, new Protocols.SenderConstraint.SenderConstraintFailure(
                                presented?.Scheme ?? PresentedAccessToken.BearerScheme, "invalid_token", certificateProblem));
                        }
                    },
                    OnChallenge = context =>
                    {
                        if (context.HttpContext.Items[SenderConstraintChallengeItem] is not Protocols.SenderConstraint.SenderConstraintFailure failure)
                            return Task.CompletedTask;
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers.WWWAuthenticate =
                            $"{failure.Scheme} error=\"{failure.Error}\", error_description=\"{failure.Description.Replace("\"", "'")}\"";
                        if (!string.IsNullOrEmpty(failure.Nonce))
                            context.Response.Headers["DPoP-Nonce"] = failure.Nonce;
                        return Task.CompletedTask;
                    },
                    OnMessageReceived = async context =>
                    {
                        // JwtBearer reads only "Authorization: Bearer"; a DPoP-scheme token is read here.
                        if (PresentedAccessToken.FromAuthorizationHeader(context.Request.Headers.Authorization) is { IsDPoP: true } dpopToken)
                            context.Token = dpopToken.Token;

                        // SignalR WebSocket auth: browsers can't set the
                        // Authorization header on a WebSocket upgrade, so
                        // the SignalR client passes the bearer in the
                        // query string. Move it onto context.Token so
                        // JwtBearer validates it like any other request.
                        // Restricted to /hubs/ paths so this never picks
                        // up unrelated callers' query strings.
                        // Only when no Authorization header was sent: a header token is the one the
                        // DPoP/mTLS checks look at, so a query token must never replace it.
                        var path = context.HttpContext.Request.Path;
                        if (path.StartsWithSegments("/hubs") && !context.Request.Headers.ContainsKey("Authorization"))
                        {
                            var qsToken = context.Request.Query["access_token"];
                            if (!string.IsNullOrEmpty(qsToken))
                            {
                                context.Token = qsToken;
                            }
                        }

                        // Signing key lookup: BearerTokenTenantResolverMiddleware
                        // has already set ITenantContext from the token's
                        // tenant_id claim before this fires, so the credential
                        // store returns keys for the right tenant via the plain
                        // GetValidationKeysAsync — no peek needed here.
                        var credentialStore = context.HttpContext.RequestServices
                            .GetService<ISigningCredentialStore>();

                        if (credentialStore != null)
                        {
                            // A token signed by a key this instance has not cached yet
                            // (rotated elsewhere) triggers one refresh instead of a 401.
                            var keys = await credentialStore.GetValidationKeysAsync(
                                tenantId: null, requiredKeyId: Keys.TokenKeyIdReader.Read(context));
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

    /// <summary>
    /// Pattern-based iss acceptance. A token's issuer is "ours" when it
    /// matches the configured base URL exactly, or is a single-label
    /// subdomain of that base on the same scheme + port (the
    /// per-tenant auto-issuer shape). Signature validation still
    /// gates the request — this is only the safety check that the
    /// claim was minted by something we recognise.
    /// </summary>
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
        mvcBuilder.AddMvcOptions(options => options.Filters.Add<Oluso.Core.Locale.TenantCurrencyNotSetExceptionFilter>());

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
    /// Absolute base URL of the workspace SPA (e.g. <c>https://app.myapp.com</c>).
    /// Used to build user-facing links — currently magic-link decide URLs
    /// pushed into approval emails. Falls back to <see cref="IssuerUri"/>
    /// when unset, which is correct for single-host deployments.
    /// </summary>
    public string? WorkspaceBaseUrl { get; set; }

    /// <summary>
    /// Enable automatic database migrations on startup
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Run the hourly signing key maintenance pass (mark expired keys, rotate ahead
    /// of expiry, clean up) inside this host. Turn off when a scheduler triggers it
    /// through <c>POST api/system/maintenance/signing-keys</c> instead; both paths
    /// are idempotent, so leaving it on alongside the callback is safe.
    /// </summary>
    public bool RunSigningKeyMaintenanceInProcess { get; set; } = true;

    /// <summary>
    /// Run the custom domain and email domain checks (pending verification every
    /// 5 minutes, revalidation of verified domains hourly) inside this host. The
    /// checks are not designed to run concurrently, so with several instances
    /// leave this on in exactly one of them and turn it off in the rest.
    /// </summary>
    public bool RunDomainChecksInProcess { get; set; } = true;

    /// <summary>
    /// Offer "Sign in with your organisation" on the platform login pages
    /// (<c>Oluso:EnableOrganizationSignIn</c>). The user enters a tenant identifier
    /// and is handed to that tenant's own login, then returned to the platform
    /// signed in. Each tenant can still opt out (<c>Tenant.AllowPlatformOrgSignIn</c>).
    /// </summary>
    public bool EnableOrganizationSignIn { get; set; } = true;

    /// <summary>
    /// Token lifetimes
    /// </summary>
    public TokenOptions Tokens { get; set; } = new();

    /// <summary>
    /// Mutual TLS (mTLS) client authentication settings (RFC 8705).
    /// </summary>
    public Core.Protocols.MtlsOptions Mtls { get; set; } = new();

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
