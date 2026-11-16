using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// A tenant client a module created and keeps up to date (<see cref="Client.NonEditable"/>) is shown to administrators
/// but cannot be changed or deleted through the admin API. Platform clients are stored non-editable for tenants, yet a
/// super admin still creates them and edits them, redirect URIs included.
/// </summary>
public class ManagedClientTests : IntegrationTestBase
{
    private const string ManagedClientId = "module-managed-client";

    public ManagedClientTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override Task InitializeAsync() => WithDbContextAsync(async db =>
    {
        if (await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == ManagedClientId)) return;
        var client = new Client
        {
            ClientId = ManagedClientId,
            ClientName = "Managed by a module",
            TenantId = "default",
            Enabled = true,
            RequireClientSecret = true,
            NonEditable = true,
        };
        client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "client_credentials", TenantId = "default" });
        client.ClientSecrets.Add(new ClientSecret { Value = "hashed", TenantId = "default" });
        db.Clients.Add(client);
        await db.SaveChangesAsync();
    });

    [Fact]
    public async Task A_module_managed_tenant_client_is_shown_but_not_changed()
    {
        var admin = await SuperAdminClient.CreateAsync(Factory, "default");
        var id = await RowIdAsync(ManagedClientId);

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/clients/{id}");
        detail.GetProperty("nonEditable").GetBoolean().Should().BeTrue();
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/admin/clients");
        Items(listed).Should().Contain(c => c.GetProperty("clientId").GetString() == ManagedClientId
            && c.GetProperty("nonEditable").GetBoolean());

        var secretId = detail.GetProperty("secrets")[0].GetProperty("id").GetInt32();
        var refused = new[]
        {
            await admin.PutAsJsonAsync($"/api/admin/clients/{id}", new { clientName = "Renamed" }),
            await admin.PostAsJsonAsync($"/api/admin/clients/{id}/keys", new { type = "jwk", value = "{}" }),
            await admin.PostAsync($"/api/admin/clients/{id}/regenerate-secret", null),
            await admin.DeleteAsync($"/api/admin/clients/{id}/secrets/{secretId}"),
            await admin.DeleteAsync($"/api/admin/clients/{id}"),
        };
        foreach (var response in refused)
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, response.RequestMessage!.RequestUri!.ToString());
            (await response.Content.ReadAsStringAsync()).Should().Contain("managed by a module");
        }

        await WithDbContextAsync(async db =>
        {
            var stored = await db.Clients.IgnoreQueryFilters().AsNoTracking().Include(c => c.ClientSecrets)
                .SingleAsync(c => c.ClientId == ManagedClientId);
            stored.ClientName.Should().Be("Managed by a module");
            stored.ClientSecrets.Should().ContainSingle();
        });
    }

    [Fact]
    public async Task A_super_admin_still_creates_platform_clients_and_adds_redirect_uris_to_them()
    {
        // In the platform context, as the platform pages work (a tenant-scoped request would put new rows in that tenant).
        var admin = await SuperAdminClient.CreateAsync(Factory, Oluso.Core.PlatformConstants.TenantId);
        var clientId = $"platform-app-{Guid.NewGuid():N}"[..24];

        var created = await admin.PostAsJsonAsync("/api/admin/platform/clients", new
        {
            clientId,
            clientName = "Platform app",
            requireClientSecret = false,
            allowedGrantTypes = new[] { "authorization_code" },
            redirectUris = new[] { "https://app.example.com/callback" },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var updated = await admin.PutAsJsonAsync($"/api/admin/platform/clients/{id}", new
        {
            redirectUris = new[] { "https://app.example.com/callback", "https://app.example.com/second-callback" },
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("redirectUris").EnumerateArray().Select(u => u.GetString())
            .Should().BeEquivalentTo("https://app.example.com/callback", "https://app.example.com/second-callback");
        body.GetProperty("nonEditable").GetBoolean().Should().BeFalse("the platform view edits platform clients");

        await WithDbContextAsync(async db =>
            (await db.Clients.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.ClientId == clientId))
                .NonEditable.Should().BeTrue("tenants still cannot edit it"));
    }

    private static IEnumerable<JsonElement> Items(JsonElement listed) =>
        listed.ValueKind == JsonValueKind.Array ? listed.EnumerateArray() : listed.GetProperty("items").EnumerateArray();

    private async Task<int> RowIdAsync(string clientId)
    {
        var id = 0;
        await WithDbContextAsync(async db =>
            id = await db.Clients.IgnoreQueryFilters().Where(c => c.ClientId == clientId).Select(c => c.Id).FirstAsync());
        return id;
    }
}
