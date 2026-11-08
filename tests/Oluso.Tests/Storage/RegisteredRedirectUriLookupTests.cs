using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.EntityFramework.Stores;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.Storage;

/// <summary>An exact registered redirect URI of an enabled client in the tenant, by one indexed lookup.</summary>
public sealed class RegisteredRedirectUriLookupTests : IDisposable
{
    private readonly SqliteConnection _connection = IdentityTestFactory.OpenSqliteConnection();

    private static ITenantContext Tenant(string id)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(id);
        tenant.Setup(t => t.HasTenant).Returns(true);
        return tenant.Object;
    }

    private async Task SeedAsync(string tenantId, string clientId, string redirectUri, bool enabled = true)
    {
        using var context = IdentityTestFactory.CreateSqliteContext(_connection, Tenant(tenantId));
        context.Clients.Add(new Client
        {
            ClientId = clientId,
            TenantId = tenantId,
            Enabled = enabled,
            RedirectUris = new List<ClientRedirectUri> { new() { RedirectUri = redirectUri, TenantId = tenantId } }
        });
        await context.SaveChangesAsync();
    }

    private ClientStore Store(string tenantId) =>
        new(IdentityTestFactory.CreateSqliteContext(_connection, Tenant(tenantId)), Tenant(tenantId));

    [Fact]
    public async Task Only_an_exact_redirect_uri_of_an_enabled_client_in_the_tenant_matches()
    {
        await SeedAsync("acme", "web", "https://app.acme.test/callback");
        await SeedAsync("acme", "old", "https://old.acme.test/callback", enabled: false);
        await SeedAsync("other", "theirs", "https://other.test/callback");

        var store = Store("acme");

        (await store.IsRegisteredRedirectUriAsync("https://app.acme.test/callback")).Should().BeTrue();
        (await store.IsRegisteredRedirectUriAsync("https://app.acme.test/callback/../steal")).Should().BeFalse();
        (await store.IsRegisteredRedirectUriAsync("https://app.acme.test/")).Should().BeFalse();
        (await store.IsRegisteredRedirectUriAsync("https://old.acme.test/callback")).Should().BeFalse("the client is disabled");
        (await store.IsRegisteredRedirectUriAsync("https://other.test/callback")).Should().BeFalse("another tenant's client");
    }

    public void Dispose() => _connection.Dispose();
}
