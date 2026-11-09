using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Oluso.EntityFramework;
using Oluso.Enterprise.Ldap.Server;
using Oluso.Enterprise.Scim.EntityFramework;
using Oluso.Keys;
using Oluso.Middleware;

namespace Oluso.Integration.Tests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for integration testing.
/// Uses in-memory database and configures services for testing.
/// </summary>
public class OlusoWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"OlusoTestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The signing-credential cache is process-wide (static fields in
        // SigningCredentialStore), so keys auto-provisioned by a prior test
        // factory leak into this one — whose fresh in-memory DB doesn't contain
        // them, yielding IDX10500 / empty JWKS / 401s. Reset it for each factory.
        // Safe because the integration suite runs collections serially
        // (xunit.runner.json: parallelizeTestCollections=false), so no live
        // factory's cache is cleared out from under it.
        SigningCredentialStore.InvalidateCache();

        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove LDAP server hosted service to avoid port conflicts in tests
            var ldapHostedService = services.SingleOrDefault(
                d => d.ImplementationType == typeof(LdapServerHostedService));
            if (ldapHostedService != null)
            {
                services.Remove(ldapHostedService);
            }

            // Replace ALL DbContextOptions<T> registrations with in-memory databases.
            // This covers OlusoDbContextSqlite, ScimDbContextSqlite, all module DbContexts,
            // and any other EF Core contexts registered by the app. The context registrations
            // themselves (including Identity's UserStore<..., OlusoDbContextSqlite>) remain
            // intact — only the backing database is swapped to in-memory.
            ReplaceAllDbContextOptionsWithInMemory(services);

            // HostValidationMiddleware only accepts localhost in Development; tests run
            // under "Testing" so the WebApplicationFactory's default localhost host is
            // rejected with HTTP 421. Allow it explicitly for the test environment.
            services.PostConfigure<HostValidationOptions>(options =>
            {
                options.AdditionalAllowedHosts.Add("localhost");
                options.AdditionalAllowedHosts.Add("127.0.0.1");
            });
        });
    }

    private void ReplaceAllDbContextOptionsWithInMemory(IServiceCollection services)
    {
        // Find all registered DbContextOptions<T> types
        var optionsDescriptors = services
            .Where(d => d.ServiceType.IsGenericType &&
                        d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .ToList();

        foreach (var descriptor in optionsDescriptors)
        {
            services.Remove(descriptor);

            var contextType = descriptor.ServiceType.GetGenericArguments()[0];
            var dbName = $"{_databaseName}_{contextType.Name}";
            var optionsBuilderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);

            // Resolve options through a factory so ApplicationServiceProvider is
            // set to the runtime service provider — DbContexts that read the live
            // ITenantContext via that path (for tenant-aware query filters)
            // would otherwise see a null SP and bypass tenant isolation in tests.
            services.AddSingleton(descriptor.ServiceType, sp =>
            {
                var builder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsBuilderType)!;
                builder.UseInMemoryDatabase(dbName);
                builder.UseApplicationServiceProvider(sp);
                return builder.Options;
            });
        }

        // Also replace non-generic DbContextOptions if registered
        var nonGenericOptions = services.Where(d => d.ServiceType == typeof(DbContextOptions)).ToList();
        foreach (var descriptor in nonGenericOptions)
        {
            services.Remove(descriptor);
        }
    }

    /// <summary>
    /// Creates an authenticated HTTP client for testing admin endpoints.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string? tenantId = "default")
    {
        var client = CreateClient();
        // Add auth header - in a real test you'd get a proper token
        // For now, tests that need auth should use the token endpoint first
        if (tenantId != null)
        {
            client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantId);
        }
        return client;
    }

    /// <summary>
    /// Creates an HTTP client that doesn't follow redirects.
    /// </summary>
    public HttpClient CreateClientWithNoRedirects()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }
}
