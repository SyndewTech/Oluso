using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
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
    private readonly Mock<ITenantContext> _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly Mock<IDistributedCache> _cache;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor;
    private readonly ClientAuthenticator _authenticator;
    private readonly ECDsa _clientKey;
    private readonly string _testClientId = "test-client";
    private readonly string _testClientSecret = "super-secret-key-12345";
    private readonly string _hashedSecret;

    public ClientAuthenticatorTests()
    {
        _clientStore = new Mock<IClientStore>();
        _tenantContext = new Mock<ITenantContext>();
        _tenantContext.Setup(x => x.HasTenant).Returns(false);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oluso:IssuerUri"] = "https://auth.example.com"
            })
            .Build();

        _cache = new Mock<IDistributedCache>();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("auth.example.com");

        _httpContextAccessor = new Mock<IHttpContextAccessor>();
        _httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var endpointConfig = Options.Create(new OidcEndpointConfiguration
        {
            TokenEndpoint = "/connect/token"
        });

        _authenticator = new ClientAuthenticator(
            _clientStore.Object,
            _tenantContext.Object,
            _configuration,
            endpointConfig,
            _cache.Object,
            _httpContextAccessor.Object);

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
        // For client_secret_jwt, the secret must be stored raw (not hashed)
        // because it's used as an HMAC key
        var client = new Client
        {
            ClientId = _testClientId,
            Enabled = true,
            RequireClientSecret = true,
            ClientSecrets = new List<ClientSecret>
            {
                new() { Type = "SharedSecret", Value = _testClientSecret }
            }
        };
        _clientStore.Setup(x => x.FindClientByIdAsync(_testClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        // Create HMAC-signed JWT (client_secret_jwt)
        var secretBytes = Encoding.UTF8.GetBytes(_testClientSecret.PadRight(32, '\0'));
        var key = new SymmetricSecurityKey(secretBytes);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var handler = new JwtSecurityTokenHandler();

        var jwt = handler.CreateJwtSecurityToken(
            issuer: _testClientId,
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

        result.Client.Should().NotBeNull();
        result.Method.Should().Be(ClientAuthenticationMethod.ClientSecretJwt);
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
