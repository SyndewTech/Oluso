using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Authentication;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Integrations;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.EntityFramework.Design;
using Oluso.EntityFramework.Identity;
using Oluso.EntityFramework.Services;
using Oluso.EntityFramework.Stores;

namespace Oluso.EntityFramework;

public static class OlusoEntityFrameworkDependencyInjection
{
    /// <summary>
    /// Registers the provider-specific <see cref="OlusoDbContext"/>, <see cref="IOlusoDbContext"/>,
    /// and <see cref="ITenantProvider"/> for the given database provider.
    /// Use this in Azure Functions or Worker hosts that need access to the Oluso identity database
    /// without pulling in the full Oluso identity server.
    /// </summary>
    public static IServiceCollection AddOlusoEntityFramework(
        this IServiceCollection services,
        string provider,
        string connectionString)
    {
        switch (provider.ToLowerInvariant())
        {
            case "sqlserver" or "mssql":
                services.AddOlusoSqlServer(connectionString);
                break;
            case "postgresql" or "postgres" or "npgsql":
                services.AddOlusoNpgsql(connectionString);
                break;
            default:
                services.AddOlusoSqlite(connectionString);
                break;
        }

        services.AddScoped<ITenantProvider, TenantProvider>();

        return services;
    }

    public static IServiceCollection AddOlusoSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<Interceptors.ReservedAuthInterceptor>();
        services.AddSingleton<Interceptors.TenantMembershipInterceptor>();
        services.AddDbContext<OlusoDbContextSqlite>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(sp.GetRequiredService<Interceptors.ReservedAuthInterceptor>());
            options.AddInterceptors(sp.GetRequiredService<Interceptors.TenantMembershipInterceptor>());
        });

        services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlite>());
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlite>());

        return services;
    }

    public static IServiceCollection AddOlusoSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<Interceptors.ReservedAuthInterceptor>();
        services.AddSingleton<Interceptors.TenantMembershipInterceptor>();
        services.AddDbContext<OlusoDbContextSqlServer>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(sp.GetRequiredService<Interceptors.ReservedAuthInterceptor>());
            options.AddInterceptors(sp.GetRequiredService<Interceptors.TenantMembershipInterceptor>());
        });

        services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlServer>());
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlServer>());

        return services;
    }

    public static IServiceCollection AddOlusoNpgsql(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<Interceptors.ReservedAuthInterceptor>();
        services.AddSingleton<Interceptors.TenantMembershipInterceptor>();
        services.AddDbContext<OlusoDbContextPostgres>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(sp.GetRequiredService<Interceptors.ReservedAuthInterceptor>());
            options.AddInterceptors(sp.GetRequiredService<Interceptors.TenantMembershipInterceptor>());
        });

        services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextPostgres>());
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<OlusoDbContextPostgres>());

        return services;
    }

    /// <summary>
    /// Slim Identity registration for Azure Functions / Worker hosts that need
    /// to look up users / roles / teams (e.g. the Approval module's approver
    /// resolver) without dragging in the full Oluso web host (audit log,
    /// telemetry, webhooks, signing keys, sessions, controllers).
    ///
    /// <para>Wires up <see cref="IRoleStore"/>, <see cref="IOlusoUserService"/>,
    /// <see cref="ITeamStore"/>, <see cref="ITeamService"/>, and the ASP.NET
    /// Identity managers they sit on top of (<c>UserManager</c>,
    /// <c>RoleManager</c>, <c>SignInManager</c>), plus the issuer resolver and
    /// custom-domain DNS verification (<see cref="TenantHostServiceExtensions"/>). <c>ITenantContextAccessor</c>
    /// is registered as a singleton so the dispatcher's per-tenant scope can
    /// pin it before invoking module code.</para>
    ///
    /// <para>Call <see cref="AddOlusoEntityFramework"/> first — this method
    /// assumes the provider-specific <c>OlusoDbContext</c> is already in the
    /// container.</para>
    /// </summary>
    public static IServiceCollection AddOlusoIdentityCoreForFunctions(
        this IServiceCollection services,
        string provider)
    {
        // SignInManager needs IHttpContextAccessor + IAuthenticationSchemeProvider
        // even though our usage path (FindByIdAsync) never reaches HTTP context
        // or auth schemes. Cheaper to register the accessors than to refactor
        // IdentityUserService away from SignInManager.
        services.AddHttpContextAccessor();
        services.AddAuthenticationCore();

        // Issuer resolver and custom-domain DNS verification: module job runners depend on them
        // (bank feed provider URLs, store domain verification).
        services.AddOlusoTenantHostServices();

        // Tenant integration settings (e.g. POS credentials) read by module job runners.
        services.AddIntegrationTenantSettings();

        var identityBuilder = services
            .AddIdentityCore<OlusoUser>()
            .AddRoles<OlusoRole>();

        // IdentityUserService takes the abstract OlusoDbContext; AddDbContext
        // only registers the concrete provider type, so map base→concrete here.
        switch (provider.ToLowerInvariant())
        {
            case "sqlserver" or "mssql":
                identityBuilder.AddEntityFrameworkStores<OlusoDbContextSqlServer>().AddUserStore<OlusoUserStore<OlusoDbContextSqlServer>>();
                services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlServer>());
                break;
            case "postgresql" or "postgres" or "npgsql":
                identityBuilder.AddEntityFrameworkStores<OlusoDbContextPostgres>().AddUserStore<OlusoUserStore<OlusoDbContextPostgres>>();
                services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextPostgres>());
                break;
            default:
                identityBuilder.AddEntityFrameworkStores<OlusoDbContextSqlite>().AddUserStore<OlusoUserStore<OlusoDbContextSqlite>>();
                services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlite>());
                break;
        }

        identityBuilder.AddSignInManager();
        identityBuilder.AddTenantLockout();
        services.AddScoped<IPasswordHistoryStore, PasswordHistoryStore>();

        services.AddScoped<IRoleStore, RoleStore>();
        services.AddScoped<IEffectiveRoleResolver, EffectiveRoleResolver>();
        services.AddScoped<IModuleAdminAssignmentNotifier, ModuleAdminAssignmentNotifier>();
        services.AddScoped<IRoleGrantService, RoleGrantService>();
        services.AddScoped<ITenantDefaultRoleSeeder, TenantDefaultRoleSeeder>();
        services.AddScoped<IOlusoUserService, IdentityUserService>();
        services.AddScoped<ITeamStore, TeamStore>();
        services.AddScoped<ITeamService, TeamService>();

        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<ITenantContextAccessor>());

        // Tenant store + tenant configuration chain. Function-host services
        // (TenantJobDispatcher, AiClient → ITenantSettingsProvider) need these,
        // and they don't ship with AddOlusoEntityFramework because that
        // method is intentionally minimal (just DbContext + ITenantProvider).
        services.AddScoped<ITenantStore, TenantStore>();
        services.AddScoped<IOrganizationFeatureStore, OrganizationFeatureStore>();
        services.AddScoped<ITenantFeatureStore, TenantFeatureStore>();
        services.AddScoped<Oluso.Core.Terminology.ITerminologyOverrideStore, TerminologyOverrideStore>();
        services.AddScoped<ITenantConfigurationSectionStore, TenantConfigurationSectionStore>();
        services.AddScoped<ISenderProfileStore, SenderProfileStore>();
        services.AddScoped<IWhatsAppContactStore, WhatsAppContactStore>();
        services.AddScoped<IWhatsAppTemplateStore, WhatsAppTemplateStore>();
        services.AddScoped<IWhatsAppInboundMessageStore, WhatsAppInboundMessageStore>();
        services.AddScoped<IWhatsAppAccountStore, WhatsAppAccountStore>();
        services.AddScoped<IEntitlementStore, EntitlementStore>();
        services.AddOptions<TenantConfigurationOptions>();
        services.AddScoped<ITenantConfigurationService, TenantConfigurationService>();
        services.AddScoped<ITenantSettingsProvider, DefaultTenantSettingsProvider>();

        // Feature gate + the two default providers. Functions now apply the
        // same tenant-feature gating as the web host instead of fail-open.
        services.AddFeatureGate();

        // TenantConfigurationService needs IDataProtectionProvider for encrypted
        // sections. On its own this is an ephemeral key ring that can't read what
        // the platform wrote; hosts that decrypt share the platform's ring
        // (Oluso.Enterprise.AzureDataProtection, read-only).
        services.AddDataProtection();

        return services;
    }
}
