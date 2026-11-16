using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Validation;

/// <summary>
/// Request objects (OpenID Connect Core 1.0 section 6, RFC 9101): what is accepted, what is refused, and the
/// parameters a request object yields.
/// </summary>
public class RequestObjectValidatorTests : IDisposable
{
    private const string Issuer = "https://default.oluso.test";
    private const string ClientId = "jar-client";

    private readonly Mock<IClientStore> _clients = new();
    private readonly TenantProtocolSettings _settings = new();
    private readonly ClientSecretProtector _secrets = new(new EphemeralDataProtectionProvider(), NullLogger<ClientSecretProtector>.Instance);
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly Client _client;
    private readonly RequestObjectValidator _validator;

    public RequestObjectValidatorTests()
    {
        _client = new Client { ClientId = ClientId, Enabled = true };
        _clients.Setup(c => c.FindClientByIdAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(_client);

        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Issuer);
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetProtocolSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_settings);

        _validator = new RequestObjectValidator(
            _clients.Object,
            issuer.Object,
            settings.Object,
            Mock.Of<IHttpClientFactory>(),
            _secrets,
            new ConfigurationBuilder().Build(),
            NullLogger<RequestObjectValidator>.Instance);
    }

    public void Dispose() => _rsa.Dispose();

    [Fact]
    public async Task Unsigned_request_object_yields_its_parameters()
    {
        var jwt = Unsigned(new Dictionary<string, object>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["state"] = "from-the-object",
            ["nonce"] = "n-1",
            ["max_age"] = 300,
            ["claims"] = new Dictionary<string, object> { ["userinfo"] = new Dictionary<string, object> { ["email"] = new Dictionary<string, object>() } }
        });

        var result = await _validator.ValidateAsync(ClientId, jwt, null);

        result.IsValid.Should().BeTrue(result.ErrorDescription);
        result.IsSigned.Should().BeFalse();
        result.Parameters["state"].Should().Be("from-the-object");
        result.Parameters["nonce"].Should().Be("n-1");
        result.Parameters["max_age"].Should().Be("300");
        JsonDocument.Parse(result.Parameters["claims"]).RootElement.GetProperty("userinfo").ValueKind.Should().Be(JsonValueKind.Object);
        result.Parameters.Should().NotContainKey("iss").And.NotContainKey("aud");
    }

    [Fact]
    public async Task Client_that_requires_request_objects_must_sign_them()
    {
        _client.RequireRequestObject = true;

        var result = await _validator.ValidateAsync(ClientId, Unsigned(new() { ["state"] = "s" }), null);

        result.Error.Should().Be(OidcConstants.Errors.InvalidRequestObject);
    }

    [Fact]
    public async Task Signed_request_object_is_verified_with_the_clients_registered_key()
    {
        RegisterRsaKey("key-1");

        var result = await _validator.ValidateAsync(ClientId, SignedRs256("key-1", new() { ["state"] = "signed" }), null);

        result.IsValid.Should().BeTrue(result.ErrorDescription);
        result.IsSigned.Should().BeTrue();
        result.Parameters["state"].Should().Be("signed");
    }

    [Fact]
    public async Task Request_object_signed_with_a_key_the_client_publishes_at_its_jwks_uri_is_verified()
    {
        const string jwksUri = "https://keys.example.com/jwks.json";
        _client.JwksUri = jwksUri;
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = "published" });
        var published = new Mock<IClientJwksUriKeys>();
        // The cached set predates the key; a fresh fetch has it.
        published.Setup(p => p.GetAsync(jwksUri, false, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        published.Setup(p => p.GetAsync(jwksUri, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { JsonSerializer.Serialize(new { kty = jwk.Kty, kid = "published", n = jwk.N, e = jwk.E }) });
        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Issuer);
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetProtocolSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_settings);
        var validator = new RequestObjectValidator(_clients.Object, issuer.Object, settings.Object, Mock.Of<IHttpClientFactory>(),
            _secrets, new ConfigurationBuilder().Build(), NullLogger<RequestObjectValidator>.Instance, published.Object);

        var result = await validator.ValidateAsync(ClientId,
            SignedRs256("published", new Dictionary<string, object> { ["client_id"] = ClientId, ["state"] = "s" }), null);

        result.IsValid.Should().BeTrue(result.ErrorDescription);
        published.Verify(p => p.GetAsync(jwksUri, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Request_object_signed_with_another_key_is_refused()
    {
        RegisterRsaKey("key-1");
        using var other = RSA.Create(2048);

        var result = await _validator.ValidateAsync(ClientId, SignedRs256("key-1", new() { ["state"] = "s" }, other), null);

        result.Error.Should().Be(OidcConstants.Errors.InvalidRequestObject);
    }

    [Fact]
    public async Task Registered_request_object_signing_alg_is_enforced()
    {
        RegisterRsaKey("key-1");
        _client.Properties.Add(new ClientProperty { Key = RequestObjectSigningAlgorithms.ClientProperty, Value = "RS256" });

        var result = await _validator.ValidateAsync(ClientId, Unsigned(new() { ["state"] = "s" }), null);

        result.Error.Should().Be(OidcConstants.Errors.InvalidRequestObject);
    }

    [Fact]
    public async Task Hmac_request_object_verifies_with_a_recoverable_secret_only()
    {
        var secret = ClientSecretProtector.GenerateSecret(hmac: true);
        _client.ClientSecrets.Add(new ClientSecret { Type = "SharedSecret", Value = _secrets.ToStoredValue(secret, recoverable: true) });

        var signed = SignedHmac(secret, new() { ["state"] = "hmac" });
        (await _validator.ValidateAsync(ClientId, signed, null)).IsValid.Should().BeTrue();

        // The same client with only a hashed secret cannot verify it: a hash is never an HMAC key.
        _client.ClientSecrets.Clear();
        _client.ClientSecrets.Add(new ClientSecret { Type = "SharedSecret", Value = _secrets.ToStoredValue(secret, recoverable: false) });
        (await _validator.ValidateAsync(ClientId, signed, null)).Error.Should().Be(OidcConstants.Errors.InvalidRequestObject);
    }

    [Theory]
    [InlineData("client_id", "someone-else")]
    [InlineData("request", "nested")]
    [InlineData("request_uri", "https://example.com/ro")]
    [InlineData("iss", "someone-else")]
    [InlineData("aud", "https://another-issuer.example")]
    public async Task Inconsistent_or_nested_request_object_is_refused(string claim, string value)
    {
        var result = await _validator.ValidateAsync(ClientId, Unsigned(new() { [claim] = value }), null);

        result.Error.Should().Be(OidcConstants.Errors.InvalidRequestObject);
    }

    [Fact]
    public async Task Expired_request_object_is_refused()
    {
        var expired = Unsigned(new() { ["exp"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds() });

        var result = await _validator.ValidateAsync(ClientId, expired, null);

        result.Error.Should().Be(OidcConstants.Errors.InvalidRequestObject);
    }

    [Fact]
    public async Task Encrypted_request_object_is_refused()
    {
        var result = await _validator.ValidateAsync(ClientId, "a.b.c.d.e", null);

        result.Error.Should().Be(OidcConstants.Errors.InvalidRequestObject);
    }

    [Fact]
    public async Task Request_and_request_uri_together_are_refused()
    {
        var result = await _validator.ValidateAsync(ClientId, Unsigned(new()), "https://client.example/ro");

        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
    }

    [Theory]
    [InlineData("http://client.example/ro")]
    [InlineData("https://127.0.0.1/ro")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.0.0.5/ro")]
    [InlineData("https://localhost/ro")]
    public async Task Request_uri_to_a_non_public_or_plain_http_target_is_never_fetched(string requestUri)
    {
        // IHttpClientFactory is a bare mock: any fetch attempt would throw, so a refusal proves none was made.
        var result = await _validator.ValidateAsync(ClientId, null, requestUri);

        result.Error.Should().Be(OidcConstants.Errors.InvalidRequestUri);
    }

    [Fact]
    public async Task Request_objects_switched_off_for_the_tenant_are_not_supported()
    {
        _settings.RequestParameterSupported = false;
        _settings.RequestUriParameterSupported = false;

        (await _validator.ValidateAsync(ClientId, Unsigned(new()), null)).Error.Should().Be(OidcConstants.Errors.RequestNotSupported);
        (await _validator.ValidateAsync(ClientId, null, "https://client.example/ro")).Error.Should().Be(OidcConstants.Errors.RequestUriNotSupported);
    }

    private void RegisterRsaKey(string kid)
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = kid });
        _client.ClientSecrets.Add(new ClientSecret { Type = "JsonWebKey", Value = JsonSerializer.Serialize(new { kty = jwk.Kty, kid, n = jwk.N, e = jwk.E, use = "sig" }) });
    }

    private static string Unsigned(Dictionary<string, object> claims)
    {
        claims.TryAdd("iss", ClientId);
        claims.TryAdd("aud", Issuer);
        static string Segment(object value) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
        return $"{Segment(new { alg = "none" })}.{Segment(claims)}.";
    }

    private string SignedRs256(string kid, Dictionary<string, object> claims, RSA? signer = null) =>
        Sign(new SigningCredentials(new RsaSecurityKey(signer ?? _rsa) { KeyId = kid }, SecurityAlgorithms.RsaSha256), claims);

    private static string SignedHmac(string secret, Dictionary<string, object> claims) =>
        Sign(new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256), claims);

    private static string Sign(SigningCredentials credentials, Dictionary<string, object> claims)
    {
        var payload = new JwtPayload(ClientId, Issuer, Array.Empty<Claim>(), null, DateTime.UtcNow.AddMinutes(5));
        foreach (var (name, value) in claims)
            payload[name] = value;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(credentials), payload));
    }
}
