using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Security invariant: the access token's tenant_id claim MUST be the user's home
/// tenant (user.TenantId from the DB), regardless of:
///   - the request host's resolved tenant
///   - the auth code's stamped TenantId
///   - which client the user is authenticating with
///
/// This protects against a hostile or buggy code path stamping an auth code with
/// a foreign tenant and tricking the token issuance into emitting a token that
/// claims the user belongs there. The DefaultProfileService.AddCustomClaims path
/// (DefaultProfileService.cs:184-188) sources tenant_id from user.TenantId; the
/// auth-code claim merge (AuthorizationCodeGrantHandler.cs:223) only sets keys
/// not already present, so the user-record claim always wins.
///
/// If any of these tests fail, a tenant boundary has leaked into token issuance —
/// treat as a P0 security regression.
/// </summary>
public class TokenTenantIntegrityTests : IntegrationTestBase
{
    public TokenTenantIntegrityTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

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
    /// Decode a JWT without signature validation — we're inspecting claims, not
    /// trusting the issuer. Test code, not security-critical.
    /// </summary>
    private static IDictionary<string, object> DecodeJwt(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var dict = new Dictionary<string, object>();
        foreach (var c in jwt.Claims)
        {
            dict[c.Type] = c.Value;
        }
        return dict;
    }

    private async Task<JsonElement> RedeemAuthCodeAsync(
        string subjectId,
        string clientId,
        string redirectUri,
        string? authCodeTenantId,
        IDictionary<string, string>? additionalClaims = null)
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"intg_{Guid.NewGuid():N}";

        var claims = additionalClaims != null
            ? new Dictionary<string, string>(additionalClaims)
            : new Dictionary<string, string>();

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = clientId,
            SubjectId = subjectId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid", "profile" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = authCodeTenantId!,
            Claims = claims,
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

        var response = await Client.PostAsync("/connect/token", tokenRequest);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"unexpected /token failure: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    /// <summary>
    /// admin@default.local (user.TenantId="default") — token must carry
    /// tenant_id="default", which is the user's home tenant from the DB. We
    /// only exercise auth-code TenantId="default" here because that's the only
    /// value the RequiredPermission check accepts for this user (other values
    /// are covered by the *_DeniedAccess tests in PlatformTenantPermissionTests).
    /// What we're pinning here is: the issued TOKEN's tenant_id is sourced from
    /// the user record, not echoed from any request input.
    /// </summary>
    [Theory]
    [InlineData("default")]
    public async Task AccessToken_TenantUser_AlwaysCarriesUserHomeTenant(string? authCodeTenantId)
    {
        var (_, clientId, redirectUri) = await GetAdminPortalContextAsync();
        var subjectId = await GetUserIdAsync("admin@default.local");
        Assert.False(string.IsNullOrEmpty(subjectId), "admin@default.local must be seeded");

        var tokenResponse = await RedeemAuthCodeAsync(subjectId!, clientId, redirectUri, authCodeTenantId);

        var accessToken = tokenResponse.GetProperty("access_token").GetString()!;
        var atClaims = DecodeJwt(accessToken);

        atClaims.Should().ContainKey("tenant_id",
            "tenant user's access token must carry their tenant claim");
        atClaims["tenant_id"].Should().Be("default",
            $"access token tenant_id must be the user's home tenant regardless of auth-code TenantId={authCodeTenantId ?? "(null)"}");

        var idToken = tokenResponse.GetProperty("id_token").GetString()!;
        var idClaims = DecodeJwt(idToken);
        idClaims.Should().ContainKey("tenant_id");
        idClaims["tenant_id"].Should().Be("default",
            "ID token must carry the same tenant_id as the access token");
    }

    /// <summary>
    /// Even if a hostile/buggy code path stuffs a foreign tenant_id into the auth
    /// code's claims dictionary (e.g. via a forged code or a regression in the
    /// authorize endpoint), the TOKEN's tenant_id claim must still resolve to the
    /// user's actual home tenant — not the value smuggled in via auth code claims.
    ///
    /// Mechanism: AuthorizationCodeGrantHandler merges auth-code claims with
    /// `if (!claims.ContainsKey)`, so the profile-service claim (sourced from
    /// user.TenantId) wins. This test pins that ordering.
    /// </summary>
    [Fact]
    public async Task AccessToken_ForgedTenantClaimInAuthCode_DoesNotPoisonToken()
    {
        var (_, clientId, redirectUri) = await GetAdminPortalContextAsync();
        var subjectId = await GetUserIdAsync("admin@default.local");
        Assert.False(string.IsNullOrEmpty(subjectId), "admin@default.local must be seeded");

        // Smuggle a foreign tenant_id into the auth code's claims bag.
        var hostileClaims = new Dictionary<string, string>
        {
            ["tenant_id"] = "_platform" // user has no membership here
        };

        var tokenResponse = await RedeemAuthCodeAsync(
            subjectId!, clientId, redirectUri,
            authCodeTenantId: "default", // role lookup still hits
            additionalClaims: hostileClaims);

        var accessToken = tokenResponse.GetProperty("access_token").GetString()!;
        var atClaims = DecodeJwt(accessToken);

        atClaims.Should().ContainKey("tenant_id");
        atClaims["tenant_id"].Should().Be("default",
            "auth-code claims must NOT override the user's actual tenant_id in the issued token");
        atClaims["tenant_id"].Should().NotBe("_platform",
            "the smuggled foreign tenant must never appear in the access token");
    }

    /// <summary>
    /// SuperAdmin (user.TenantId = "_platform" — their home tenant) MUST always
    /// have tenant_id="_platform" in their token, regardless of what tenant the
    /// auth code was stamped with. The token's tenant claim is sourced from the
    /// user record only, never from request inputs.
    /// </summary>
    [Theory]
    [InlineData("_platform")]
    [InlineData("default")]
    [InlineData(null)]
    public async Task AccessToken_SuperAdmin_TenantIdIsAlwaysPlatform(string? authCodeTenantId)
    {
        var (subjectId, clientId, redirectUri) = await GetAdminPortalContextAsync();
        Assert.False(string.IsNullOrEmpty(subjectId), "superadmin must be seeded");

        var tokenResponse = await RedeemAuthCodeAsync(subjectId, clientId, redirectUri, authCodeTenantId);

        var accessToken = tokenResponse.GetProperty("access_token").GetString()!;
        var atClaims = DecodeJwt(accessToken);

        atClaims.Should().ContainKey("tenant_id",
            "SuperAdmin's home tenant is _platform — token MUST carry that claim");
        atClaims["tenant_id"].Should().Be(PlatformConstants.TenantId,
            $"SuperAdmin token must always carry their home tenant regardless of auth-code TenantId={authCodeTenantId ?? "(null)"}");
    }

    /// <summary>
    /// Two users with different home tenants going through the same client
    /// must each get a token bound to THEIR home tenant, never the other's.
    /// Catches code paths that might carry tenant context across user identities
    /// (e.g. cached tenant context in a singleton, claims-bag bleed-through).
    /// </summary>
    [Fact]
    public async Task AccessToken_TwoUsersDifferentTenants_NoCrossContamination()
    {
        var (_, clientId, redirectUri) = await GetAdminPortalContextAsync();

        // admin@default.local: home tenant = "default"
        // superadmin@oluso.local: home tenant = "_platform"
        var defaultTenantUserId = await GetUserIdAsync("admin@default.local");
        var platformTenantUserId = await GetUserIdAsync("superadmin@oluso.local");
        Assert.False(string.IsNullOrEmpty(defaultTenantUserId));
        Assert.False(string.IsNullOrEmpty(platformTenantUserId));

        // Issue tokens for both users back-to-back.
        var defaultUserResp = await RedeemAuthCodeAsync(defaultTenantUserId!, clientId, redirectUri, "default");
        var platformUserResp = await RedeemAuthCodeAsync(platformTenantUserId!, clientId, redirectUri, "default");

        var tenantUserToken = DecodeJwt(defaultUserResp.GetProperty("access_token").GetString()!);
        var nullTenantUserToken = DecodeJwt(platformUserResp.GetProperty("access_token").GetString()!);

        // Subjects must match the requesting user.
        tenantUserToken["sub"].Should().Be(defaultTenantUserId);
        nullTenantUserToken["sub"].Should().Be(platformTenantUserId);

        // Tenant user keeps their tenant.
        tenantUserToken.Should().ContainKey("tenant_id");
        tenantUserToken["tenant_id"].Should().Be("default");

        // Platform-tenant user must NOT pick up "default" from the previous request.
        nullTenantUserToken.Should().ContainKey("tenant_id");
        nullTenantUserToken["tenant_id"].Should().Be(PlatformConstants.TenantId,
            "platform-tenant user must keep their own tenant, not inherit from a previous request");
    }
}
