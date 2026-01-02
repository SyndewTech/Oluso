using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Data;
using Oluso.Enterprise.Ldap.Authentication;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.Connection;
using Oluso.Enterprise.Ldap.Entities;
using Oluso.Enterprise.Ldap.EntityFramework;
using Oluso.Enterprise.Ldap.GroupMapping;
using Oluso.Enterprise.Ldap.Integration;
using Oluso.Enterprise.Ldap.Server;
using Oluso.Enterprise.Ldap.Stores;
using Oluso.Enterprise.Ldap.UserSync;

namespace Oluso.Enterprise.Ldap;

/// <summary>
/// Extension methods for adding LDAP/Active Directory support to Oluso
/// </summary>
public static class LdapExtensions
{
    /// <summary>
    /// Adds LDAP/Active Directory authentication support.
    /// This is a licensed enterprise add-on feature.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddMultiTenancy()
    ///     .AddUserJourneyEngine()
    ///     .AddLdap();
    /// </code>
    /// </example>
    public static OlusoBuilder AddLdap(this OlusoBuilder builder)
    {
        var section = builder.Configuration.GetSection(LdapOptions.SectionName);
        builder.Services.Configure<LdapOptions>(section);

        return builder.AddLdapInternal();
    }

    /// <summary>
    /// Adds LDAP/Active Directory authentication with custom configuration.
    /// This is a licensed enterprise add-on feature.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddLdap(options =>
    ///     {
    ///         options.Server = "ldap.example.com";
    ///         options.Port = 389;
    ///         options.BaseDn = "dc=example,dc=com";
    ///     });
    /// </code>
    /// </example>
    public static OlusoBuilder AddLdap(
        this OlusoBuilder builder,
        Action<LdapOptions> configure)
    {
        var options = new LdapOptions();
        configure(options);
        builder.Services.Configure<LdapOptions>(opt =>
        {
            opt.Server = options.Server;
            opt.Port = options.Port;
            opt.UseSsl = options.UseSsl;
            opt.UseStartTls = options.UseStartTls;
            opt.BaseDn = options.BaseDn;
            opt.BindDn = options.BindDn;
            opt.BindPassword = options.BindPassword;
            opt.UserSearch = options.UserSearch;
            opt.GroupSearch = options.GroupSearch;
            opt.AttributeMappings = options.AttributeMappings;
            opt.ConnectionTimeoutSeconds = options.ConnectionTimeoutSeconds;
            opt.SearchTimeoutSeconds = options.SearchTimeoutSeconds;
            opt.MaxPoolSize = options.MaxPoolSize;
        });

        return builder.AddLdapInternal();
    }

    /// <summary>
    /// Adds LDAP/Active Directory authentication with fluent builder.
    /// This is a licensed enterprise add-on feature.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddLdap(ldap => ldap
    ///         .WithServer("ldap.example.com", 389)
    ///         .WithBaseDn("dc=example,dc=com")
    ///         .WithServiceAccount("cn=admin,dc=example,dc=com", "password")
    ///         .WithSsl()
    ///         .MapGroupToRole("CN=Admins,OU=Groups,DC=example,DC=com", "Admin"));
    /// </code>
    /// </example>
    public static OlusoBuilder AddLdap(
        this OlusoBuilder builder,
        Action<LdapBuilder> configure)
    {
        var ldapBuilder = new LdapBuilder(builder.Services);
        configure(ldapBuilder);
        ldapBuilder.Build();

        return builder.AddLdapInternal();
    }

    private static OlusoBuilder AddLdapInternal(this OlusoBuilder builder)
    {
        // Register LDAP services
        builder.Services.AddSingleton<ILdapConnectionPool, LdapConnectionPool>();
        builder.Services.AddScoped<ILdapAuthenticator, LdapAuthenticator>();
        builder.Services.AddScoped<ILdapGroupMapper, LdapGroupMapper>();
        builder.Services.AddScoped<ILdapUserSync, LdapUserSync>();

        // Register step handler for User Journey Engine
        builder.Services.AddScoped<LdapStepHandler>();

        return builder;
    }

    /// <summary>
    /// Adds LDAP Server functionality to expose Oluso users via LDAP protocol.
    /// This allows legacy applications to authenticate against Oluso using LDAP.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddLdap()
    ///     .AddLdapServer(server => server
    ///         .WithPort(389)
    ///         .WithBaseDn("dc=oluso,dc=local")
    ///         .WithAdmin("cn=admin,dc=oluso,dc=local", "password"));
    /// </code>
    /// </example>
    public static OlusoBuilder AddLdapServer(
        this OlusoBuilder builder,
        Action<LdapServerBuilder> configure)
    {
        var serverBuilder = new LdapServerBuilder(builder.Services);
        configure(serverBuilder);
        serverBuilder.Build();

        builder.Services.AddSingleton<ILdapServer, LdapServer>();
        builder.Services.AddHostedService<LdapServerHostedService>();

        // Note: ILdapServiceAccountStore is registered by AddLdapDbContext/AddLdapSqlite
        // which should be called alongside AddLdapServer

        return builder;
    }

    /// <summary>
    /// Add LDAP DbContext using the same connection as the host application.
    /// Uses a separate migrations history table.
    /// </summary>
    /// <typeparam name="THostContext">The host application's DbContext type</typeparam>
    /// <param name="services">Service collection</param>
    public static IServiceCollection AddLdapDbContext<THostContext>(this IServiceCollection services)
        where THostContext : DbContext
    {
        var migrationsTable = PluginDbContextExtensions.GetMigrationsTableName(LdapDbContext.PluginIdentifier);

        services.AddDbContext<LdapDbContext>((serviceProvider, options) =>
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
                var pgTable = PluginDbContextExtensions.GetMigrationsTableNamePostgres(LdapDbContext.PluginIdentifier);
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
                // Fallback - try to use the same options builder pattern
                throw new InvalidOperationException(
                    $"Unsupported database provider: {providerName}. " +
                    "Use AddLdapDbContext with explicit configuration instead.");
            }
        });

        // Register for automatic migration discovery
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<LdapDbContext>());

        // Register service account store and password hasher for REST API management
        services.AddScoped<ILdapServiceAccountStore, LdapServiceAccountStore>();
        services.AddSingleton<IPasswordHasher<LdapServiceAccount>, PasswordHasher<LdapServiceAccount>>();

        return services;
    }

    /// <summary>
    /// Add LDAP DbContext with a custom connection string (separate database).
    /// Uses a separate migrations history table.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureDb">DbContext options configuration</param>
    public static IServiceCollection AddLdapDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<LdapDbContext>(configureDb);

        // Register for automatic migration discovery
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<LdapDbContext>());

        // Register service account store and password hasher for REST API management
        services.AddScoped<ILdapServiceAccountStore, LdapServiceAccountStore>();
        services.AddSingleton<IPasswordHasher<LdapServiceAccount>, PasswordHasher<LdapServiceAccount>>();

        return services;
    }

    /// <summary>
    /// Add LDAP DbContext with SQLite.
    /// </summary>
    public static IServiceCollection AddLdapSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddLdapDbContext(options =>
            options.UseSqlite(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(LdapDbContext.PluginIdentifier))));
    }

    /// <summary>
    /// Add LDAP DbContext with SQL Server.
    /// </summary>
    public static IServiceCollection AddLdapSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddLdapDbContext(options =>
            options.UseSqlServer(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(LdapDbContext.PluginIdentifier))));
    }

    /// <summary>
    /// Add LDAP DbContext with PostgreSQL.
    /// </summary>
    public static IServiceCollection AddLdapNpgsql(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddLdapDbContext(options =>
            options.UseNpgsql(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(LdapDbContext.PluginIdentifier))));
    }
}

/// <summary>
/// Fluent builder for LDAP configuration
/// </summary>
public class LdapBuilder
{
    private readonly IServiceCollection _services;
    private readonly LdapOptions _options = new();
    private readonly Dictionary<string, string> _groupMappings = new();

    public LdapBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configure LDAP server connection
    /// </summary>
    public LdapBuilder WithServer(string server, int port = 389)
    {
        _options.Server = server;
        _options.Port = port;
        return this;
    }

    /// <summary>
    /// Enable SSL/TLS (LDAPS)
    /// </summary>
    public LdapBuilder WithSsl(bool useSsl = true)
    {
        _options.UseSsl = useSsl;
        if (useSsl && _options.Port == 389)
        {
            _options.Port = 636;
        }
        return this;
    }

    /// <summary>
    /// Enable StartTLS
    /// </summary>
    public LdapBuilder WithStartTls(bool useStartTls = true)
    {
        _options.UseStartTls = useStartTls;
        return this;
    }

    /// <summary>
    /// Configure base DN for searches
    /// </summary>
    public LdapBuilder WithBaseDn(string baseDn)
    {
        _options.BaseDn = baseDn;
        return this;
    }

    /// <summary>
    /// Configure service account for binding
    /// </summary>
    public LdapBuilder WithServiceAccount(string bindDn, string password)
    {
        _options.BindDn = bindDn;
        _options.BindPassword = password;
        return this;
    }

    /// <summary>
    /// Configure user search settings
    /// </summary>
    public LdapBuilder WithUserSearch(Action<LdapUserSearchOptions> configure)
    {
        configure(_options.UserSearch);
        return this;
    }

    /// <summary>
    /// Configure group search settings
    /// </summary>
    public LdapBuilder WithGroupSearch(Action<LdapGroupSearchOptions> configure)
    {
        configure(_options.GroupSearch);
        return this;
    }

    /// <summary>
    /// Configure attribute mappings
    /// </summary>
    public LdapBuilder WithAttributeMapping(Action<LdapAttributeMappings> configure)
    {
        configure(_options.AttributeMappings);
        return this;
    }

    /// <summary>
    /// Map LDAP group to application role
    /// </summary>
    public LdapBuilder MapGroupToRole(string ldapGroup, string role)
    {
        _groupMappings[ldapGroup] = role;
        return this;
    }

    /// <summary>
    /// Configure connection pool size
    /// </summary>
    public LdapBuilder WithPoolSize(int maxConnections)
    {
        _options.MaxPoolSize = maxConnections;
        return this;
    }

    /// <summary>
    /// Configure timeouts
    /// </summary>
    public LdapBuilder WithTimeout(int connectionSeconds, int searchSeconds)
    {
        _options.ConnectionTimeoutSeconds = connectionSeconds;
        _options.SearchTimeoutSeconds = searchSeconds;
        return this;
    }

    internal IServiceCollection Build()
    {
        _services.Configure<LdapOptions>(opt =>
        {
            opt.Server = _options.Server;
            opt.Port = _options.Port;
            opt.UseSsl = _options.UseSsl;
            opt.UseStartTls = _options.UseStartTls;
            opt.BaseDn = _options.BaseDn;
            opt.BindDn = _options.BindDn;
            opt.BindPassword = _options.BindPassword;
            opt.UserSearch = _options.UserSearch;
            opt.GroupSearch = _options.GroupSearch;
            opt.AttributeMappings = _options.AttributeMappings;
            opt.ConnectionTimeoutSeconds = _options.ConnectionTimeoutSeconds;
            opt.SearchTimeoutSeconds = _options.SearchTimeoutSeconds;
            opt.MaxPoolSize = _options.MaxPoolSize;
        });

        return _services;
    }
}

/// <summary>
/// Fluent builder for LDAP Server configuration
/// </summary>
public class LdapServerBuilder
{
    private readonly IServiceCollection _services;
    private readonly LdapServerOptions _options = new();

    public LdapServerBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public LdapServerBuilder Enable(bool enabled = true)
    {
        _options.Enabled = enabled;
        return this;
    }

    public LdapServerBuilder WithPort(int port)
    {
        _options.Port = port;
        return this;
    }

    public LdapServerBuilder WithSsl(int sslPort = 636, string certPath = "", string certPassword = "")
    {
        _options.EnableSsl = true;
        _options.SslPort = sslPort;
        _options.SslCertificatePath = certPath;
        _options.SslCertificatePassword = certPassword;
        return this;
    }

    public LdapServerBuilder WithBaseDn(string baseDn)
    {
        _options.BaseDn = baseDn;
        return this;
    }

    public LdapServerBuilder WithOrganization(string organization)
    {
        _options.Organization = organization;
        return this;
    }

    public LdapServerBuilder WithAdmin(string adminDn, string password)
    {
        _options.AdminDn = adminDn;
        _options.AdminPassword = password;
        return this;
    }

    public LdapServerBuilder AllowAnonymousBind(bool allow = true)
    {
        _options.AllowAnonymousBind = allow;
        return this;
    }

    public LdapServerBuilder WithTenantIsolation(bool enabled = true)
    {
        _options.TenantIsolation = enabled;
        return this;
    }

    public LdapServerBuilder WithMaxConnections(int max)
    {
        _options.MaxConnections = max;
        return this;
    }

    public LdapServerBuilder WithMaxSearchResults(int max)
    {
        _options.MaxSearchResults = max;
        return this;
    }

    internal IServiceCollection Build()
    {
        _services.Configure<LdapServerOptions>(opt =>
        {
            opt.Enabled = _options.Enabled;
            opt.Port = _options.Port;
            opt.SslPort = _options.SslPort;
            opt.EnableSsl = _options.EnableSsl;
            opt.EnableStartTls = _options.EnableStartTls;
            opt.SslCertificatePath = _options.SslCertificatePath;
            opt.SslCertificatePassword = _options.SslCertificatePassword;
            opt.BaseDn = _options.BaseDn;
            opt.Organization = _options.Organization;
            opt.UserOu = _options.UserOu;
            opt.GroupOu = _options.GroupOu;
            opt.TenantOu = _options.TenantOu;
            opt.MaxConnections = _options.MaxConnections;
            opt.ConnectionTimeoutSeconds = _options.ConnectionTimeoutSeconds;
            opt.MaxSearchResults = _options.MaxSearchResults;
            opt.AllowAnonymousBind = _options.AllowAnonymousBind;
            opt.AdminDn = _options.AdminDn;
            opt.AdminPassword = _options.AdminPassword;
            opt.TenantIsolation = _options.TenantIsolation;
        });

        return _services;
    }
}
