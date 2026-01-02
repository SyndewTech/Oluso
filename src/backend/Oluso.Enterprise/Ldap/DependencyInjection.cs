using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Ldap.Authentication;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.Connection;
using Oluso.Enterprise.Ldap.GroupMapping;
using Oluso.Enterprise.Ldap.Integration;
using Oluso.Enterprise.Ldap.Server;
using Oluso.Enterprise.Ldap.UserSync;

namespace Oluso.Enterprise.Ldap;

public static class DependencyInjection
{
    /// <summary>
    /// Adds LDAP services to the service collection.
    /// This is a licensed add-on feature.
    /// </summary>
    public static IServiceCollection AddLdap(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddLdap(configuration, _ => { });
    }

    /// <summary>
    /// Adds LDAP services with custom configuration.
    /// This is a licensed add-on feature.
    /// </summary>
    public static IServiceCollection AddLdap(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<LdapOptions> configure)
    {
        // Validate license
        ValidateLdapLicense(services);

        // Bind configuration
        services.Configure<LdapOptions>(configuration.GetSection(LdapOptions.SectionName));
        services.PostConfigure(configure);

        // Register core services
        services.AddSingleton<ILdapConnectionPool, LdapConnectionPool>();
        services.AddScoped<ILdapAuthenticator, LdapAuthenticator>();
        services.AddScoped<ILdapGroupMapper, LdapGroupMapper>();
        services.AddScoped<ILdapUserSync, LdapUserSync>();

        // Register direct identity provider for unified login page
        services.AddScoped<IDirectIdentityProvider, LdapExternalIdentityProvider>();

        // Register journey step handler and its type configuration
        services.AddScoped<LdapStepHandler>();
        services.AddSingleton<IConfigureStepHandlers, LdapStepHandlerConfiguration>();

        return services;
    }

    private static void ValidateLdapLicense(IServiceCollection services)
    {
        // Build a temporary service provider to check license
        var tempProvider = services.BuildServiceProvider();
        var licenseValidator = tempProvider.GetService<ILicenseValidator>();

        if (licenseValidator != null)
        {
            var validation = licenseValidator.ValidateFeature(LicensedFeatures.Ldap);
            if (!validation.IsValid)
            {
                throw new LicenseException(
                    $"LDAP add-on requires a valid license. {validation.Message}",
                    LicensedFeatures.Ldap);
            }
        }
        // If no license validator is registered, allow (development mode)
    }

    /// <summary>
    /// Adds LDAP services with options builder pattern
    /// </summary>
    public static IServiceCollection AddLdap(
        this IServiceCollection services,
        Action<LdapOptionsBuilder> builder)
    {
        var optionsBuilder = new LdapOptionsBuilder();
        builder(optionsBuilder);

        services.Configure<LdapOptions>(options =>
        {
            optionsBuilder.Apply(options);
        });

        // Register core services
        services.AddSingleton<ILdapConnectionPool, LdapConnectionPool>();
        services.AddScoped<ILdapAuthenticator, LdapAuthenticator>();
        services.AddScoped<ILdapGroupMapper, LdapGroupMapper>();
        services.AddScoped<ILdapUserSync, LdapUserSync>();
        services.AddScoped<IDirectIdentityProvider, LdapExternalIdentityProvider>();

        // Register journey step handler
        services.AddScoped<LdapStepHandler>();

        return services;
    }

    /// <summary>
    /// Adds LDAP Server functionality to expose Identity users via LDAP protocol
    /// </summary>
    public static IServiceCollection AddLdapServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddLdapServer(configuration, _ => { });
    }

    /// <summary>
    /// Adds LDAP Server with custom configuration
    /// </summary>
    public static IServiceCollection AddLdapServer(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<LdapServerOptions> configure)
    {
        services.Configure<LdapServerOptions>(configuration.GetSection(LdapServerOptions.SectionName));
        services.PostConfigure(configure);

        services.AddSingleton<ILdapServer, LdapServer>();
        services.AddHostedService<LdapServerHostedService>();

        // Register LDAP tenant settings service for per-tenant LDAP configuration
        services.AddScoped<ILdapTenantSettingsService, LdapTenantSettingsService>();

        return services;
    }

    /// <summary>
    /// Adds LDAP Server with options builder pattern
    /// </summary>
    public static IServiceCollection AddLdapServer(
        this IServiceCollection services,
        Action<LdapServerOptionsBuilder> builder)
    {
        var optionsBuilder = new LdapServerOptionsBuilder();
        builder(optionsBuilder);

        services.Configure<LdapServerOptions>(options =>
        {
            optionsBuilder.Apply(options);
        });

        services.AddSingleton<ILdapServer, LdapServer>();
        services.AddHostedService<LdapServerHostedService>();

        // Register LDAP tenant settings service for per-tenant LDAP configuration
        services.AddScoped<ILdapTenantSettingsService, LdapTenantSettingsService>();

        return services;
    }
}

/// <summary>
/// Fluent builder for LDAP Server options
/// </summary>
public class LdapServerOptionsBuilder
{
    private readonly LdapServerOptions _options = new();

    public LdapServerOptionsBuilder Enable(bool enabled = true)
    {
        _options.Enabled = enabled;
        return this;
    }

    public LdapServerOptionsBuilder WithPort(int port)
    {
        _options.Port = port;
        return this;
    }

    public LdapServerOptionsBuilder WithSsl(int sslPort = 636, string certPath = "", string certPassword = "")
    {
        _options.EnableSsl = true;
        _options.SslPort = sslPort;
        _options.SslCertificatePath = certPath;
        _options.SslCertificatePassword = certPassword;
        return this;
    }

    public LdapServerOptionsBuilder WithBaseDn(string baseDn)
    {
        _options.BaseDn = baseDn;
        return this;
    }

    public LdapServerOptionsBuilder WithOrganization(string organization)
    {
        _options.Organization = organization;
        return this;
    }

    public LdapServerOptionsBuilder WithAdmin(string adminDn, string password)
    {
        _options.AdminDn = adminDn;
        _options.AdminPassword = password;
        return this;
    }

    public LdapServerOptionsBuilder AllowAnonymousBind(bool allow = true)
    {
        _options.AllowAnonymousBind = allow;
        return this;
    }

    public LdapServerOptionsBuilder WithTenantIsolation(bool enabled = true)
    {
        _options.TenantIsolation = enabled;
        return this;
    }

    public LdapServerOptionsBuilder WithMaxConnections(int max)
    {
        _options.MaxConnections = max;
        return this;
    }

    public LdapServerOptionsBuilder WithMaxSearchResults(int max)
    {
        _options.MaxSearchResults = max;
        return this;
    }

    internal void Apply(LdapServerOptions options)
    {
        options.Enabled = _options.Enabled;
        options.Port = _options.Port;
        options.SslPort = _options.SslPort;
        options.EnableSsl = _options.EnableSsl;
        options.EnableStartTls = _options.EnableStartTls;
        options.SslCertificatePath = _options.SslCertificatePath;
        options.SslCertificatePassword = _options.SslCertificatePassword;
        options.BaseDn = _options.BaseDn;
        options.Organization = _options.Organization;
        options.UserOu = _options.UserOu;
        options.GroupOu = _options.GroupOu;
        options.TenantOu = _options.TenantOu;
        options.MaxConnections = _options.MaxConnections;
        options.ConnectionTimeoutSeconds = _options.ConnectionTimeoutSeconds;
        options.MaxSearchResults = _options.MaxSearchResults;
        options.AllowAnonymousBind = _options.AllowAnonymousBind;
        options.AdminDn = _options.AdminDn;
        options.AdminPassword = _options.AdminPassword;
        options.TenantIsolation = _options.TenantIsolation;
    }
}

/// <summary>
/// Fluent builder for LDAP options
/// </summary>
public class LdapOptionsBuilder
{
    private readonly LdapOptions _options = new();
    private readonly Dictionary<string, string> _groupMappings = new();

    public LdapOptionsBuilder WithServer(string server, int port = 389)
    {
        _options.Server = server;
        _options.Port = port;
        return this;
    }

    public LdapOptionsBuilder WithSsl(bool useSsl = true)
    {
        _options.UseSsl = useSsl;
        if (useSsl && _options.Port == 389)
        {
            _options.Port = 636;
        }
        return this;
    }

    public LdapOptionsBuilder WithStartTls(bool useStartTls = true)
    {
        _options.UseStartTls = useStartTls;
        return this;
    }

    public LdapOptionsBuilder WithBaseDn(string baseDn)
    {
        _options.BaseDn = baseDn;
        return this;
    }

    public LdapOptionsBuilder WithServiceAccount(string bindDn, string password)
    {
        _options.BindDn = bindDn;
        _options.BindPassword = password;
        return this;
    }

    public LdapOptionsBuilder WithUserSearch(Action<LdapUserSearchOptions> configure)
    {
        configure(_options.UserSearch);
        return this;
    }

    public LdapOptionsBuilder WithGroupSearch(Action<LdapGroupSearchOptions> configure)
    {
        configure(_options.GroupSearch);
        return this;
    }

    public LdapOptionsBuilder WithAttributeMapping(Action<LdapAttributeMappings> configure)
    {
        configure(_options.AttributeMappings);
        return this;
    }

    public LdapOptionsBuilder MapGroupToRole(string ldapGroup, string role)
    {
        _groupMappings[ldapGroup] = role;
        return this;
    }

    public LdapOptionsBuilder WithPoolSize(int maxConnections)
    {
        _options.MaxPoolSize = maxConnections;
        return this;
    }

    public LdapOptionsBuilder WithTimeout(int connectionSeconds, int searchSeconds)
    {
        _options.ConnectionTimeoutSeconds = connectionSeconds;
        _options.SearchTimeoutSeconds = searchSeconds;
        return this;
    }

    internal void Apply(LdapOptions options)
    {
        options.Server = _options.Server;
        options.Port = _options.Port;
        options.UseSsl = _options.UseSsl;
        options.UseStartTls = _options.UseStartTls;
        options.BaseDn = _options.BaseDn;
        options.BindDn = _options.BindDn;
        options.BindPassword = _options.BindPassword;
        options.ConnectionTimeoutSeconds = _options.ConnectionTimeoutSeconds;
        options.SearchTimeoutSeconds = _options.SearchTimeoutSeconds;
        options.MaxPoolSize = _options.MaxPoolSize;
        options.UserSearch = _options.UserSearch;
        options.GroupSearch = _options.GroupSearch;
        options.AttributeMappings = _options.AttributeMappings;
    }

    internal Dictionary<string, string> GetGroupMappings() => _groupMappings;
}

/// <summary>
/// Registers LDAP step handler with the type registry
/// </summary>
public class LdapStepHandlerConfiguration : IConfigureStepHandlers
{
    public void Configure(IExtendedStepHandlerRegistry registry)
    {
        registry.Register<LdapStepHandler>("ldap", builder =>
        {
            builder
                .WithDescription("Authenticate users against an LDAP/Active Directory server")
                .InCategory("Authentication")
                .FromModule("Oluso.Enterprise.Ldap")
                .RequiresFeature("LdapServer:enabled");
        });
    }
}

/// <summary>
/// Configuration schema for LDAP step (for AdminUI)
/// Uses x-enumSource for dynamic LDAP provider selection
/// </summary>
public static class LdapStepConfigSchema
{
    public const string JsonSchema = """
    {
      "$schema": "http://json-schema.org/draft-07/schema#",
      "type": "object",
      "x-optionsEndpoint": "/api/IdentityProviders/options",
      "properties": {
        "ldapProvider": {
          "type": "string",
          "title": "LDAP Provider",
          "description": "Select a configured LDAP provider to use for authentication",
          "x-control": "select",
          "x-enumSource": {
            "endpoint": "/api/IdentityProviders/options",
            "path": "ldapProviders",
            "valueField": "value",
            "labelField": "label"
          }
        },
        "server": {
          "type": "string",
          "title": "LDAP Server Override",
          "description": "Override LDAP server hostname (leave empty to use provider settings)"
        },
        "baseDn": {
          "type": "string",
          "title": "Base DN Override",
          "description": "Override base distinguished name (leave empty to use provider settings)"
        },
        "userSearchFilter": {
          "type": "string",
          "title": "User Search Filter",
          "description": "LDAP filter for finding users (e.g., (sAMAccountName={0}))",
          "default": "(sAMAccountName={0})"
        },
        "bindDn": {
          "type": "string",
          "title": "Bind DN",
          "description": "Service account DN for LDAP binding",
          "x-control": "secret-input"
        },
        "useSsl": {
          "type": "boolean",
          "title": "Use SSL/TLS",
          "description": "Connect using SSL/TLS (LDAPS)",
          "default": false
        },
        "allowPasswordChange": {
          "type": "boolean",
          "title": "Allow Password Change",
          "description": "Allow users to change their LDAP password",
          "default": false
        },
        "syncGroups": {
          "type": "boolean",
          "title": "Sync Groups",
          "description": "Synchronize LDAP groups as claims",
          "default": true
        }
      }
    }
    """;
}
