using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.EntityFramework;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for OAuth 2.0 Dynamic Client Registration (RFC 7591/7592).
/// Tests the full DCR flow including registration, retrieval, update, and deletion.
/// </summary>
public class DynamicClientRegistrationTests : IntegrationTestBase
{
    private const string RegisterEndpoint = "/connect/register";

    public DynamicClientRegistrationTests(OlusoWebApplicationFactory factory)
        : base(factory)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // Ensure DCR is enabled for tests by updating tenant settings
        await WithDbContextAsync(async db =>
        {
            var tenant = await db.Tenants.FindAsync("default");
            if (tenant != null)
            {
                // Ensure ProtocolConfiguration exists
                tenant.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenant.Id };
                tenant.ProtocolConfiguration.EnableDynamicClientRegistration = true;
                tenant.ProtocolConfiguration.AllowOpenDynamicRegistration = true; // Allow unauthenticated registration for basic tests
                tenant.ProtocolConfiguration.DynamicRegistrationMaxRedirectUris = 10;
                tenant.ProtocolConfiguration.DynamicRegistrationAllowedScopesJson = "[\"openid\", \"profile\", \"email\", \"api\"]";
                tenant.ProtocolConfiguration.DynamicRegistrationAllowedGrantTypesJson = "[\"authorization_code\", \"refresh_token\", \"client_credentials\"]";
                tenant.ProtocolConfiguration.DynamicRegistrationRequirePkce = true;
                await db.SaveChangesAsync();
            }
        });
    }

    #region Client Registration (POST /connect/register)

    [Fact]
    public async Task Register_WithValidRequest_ReturnsCreatedClient()
    {
        var request = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            ClientName = "Test DCR Client",
            GrantTypes = ["authorization_code", "refresh_token"],
            Scope = "openid profile"
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.ClientId.Should().NotBeNullOrEmpty();
        result.ClientId.Should().StartWith("dcr_");
        result.RedirectUris.Should().Contain("https://example.com/callback");
        result.GrantTypes.Should().Contain("authorization_code");
        result.RegistrationAccessToken.Should().NotBeNullOrEmpty();
        result.RegistrationClientUri.Should().Contain(result.ClientId);
    }

    [Fact]
    public async Task Register_WithClientSecret_ReturnsSecret()
    {
        var request = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            ClientName = "Confidential Client",
            TokenEndpointAuthMethod = "client_secret_basic"
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);
        result!.ClientSecret.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_WithPublicClient_NoSecret()
    {
        var request = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            TokenEndpointAuthMethod = "none"
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);
        result!.ClientSecret.Should().BeNull();
    }

    [Fact]
    public async Task Register_WithNoRedirectUris_ReturnsBadRequest()
    {
        var request = new DcrRequest
        {
            ClientName = "Missing Redirects"
            // No redirect_uris
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<DcrError>(JsonOptions);
        error!.Error.Should().Be("invalid_redirect_uri");
    }

    [Fact]
    public async Task Register_WithHttpRedirectUri_ReturnsBadRequest()
    {
        var request = new DcrRequest
        {
            RedirectUris = ["http://insecure.example.com/callback"] // HTTP not HTTPS
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<DcrError>(JsonOptions);
        error!.Error.Should().Be("invalid_redirect_uri");
    }

    [Fact]
    public async Task Register_WithLocalhostHttp_Succeeds()
    {
        // Localhost HTTP should be allowed for development
        var request = new DcrRequest
        {
            RedirectUris = ["http://localhost:3000/callback"]
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_WithDisallowedGrantType_ReturnsBadRequest()
    {
        var request = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            GrantTypes = ["password"] // Not in allowed list
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<DcrError>(JsonOptions);
        error!.Error.Should().Be("invalid_client_metadata");
        error.ErrorDescription.Should().Contain("password");
    }

    [Fact]
    public async Task Register_WithDisallowedScope_ReturnsBadRequest()
    {
        var request = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            Scope = "openid admin_access" // admin_access not allowed
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<DcrError>(JsonOptions);
        error!.Error.Should().Be("invalid_client_metadata");
    }

    [Fact]
    public async Task Register_StoresMetadataProperties()
    {
        var request = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            ClientName = "Full Metadata Client",
            TosUri = "https://example.com/tos",
            PolicyUri = "https://example.com/privacy",
            SoftwareId = "my-app-v1",
            SoftwareVersion = "1.0.0",
            Contacts = ["admin@example.com", "support@example.com"]
        };

        var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);
        result!.TosUri.Should().Be("https://example.com/tos");
        result.PolicyUri.Should().Be("https://example.com/privacy");
        result.SoftwareId.Should().Be("my-app-v1");
        result.SoftwareVersion.Should().Be("1.0.0");
        result.Contacts.Should().Contain("admin@example.com");
    }

    #endregion

    #region Client Retrieval (GET /connect/register/{clientId})

    [Fact]
    public async Task GetClient_WithValidToken_ReturnsClientConfig()
    {
        // First register a client
        var registerRequest = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            ClientName = "Get Test Client"
        };

        var registerResponse = await Client.PostAsJsonAsync(RegisterEndpoint, registerRequest, JsonOptions);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);

        // Now retrieve it
        var getClient = Factory.CreateClient();
        getClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered!.RegistrationAccessToken);

        var getResponse = await getClient.GetAsync($"{RegisterEndpoint}/{registered.ClientId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrieved = await getResponse.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);
        retrieved!.ClientId.Should().Be(registered.ClientId);
        retrieved.ClientName.Should().Be("Get Test Client");
        // Registration access token should NOT be in GET response per RFC 7592
        retrieved.RegistrationAccessToken.Should().BeNull();
    }

    [Fact]
    public async Task GetClient_WithoutToken_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync($"{RegisterEndpoint}/dcr_nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetClient_WithInvalidToken_ReturnsUnauthorized()
    {
        var getClient = Factory.CreateClient();
        getClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await getClient.GetAsync($"{RegisterEndpoint}/dcr_nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetClient_WithTokenForDifferentClient_ReturnsUnauthorized()
    {
        // Register two clients
        var client1Response = await Client.PostAsJsonAsync(RegisterEndpoint, new DcrRequest
        {
            RedirectUris = ["https://client1.example.com/callback"]
        }, JsonOptions);
        var client1 = await client1Response.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);

        var client2Response = await Client.PostAsJsonAsync(RegisterEndpoint, new DcrRequest
        {
            RedirectUris = ["https://client2.example.com/callback"]
        }, JsonOptions);
        var client2 = await client2Response.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);

        // Try to get client1 with client2's token
        var getClient = Factory.CreateClient();
        getClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", client2!.RegistrationAccessToken);

        var response = await getClient.GetAsync($"{RegisterEndpoint}/{client1!.ClientId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Client Update (PUT /connect/register/{clientId})

    [Fact]
    public async Task UpdateClient_WithValidToken_UpdatesClient()
    {
        // Register a client
        var registerRequest = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            ClientName = "Original Name"
        };

        var registerResponse = await Client.PostAsJsonAsync(RegisterEndpoint, registerRequest, JsonOptions);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);

        // Update it
        var updateClient = Factory.CreateClient();
        updateClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered!.RegistrationAccessToken);

        var updateRequest = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback", "https://example.com/callback2"],
            ClientName = "Updated Name"
        };

        var updateResponse = await updateClient.PutAsJsonAsync(
            $"{RegisterEndpoint}/{registered.ClientId}", updateRequest, JsonOptions);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResponse.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);
        updated!.ClientName.Should().Be("Updated Name");
        updated.RedirectUris.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateClient_CannotRemoveAllRedirectUris()
    {
        // Register a client
        var registerResponse = await Client.PostAsJsonAsync(RegisterEndpoint, new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"]
        }, JsonOptions);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);

        // Try to update with empty redirect URIs
        var updateClient = Factory.CreateClient();
        updateClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered!.RegistrationAccessToken);

        var updateRequest = new DcrRequest
        {
            RedirectUris = [] // Empty list
        };

        var response = await updateClient.PutAsJsonAsync(
            $"{RegisterEndpoint}/{registered.ClientId}", updateRequest, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Client Deletion (DELETE /connect/register/{clientId})

    [Fact]
    public async Task DeleteClient_WithValidToken_DeletesClient()
    {
        // Register a client
        var registerResponse = await Client.PostAsJsonAsync(RegisterEndpoint, new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"]
        }, JsonOptions);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);

        // Delete it
        var deleteClient = Factory.CreateClient();
        deleteClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered!.RegistrationAccessToken);

        var deleteResponse = await deleteClient.DeleteAsync($"{RegisterEndpoint}/{registered.ClientId}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's deleted - can't retrieve anymore
        var getResponse = await deleteClient.GetAsync($"{RegisterEndpoint}/{registered.ClientId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized); // Token is also deleted
    }

    [Fact]
    public async Task DeleteClient_TokenNoLongerWorks()
    {
        // Register a client
        var registerResponse = await Client.PostAsJsonAsync(RegisterEndpoint, new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"]
        }, JsonOptions);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);

        var managementClient = Factory.CreateClient();
        managementClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered!.RegistrationAccessToken);

        // Delete the client
        await managementClient.DeleteAsync($"{RegisterEndpoint}/{registered.ClientId}");

        // Try to use the token again - should fail
        var getResponse = await managementClient.GetAsync($"{RegisterEndpoint}/{registered.ClientId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Protected Registration

    [Fact]
    public async Task Register_WhenProtected_RequiresInitialAccessToken()
    {
        // Disable open registration
        await WithDbContextAsync(async db =>
        {
            var tenant = await db.Tenants.FindAsync("default");
            tenant!.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenant.Id };
            tenant.ProtocolConfiguration.AllowOpenDynamicRegistration = false;
            await db.SaveChangesAsync();
        });

        try
        {
            var request = new DcrRequest
            {
                RedirectUris = ["https://example.com/callback"]
            };

            var response = await Client.PostAsJsonAsync(RegisterEndpoint, request, JsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var error = await response.Content.ReadFromJsonAsync<DcrError>(JsonOptions);
            error!.Error.Should().Be("invalid_token");
        }
        finally
        {
            // Re-enable open registration for other tests
            await WithDbContextAsync(async db =>
            {
                var tenant = await db.Tenants.FindAsync("default");
                tenant!.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenant.Id };
                tenant.ProtocolConfiguration.AllowOpenDynamicRegistration = true;
                await db.SaveChangesAsync();
            });
        }
    }

    #endregion

    #region Client Can Be Used After Registration

    [Fact]
    public async Task RegisteredClient_CanBeUsedForAuthorization()
    {
        // Register a client
        var registerRequest = new DcrRequest
        {
            RedirectUris = ["https://example.com/callback"],
            GrantTypes = ["authorization_code"],
            Scope = "openid profile",
            TokenEndpointAuthMethod = "none"
        };

        var registerResponse = await Client.PostAsJsonAsync(RegisterEndpoint, registerRequest, JsonOptions);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DcrResponse>(JsonOptions);

        // Verify the client exists in the database and can be found
        await WithDbContextAsync(async db =>
        {
            var client = await db.Clients.FindAsync(registered!.ClientId);
            client.Should().NotBeNull();
            client!.IsDynamicallyRegistered.Should().BeTrue();
            client.RequirePkce.Should().BeTrue(); // Should enforce PKCE by default
        });
    }

    #endregion

    #region Test Models

    private class DcrRequest
    {
        [JsonPropertyName("redirect_uris")]
        public List<string>? RedirectUris { get; set; }

        [JsonPropertyName("token_endpoint_auth_method")]
        public string? TokenEndpointAuthMethod { get; set; }

        [JsonPropertyName("grant_types")]
        public List<string>? GrantTypes { get; set; }

        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("tos_uri")]
        public string? TosUri { get; set; }

        [JsonPropertyName("policy_uri")]
        public string? PolicyUri { get; set; }

        [JsonPropertyName("software_id")]
        public string? SoftwareId { get; set; }

        [JsonPropertyName("software_version")]
        public string? SoftwareVersion { get; set; }

        [JsonPropertyName("contacts")]
        public List<string>? Contacts { get; set; }
    }

    private class DcrResponse
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = null!;

        [JsonPropertyName("client_secret")]
        public string? ClientSecret { get; set; }

        [JsonPropertyName("redirect_uris")]
        public List<string>? RedirectUris { get; set; }

        [JsonPropertyName("grant_types")]
        public List<string>? GrantTypes { get; set; }

        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [JsonPropertyName("registration_access_token")]
        public string? RegistrationAccessToken { get; set; }

        [JsonPropertyName("registration_client_uri")]
        public string? RegistrationClientUri { get; set; }

        [JsonPropertyName("tos_uri")]
        public string? TosUri { get; set; }

        [JsonPropertyName("policy_uri")]
        public string? PolicyUri { get; set; }

        [JsonPropertyName("software_id")]
        public string? SoftwareId { get; set; }

        [JsonPropertyName("software_version")]
        public string? SoftwareVersion { get; set; }

        [JsonPropertyName("contacts")]
        public List<string>? Contacts { get; set; }
    }

    private class DcrError
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = null!;

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    #endregion
}
