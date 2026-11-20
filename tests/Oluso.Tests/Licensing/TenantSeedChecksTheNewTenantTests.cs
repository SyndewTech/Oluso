using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.Licensing;

/// <summary>
/// A new tenant is seeded with the modules it has, not those of the tenant whose request created it (often the
/// platform's): a module the creator has but the new tenant doesn't leaves nothing behind, and stays unseeded so
/// switching it on later seeds it.
/// </summary>
public class TenantSeedChecksTheNewTenantTests
{
    private const string Creator = "_platform";
    private const string NewTenant = "new-tenant";

    private sealed class ProbeModule(string key) : IModuleDescriptor
    {
        public List<string> SeededTenants { get; } = new();
        public string FeatureKey => key;
        public string DisplayName => "Probe";
        public string Description => "Probe";
        public string Version => "1.0.0";
        public ModuleAuthor Author => new("Test", "test@example.invalid", "https://example.invalid");
        public string Category => "Test";
        public string? IconName => null;
        public IEnumerable<string> Dependencies => [];
        public string AutomationPermission => "probe.admin";
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
        public void ConfigureDatabase(IServiceCollection services, string provider, string connectionString) { }

        public Task SeedTenantAsync(IServiceProvider services, string tenantId, CancellationToken cancellationToken = default)
        {
            SeededTenants.Add(tenantId);
            return Task.CompletedTask;
        }
    }

    private static async Task<(ProbeModule Module, Mock<ITenantSeedTracker> Tracker)> CreateTenantAsync(bool newTenantHasModule)
    {
        var module = new ProbeModule($"seed-probe-{Guid.NewGuid():N}");
        ModuleRegistry.Instance.Register(module);

        var provider = new Mock<IFeatureAvailabilityProvider>();
        provider.Setup(p => p.IsFeatureAvailableAsync(module.FeatureKey, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? tenantId, string? _, CancellationToken _) => tenantId == Creator || (tenantId == NewTenant && newTenantHasModule));
        var tracker = new Mock<ITenantSeedTracker>();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new TestTenantContext { TenantId = Creator });
        services.AddSingleton(provider.Object);
        services.AddSingleton<ILogger<FeatureGate>>(NullLogger<FeatureGate>.Instance);
        services.AddScoped<IFeatureGate, FeatureGate>();
        services.AddSingleton(tracker.Object);
        using var root = services.BuildServiceProvider();

        var sink = new TenantSeedEventSink(root, NullLogger<TenantSeedEventSink>.Instance);
        await sink.HandleAsync(new AdminTenantCreatedEvent(new Tenant { Id = NewTenant, Name = NewTenant, Identifier = NewTenant }, "admin-1"));
        return (module, tracker);
    }

    [Fact]
    public async Task A_module_only_the_creating_tenant_has_is_not_seeded_into_the_new_tenant()
    {
        var (module, tracker) = await CreateTenantAsync(newTenantHasModule: false);

        module.SeededTenants.Should().BeEmpty();
        tracker.Verify(t => t.MarkModuleSeededAsync(NewTenant, module.FeatureKey, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_module_the_new_tenant_has_is_seeded_into_it()
    {
        var (module, _) = await CreateTenantAsync(newTenantHasModule: true);

        module.SeededTenants.Should().Equal(NewTenant);
    }
}
