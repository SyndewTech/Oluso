using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Oluso.Core.Domain.Entities;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// What a Matrix homeserver's clients read from the issuer's metadata (Matrix spec, OAuth 2.0 API; RFC 8414):
/// <c>prompt_values_supported</c> with <c>create</c>, <c>refresh_token</c> among the grant types, and the device
/// authorization endpoint with its grant (for sign in with QR) when the tenant allows the grant, and not otherwise.
/// </summary>
public class MatrixDiscoveryTests : IntegrationTestBase
{
    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    public MatrixDiscoveryTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
    }

    private async Task<JsonElement> MetadataAsync(string path = "/.well-known/openid-configuration")
    {
        var document = await Client.GetFromJsonAsync<JsonDocument>(path);
        return document!.RootElement.Clone();
    }

    private static IEnumerable<string> Strings(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(e => e.GetString()!);

    [Theory]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/oauth-authorization-server")]
    public async Task The_metadata_lists_the_prompt_values_refresh_and_the_device_grant(string path)
    {
        var root = await MetadataAsync(path);

        Strings(root, "prompt_values_supported").Should().Contain(new[] { "create", "login", "none", "consent" });
        Strings(root, "prompt_values_supported").Should().NotContain("select_account");
        Strings(root, "grant_types_supported").Should().Contain(new[] { "authorization_code", "refresh_token", DeviceGrant });
        root.GetProperty("device_authorization_endpoint").GetString().Should().EndWith("/connect/deviceauthorization");
    }

    [Fact]
    public async Task A_tenant_that_does_not_allow_the_device_grant_has_no_device_endpoint()
    {
        string? before = null;
        await WithDbContextAsync(async db =>
        {
            var tenant = await db.Tenants.FindAsync("default");
            tenant!.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenant.Id };
            before = tenant.ProtocolConfiguration.AllowedGrantTypesJson;
            tenant.ProtocolConfiguration.AllowedGrantTypesJson = "[\"authorization_code\",\"refresh_token\"]";
            await db.SaveChangesAsync();
        });

        try
        {
            var root = await MetadataAsync();
            Strings(root, "grant_types_supported").Should().NotContain(DeviceGrant);
            root.TryGetProperty("device_authorization_endpoint", out _).Should().BeFalse();
            Strings(root, "prompt_values_supported").Should().Contain("create");
        }
        finally
        {
            await WithDbContextAsync(async db =>
            {
                var tenant = await db.Tenants.FindAsync("default");
                tenant!.ProtocolConfiguration!.AllowedGrantTypesJson = before;
                await db.SaveChangesAsync();
            });
        }
    }
}
