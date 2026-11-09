using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Oluso.Core;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Security invariant: a client that belongs to a regular tenant must be
/// invisible at the platform's token endpoint. A caller hitting the platform
/// host (where the request resolves to PlatformConstants.TenantId) must not be
/// able to authenticate a tenant-owned client and obtain tokens through it.
///
/// The guard lives in ClientStore.FindClientByIdAsync: the cross-filter fallback
/// that surfaces shared platform clients (admin-portal, workspace-portal, ...)
/// from any tenant request is constrained to TenantId == PlatformConstants.TenantId.
/// When the request context already IS the platform tenant, the first
/// (tenant-filtered) query is authoritative and the fallback never runs — so a
/// tenant client is simply not found and ClientAuthenticator returns
/// "invalid_client / Unknown client".
///
/// These tests pin that behavior end-to-end through /connect/token so a future
/// refactor of the ClientStore fallback can't silently let a tenant client
/// authenticate against the platform issuer.
///
/// Subject under test: "account-ui" — the seeded public PKCE client that lives
/// in the "default" tenant (SeedData.SeedClientsAsync). The request context is
/// driven by the X-Tenant-Id header, which TenantResolutionMiddleware honours
/// for unauthenticated /connect/token requests.
/// </summary>
public class PlatformClientIsolationTests : IntegrationTestBase
{
    private const string TenantHeader = "X-Tenant-Id";
    private const string TenantClientId = "account-ui";

    public PlatformClientIsolationTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    private async Task<string?> GetClientTenantIdAsync(string clientId)
    {
        string? tenantId = null;
        await WithDbContextAsync(async db =>
        {
            tenantId = await db.Clients
                .IgnoreQueryFilters()
                .Where(c => c.ClientId == clientId)
                .Select(c => c.TenantId)
                .FirstOrDefaultAsync();
        });
        return tenantId;
    }

    private async Task<(HttpStatusCode status, string? error, string body)> PostTokenAsync(string tenantContext)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TenantHeader, tenantContext);

        // Authorization-code grant with a bogus code: client authentication runs
        // FIRST in TokenRequestValidator, so an unresolvable client short-circuits
        // with invalid_client before the (invalid) code is ever inspected.
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = TenantClientId,
            ["code"] = "bogus-code",
            ["redirect_uri"] = "http://localhost:5173/callback",
            ["code_verifier"] = "bogus-verifier"
        });

        var response = await client.PostAsync("/connect/token", request);
        var body = await response.Content.ReadAsStringAsync();

        string? error = null;
        if (!string.IsNullOrEmpty(body) && body.TrimStart().StartsWith("{"))
        {
            var root = JsonDocument.Parse(body).RootElement;
            if (root.TryGetProperty("error", out var e))
                error = e.GetString();
        }

        return (response.StatusCode, error, body);
    }

    /// <summary>
    /// The core invariant: account-ui (tenant "default") presented at the
    /// platform token endpoint is rejected as an unknown client — the tenant
    /// client never surfaces in platform context.
    /// </summary>
    [Fact]
    public async Task TenantClient_AtPlatformTokenEndpoint_RejectedAsUnknownClient()
    {
        var clientTenant = await GetClientTenantIdAsync(TenantClientId);
        clientTenant.Should().Be("default",
            $"'{TenantClientId}' must be seeded as a regular-tenant client for this test to be meaningful");
        clientTenant.Should().NotBe(PlatformConstants.TenantId,
            "the subject client must NOT be a platform client");

        var (status, error, body) = await PostTokenAsync(PlatformConstants.TenantId);

        status.Should().Be(HttpStatusCode.BadRequest,
            $"a tenant-owned client must not authenticate at the platform token endpoint. Body: {body}");
        error.Should().Be("invalid_client",
            $"the tenant client must be invisible in platform context (Unknown client), got: {body}");
        body.Should().Contain("Unknown client");
    }

    /// <summary>
    /// Positive control: the SAME client, the SAME request — but resolved in its
    /// own tenant context — DOES authenticate. The request then fails on the bogus
    /// authorization code (invalid_grant), never on client resolution. This proves
    /// the platform-context rejection above is specifically tenant-client
    /// invisibility, not a blanket failure of the request shape.
    /// </summary>
    [Fact]
    public async Task TenantClient_AtOwnTenantTokenEndpoint_ResolvesClient()
    {
        var (status, error, body) = await PostTokenAsync("default");

        status.Should().Be(HttpStatusCode.BadRequest,
            $"the bogus authorization code must still be rejected. Body: {body}");
        error.Should().NotBe("invalid_client",
            $"in its home tenant the client must resolve — failure should be grant-specific, got: {body}");
        body.Should().NotContain("Unknown client",
            "the client is resolvable in its own tenant, so 'Unknown client' must not appear");
    }
}
