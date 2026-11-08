using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Authorization;
using Oluso.Core.Data;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Verification.EntityFramework;
using Oluso.Enterprise.Verification.Journey;
using Oluso.Enterprise.Verification.Providers;
using Oluso.Enterprise.Verification.Services;
using Oluso.Enterprise.Verification.Stores;

namespace Oluso.Enterprise.Verification;

/// <summary>Permissions the module registers.</summary>
public static class VerificationPermissions
{
    public const string Category = "Business Verification";
    public const string Read = "business_verification.read";
    public const string Review = "business_verification.review";
    public const string Manage = "business_verification.manage";
}

public static class VerificationExtensions
{
    /// <summary>How long one vendor call may take before the attempt counts as provider unavailable.</summary>
    public static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Business (KYB) verification: the business_verification journey step, the Dojah and
    /// Youverify providers, the organisation's encrypted provider settings
    /// ("IdentityVerification" section), the verification service, provider webhooks
    /// (<c>/api/verification/webhooks/{provider}</c> on the organisation's host), admin and
    /// account APIs, and the business.verification_completed event. Needs the user
    /// journey engine and EF stores; add the module's database with
    /// <see cref="AddOlusoVerificationForProvider"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddUserJourneyEngine()
    ///     .AddEntityFrameworkStoresForProvider(provider, connectionString)
    ///     .AddOlusoVerification();
    /// builder.Services.AddOlusoVerificationForProvider(provider, connectionString);
    /// </code>
    /// </example>
    public static OlusoBuilder AddOlusoVerification(this OlusoBuilder builder)
    {
        var services = builder.Services;

        PermissionRegistry.Instance.RegisterPermissions(new[]
        {
            new PermissionDefinition(VerificationPermissions.Read, "View business verifications", VerificationPermissions.Category,
                "View verified businesses, the people acting for them, their documents and the provider checks behind them"),
            new PermissionDefinition(VerificationPermissions.Review, "Review business verifications", VerificationPermissions.Category,
                "Approve or reject businesses and applicants waiting on a decision"),
            new PermissionDefinition(VerificationPermissions.Manage, "Manage business verification providers", VerificationPermissions.Category,
                "Configure verification providers, their credentials, country routing and result reuse"),
        });

        services.Configure<TenantConfigurationOptions>(o => o.EncryptedSections.Add(IdentityVerificationSettings.SectionKey));
        services.AddHttpContextAccessor();

        services.AddHttpClient<DojahVerificationProvider>(client => client.Timeout = ProviderTimeout);
        services.AddHttpClient<YouverifyVerificationProvider>(client => client.Timeout = ProviderTimeout);
        services.TryAddEnumerable(ServiceDescriptor.Transient<IVerificationProvider, DojahVerificationProvider>(sp => sp.GetRequiredService<DojahVerificationProvider>()));
        services.TryAddEnumerable(ServiceDescriptor.Transient<IVerificationProvider, YouverifyVerificationProvider>(sp => sp.GetRequiredService<YouverifyVerificationProvider>()));

        services.TryAddScoped<IVerificationProviderRegistry, VerificationProviderRegistry>();
        services.TryAddScoped<IIdentityVerificationSettingsService, IdentityVerificationSettingsService>();
        services.TryAddScoped<IBusinessVerificationRouter, BusinessVerificationRouter>();
        services.TryAddScoped<IBusinessVerificationService, BusinessVerificationService>();
        services.TryAddScoped<BusinessVerificationNotifier>();
        services.AddScoped<IJourneyUserAttachedHandler, BusinessApplicantJourneyLinker>();
        services.AddScoped<IFileAccessProvider, BusinessApplicantFileAccessProvider>();

        services.TryAddScoped<BusinessVerificationStepHandler>();
        services.AddSingleton<IConfigureStepHandlers, BusinessVerificationStepConfiguration>();
        services.AddJourneyStepFormDescribers(typeof(BusinessVerificationStepHandler).Assembly);

        services.AddEventPropertySource<EnterpriseVerificationEventPropertySource>();

        services.AddOptions<VerificationDatabaseRegistration>()
            .Validate<IServiceProviderIsService>(
                (_, registered) => registered.IsService(typeof(IBusinessVerificationStore)),
                $"Business verification is enabled ({nameof(AddOlusoVerification)}) but its database isn't registered: "
                + $"call services.{nameof(AddOlusoVerificationForProvider)}(provider, connectionString).")
            .ValidateOnStart();
        return builder;
    }

    /// <summary>Validated when the host starts: the module's services need its database.</summary>
    private sealed class VerificationDatabaseRegistration;

    /// <summary>
    /// The module's database: its own context and migrations history table
    /// (<c>__EFMigrationsHistory_Verification</c>), on the host's connection by default.
    /// Pending migrations apply at startup with the other contexts.
    /// </summary>
    public static IServiceCollection AddOlusoVerificationForProvider(this IServiceCollection services, string provider, string connectionString) =>
        provider.ToLowerInvariant() switch
        {
            "sqlserver" or "mssql" => services.AddVerificationContext<VerificationDbContextSqlServer>(o =>
                o.UseSqlServer(connectionString, x => x.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(VerificationDbContext.PluginIdentifier)))),
            "postgresql" or "postgres" or "npgsql" => services.AddVerificationContext<VerificationDbContextPostgres>(o =>
                o.UseNpgsql(connectionString, x => x.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(VerificationDbContext.PluginIdentifier)))),
            "sqlite" => services.AddVerificationContext<VerificationDbContextSqlite>(o =>
                o.UseSqlite(connectionString, x => x.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(VerificationDbContext.PluginIdentifier)))),
            _ => throw new ArgumentException($"Unsupported database provider '{provider}'. Use SqlServer, PostgreSQL or Sqlite.", nameof(provider)),
        };

    private static IServiceCollection AddVerificationContext<TContext>(this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
        where TContext : VerificationDbContext
    {
        services.AddDbContext<TContext>(configure);
        services.AddScoped<VerificationDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IBusinessVerificationStore, BusinessVerificationStore>();
        return services;
    }
}
