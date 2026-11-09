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
using Oluso.EntityFramework;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Reproduces the "User does not have the required permission to access this application"
/// failure that happens on interactive refresh against the admin-portal client on the
/// platform tenant. Verifies the AuthorizationCodeGrantHandler RequiredPermission check
/// at AuthorizationCodeGrantHandler.cs:166-183 against the role-fallback path at
/// DefaultProfileService.cs:112 (tenant-specific lookup, then null fallback).
///
/// Setup mirrors production seeding:
///   - admin-portal client lives on PlatformConstants.TenantId ("_platform"),
///     RequiredPermission = "admin.access".
///   - SuperAdmin role has TenantId = null and a "permission" claim of "admin.access".
///   - superadmin@oluso.local user has TenantId = null and is in SuperAdmin.
///
/// If the auth code's TenantId is "_platform" (what TenantResolutionMiddleware would
/// assign on platform-host requests), GetUserPermissionsAsync should:
///   1. Look up role by ("SuperAdmin", "_platform") — miss
///   2. Fall back to ("SuperAdmin", null) — hit
///   3. Return ["admin.access", ...]
///   4. Permission check passes, /token returns 200.
///
/// A failure here would mean the role-fallback path is broken — confirming the bug
/// without needing to reproduce the full browser/cookie flow.
/// </summary>
public class PlatformTenantPermissionTests : IntegrationTestBase
{
    public PlatformTenantPermissionTests(OlusoWebApplicationFactory factory) : base(factory)
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

    [Fact]
    public async Task Token_AdminPortal_PlatformTenantAuthCode_ReturnsTokens()
    {
        // Arrange — auth code with TenantId = "_platform" matches what
        // TenantResolutionMiddleware assigns on platform-host requests.
        var (subjectId, clientId, redirectUri) = await GetAdminPortalContextAsync();

        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(clientId))
        {
            // Seeded data missing — surface as test failure rather than silent skip.
            Assert.Fail($"Required seed data missing. subjectId={subjectId ?? "(null)"}, clientId={clientId ?? "(null)"}");
        }

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"plat_test_{Guid.NewGuid():N}";

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = clientId,
            SubjectId = subjectId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid", "profile" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = PlatformConstants.TenantId,
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddMinutes(5),
            IsConsumed = false
        });

        // Act
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier
        });

        var response = await Client.PostAsync("/connect/token", tokenRequest);

        // Assert — if this fails with 400/access_denied, the bug is reproduced:
        // the role-fallback path at DefaultProfileService.cs:112 isn't matching
        // SuperAdmin's null-tenant role against the auth code's "_platform" tenant.
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"expected token success but got {(int)response.StatusCode}: {body}");

        var tokenResponse = JsonDocument.Parse(body).RootElement;
        tokenResponse.TryGetProperty("access_token", out _).Should().BeTrue();
    }

    /// <summary>
    /// Security invariant + regression guard.
    ///
    /// History: Login.cshtml.cs used to stamp the cookie's tenant_id from the URL host
    /// (e.g. "_platform") instead of the user's home tenant ("default"). Initial /token
    /// worked because HandleStandaloneCallbackAsync overrides claims["tenant_id"] with
    /// userInfo.TenantId before issuing the auth code. Silent renew used the raw cookie
    /// path (HandleAlreadyAuthenticatedAsync) which has no override, so auth codes got
    /// stamped "_platform" and the RequiredPermission check failed.
    ///
    /// The fix lives in Login.cshtml.cs.SignInUserAsync. This test pins down the
    /// downstream invariant: even if a code somehow reaches /token bearing a tenant
    /// the user has no role in (regression in cookie issuance, forged code, etc.),
    /// /connect/token MUST deny it. Same shape as the foreign-tenant guard — the
    /// platform tenant is just a high-value example of "a tenant the user isn't in".
    /// </summary>
    [Fact]
    public async Task Token_AdminPortal_TenantUser_PlatformAuthCode_DeniedAccess()
    {
        var (_, clientId, redirectUri) = await GetAdminPortalContextAsync();

        string? tenantUserId = null;
        await WithDbContextAsync(async db =>
        {
            var u = await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Email == "admin@default.local");
            tenantUserId = u?.Id;
        });

        Assert.False(string.IsNullOrEmpty(tenantUserId), "admin@default.local must be seeded");

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"tenant_user_plat_{Guid.NewGuid():N}";

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = clientId,
            SubjectId = tenantUserId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid", "profile" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = PlatformConstants.TenantId, // user has no role under "_platform"
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
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"a tenant user must NOT obtain tokens via an auth code stamped with a foreign tenant " +
            $"(here: the platform tenant). Got {(int)response.StatusCode}: {body}");
        body.Should().Contain("access_denied");
    }

    /// <summary>
    /// Triangulates the bug: SAME tenant Admin user that fails with auth code
    /// TenantId="_platform" succeeds when the auth code carries the user's HOME
    /// tenant ("default"). Proves the fix target — cookie tenant_id must be
    /// derived from user.TenantId, not the URL host.
    /// </summary>
    [Fact]
    public async Task Token_AdminPortal_TenantUser_HomeTenantAuthCode_Works()
    {
        var (_, clientId, redirectUri) = await GetAdminPortalContextAsync();

        string? tenantUserId = null;
        await WithDbContextAsync(async db =>
        {
            var u = await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Email == "admin@default.local");
            tenantUserId = u?.Id;
        });

        if (string.IsNullOrEmpty(tenantUserId) || string.IsNullOrEmpty(clientId))
        {
            Assert.Fail($"Required seed data missing. tenantUserId={tenantUserId ?? "(null)"}, clientId={clientId ?? "(null)"}");
        }

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"tenant_user_home_{Guid.NewGuid():N}";

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = clientId,
            SubjectId = tenantUserId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid", "profile" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = "default", // user's home tenant — what the cookie SHOULD carry post-fix
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
            $"tenant Admin user with auth code stamped to their home tenant should succeed, got {(int)response.StatusCode}: {body}");
    }

    /// <summary>
    /// Security guard: a user from tenant A whose role only exists in tenant A
    /// must NOT be able to satisfy the RequiredPermission check using an auth code
    /// stamped with tenant B (where they have no role assignment).
    ///
    /// This protects against a malicious or buggy code path that creates an auth
    /// code with the wrong tenant — e.g. a forged or replayed code bearing a
    /// tenant the user has no membership in. Even if such a code reached /token,
    /// the role lookup must fail.
    ///
    /// Concretely: admin@default.local has Admin role at TenantId="default" only.
    /// An auth code with TenantId="_platform" gives them no permissions because
    /// their role doesn't exist under "_platform" and SuperAdmin (the only
    /// null-tenant role with admin.access) is not assigned to them.
    /// </summary>
    [Fact]
    public async Task Token_AdminPortal_TenantUser_ForeignTenantAuthCode_DeniedAccess()
    {
        var (_, clientId, redirectUri) = await GetAdminPortalContextAsync();

        string? tenantUserId = null;
        await WithDbContextAsync(async db =>
        {
            var u = await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Email == "admin@default.local");
            tenantUserId = u?.Id;
        });

        Assert.False(string.IsNullOrEmpty(tenantUserId), "admin@default.local must be seeded");

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"foreign_tenant_{Guid.NewGuid():N}";

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = clientId,
            SubjectId = tenantUserId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid", "profile" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = "some-other-tenant", // user has no role here
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
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"tenant user must NOT obtain tokens via an auth code bearing a tenant they have no role in. Got {(int)response.StatusCode}: {body}");
        body.Should().Contain("access_denied");
    }

    [Fact]
    public async Task Token_AdminPortal_NullTenantAuthCode_ReturnsTokens()
    {
        // Arrange — control case: TenantId = null on the auth code.
        // If platform-tenant test fails but this passes, the issue is specifically
        // about how "_platform" interacts with role lookup.
        var (subjectId, clientId, redirectUri) = await GetAdminPortalContextAsync();

        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(clientId))
        {
            Assert.Fail($"Required seed data missing. subjectId={subjectId ?? "(null)"}, clientId={clientId ?? "(null)"}");
        }

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authCode = $"null_test_{Guid.NewGuid():N}";

        await StoreAuthorizationCodeAsync(new AuthorizationCode
        {
            Code = authCode,
            ClientId = clientId,
            SubjectId = subjectId,
            RedirectUri = redirectUri,
            Scopes = new List<string> { "openid", "profile" },
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "S256",
            TenantId = null,
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
            $"expected token success but got {(int)response.StatusCode}: {body}");
    }
}
