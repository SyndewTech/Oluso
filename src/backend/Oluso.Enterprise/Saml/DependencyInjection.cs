using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Data;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Saml.Configuration;
using Oluso.Enterprise.Saml.Endpoints;
using Oluso.Enterprise.Saml.EntityFramework;
using Oluso.Enterprise.Saml.IdentityProvider;
using Oluso.Enterprise.Saml.Integration;
using Oluso.Enterprise.Saml.Protocol;
using Oluso.Enterprise.Saml.ServiceProvider;
using Oluso.Enterprise.Saml.Services;
using Oluso.Enterprise.Saml.Stores;

namespace Oluso.Enterprise.Saml;

public static class DependencyInjection
{
    /// <summary>
    /// Adds SAML 2.0 services (both SP and IdP).
    /// This is a licensed add-on feature.
    /// </summary>
    public static IServiceCollection AddSaml(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Validate license once for both SP and IdP
        ValidateSamlLicense(services);

        return services
            .AddSamlServiceProviderInternal(configuration)
            .AddSamlIdentityProviderInternal(configuration);
    }

    /// <summary>
    /// Adds SAML Service Provider functionality.
    /// This is a licensed add-on feature.
    /// </summary>
    public static IServiceCollection AddSamlServiceProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ValidateSamlLicense(services);
        return services.AddSamlServiceProviderInternal(configuration);
    }

    private static IServiceCollection AddSamlServiceProviderInternal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SamlSpOptions>(configuration.GetSection(SamlSpOptions.SectionName));

        // Register HttpClient for metadata fetching
        services.AddHttpClient();

        // Note: SAML metadata endpoints return XML directly via ContentResult,
        // not via MVC content negotiation. No need to add XML formatters globally.

        // Register SAML Service Provider
        // Requires IIdentityProviderStore, ITenantContext, and IIssuerResolver for database-backed IdP configuration
        services.AddScoped<ISamlServiceProvider>(sp =>
        {
            var identityProviderStore = sp.GetRequiredService<IIdentityProviderStore>();
            var tenantContext = sp.GetRequiredService<ITenantContext>();
            var issuerResolver = sp.GetRequiredService<IIssuerResolver>();

            return new SamlServiceProvider(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SamlSpOptions>>(),
                identityProviderStore,
                tenantContext,
                issuerResolver,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SamlServiceProvider>>(),
                sp.GetRequiredService<IHttpClientFactory>());
        });

        // Register SAML protocol service
        services.AddScoped<ISamlProtocolService, SamlProtocolService>();

        // Register journey step handler for SAML authentication
        services.AddScoped<SamlStepHandler>();
        services.AddScoped<IStepHandler>(sp => sp.GetRequiredService<SamlStepHandler>());

        return services;
    }

    private static void ValidateSamlLicense(IServiceCollection services)
    {
        // Build a temporary service provider to check license
        var tempProvider = services.BuildServiceProvider();
        var licenseValidator = tempProvider.GetService<ILicenseValidator>();

        if (licenseValidator != null)
        {
            var validation = licenseValidator.ValidateFeature(LicensedFeatures.Saml);
            if (!validation.IsValid)
            {
                throw new LicenseException(
                    $"SAML 2.0 add-on requires a valid license. {validation.Message}",
                    LicensedFeatures.Saml);
            }
        }
        // If no license validator is registered, allow (development mode)
    }

    /// <summary>
    /// Adds SAML Service Provider with custom configuration.
    /// Note: Requires IIdentityProviderStore, ITenantContext, and IIssuerResolver to be registered.
    /// </summary>
    public static IServiceCollection AddSamlServiceProvider(
        this IServiceCollection services,
        Action<SamlSpOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient();

        services.AddScoped<ISamlServiceProvider>(sp =>
        {
            var identityProviderStore = sp.GetRequiredService<IIdentityProviderStore>();
            var tenantContext = sp.GetRequiredService<ITenantContext>();
            var issuerResolver = sp.GetRequiredService<IIssuerResolver>();

            return new SamlServiceProvider(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SamlSpOptions>>(),
                identityProviderStore,
                tenantContext,
                issuerResolver,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SamlServiceProvider>>(),
                sp.GetRequiredService<IHttpClientFactory>());
        });

        // Register journey step handler for SAML authentication
        services.AddScoped<SamlStepHandler>();

        return services;
    }

    /// <summary>
    /// Adds SAML Identity Provider functionality.
    /// This is a licensed add-on feature.
    /// Note: Requires ISamlServiceProviderStore to be registered (via Oluso.EntityFramework).
    /// </summary>
    public static IServiceCollection AddSamlIdentityProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ValidateSamlLicense(services);
        return services.AddSamlIdentityProviderInternal(configuration);
    }

    private static IServiceCollection AddSamlIdentityProviderInternal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SamlIdpOptions>(configuration.GetSection(SamlIdpOptions.SectionName));
        services.AddScoped<ISamlIdentityProvider, SamlIdentityProvider>();

        // Register SAML tenant settings service for storing SAML-specific tenant config
        services.AddScoped<ISamlTenantSettingsService, SamlTenantSettingsService>();

        // Register SAML Service Provider store (for SAML IdP to manage trusted SPs)
        services.AddScoped<ISamlServiceProviderStore, SamlServiceProviderStore>();

        return services;
    }

    /// <summary>
    /// Adds SAML Identity Provider with custom configuration
    /// </summary>
    public static IServiceCollection AddSamlIdentityProvider(
        this IServiceCollection services,
        Action<SamlIdpOptions> configure)
    {
        services.Configure(configure);
        services.AddScoped<ISamlIdentityProvider, SamlIdentityProvider>();

        return services;
    }

    /// <summary>
    /// Adds SAML with fluent builder pattern
    /// </summary>
    public static IServiceCollection AddSaml(
        this IServiceCollection services,
        Action<SamlBuilder> builder)
    {
        var samlBuilder = new SamlBuilder(services);
        builder(samlBuilder);
        return services;
    }
}

/// <summary>
/// Extension methods for configuring SAML DbContext
/// </summary>
public static class SamlDbContextExtensions
{
    /// <summary>
    /// Add SAML DbContext using the same connection as the host application.
    /// Uses a separate migrations history table.
    /// </summary>
    /// <typeparam name="THostContext">The host application's DbContext type</typeparam>
    /// <param name="services">Service collection</param>
    public static IServiceCollection AddSamlDbContext<THostContext>(this IServiceCollection services)
        where THostContext : DbContext
    {
        var migrationsTable = PluginDbContextExtensions.GetMigrationsTableName(SamlDbContext.PluginIdentifier);

        services.AddDbContext<SamlDbContext>((serviceProvider, options) =>
        {
            // Get the host context's connection
            var hostContext = serviceProvider.GetRequiredService<THostContext>();
            var connection = hostContext.Database.GetDbConnection();

            // Use the same connection with separate migrations table
            // Detect provider and configure appropriately
            var providerName = hostContext.Database.ProviderName ?? "";

            if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connection, sql =>
                    sql.MigrationsHistoryTable(migrationsTable));
            }
            else if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                     providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                var pgTable = PluginDbContextExtensions.GetMigrationsTableNamePostgres(SamlDbContext.PluginIdentifier);
                options.UseNpgsql(connection, npg =>
                    npg.MigrationsHistoryTable(pgTable));
            }
            else if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connection, sqlite =>
                    sqlite.MigrationsHistoryTable(migrationsTable));
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported database provider: {providerName}. " +
                    "Use AddSamlDbContext with explicit configuration instead.");
            }
        });

        // Register for automatic migration discovery
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<SamlDbContext>());

        return services;
    }

    /// <summary>
    /// Add SAML DbContext with a custom connection string (separate database).
    /// Uses a separate migrations history table.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureDb">DbContext options configuration</param>
    public static IServiceCollection AddSamlDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<SamlDbContext>(configureDb);

        // Register for automatic migration discovery
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<SamlDbContext>());

        return services;
    }

    /// <summary>
    /// Add SAML DbContext with SQLite.
    /// </summary>
    public static IServiceCollection AddSamlSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddSamlDbContext(options =>
            options.UseSqlite(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(SamlDbContext.PluginIdentifier))));
    }

    /// <summary>
    /// Add SAML DbContext with SQL Server.
    /// </summary>
    public static IServiceCollection AddSamlSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddSamlDbContext(options =>
            options.UseSqlServer(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(SamlDbContext.PluginIdentifier))));
    }

    /// <summary>
    /// Add SAML DbContext with PostgreSQL.
    /// </summary>
    public static IServiceCollection AddSamlNpgsql(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddSamlDbContext(options =>
            options.UseNpgsql(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(SamlDbContext.PluginIdentifier))));
    }
}

/// <summary>
/// Fluent builder for SAML configuration
/// </summary>
public class SamlBuilder
{
    private readonly IServiceCollection _services;

    public SamlBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configures Service Provider options.
    /// Note: Requires IIdentityProviderStore, ITenantContext, and IIssuerResolver to be registered.
    /// </summary>
    public SamlBuilder WithServiceProvider(Action<SamlSpOptionsBuilder> configure)
    {
        var builder = new SamlSpOptionsBuilder();
        configure(builder);

        _services.Configure<SamlSpOptions>(options => builder.Apply(options));
        _services.AddHttpClient();
        _services.AddScoped<ISamlServiceProvider>(sp =>
        {
            var identityProviderStore = sp.GetRequiredService<IIdentityProviderStore>();
            var tenantContext = sp.GetRequiredService<ITenantContext>();
            var issuerResolver = sp.GetRequiredService<IIssuerResolver>();

            return new SamlServiceProvider(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SamlSpOptions>>(),
                identityProviderStore,
                tenantContext,
                issuerResolver,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SamlServiceProvider>>(),
                sp.GetRequiredService<IHttpClientFactory>());
        });
        _services.AddScoped<SamlStepHandler>();

        return this;
    }

    /// <summary>
    /// Configures Identity Provider options
    /// </summary>
    public SamlBuilder WithIdentityProvider(Action<SamlIdpOptionsBuilder> configure)
    {
        var builder = new SamlIdpOptionsBuilder();
        configure(builder);

        _services.Configure<SamlIdpOptions>(options => builder.Apply(options));
        _services.AddScoped<ISamlIdentityProvider, SamlIdentityProvider>();

        return this;
    }
}

/// <summary>
/// Builder for SP options
/// </summary>
public class SamlSpOptionsBuilder
{
    private readonly SamlSpOptions _options = new();

    public SamlSpOptionsBuilder WithEntityId(string entityId)
    {
        _options.EntityId = entityId;
        return this;
    }

    public SamlSpOptionsBuilder WithBaseUrl(string baseUrl)
    {
        _options.BaseUrl = baseUrl;
        return this;
    }

    public SamlSpOptionsBuilder WithSigningCertificate(Action<CertificateOptions> configure)
    {
        _options.SigningCertificate = new CertificateOptions();
        configure(_options.SigningCertificate);
        return this;
    }

    public SamlSpOptionsBuilder WithDecryptionCertificate(Action<CertificateOptions> configure)
    {
        _options.DecryptionCertificate = new CertificateOptions();
        configure(_options.DecryptionCertificate);
        return this;
    }

    public SamlSpOptionsBuilder RequireSignedResponses(bool require = true)
    {
        _options.RequireSignedResponses = require;
        return this;
    }

    public SamlSpOptionsBuilder RequireSignedAssertions(bool require = true)
    {
        _options.RequireSignedAssertions = require;
        return this;
    }

    public SamlSpOptionsBuilder RequireEncryptedAssertions(bool require = true)
    {
        _options.RequireEncryptedAssertions = require;
        return this;
    }

    public SamlSpOptionsBuilder AddIdentityProvider(Action<SamlIdpConfigBuilder> configure)
    {
        var builder = new SamlIdpConfigBuilder();
        configure(builder);
        _options.IdentityProviders.Add(builder.Build());
        return this;
    }

    internal void Apply(SamlSpOptions options)
    {
        options.EntityId = _options.EntityId;
        options.BaseUrl = _options.BaseUrl;
        options.AssertionConsumerServicePath = _options.AssertionConsumerServicePath;
        options.SingleLogoutServicePath = _options.SingleLogoutServicePath;
        options.MetadataPath = _options.MetadataPath;
        options.SigningCertificate = _options.SigningCertificate;
        options.DecryptionCertificate = _options.DecryptionCertificate;
        options.SignAuthnRequests = _options.SignAuthnRequests;
        options.RequireSignedResponses = _options.RequireSignedResponses;
        options.RequireSignedAssertions = _options.RequireSignedAssertions;
        options.RequireEncryptedAssertions = _options.RequireEncryptedAssertions;
        options.NameIdFormat = _options.NameIdFormat;
        options.AllowedClockSkewSeconds = _options.AllowedClockSkewSeconds;
        options.IdentityProviders = _options.IdentityProviders;
    }
}

/// <summary>
/// Builder for IdP configuration within SP
/// </summary>
public class SamlIdpConfigBuilder
{
    private readonly SamlIdpConfig _config = new();

    public SamlIdpConfigBuilder WithName(string name)
    {
        _config.Name = name;
        return this;
    }

    public SamlIdpConfigBuilder WithDisplayName(string displayName)
    {
        _config.DisplayName = displayName;
        return this;
    }

    public SamlIdpConfigBuilder WithEntityId(string entityId)
    {
        _config.EntityId = entityId;
        return this;
    }

    public SamlIdpConfigBuilder WithMetadataUrl(string url)
    {
        _config.MetadataUrl = url;
        return this;
    }

    public SamlIdpConfigBuilder WithSsoUrl(string url, string binding = "Redirect")
    {
        _config.SingleSignOnServiceUrl = url;
        _config.SingleSignOnServiceBinding = binding;
        return this;
    }

    public SamlIdpConfigBuilder WithSigningCertificate(Action<CertificateOptions> configure)
    {
        _config.SigningCertificate = new CertificateOptions();
        configure(_config.SigningCertificate);
        return this;
    }

    public SamlIdpConfigBuilder MapClaim(string samlAttribute, string claimType)
    {
        _config.ClaimMappings[samlAttribute] = claimType;
        return this;
    }

    internal SamlIdpConfig Build() => _config;
}

/// <summary>
/// Builder for IdP options
/// </summary>
public class SamlIdpOptionsBuilder
{
    private readonly SamlIdpOptions _options = new();

    public SamlIdpOptionsBuilder Enable(bool enabled = true)
    {
        _options.Enabled = enabled;
        return this;
    }

    public SamlIdpOptionsBuilder WithEntityId(string entityId)
    {
        _options.EntityId = entityId;
        return this;
    }

    public SamlIdpOptionsBuilder WithBaseUrl(string baseUrl)
    {
        _options.BaseUrl = baseUrl;
        return this;
    }

    public SamlIdpOptionsBuilder WithSigningCertificate(Action<CertificateOptions> configure)
    {
        configure(_options.SigningCertificate);
        return this;
    }

    public SamlIdpOptionsBuilder WithAssertionLifetime(int minutes)
    {
        _options.AssertionLifetimeMinutes = minutes;
        return this;
    }

    public SamlIdpOptionsBuilder AddServiceProvider(Action<SamlSpConfigBuilder> configure)
    {
        var builder = new SamlSpConfigBuilder();
        configure(builder);
        _options.ServiceProviders.Add(builder.Build());
        return this;
    }

    internal void Apply(SamlIdpOptions options)
    {
        options.Enabled = _options.Enabled;
        options.EntityId = _options.EntityId;
        options.BaseUrl = _options.BaseUrl;
        options.SingleSignOnServicePath = _options.SingleSignOnServicePath;
        options.SingleLogoutServicePath = _options.SingleLogoutServicePath;
        options.MetadataPath = _options.MetadataPath;
        options.SigningCertificate = _options.SigningCertificate;
        options.EncryptionCertificate = _options.EncryptionCertificate;
        options.AssertionLifetimeMinutes = _options.AssertionLifetimeMinutes;
        options.NameIdFormats = _options.NameIdFormats;
        options.ServiceProviders = _options.ServiceProviders;
    }
}

/// <summary>
/// Builder for SP configuration within IdP
/// </summary>
public class SamlSpConfigBuilder
{
    private readonly SamlSpConfig _config = new();

    public SamlSpConfigBuilder WithEntityId(string entityId)
    {
        _config.EntityId = entityId;
        return this;
    }

    public SamlSpConfigBuilder WithDisplayName(string displayName)
    {
        _config.DisplayName = displayName;
        return this;
    }

    public SamlSpConfigBuilder WithMetadataUrl(string url)
    {
        _config.MetadataUrl = url;
        return this;
    }

    public SamlSpConfigBuilder WithAcsUrl(string url)
    {
        _config.AssertionConsumerServiceUrl = url;
        return this;
    }

    public SamlSpConfigBuilder EncryptAssertions(bool encrypt = true)
    {
        _config.EncryptAssertions = encrypt;
        return this;
    }

    public SamlSpConfigBuilder WithEncryptionCertificate(Action<CertificateOptions> configure)
    {
        _config.EncryptionCertificate = new CertificateOptions();
        configure(_config.EncryptionCertificate);
        return this;
    }

    public SamlSpConfigBuilder AllowClaims(params string[] claimTypes)
    {
        _config.AllowedClaims.AddRange(claimTypes);
        return this;
    }

    internal SamlSpConfig Build() => _config;
}

/// <summary>
/// Registers SAML step handler with the step handler registry
/// </summary>
public class SamlStepHandlerRegistration
{
    /// <summary>
    /// Gets the step type info for registration
    /// </summary>
    public static StepTypeInfo GetStepTypeInfo() => new()
    {
        Type = "saml",
        Description = "Authenticate users via SAML 2.0 Identity Provider (SSO)",
        Category = "Authentication",
        Module = "Oluso.Enterprise.Saml",
        HandlerType = typeof(SamlStepHandler)
    };
}

/// <summary>
/// Configuration schema for SAML step (for AdminUI)
/// Uses x-enumSource for dynamic SAML IdP selection from configured Identity Providers
/// </summary>
public static class SamlStepConfigSchema
{
    public const string JsonSchema = """
    {
      "$schema": "http://json-schema.org/draft-07/schema#",
      "type": "object",
      "properties": {
        "idpName": {
          "type": "string",
          "title": "SAML Identity Provider",
          "description": "Select a configured SAML Identity Provider",
          "x-enumSource": {
            "endpoint": "/identity-providers",
            "valueField": "scheme",
            "labelField": "displayName",
            "filters": { "type": "Saml2" }
          }
        },
        "autoRedirect": {
          "type": "boolean",
          "title": "Auto Redirect",
          "description": "Auto-redirect if only one IdP configured",
          "default": false
        },
        "forceAuthn": {
          "type": "boolean",
          "title": "Force Authentication",
          "description": "Force user to re-authenticate even if already logged in at IdP",
          "default": false
        },
        "isPassive": {
          "type": "boolean",
          "title": "Passive Mode",
          "description": "Don't show login UI at IdP if user not authenticated",
          "default": false
        },
        "autoProvision": {
          "type": "boolean",
          "title": "Auto Provision Users",
          "description": "Auto-create local user on first login",
          "default": true
        },
        "proxyMode": {
          "type": "boolean",
          "title": "Proxy Mode",
          "description": "Pass through external claims without local user (federation broker)",
          "default": false
        }
      }
    }
    """;
}

/// <summary>
/// Extension methods for mapping SAML endpoints
/// </summary>
public static class SamlEndpointExtensions
{
    /// <summary>
    /// Maps SAML endpoints (both SP and IdP if enabled)
    /// </summary>
    public static IEndpointRouteBuilder MapSamlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Map SP endpoints (consumes external SAML IdPs)
        endpoints.MapControllerRoute(
            name: "saml_metadata",
            pattern: "saml/metadata",
            defaults: new { controller = "SamlSp", action = "Metadata" });

        endpoints.MapControllerRoute(
            name: "saml_login",
            pattern: "saml/login/{idpName}",
            defaults: new { controller = "SamlSp", action = "Login" });

        endpoints.MapControllerRoute(
            name: "saml_acs",
            pattern: "saml/acs",
            defaults: new { controller = "SamlSp", action = "AssertionConsumerService" });

        endpoints.MapControllerRoute(
            name: "saml_slo",
            pattern: "saml/slo",
            defaults: new { controller = "SamlSp", action = "SingleLogout" });

        // Map IdP endpoints (issues SAML assertions to external SPs)
        endpoints.MapControllerRoute(
            name: "saml_idp_metadata",
            pattern: "saml/idp/metadata",
            defaults: new { controller = "SamlIdp", action = "Metadata" });

        endpoints.MapControllerRoute(
            name: "saml_idp_sso",
            pattern: "saml/idp/sso",
            defaults: new { controller = "SamlIdp", action = "SingleSignOn" });

        endpoints.MapControllerRoute(
            name: "saml_idp_continue",
            pattern: "saml/idp/continue",
            defaults: new { controller = "SamlIdp", action = "ContinueSso" });

        endpoints.MapControllerRoute(
            name: "saml_idp_slo",
            pattern: "saml/idp/slo",
            defaults: new { controller = "SamlIdp", action = "SingleLogout" });

        return endpoints;
    }

    /// <summary>
    /// Maps only SAML SP endpoints
    /// </summary>
    public static IEndpointRouteBuilder MapSamlSpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute(
            name: "saml_metadata",
            pattern: "saml/metadata",
            defaults: new { controller = "SamlSp", action = "Metadata" });

        endpoints.MapControllerRoute(
            name: "saml_login",
            pattern: "saml/login/{idpName}",
            defaults: new { controller = "SamlSp", action = "Login" });

        endpoints.MapControllerRoute(
            name: "saml_acs",
            pattern: "saml/acs",
            defaults: new { controller = "SamlSp", action = "AssertionConsumerService" });

        endpoints.MapControllerRoute(
            name: "saml_slo",
            pattern: "saml/slo",
            defaults: new { controller = "SamlSp", action = "SingleLogout" });

        return endpoints;
    }

    /// <summary>
    /// Maps only SAML IdP endpoints
    /// </summary>
    public static IEndpointRouteBuilder MapSamlIdpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllerRoute(
            name: "saml_idp_metadata",
            pattern: "saml/idp/metadata",
            defaults: new { controller = "SamlIdp", action = "Metadata" });

        endpoints.MapControllerRoute(
            name: "saml_idp_sso",
            pattern: "saml/idp/sso",
            defaults: new { controller = "SamlIdp", action = "SingleSignOn" });

        endpoints.MapControllerRoute(
            name: "saml_idp_continue",
            pattern: "saml/idp/continue",
            defaults: new { controller = "SamlIdp", action = "ContinueSso" });

        endpoints.MapControllerRoute(
            name: "saml_idp_slo",
            pattern: "saml/idp/slo",
            defaults: new { controller = "SamlIdp", action = "SingleLogout" });

        return endpoints;
    }
}
