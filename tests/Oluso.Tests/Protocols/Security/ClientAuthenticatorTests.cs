using Oluso.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Protocols.Mtls;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Security;

/// <summary>
/// Security tests for client authentication mechanisms.
/// Tests OAuth 2.0 client authentication methods:
/// - client_secret_basic
/// - client_secret_post
/// - client_secret_jwt
/// - private_key_jwt
/// </summary>
public class ClientAuthenticatorTests : IDisposable
{
    private readonly Mock<IClientStore> _clientStore;
    private readonly Mock<IIssuerResolver> _issuerResolver;
    private readonly Mock<IDistributedCache> _cache;
    private readonly ClientAuthenticator _authenticator;
    private readonly ClientSecretProtector _secrets = new(new EphemeralDataProtectionProvider(), NullLogger<ClientSecretProtector>.Instance);
    private readonly ECDsa _clientKey;
    private readonly string _testClientId = "test-client";
    private readonly string _testClientSecret = "super-secret-key-12345";
    private readonly string _hashedSecret;

    public ClientAuthenticatorTests()
    {
        _clientStore = new Mock<IClientStore>();
        _issuerResolver = new Mock<IIssuerResolver>();
        _issuerResolver.Setup(x => x.GetIssuerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://auth.example.com");

        _cache = new Mock<IDistributedCache>();
        var cacheStore = new Dictionary<string, byte[]>();
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => cacheStore.TryGetValue(key, out var v) ? v : null);
        _cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((key, value, _, _) => cacheStore[key] = value)
            .Returns(Task.CompletedTask);

        var endpointConfig = Options.Create(new OidcEndpointConfiguration
        {
            TokenEndpoint = "/connect/token"
        });

        var certExtractor = new Mock<IClientCertificateExtractor>();
        var mtlsOptions = new MtlsOptions { Enabled = false };

        _authenticator = new ClientAuthenticator(
            _clientStore.Object,
            _issuerResolver.Object,
            endpointConfig,
            _cache.Object,
            certExtractor.Object,
            mtlsOptions,
            _secrets,
            NullLogger<ClientAuthenticator>.Instance);

        // Generate client signing key for JWT tests
        _clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Pre-compute hashed secret
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(_testClientSecret);
        var hash = sha256.ComputeHash(bytes);
        _hashedSecret = Convert.ToBase64String(hash);
    }

    public void Dispose()
    {
        _clientKey.Dispose();
    }

    #region Basic Authentication Tests

    [Fact]
    public async Task AuthenticateAsync_WithValidBasicAuth_ReturnsSuccess()
    {
        SetupConfidentialClient();
        var request = CreateRequestWithBasicAuth(_testClientId, _testClientSecret);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.ClientSecretBasic);
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WithWrongSecret_ReturnsFail()
    {
        SetupConfidentialClient();
        var request = CreateRequestWithBasicAuth(_testClientId, "wrong-secret");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().BeNull();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
    }

    [Fact]
    public async Task AuthenticateAsync_WithMalformedBasicAuth_IgnoresAndTriesNextMethod()
    {
        SetupPublicClient();
        var request = CreateRequest();
        request.Headers.Authorization = "Basic not-valid-base64!!!";
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody($"client_id={_testClientId}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        // Should fall through to public client authentication
        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.None);
    }

    [Fact]
    public async Task AuthenticateAsync_WithBasicAuthMissingColon_IgnoresBasicAuth()
    {
        SetupPublicClient();
        var request = CreateRequest();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("nocolon"));
        request.Headers.Authorization = $"Basic {encoded}";
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody($"client_id={_testClientId}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WithBasicAuthEmptyPassword_AuthenticatesPublicClient()
    {
        // RFC 6749 §2.3.1: IdentityModel OidcClient's default AuthorizationHeader style
        // sends Basic base64("client_id:") (empty password) for public clients, with the
        // client_id only in the header — not the body.
        SetupPublicClient();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{Uri.EscapeDataString(_testClientId)}:"));
        var request = CreateRequest();
        request.Headers.Authorization = $"Basic {encoded}";

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.None);
    }

    [Fact]
    public async Task AuthenticateAsync_WithBasicAuthEmptyPassword_ConfidentialClient_RequiresCredentials()
    {
        SetupConfidentialClient();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{Uri.EscapeDataString(_testClientId)}:"));
        var request = CreateRequest();
        request.Headers.Authorization = $"Basic {encoded}";

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().BeNull();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
    }

    [Fact]
    public async Task AuthenticateAsync_WithBasicAuthEncodedSpecialChars_DecodesCorrectly()
    {
        var clientId = "client:with:colons";
        var secret = "secret+with=special&chars";

        SetupConfidentialClient(clientId, secret);

        // Per RFC 6749, client_id and secret are URL-encoded before base64
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(secret)}"));
        var request = CreateRequest();
        request.Headers.Authorization = $"Basic {encoded}";

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
    }

    #endregion

    #region Post Authentication Tests

    [Fact]
    public async Task AuthenticateAsync_WithValidPostAuth_ReturnsSuccess()
    {
        SetupConfidentialClient();
        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody($"client_id={_testClientId}&client_secret={_testClientSecret}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.ClientSecretPost);
    }

    [Fact]
    public async Task AuthenticateAsync_WithPostAuthNoSecret_TriesPublicClient()
    {
        SetupPublicClient();
        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody($"client_id={_testClientId}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.None);
    }

    #endregion

    #region JWT Authentication Tests

    [Fact]
    public async Task AuthenticateAsync_WithValidPrivateKeyJwt_ReturnsSuccess()
    {
        var jwk = GetPublicJwk();
        SetupConfidentialClientWithJwk(jwk);

        var assertion = CreateClientAssertion(_clientKey, _testClientId);
        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.PrivateKeyJwt);
    }

    [Fact]
    public async Task AuthenticateAsync_WithUnsupportedAssertionType_ReturnsFail()
    {
        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            "client_assertion_type=urn:ietf:params:oauth:client-assertion-type:unsupported" +
            "&client_assertion=some.jwt.here");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("client_assertion_type");
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidJwtFormat_ReturnsFail()
    {
        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            "&client_assertion=not.a.valid.jwt.at.all");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
    }

    [Fact]
    public async Task AuthenticateAsync_WithMissingSubAndIss_ReturnsFail()
    {
        // Create JWT without sub and iss claims
        var key = new ECDsaSecurityKey(_clientKey);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
        var handler = new JwtSecurityTokenHandler();

        var jwt = handler.CreateJwtSecurityToken(
            issuer: null,
            subject: null,
            audience: "https://auth.example.com/connect/token",
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        var assertion = handler.WriteToken(jwt);

        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        (result.ErrorDescription?.Contains("sub") == true || result.ErrorDescription?.Contains("iss") == true)
            .Should().BeTrue("error should mention sub or iss");
    }

    [Fact]
    public async Task AuthenticateAsync_WithMismatchedSubAndIss_ReturnsFail()
    {
        var jwk = GetPublicJwk();
        SetupConfidentialClientWithJwk(jwk);

        // Create JWT where sub != iss (violates RFC 7523)
        var key = new ECDsaSecurityKey(_clientKey);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
        var handler = new JwtSecurityTokenHandler();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "different-issuer", // Different from sub
            Audience = "https://auth.example.com/connect/token",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = credentials,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", _testClientId),
                new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString())
            })
        };

        var jwt = handler.CreateToken(descriptor);

        var assertion = handler.WriteToken(jwt);

        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("sub").And.Contain("iss").And.Contain("match");
    }

    [Fact]
    public async Task AuthenticateAsync_WithWrongAudience_ReturnsFail()
    {
        var jwk = GetPublicJwk();
        SetupConfidentialClientWithJwk(jwk);

        var key = new ECDsaSecurityKey(_clientKey);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
        var handler = new JwtSecurityTokenHandler();

        var jwt = handler.CreateJwtSecurityToken(
            issuer: _testClientId,
            audience: "https://wrong-server.com/token", // Wrong audience!
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        var assertion = handler.WriteToken(jwt);

        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("aud");
    }

    [Fact]
    public async Task AuthenticateAsync_WithExpiredJwt_ReturnsFail()
    {
        var jwk = GetPublicJwk();
        SetupConfidentialClientWithJwk(jwk);

        var key = new ECDsaSecurityKey(_clientKey);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
        var handler = new JwtSecurityTokenHandler();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _testClientId,
            Audience = "https://auth.example.com/connect/token",
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = DateTime.UtcNow.AddMinutes(-5), // Expired!
            SigningCredentials = credentials,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", _testClientId)
            })
        };

        var jwt = handler.CreateToken(descriptor);
        var assertion = handler.WriteToken(jwt);

        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("expired");
    }

    [Fact]
    public async Task AuthenticateAsync_WithReplayedJwt_ReturnsFail()
    {
        var jwk = GetPublicJwk();
        SetupConfidentialClientWithJwk(jwk);

        var jti = Guid.NewGuid().ToString();
        var assertion = CreateClientAssertion(_clientKey, _testClientId, jti);

        var request1 = CreateRequest();
        request1.ContentType = "application/x-www-form-urlencoded";
        request1.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        // First request should succeed
        var result1 = await _authenticator.AuthenticateAsync(request1, CancellationToken.None);
        result1.Client.Should().NotBeNull();

        // Same assertion replayed - should fail
        var request2 = CreateRequest();
        request2.ContentType = "application/x-www-form-urlencoded";
        request2.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result2 = await _authenticator.AuthenticateAsync(request2, CancellationToken.None);

        result2.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result2.ErrorDescription.Should().Contain("replay");
    }

    [Fact]
    public async Task AuthenticateAsync_WithWrongSigningKey_ReturnsFail()
    {
        var jwk = GetPublicJwk();
        SetupConfidentialClientWithJwk(jwk);

        // Create assertion with a DIFFERENT key than what's registered
        using var differentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var assertion = CreateClientAssertion(differentKey, _testClientId);

        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("validation failed");
    }

    /// <summary>
    /// RFC 7523 Section 3: nbf claim - assertion MUST NOT be used before nbf time
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_WithFutureNbf_ReturnsFail()
    {
        var jwk = GetPublicJwk();
        SetupConfidentialClientWithJwk(jwk);

        var key = new ECDsaSecurityKey(_clientKey);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
        var handler = new JwtSecurityTokenHandler();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _testClientId,
            Audience = "https://auth.example.com/connect/token",
            NotBefore = DateTime.UtcNow.AddMinutes(10), // Not valid yet!
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = credentials,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", _testClientId),
                new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString())
            })
        };

        var jwt = handler.CreateToken(descriptor);
        var assertion = handler.WriteToken(jwt);

        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        // Should indicate the token is not yet valid
        (result.ErrorDescription?.Contains("not yet valid") == true ||
         result.ErrorDescription?.Contains("nbf") == true ||
         result.ErrorDescription?.Contains("NotBefore") == true ||
         result.ErrorDescription?.Contains("validation failed") == true)
            .Should().BeTrue("error should indicate token is not yet valid");
    }

    /// <summary>
    /// RFC 7523 Section 3: iat claim - authorization servers MAY reject assertions with iat too far in the past
    /// Best practice: reject assertions older than a reasonable time window (e.g., 5 minutes)
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_WithStaleIat_ShouldRejectOrWarn()
    {
        var jwk = GetPublicJwk();
        SetupConfidentialClientWithJwk(jwk);

        var key = new ECDsaSecurityKey(_clientKey);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
        var handler = new JwtSecurityTokenHandler();

        // Create assertion with iat from 1 hour ago (stale)
        var staleIssuedAt = DateTime.UtcNow.AddHours(-1);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _testClientId,
            Audience = "https://auth.example.com/connect/token",
            IssuedAt = staleIssuedAt,
            NotBefore = staleIssuedAt,
            Expires = DateTime.UtcNow.AddMinutes(5), // Still "valid" per exp
            SigningCredentials = credentials,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", _testClientId),
                new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString())
            })
        };

        var jwt = handler.CreateToken(descriptor);
        var assertion = handler.WriteToken(jwt);

        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        // Per RFC 7523, servers MAY reject stale assertions
        // This test documents expected behavior - if implementation allows stale tokens,
        // consider adding iat age validation for security
        // For now, we just document the behavior
        if (result.Error != null)
        {
            // If rejected, it should mention iat, stale, or time-related issue
            (result.ErrorDescription?.Contains("iat") == true ||
             result.ErrorDescription?.Contains("stale") == true ||
             result.ErrorDescription?.Contains("issued") == true ||
             result.ErrorDescription?.Contains("old") == true)
                .Should().BeTrue("rejection should mention time-related issue");
        }
        // Note: If this assertion is allowed, consider adding iat age validation
        // as a security enhancement per RFC 7523 Section 3
    }

    [Fact]
    public async Task AuthenticateAsync_WithClientSecretJwt_ReturnsSuccess()
    {
        // client_secret_jwt keys HMAC with the secret itself (OIDC Core 10.1), so the client's secret is stored
        // recoverable (encrypted), and the JWT is signed with its exact UTF-8 octets.
        var secret = ClientSecretProtector.GenerateSecret(hmac: true);
        var client = HmacClient(_secrets.ToStoredValue(secret, recoverable: true));

        var result = await AuthenticateWithAssertionAsync(SignHmacAssertion(secret, SecurityAlgorithms.HmacSha256));

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.ClientSecretJwt);
    }

    [Fact]
    public async Task AuthenticateAsync_WithClientSecretJwt_AndOnlyAHashedSecret_Fails()
    {
        // A one-way hash is never an HMAC key: a JWT keyed with the stored hash (anyone who read it) must fail,
        // as must one keyed with the secret, which the server cannot recover.
        var secret = ClientSecretProtector.GenerateSecret(hmac: true);
        var stored = _secrets.ToStoredValue(secret, recoverable: false);
        HmacClient(stored);

        (await AuthenticateWithAssertionAsync(SignHmacAssertion(stored.PadRight(32, 'x'), SecurityAlgorithms.HmacSha256)))
            .Client.Should().BeNull();
        (await AuthenticateWithAssertionAsync(SignHmacAssertion(secret, SecurityAlgorithms.HmacSha256)))
            .Client.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WithClientSecretJwt_SecretShorterThanTheHash_Fails()
    {
        // RFC 7518 section 3.2: an HMAC key at least as long as the hash output. A 44-byte secret is enough for
        // HS256, not for HS512; the key is never padded to make up the difference.
        var secret = ClientSecretProtector.GenerateSecret(hmac: false);
        HmacClient(_secrets.ToStoredValue(secret, recoverable: true));

        (await AuthenticateWithAssertionAsync(SignHmacAssertion(secret, SecurityAlgorithms.HmacSha256)))
            .Client.Should().NotBeNull();
        (await AuthenticateWithAssertionAsync(SignHmacAssertion(secret + secret, SecurityAlgorithms.HmacSha512)))
            .Client.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_PresentingTheStoredHashAsTheSecret_Fails()
    {
        // The stored value is never itself accepted as the secret: a leaked hash is not a credential.
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = true,
            ClientSecrets = new List<ClientSecret> { new() { Type = "SharedSecret", Value = _hashedSecret } }
        };
        _clientStore.Setup(x => x.FindClientByIdAsync(_testClientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);

        var result = await _authenticator.AuthenticateAsync(CreateRequestWithBasicAuth(_testClientId, _hashedSecret), CancellationToken.None);

        result.Client.Should().BeNull();
        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
    }

    private Client HmacClient(string storedSecret)
    {
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = true,
            TokenEndpointAuthMethod = "client_secret_jwt",
            ClientSecrets = new List<ClientSecret> { new() { Type = "SharedSecret", Value = storedSecret } }
        };
        _clientStore.Setup(x => x.FindClientByIdAsync(_testClientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        return client;
    }

    private string SignHmacAssertion(string key, string algorithm)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.CreateJwtSecurityToken(
            issuer: _testClientId,
            audience: "https://auth.example.com/connect/token",
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);
        jwt.Payload["sub"] = _testClientId;
        jwt.Payload["jti"] = Guid.NewGuid().ToString("N");
        return handler.WriteToken(jwt);
    }

    private Task<ClientAuthenticationResult> AuthenticateWithAssertionAsync(string assertion)
    {
        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody(
            $"client_assertion_type={Uri.EscapeDataString(ClientAssertionTypes.JwtBearer)}" +
            $"&client_assertion={Uri.EscapeDataString(assertion)}");
        return _authenticator.AuthenticateAsync(request, CancellationToken.None);
    }

    #endregion

    #region Public Client Tests

    [Fact]
    public async Task AuthenticateAsync_PublicClient_NoSecretRequired_Succeeds()
    {
        SetupPublicClient();
        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody($"client_id={_testClientId}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.None);
    }

    [Fact]
    public async Task AuthenticateAsync_ConfidentialClient_WithNoSecret_ReturnsFail()
    {
        SetupConfidentialClient();
        var request = CreateRequest();
        request.ContentType = "application/x-www-form-urlencoded";
        request.Body = CreateFormBody($"client_id={_testClientId}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("credentials required");
    }

    [Fact]
    public async Task AuthenticateAsync_ClientIdFromQuery_Works()
    {
        SetupPublicClient();
        var request = CreateRequest();
        request.QueryString = new QueryString($"?client_id={_testClientId}");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Client.Should().NotBeNull();
    }

    #endregion

    #region Client Status Tests

    [Fact]
    public async Task AuthenticateAsync_DisabledClient_ReturnsFail()
    {
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = false,
            RequireClientSecret = true,
            ClientSecrets = new List<ClientSecret>
            {
                new() { Type = "SharedSecret", Value = _hashedSecret }
            }
        };
        _clientStore.Setup(x => x.FindClientByIdAsync(_testClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var request = CreateRequestWithBasicAuth(_testClientId, _testClientSecret);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("disabled");
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownClient_ReturnsFail()
    {
        _clientStore.Setup(x => x.FindClientByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var request = CreateRequestWithBasicAuth("unknown-client", "secret");

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("Unknown client");
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredSecret_ReturnsFail()
    {
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = true,
            ClientSecrets = new List<ClientSecret>
            {
                new()
                {
                    Type = "SharedSecret",
                    Value = _hashedSecret,
                    Expiration = DateTime.UtcNow.AddDays(-1) // Expired!
                }
            }
        };
        _clientStore.Setup(x => x.FindClientByIdAsync(_testClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var request = CreateRequestWithBasicAuth(_testClientId, _testClientSecret);

        var result = await _authenticator.AuthenticateAsync(request, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
    }

    #endregion

    #region Simple AuthenticateAsync Overload Tests

    [Fact]
    public async Task AuthenticateAsync_Simple_WithEmptyClientId_ReturnsFail()
    {
        var result = await _authenticator.AuthenticateAsync("", null, CancellationToken.None);

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        result.ErrorDescription.Should().Contain("client_id");
    }

    [Fact]
    public async Task AuthenticateAsync_Simple_WithValidSecret_ReturnsSuccess()
    {
        SetupConfidentialClient();

        var result = await _authenticator.AuthenticateAsync(_testClientId, _testClientSecret, CancellationToken.None);

        result.Client.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_Simple_PublicClientNoSecret_ReturnsSuccess()
    {
        SetupPublicClient();

        var result = await _authenticator.AuthenticateAsync(_testClientId, null, CancellationToken.None);

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.None);
    }

    #endregion

    #region Helpers

    private void SetupConfidentialClient(string? clientId = null, string? secret = null)
    {
        clientId ??= _testClientId;
        secret ??= _testClientSecret;

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(secret);
        var hash = sha256.ComputeHash(bytes);
        var hashedSecret = Convert.ToBase64String(hash);

        var client = new Client
        {
            ClientId = clientId,
            Enabled = true,
            RequireClientSecret = true,
            ClientSecrets = new List<ClientSecret>
            {
                new() { Type = "SharedSecret", Value = hashedSecret }
            }
        };
        _clientStore.Setup(x => x.FindClientByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    private void SetupConfidentialClientWithJwk(string jwk)
    {
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = true,
            ClientSecrets = new List<ClientSecret>
            {
                new() { Type = "JsonWebKey", Value = jwk }
            }
        };
        _clientStore.Setup(x => x.FindClientByIdAsync(_testClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    private void SetupPublicClient()
    {
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = false,
            ClientSecrets = new List<ClientSecret>()
        };
        _clientStore.Setup(x => x.FindClientByIdAsync(_testClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

    private string GetPublicJwk()
    {
        var ecParams = _clientKey.ExportParameters(false);
        var jwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            Crv = "P-256",
            X = Base64UrlEncoder.Encode(ecParams.Q.X!),
            Y = Base64UrlEncoder.Encode(ecParams.Q.Y!)
        };
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            kty = jwk.Kty,
            crv = jwk.Crv,
            x = jwk.X,
            y = jwk.Y
        });
    }

    private string CreateClientAssertion(ECDsa key, string clientId, string? jti = null)
    {
        var securityKey = new ECDsaSecurityKey(key);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);
        var handler = new JwtSecurityTokenHandler();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = clientId,
            Audience = "https://auth.example.com/connect/token",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = credentials,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", clientId),
                new System.Security.Claims.Claim("jti", jti ?? Guid.NewGuid().ToString())
            })
        };

        var jwt = handler.CreateToken(descriptor);
        return handler.WriteToken(jwt);
    }

    private HttpRequest CreateRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("auth.example.com");
        return context.Request;
    }

    private HttpRequest CreateRequestWithBasicAuth(string clientId, string clientSecret)
    {
        var request = CreateRequest();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}"));
        request.Headers.Authorization = $"Basic {encoded}";
        return request;
    }

    private Stream CreateFormBody(string formData)
    {
        var bytes = Encoding.UTF8.GetBytes(formData);
        return new MemoryStream(bytes);
    }

    #endregion
}

/// <summary>
/// Constants for client assertion types
/// </summary>
internal static class ClientAssertionTypes
{
    public const string JwtBearer = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
}
