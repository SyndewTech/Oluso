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
using Oluso.Integration.Tests.Fixtures;
using static Oluso.Integration.Tests.Fixtures.DPoPTestProofs;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Tenant-switching authorization gate at TenantResolutionMiddleware.ValidateAuthenticatedAccessAsync.
///
/// Design contract:
///   - SuperAdmin: can switch to any tenant via X-Tenant-Id header (line 290-294 — unconditional grant).
///   - Regular/Org users: must satisfy at least one of —
///       (a) requested tenant in JWT allowed_tenants claim (line 297-303), OR
///       (b) requested tenant matches the user's own JWT tenant_id (line 305-315 — backward-compat fallback).
///   - All other cases: 403.
///
/// These tests pin the contract so that future changes (e.g. removing the JWT
/// tenant_id fallback) surface explicitly. They use real JWTs minted via the
/// auth-code grant — same path production uses.
/// </summary>
public class TenantSwitchingTests : IntegrationTestBase
{
    public TenantSwitchingTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Trigger signing key generation so MintAccessTokenAsync doesn't
        // race with other test classes during parallel execution.
        await Client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "cc-client",
                ["client_secret"] = "test-secret",
                ["scope"] = "openid"
            }));
    }

    private const string TenantHeader = "X-Tenant-Id";
    // /api/admin/context has [Authorize(Policy = "AdminApi")] — exercises the
    // JWT bearer scheme + tenant resolution middleware end-to-end.
    private const string ApiEndpoint = "/api/admin/context";

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task StoreAuthorizationCodeAsync(AuthorizationCode code)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuthorizationCodeStore>();
        await store.StoreAsync(code);
    }

    private async Task<(string subjectId, string clientId, string redirectUri)> GetAdminPortalContextAsync()
    {
        string? subjectId = null;
        string? clientId = null;
        string? redirectUri = null;

        await WithDbContextAsync(async db =>
        {
            var superAdmin = await db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == "superadmin@oluso.local");
            subjectId = superAdmin?.Id;

            var adminPortal = await db.Clients
                .IgnoreQueryFilters()
                .Include(c => c.RedirectUris)
                .FirstOrDefaultAsync(c => c.ClientId == "admin-portal");
            clientId = adminPortal?.ClientId;
            redirectUri = adminPortal?.RedirectUris.FirstOrDefault()?.RedirectUri;
        });

        return (subjectId!, clientId!, redirectUri!);
    }

    private async Task<string?> GetUserIdAsync(string email)
    {
        string? id = null;
        await WithDbContextAsync(async db =>
        {
            var u = await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Email == email);
            id = u?.Id;
        });
        return id;
    }

    /// <summary>
    /// Issues a real access token via auth-code grant for the given user, stamping
    /// the auth code with a tenant the user is allowed in (so the grant succeeds).
    /// </summary>
    private async Task<string> MintAccessTokenAsync(string userId, string authCodeTenantId, ECDsa? dpopKey = null, string? viaTenant = null) =>
        (await SignInAsync(userId, authCodeTenantId, dpopKey, viaTenant)).GetProperty("access_token").GetString()!;

    /// <summary>The token response of an auth-code sign-in to admin-portal; with a refresh token when <paramref name="offline"/>.</summary>
    private async Task<JsonElement> SignInAsync(
        string userId, string authCodeTenantId, ECDsa? dpopKey = null, string? viaTenant = null, bool offline = false)
    {
        var (_, clientId, redirectUri) = await GetAdminPortalContextAsync();
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"sw_{Guid.NewGuid():N}";

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = clientId,
            SubjectId = userId,
            RedirectUri = redirectUri,
            Scopes = offline ? new List<string> { "openid", "profile", "offline_access" } : new List<string> { "openid", "profile" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = authCodeTenantId,
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddMinutes(5),
            IsConsumed = false
        });

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = tokenRequest };
        if (dpopKey != null) request.Headers.Add("DPoP", Proof(dpopKey, "POST", "/connect/token", accessToken: null));
        if (viaTenant != null) request.Headers.Add(TenantHeader, viaTenant);
        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"failed to mint access token: {(int)response.StatusCode} {body}");

        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<HttpResponseMessage> CallApiEndpointAsync(string accessToken, string? tenantHeader = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (tenantHeader != null)
        {
            client.DefaultRequestHeaders.Add(TenantHeader, tenantHeader);
        }
        return await client.GetAsync(ApiEndpoint);
    }

    /// <summary>
    /// SuperAdmin (user.TenantId=null, super_admin=true claim in JWT) can switch
    /// to ANY tenant via the X-Tenant-Id header. This is the privileged path
    /// admin tooling relies on for cross-tenant inspection.
    /// </summary>
    [Theory]
    [InlineData("default")]
    [InlineData("_platform")]
    public async Task SuperAdmin_CanSwitchTenantViaHeader(string targetTenant)
    {
        var superAdminId = await GetUserIdAsync("superadmin@oluso.local");
        Assert.False(string.IsNullOrEmpty(superAdminId), "superadmin must be seeded");

        var token = await MintAccessTokenAsync(superAdminId!, "_platform");

        var response = await CallApiEndpointAsync(token, tenantHeader: targetTenant);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"SuperAdmin must be able to switch to any tenant via {TenantHeader} header");
    }

    /// <summary>
    /// A regular tenant user (user.TenantId="default", no allowed_tenants for
    /// other tenants) trying to switch to a tenant they don't belong to MUST be
    /// rejected with 403 — protects the sanctity of other tenants.
    /// </summary>
    [Fact]
    public async Task TenantUser_CannotSwitchToForeignTenant_Returns403()
    {
        var tenantUserId = await GetUserIdAsync("admin@default.local");
        Assert.False(string.IsNullOrEmpty(tenantUserId), "admin@default.local must be seeded");

        var token = await MintAccessTokenAsync(tenantUserId!, "default");

        // Try to switch to the platform tenant (user has no membership).
        var response = await CallApiEndpointAsync(token, tenantHeader: "_platform");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "tenant user must be denied switching to a tenant they have no membership in");
    }

    /// <summary>
    /// A regular tenant user accessing their OWN tenant via the header must
    /// succeed. Today this works via the JWT tenant_id fallback (line 305-315);
    /// after the fallback is removed, it must work via allowed_tenants (which
    /// must include the user's home tenant by construction).
    ///
    /// If this test fails after removing the fallback, the fix is to add the
    /// user's home tenant to the allowed_tenants claim emitted by
    /// TokenService.GetOrganizationClaimsAsync and OlusoClaimsPrincipalFactory.
    /// </summary>
    [Fact]
    public async Task TenantUser_CanAccessOwnTenantViaHeader()
    {
        var tenantUserId = await GetUserIdAsync("admin@default.local");
        Assert.False(string.IsNullOrEmpty(tenantUserId), "admin@default.local must be seeded");

        var token = await MintAccessTokenAsync(tenantUserId!, "default");

        var response = await CallApiEndpointAsync(token, tenantHeader: "default");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "tenant user must be able to access their own tenant via the X-Tenant-Id header");
    }

    /// <summary>
    /// No tenant header: the middleware falls back to the JWT's tenant_id
    /// (ResolveAuthenticatedApiTenant line 204). Should succeed for the user's
    /// home tenant. Documents the no-header default behavior.
    /// </summary>
    [Fact]
    public async Task TenantUser_NoHeader_FallsBackToOwnTenant()
    {
        var tenantUserId = await GetUserIdAsync("admin@default.local");
        Assert.False(string.IsNullOrEmpty(tenantUserId), "admin@default.local must be seeded");

        var token = await MintAccessTokenAsync(tenantUserId!, "default");

        var response = await CallApiEndpointAsync(token, tenantHeader: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "tenant user with no header should fall back to JWT tenant_id");
    }
    private const string SwitchToDefault = "/api/account/tenants/default/switch";

    private async Task<HttpResponseMessage> SwitchToDefaultAsync(string scheme, string accessToken, string? proof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SwitchToDefault);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, accessToken);
        request.Headers.Add(TenantHeader, "default");
        if (proof != null) request.Headers.Add("DPoP", proof);
        return await Factory.CreateClient().SendAsync(request);
    }

    private async Task<HttpResponseMessage> CallApiEndpointWithDPoPAsync(string accessToken, ECDsa key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", accessToken);
        request.Headers.Add(TenantHeader, "default");
        request.Headers.Add("DPoP", Proof(key, "GET", ApiEndpoint, accessToken));
        return await Factory.CreateClient().SendAsync(request);
    }

    private static string? TokenTenant(string accessToken) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(accessToken)
            .Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;

    /// <summary>
    /// The switch endpoint mints the token the SPAs adopt. A session whose token is
    /// bound to a DPoP key keeps that binding across the switch: the new token is
    /// bound to the same key, refused as a bearer token, and usable with a proof.
    /// </summary>
    [Fact]
    public async Task Switch_with_a_DPoP_bound_token_returns_a_token_bound_to_the_same_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var superAdminId = await GetUserIdAsync("superadmin@oluso.local");
        var token = await MintAccessTokenAsync(superAdminId!, "_platform", key);
        TokenJkt(token).Should().NotBeNullOrEmpty("the token endpoint binds the token to the proof's key");

        var response = await SwitchToDefaultAsync("DPoP", token, Proof(key, "POST", SwitchToDefault, token));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var switched = body.GetProperty("accessToken").GetString()!;
        body.GetProperty("tokenType").GetString().Should().Be("DPoP");
        TokenJkt(switched).Should().Be(TokenJkt(token));
        TokenTenant(switched).Should().Be("default");

        (await CallApiEndpointAsync(switched, tenantHeader: "default")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "a DPoP-bound token is never accepted as a bearer token");
        (await CallApiEndpointWithDPoPAsync(switched, key)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Switch_with_a_DPoP_bound_token_needs_a_proof_from_its_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var superAdminId = await GetUserIdAsync("superadmin@oluso.local");
        var token = await MintAccessTokenAsync(superAdminId!, "_platform", key);

        var asBearer = await SwitchToDefaultAsync("Bearer", token, proof: null);
        var otherKeyProof = await SwitchToDefaultAsync("DPoP", token, Proof(otherKey, "POST", SwitchToDefault, token));

        asBearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        otherKeyProof.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Switch_with_an_unbound_token_returns_an_unbound_token()
    {
        var superAdminId = await GetUserIdAsync("superadmin@oluso.local");
        var token = await MintAccessTokenAsync(superAdminId!, "_platform");

        var response = await SwitchToDefaultAsync("Bearer", token, proof: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var switched = body.GetProperty("accessToken").GetString()!;
        body.GetProperty("tokenType").GetString().Should().Be("Bearer");
        TokenJkt(switched).Should().BeNull();
        TokenTenant(switched).Should().Be("default");
        (await CallApiEndpointAsync(switched, tenantHeader: "default")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
    /// <summary>
    /// After a switch the SPA reloads and calls the API with the session's
    /// DPoP-bound token under the DPoP scheme and the new tenant in X-Tenant-Id.
    /// </summary>
    [Theory]
    [InlineData("default")]
    [InlineData("_platform")]
    public async Task SuperAdmin_with_a_DPoP_bound_token_can_act_in_another_tenant_via_header(string targetTenant)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var superAdminId = await GetUserIdAsync("superadmin@oluso.local");
        var token = await MintAccessTokenAsync(superAdminId!, "_platform", key);

        using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", token);
        request.Headers.Add(TenantHeader, targetTenant);
        request.Headers.Add("DPoP", Proof(key, "GET", ApiEndpoint, token));
        var response = await Factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"{response.Headers.WwwAuthenticate} {await response.Content.ReadAsStringAsync()}");
    }
    private const string OwnedOrg = "switch-owned-org";
    private const string OwnedHomeTenant = "switch-owned-home";
    private const string OwnedSecondTenant = "switch-owned-second";

    /// <summary>
    /// An owner of an organization with two tenants, homed in one, with no
    /// membership row in the other: the middleware admits them to both.
    /// </summary>
    private async Task<string> OrganizationOwnerAsync()
    {
        var userId = Guid.NewGuid().ToString();
        var email = $"owner-{userId[..8]}@switch.test";
        await WithDbContextAsync(async db =>
        {
            if (!await db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Id == OwnedOrg))
            {
                db.Organizations.Add(new Organization { Id = OwnedOrg, Name = "Switch Org", Slug = OwnedOrg });
                foreach (var tenantId in new[] { OwnedHomeTenant, OwnedSecondTenant })
                {
                    db.Tenants!.Add(new Tenant
                    {
                        Id = tenantId,
                        Name = tenantId,
                        DisplayName = tenantId,
                        Identifier = tenantId,
                        OrganizationId = OwnedOrg,
                        Enabled = true
                    });
                }
            }
            db.Users.Add(new OlusoUser
            {
                Id = userId,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(),
                TenantId = OwnedHomeTenant,
                IsActive = true
            });
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                Id = Guid.NewGuid().ToString(),
                OrganizationId = OwnedOrg,
                UserId = userId,
                Role = OrganizationRole.Owner
            });
            await db.SaveChangesAsync();
        });
        return userId;
    }

    private static IReadOnlyList<string> ClaimValues(string accessToken, string type) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(accessToken)
            .Claims.Where(c => c.Type == type).Select(c => c.Value).ToList();

    [Fact]
    public async Task Organization_owner_is_allowed_every_tenant_of_their_organization()
    {
        var ownerId = await OrganizationOwnerAsync();

        var token = await MintAccessTokenAsync(ownerId, OwnedHomeTenant, viaTenant: OwnedHomeTenant);

        TokenTenant(token).Should().Be(OwnedHomeTenant);
        ClaimValues(token, "allowed_tenants").Should().Contain(new[] { OwnedHomeTenant, OwnedSecondTenant });
    }

    /// <summary>
    /// The owner switches into the organization's other tenant: the token is for
    /// that tenant, with the owner's claims, and the workspace profile loads there.
    /// </summary>
    [Fact]
    public async Task Organization_owner_switches_into_another_tenant_of_their_organization()
    {
        var ownerId = await OrganizationOwnerAsync();
        var token = await MintAccessTokenAsync(ownerId, OwnedHomeTenant, viaTenant: OwnedHomeTenant);
        var switchPath = $"/api/account/tenants/{OwnedSecondTenant}/switch";

        using var switchRequest = new HttpRequestMessage(HttpMethod.Post, switchPath);
        switchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        switchRequest.Headers.Add(TenantHeader, OwnedSecondTenant);
        var response = await Factory.CreateClient().SendAsync(switchRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var switched = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        TokenTenant(switched).Should().Be(OwnedSecondTenant);
        ClaimValues(switched, "allowed_tenants").Should().Contain(OwnedSecondTenant);
        ClaimValues(switched, "is_org_admin").Should().Equal("true");

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/workspace/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", switched);
        meRequest.Headers.Add(TenantHeader, OwnedSecondTenant);
        var me = await Factory.CreateClient().SendAsync(meRequest);

        me.StatusCode.Should().Be(HttpStatusCode.OK, await me.Content.ReadAsStringAsync());
        (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetString().Should().Be(OwnedSecondTenant);
    }
    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken, string? tenant, ECDsa? dpopKey = null)
    {
        var (_, clientId, _) = await GetAdminPortalContextAsync();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        };
        if (tenant != null) form["tenant"] = tenant;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent(form) };
        if (dpopKey != null) request.Headers.Add("DPoP", Proof(dpopKey, "POST", "/connect/token", accessToken: null));
        return await Client.SendAsync(request);
    }

    /// <summary>
    /// A refresh naming a tenant switches the session: the new tokens are for that
    /// tenant, still bound to the session's DPoP key, and the rotated refresh token
    /// keeps the tenant, so a plain refresh afterwards stays there.
    /// </summary>
    [Fact]
    public async Task Refresh_with_a_tenant_switches_the_session_and_keeps_it_switched()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ownerId = await OrganizationOwnerAsync();
        var signIn = await SignInAsync(ownerId, OwnedHomeTenant, key, viaTenant: OwnedHomeTenant, offline: true);
        var jkt = TokenJkt(signIn.GetProperty("access_token").GetString()!);

        var switchResponse = await RefreshAsync(signIn.GetProperty("refresh_token").GetString()!, OwnedSecondTenant, key);

        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK, await switchResponse.Content.ReadAsStringAsync());
        var switched = await switchResponse.Content.ReadFromJsonAsync<JsonElement>();
        var switchedToken = switched.GetProperty("access_token").GetString()!;
        TokenTenant(switchedToken).Should().Be(OwnedSecondTenant);
        ClaimValues(switchedToken, "allowed_tenants").Should().Contain(OwnedSecondTenant);
        TokenJkt(switchedToken).Should().Be(jkt);

        var later = await RefreshAsync(switched.GetProperty("refresh_token").GetString()!, tenant: null, key);

        later.StatusCode.Should().Be(HttpStatusCode.OK, await later.Content.ReadAsStringAsync());
        TokenTenant((await later.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!)
            .Should().Be(OwnedSecondTenant, "the refresh token remembers the tenant it was switched to");
    }

    [Fact]
    public async Task Refresh_with_a_tenant_the_user_cannot_reach_is_refused_and_spends_nothing()
    {
        var ownerId = await OrganizationOwnerAsync();
        var signIn = await SignInAsync(ownerId, OwnedHomeTenant, viaTenant: OwnedHomeTenant, offline: true);
        var refreshToken = signIn.GetProperty("refresh_token").GetString()!;

        var refused = await RefreshAsync(refreshToken, "default");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("access_denied");
        var plain = await RefreshAsync(refreshToken, tenant: null);
        plain.StatusCode.Should().Be(HttpStatusCode.OK, "a refused switch leaves the refresh token usable");
        TokenTenant((await plain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!)
            .Should().Be(OwnedHomeTenant);
    }

    [Fact]
    public async Task Tenant_is_refused_on_other_grants()
    {
        var response = await Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cc-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "openid",
            ["tenant"] = "default",
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_request");
    }
}
