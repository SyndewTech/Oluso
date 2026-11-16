using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.EntityFramework;

namespace Oluso.Integration.Tests.Fixtures;

/// <summary>An admin API client for the seeded super admin, signed in to admin-portal and working in a tenant.</summary>
public static class SuperAdminClient
{
    public static async Task<HttpClient> CreateAsync(OlusoWebApplicationFactory factory, string tenantId)
    {
        string userId, clientId, redirectUri;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
            userId = (await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == "superadmin@oluso.local")).Id;
            var portal = await db.Clients.IgnoreQueryFilters().Include(c => c.RedirectUris).FirstAsync(c => c.ClientId == "admin-portal");
            clientId = portal.ClientId;
            redirectUri = portal.RedirectUris.First().RedirectUri;
        }

        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var code = $"ps_{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuthorizationCodeStore>().StoreAsync(new AuthorizationCode
            {
                Code = code, ClientId = clientId, SubjectId = userId, RedirectUri = redirectUri,
                Scopes = new List<string> { "openid", "profile" },
                CodeChallenge = challenge, CodeChallengeMethod = "S256", TenantId = "_platform",
                CreationTime = DateTime.UtcNow, Expiration = DateTime.UtcNow.AddMinutes(5),
            });
        }

        var response = await factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId, ["code_verifier"] = verifier,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
        http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        return http;
    }
}
