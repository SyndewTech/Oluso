using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>A client's "Last accessed" is set when it gets tokens, at most about once an hour.</summary>
public class ClientLastAccessedTests : IntegrationTestBase
{
    private const string ClientId = "cc-client";

    public ClientLastAccessedTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
    }

    private Task SetLastAccessedAsync(DateTime? value) => WithDbContextAsync(async db =>
    {
        var client = await db.Clients.IgnoreQueryFilters().SingleAsync(c => c.ClientId == ClientId);
        client.LastAccessed = value;
        await db.SaveChangesAsync();
    });

    private async Task<DateTime?> LastAccessedAsync()
    {
        DateTime? value = null;
        await WithDbContextAsync(async db => value = await db.Clients.IgnoreQueryFilters()
            .Where(c => c.ClientId == ClientId).Select(c => c.LastAccessed).SingleAsync());
        return value;
    }

    private async Task GetTokenAsync()
    {
        var response = await Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ClientId,
            ["client_secret"] = "test-secret",
            ["scope"] = "openid",
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Getting_a_token_records_the_access_but_not_more_than_hourly()
    {
        await SetLastAccessedAsync(null);
        await GetTokenAsync();
        (await LastAccessedAsync()).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        var recent = DateTime.UtcNow.AddMinutes(-10);
        await SetLastAccessedAsync(recent);
        await GetTokenAsync();
        (await LastAccessedAsync()).Should().BeCloseTo(recent, TimeSpan.FromSeconds(1), "it was written less than an hour ago");

        await SetLastAccessedAsync(DateTime.UtcNow.AddHours(-2));
        await GetTokenAsync();
        (await LastAccessedAsync()).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }
}
