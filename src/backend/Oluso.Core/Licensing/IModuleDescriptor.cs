using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Oluso.Core.Licensing;

/// <summary>
/// Contract every module MUST implement to declare itself and register its services.
/// </summary>
public interface IModuleDescriptor
{
    // ── Metadata ──
    string FeatureKey { get; }
    string DisplayName { get; }
    string Description { get; }
    string Version { get; }
    ModuleAuthor Author { get; }
    string Category { get; }
    string? IconName { get; }
    /// <summary>
    /// Optional accent-colour key (from the admin app palette) used when this
    /// module surfaces as a workspace app. Null falls back to a neutral default.
    /// </summary>
    string? Color => null;
    IEnumerable<string> Dependencies { get; }

    /// <summary>
    /// The permission that lets an administrator build automations on this module's triggers and steps.
    /// It sits under the module's own permission prefix, so the module's administrator role holds it
    /// through its wildcard.
    /// </summary>
    string AutomationPermission { get; }

    // ── Registration ──

    /// <summary>
    /// Register all module services: stores, services, permissions, approval, webhooks, claims, etc.
    /// This replaces the old AddXxxModule() method body.
    /// </summary>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Register the module's DbContext for the given provider.
    /// This replaces the old AddXxxForProvider() method body.
    /// </summary>
    void ConfigureDatabase(IServiceCollection services, string provider, string connectionString);

    // ── Tenant Seeding ──

    /// <summary>
    /// Seed tenant-specific data when a new tenant is provisioned.
    /// Called by the TenantSeedEventSink after AdminTenantCreatedEvent.
    /// Override to seed module-specific defaults (leave types, order statuses, pipelines, etc.)
    /// </summary>
    Task SeedTenantAsync(IServiceProvider services, string tenantId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Email templates to seed for new tenants. Default: empty.
    /// Templates are persisted by ITenantSeedPersister (provided by the Workflows module).
    /// </summary>
    IEnumerable<EmailTemplateSeed> GetEmailTemplateSeeds() => [];
}

public record ModuleAuthor(string Name, string? Email = null, string? Url = null);
