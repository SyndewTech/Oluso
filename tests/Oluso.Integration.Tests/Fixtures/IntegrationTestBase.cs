using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Oluso.EntityFramework;
using Xunit;

namespace Oluso.Integration.Tests.Fixtures;

/// <summary>
/// Base class for integration tests providing common functionality.
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<OlusoWebApplicationFactory>, IAsyncLifetime
{
    protected readonly OlusoWebApplicationFactory Factory;
    protected readonly HttpClient Client;
    protected readonly JsonSerializerOptions JsonOptions;

    protected IntegrationTestBase(OlusoWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Gets the database context for direct database access in tests.
    /// </summary>
    protected OlusoDbContext GetDbContext()
    {
        var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
    }

    /// <summary>
    /// Executes an action with a scoped database context.
    /// </summary>
    protected async Task WithDbContextAsync(Func<OlusoDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
        await action(db);
    }

    /// <summary>
    /// Gets an access token for the specified user credentials.
    /// </summary>
    protected async Task<string?> GetAccessTokenAsync(string username, string password, string clientId = "test-client", string clientSecret = "test-secret")
    {
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = "openid profile email"
        });

        var response = await Client.PostAsync("/connect/token", tokenRequest);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        return content?.AccessToken;
    }

    /// <summary>
    /// Creates an HTTP client with the Bearer token set.
    /// </summary>
    protected HttpClient CreateAuthenticatedClient(string accessToken, string? tenantId = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        if (tenantId != null)
        {
            client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantId);
        }

        return client;
    }

    private record TokenResponse(
        string AccessToken,
        string TokenType,
        int ExpiresIn,
        string? RefreshToken,
        string? Scope);
}
