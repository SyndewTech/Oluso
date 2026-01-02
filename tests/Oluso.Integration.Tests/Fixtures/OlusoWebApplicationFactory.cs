using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Oluso.EntityFramework;
using Oluso.Enterprise.Ldap.Server;
using Oluso.Enterprise.Scim.EntityFramework;

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

            // Remove existing OlusoDbContext registrations
            RemoveDbContextRegistrations<OlusoDbContext>(services);

            // Remove existing ScimDbContext registrations
            RemoveDbContextRegistrations<ScimDbContext>(services);

            // Add in-memory database for OlusoDbContext
            services.AddDbContext<OlusoDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Add in-memory database for ScimDbContext
            services.AddDbContext<ScimDbContext>(options =>
            {
                options.UseInMemoryDatabase($"{_databaseName}_Scim");
            });

            // Build service provider and ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
            db.Database.EnsureCreated();

            var scimDb = scope.ServiceProvider.GetService<ScimDbContext>();
            scimDb?.Database.EnsureCreated();
        });
    }

    private static void RemoveDbContextRegistrations<TContext>(IServiceCollection services) where TContext : DbContext
    {
        // Remove DbContextOptions
        var optionsDescriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(DbContextOptions<TContext>));
        if (optionsDescriptor != null)
        {
            services.Remove(optionsDescriptor);
        }

        // Remove DbContext registration
        var contextDescriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(TContext));
        if (contextDescriptor != null)
        {
            services.Remove(contextDescriptor);
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
