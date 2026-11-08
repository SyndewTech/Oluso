using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Integration.Tests.Fixtures;
using Xunit;
using MtlsOptions = Oluso.Core.Protocols.MtlsOptions;
using MtlsCertificateSource = Oluso.Core.Protocols.MtlsCertificateSource;
using MtlsProxyCertificateFormat = Oluso.Core.Protocols.MtlsProxyCertificateFormat;
using OidcClient = Oluso.Core.Domain.Entities.Client;

namespace Oluso.Integration.Tests;

/// <summary>
/// Integration tests for mTLS (Mutual TLS) client authentication per RFC 8705.
///
/// Since WebApplicationFactory uses in-process HTTP (no real TLS), these tests
/// exercise the proxy-header code path: MtlsOptions.CertificateSource = Header
/// with a custom header name, sending the client certificate as Base64 DER.
/// </summary>
public class MtlsAuthenticationTests : IntegrationTestBase
{
    private const string MtlsClientId = "mtls-client";
    private const string TestCertHeader = "X-Test-Client-Cert";

    // One cert for the whole class. xUnit builds a fresh test-class instance per
    // method, so generating a new cert in InitializeAsync left whichever token
    // test ran second sending a cert that no longer matched the client seeded by
    // the first test (it's seeded once via the "if (existing != null) return"
    // guard) — yielding a thumbprint mismatch / invalid_client (400). A single
    // shared cert keeps the seeded client and every request in agreement.
    private static readonly X509Certificate2 _testCert = CreateTestCert();

    private static X509Certificate2 CreateTestCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=mtls-test-client",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));
    }

    public MtlsAuthenticationTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
    }

    public override async Task InitializeAsync()
    {
        // Configure mTLS to read certs from a custom header (proxy mode)
        var mtlsOptions = Factory.Services.GetRequiredService<MtlsOptions>();
        mtlsOptions.Enabled = true;
        mtlsOptions.CertificateSource = MtlsCertificateSource.Header;
        mtlsOptions.ProxyCertificateHeaderName = TestCertHeader;
        mtlsOptions.ProxyCertificateFormat = MtlsProxyCertificateFormat.Base64Der;
        mtlsOptions.AllowCertificateBoundAccessTokens = true;

        // Seed the mTLS client in the _platform tenant
        await WithDbContextAsync(async db =>
        {
            var existing = await db.Clients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ClientId == MtlsClientId);
            if (existing != null)
                return;

            var client = new OidcClient
            {
                ClientId = MtlsClientId,
                ClientName = "mTLS Test Client",
                TenantId = PlatformConstants.TenantId,
                Enabled = true,
                RequireClientSecret = true,
                RequirePkce = false,
                AllowOfflineAccess = false,
                TokenEndpointAuthMethod = "self_signed_tls_client_auth",
                AllowedGrantTypes = new List<ClientGrantType>
                {
                    new() { GrantType = "client_credentials", TenantId = PlatformConstants.TenantId }
                },
                AllowedScopes = new List<ClientScope>
                {
                    new() { Scope = "openid", TenantId = PlatformConstants.TenantId }
                },
                ClientSecrets = new List<ClientSecret>
                {
                    new()
                    {
                        Type = "X509CertificateBase64",
                        Value = Convert.ToBase64String(_testCert.RawData),
                        TenantId = PlatformConstants.TenantId
                    }
                }
            };

            db.Clients.Add(client);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Token_WithValidMtlsCertificate_ReturnsAccessToken()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = MtlsClientId
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = request
        };
        httpRequest.Headers.Add(TestCertHeader, Convert.ToBase64String(_testCert.RawData));
        httpRequest.Headers.Add("X-Tenant-Id", "default");

        // Act
        var response = await Factory.CreateClient().SendAsync(httpRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        tokenResponse.Should().NotBeNull();

        var root = tokenResponse!.RootElement;
        root.TryGetProperty("access_token", out var accessToken).Should().BeTrue();
        accessToken.GetString().Should().NotBeNullOrEmpty();

        root.TryGetProperty("token_type", out var tokenType).Should().BeTrue();
        tokenType.GetString().Should().BeEquivalentTo("Bearer");

        root.TryGetProperty("expires_in", out var expiresIn).Should().BeTrue();
        expiresIn.GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Token_WithWrongMtlsCertificate_ReturnsError()
    {
        // Arrange - generate a completely different certificate
        using var wrongRsa = RSA.Create(2048);
        var wrongReq = new CertificateRequest(
            "CN=wrong-client",
            wrongRsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var wrongCert = wrongReq.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));

        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = MtlsClientId
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = request
        };
        httpRequest.Headers.Add(TestCertHeader, Convert.ToBase64String(wrongCert.RawData));
        httpRequest.Headers.Add("X-Tenant-Id", "default");

        // Act
        var response = await Factory.CreateClient().SendAsync(httpRequest);

        // Assert - should fail because cert thumbprint doesn't match
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);

        var error = await response.Content.ReadFromJsonAsync<JsonDocument>();
        error!.RootElement.GetProperty("error").GetString().Should().Be("invalid_client");
    }

    [Fact]
    public async Task Token_WithMtlsCertificate_IncludesCnfClaim()
    {
        // Arrange
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = MtlsClientId
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = request
        };
        httpRequest.Headers.Add(TestCertHeader, Convert.ToBase64String(_testCert.RawData));
        httpRequest.Headers.Add("X-Tenant-Id", "default");

        // Act
        var response = await Factory.CreateClient().SendAsync(httpRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var accessTokenString = tokenResponse!.RootElement.GetProperty("access_token").GetString()!;

        // Parse the JWT (it's a three-part dot-separated string)
        var parts = accessTokenString.Split('.');
        parts.Should().HaveCountGreaterThanOrEqualTo(3, "access token should be a JWT");

        // Decode the payload (part[1]) from Base64Url
        var payloadJson = DecodeBase64Url(parts[1]);
        var payload = JsonDocument.Parse(payloadJson);

        // Assert - cnf claim with x5t#S256 matching the test cert
        payload.RootElement.TryGetProperty("cnf", out var cnf).Should().BeTrue(
            "mTLS-bound access token must contain a cnf claim");

        cnf.TryGetProperty("x5t#S256", out var thumbprint).Should().BeTrue(
            "cnf claim must contain x5t#S256 member");

        // Compute expected thumbprint (base64url-encoded SHA-256 of DER cert)
        var expectedThumbprint = ComputeBase64UrlSha256Thumbprint(_testCert);
        thumbprint.GetString().Should().Be(expectedThumbprint);
    }

    [Fact]
    public async Task Discovery_IncludesMtlsMethods()
    {
        // Act
        var response = await Client.GetAsync("/.well-known/openid-configuration");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var discovery = await response.Content.ReadFromJsonAsync<JsonDocument>();
        discovery.Should().NotBeNull();
        var root = discovery!.RootElement;

        // token_endpoint_auth_methods_supported should include mTLS methods
        root.TryGetProperty("token_endpoint_auth_methods_supported", out var authMethods).Should().BeTrue();
        var methods = authMethods.EnumerateArray().Select(e => e.GetString()).ToList();
        methods.Should().Contain("tls_client_auth");
        methods.Should().Contain("self_signed_tls_client_auth");

        // tls_client_certificate_bound_access_tokens should be true
        root.TryGetProperty("tls_client_certificate_bound_access_tokens", out var boundTokens).Should().BeTrue();
        boundTokens.GetBoolean().Should().BeTrue();
    }

    private static string ComputeBase64UrlSha256Thumbprint(X509Certificate2 cert)
    {
        var hash = SHA256.HashData(cert.RawData);
        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string DecodeBase64Url(string base64Url)
    {
        var padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        var bytes = Convert.FromBase64String(padded);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    [Fact]
    public async Task A_certificate_bound_token_is_refused_without_its_certificate()
    {
        var token = await CertificateBoundTokenAsync();

        var response = await CallUserInfoAsync(token, certificate: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("invalid_token").And.Contain("bound to a client certificate");
    }

    [Fact]
    public async Task A_certificate_bound_token_is_refused_with_another_certificate()
    {
        var token = await CertificateBoundTokenAsync();
        using var otherRsa = RSA.Create(2048);
        using var other = new CertificateRequest("CN=someone-else", otherRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));

        var response = await CallUserInfoAsync(token, other);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("not the one the access token is bound to");
    }

    [Fact]
    public async Task A_certificate_bound_token_is_authenticated_with_its_certificate()
    {
        var token = await CertificateBoundTokenAsync();

        var response = await CallUserInfoAsync(token, _testCert);

        // Past authentication: userinfo itself answers, refusing a client token that has no user.
        response.Headers.WwwAuthenticate.ToString().Should().NotContain("certificate");
        (await response.Content.ReadAsStringAsync()).Should().Contain("subject claim");
    }

    private async Task<string> CertificateBoundTokenAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = MtlsClientId,
            }),
        };
        request.Headers.Add(TestCertHeader, Convert.ToBase64String(_testCert.RawData));
        // Requested through a tenant's routing: the token is still the platform client's, signed by the platform.
        request.Headers.Add("X-Tenant-Id", "default");
        var response = await Factory.CreateClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var token = (await response.Content.ReadFromJsonAsync<JsonDocument>())!.RootElement.GetProperty("access_token").GetString()!;
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Should().Contain(c => c.Type == "cnf" && c.Value.Contains("x5t#S256"));
        return token;
    }

    private async Task<HttpResponseMessage> CallUserInfoAsync(string token, X509Certificate2? certificate)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (certificate != null) request.Headers.Add(TestCertHeader, Convert.ToBase64String(certificate.RawData));
        request.Headers.Add("X-Tenant-Id", "default");
        return await Factory.CreateClient().SendAsync(request);
    }
}
