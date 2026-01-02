using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Protocols.DPoP;
using Oluso.Protocols.DPoP;
using Xunit;

namespace Oluso.Tests.Protocols.Security;

/// <summary>
/// Security tests for DPoP (RFC 9449) proof validation.
/// These tests ensure the DPoP implementation correctly validates proofs
/// and prevents various attack vectors.
/// </summary>
public class DPoPProofValidatorTests : IAsyncLifetime
{
    private readonly Mock<IDPoPNonceStore> _nonceStore;
    private readonly DPoPProofValidator _validator;
    private readonly ECDsa _ecKey;
    private readonly RSA _rsaKey;
    private readonly JsonWebKey _ecJwk;
    private readonly JsonWebKey _rsaJwk;

    public DPoPProofValidatorTests()
    {
        _nonceStore = new Mock<IDPoPNonceStore>();
        _nonceStore.Setup(x => x.ValidateJtiAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _nonceStore.Setup(x => x.IsNonceRequired).Returns(false);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oluso:DPoP:ProofLifetimeSeconds"] = "60",
                ["Oluso:DPoP:ClockSkewSeconds"] = "5"
            })
            .Build();

        _validator = new DPoPProofValidator(_nonceStore.Object, config, NullLogger<DPoPProofValidator>.Instance);

        // Generate test keys
        _ecKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _rsaKey = RSA.Create(2048);

        var ecParams = _ecKey.ExportParameters(false);
        _ecJwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            Crv = "P-256",
            X = Base64UrlEncoder.Encode(ecParams.Q.X!),
            Y = Base64UrlEncoder.Encode(ecParams.Q.Y!)
        };

        var rsaParams = _rsaKey.ExportParameters(false);
        _rsaJwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.RSA,
            E = Base64UrlEncoder.Encode(rsaParams.Exponent!),
            N = Base64UrlEncoder.Encode(rsaParams.Modulus!)
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _ecKey.Dispose();
        _rsaKey.Dispose();
        return Task.CompletedTask;
    }

    #region Valid Proof Tests

    [Fact]
    public async Task ValidateAsync_WithValidECProof_ReturnsSuccess()
    {
        var proof = CreateValidDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
        result.JwkThumbprint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithValidRSAProof_ReturnsSuccess()
    {
        var proof = CreateValidDPoPProof(_rsaKey, _rsaJwk, SecurityAlgorithms.RsaSha256);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region typ Header Validation

    [Fact]
    public async Task ValidateAsync_WithMissingTypHeader_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, typHeader: null);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_dpop_proof");
        result.ErrorDescription.Should().Contain("typ");
    }

    [Fact]
    public async Task ValidateAsync_WithWrongTypHeader_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, typHeader: "JWT");
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("dpop+jwt");
    }

    #endregion

    #region Algorithm Validation (No HMAC Allowed)

    [Fact]
    public async Task ValidateAsync_WithHmacAlgorithm_ReturnsFail()
    {
        // HMAC is symmetric and MUST NOT be allowed for DPoP
        var proof = CreateDPoPProofWithAlgorithm("HS256");
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("Unsupported algorithm");
    }

    [Fact]
    public async Task ValidateAsync_WithNoneAlgorithm_ReturnsFail()
    {
        var proof = CreateDPoPProofWithAlgorithm("none");
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("Unsupported algorithm");
    }

    [Theory]
    [InlineData("RS256")]
    [InlineData("RS384")]
    [InlineData("RS512")]
    [InlineData("ES256")]
    [InlineData("ES384")]
    [InlineData("PS256")]
    public async Task ValidateAsync_WithSupportedAsymmetricAlgorithm_Succeeds(string alg)
    {
        SecurityKey key;
        JsonWebKey jwk;

        if (alg.StartsWith("RS") || alg.StartsWith("PS"))
        {
            key = new RsaSecurityKey(_rsaKey);
            jwk = _rsaJwk;
        }
        else
        {
            key = new ECDsaSecurityKey(_ecKey);
            jwk = _ecJwk;
        }

        var proof = CreateDPoPProof(key, jwk, alg);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue($"Algorithm {alg} should be supported");
    }

    #endregion

    #region JWK Header Validation

    [Fact]
    public async Task ValidateAsync_WithMissingJwkHeader_ReturnsFail()
    {
        var proof = CreateDPoPProofWithoutJwk();
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("jwk");
    }

    [Fact]
    public async Task ValidateAsync_WithPrivateKeyInJwk_ReturnsFail()
    {
        // Private key material MUST NOT be in the JWK
        var ecWithPrivate = _ecKey.ExportParameters(true);
        var jwkWithPrivate = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            Crv = "P-256",
            X = Base64UrlEncoder.Encode(ecWithPrivate.Q.X!),
            Y = Base64UrlEncoder.Encode(ecWithPrivate.Q.Y!),
            D = Base64UrlEncoder.Encode(ecWithPrivate.D!) // Private key!
        };

        var proof = CreateDPoPProof(_ecKey, jwkWithPrivate, SecurityAlgorithms.EcdsaSha256);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("private key");
    }

    #endregion

    #region Signature Validation

    [Fact]
    public async Task ValidateAsync_WithInvalidSignature_ReturnsFail()
    {
        var proof = CreateValidDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256);

        // Tamper with the signature (last segment)
        var parts = proof.Split('.');
        parts[2] = Base64UrlEncoder.Encode(new byte[64]); // Invalid signature
        var tamperedProof = string.Join(".", parts);

        var context = CreateContext(tamperedProof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("Signature");
    }

    [Fact]
    public async Task ValidateAsync_WithMismatchedKey_ReturnsFail()
    {
        // Sign with one key but include different key in JWK header
        using var differentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var differentParams = differentKey.ExportParameters(false);
        var differentJwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            Crv = "P-256",
            X = Base64UrlEncoder.Encode(differentParams.Q.X!),
            Y = Base64UrlEncoder.Encode(differentParams.Q.Y!)
        };

        // Sign with _ecKey but include differentJwk in header
        var proof = CreateDPoPProof(_ecKey, differentJwk, SecurityAlgorithms.EcdsaSha256);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("Signature");
    }

    #endregion

    #region Required Claims Validation

    [Fact]
    public async Task ValidateAsync_WithMissingJti_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, jti: OmitField);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("jti");
    }

    [Fact]
    public async Task ValidateAsync_WithMissingHtm_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, htm: OmitField);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("htm");
    }

    [Fact]
    public async Task ValidateAsync_WithMissingHtu_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, htu: OmitField);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("htu");
    }

    [Fact]
    public async Task ValidateAsync_WithMissingIat_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, iat: long.MinValue);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("iat");
    }

    #endregion

    #region HTTP Method (htm) Validation

    [Fact]
    public async Task ValidateAsync_WithMismatchedHttpMethod_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, htm: "GET");
        var context = CreateContext(proof, httpMethod: "POST"); // Mismatch!

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("htm").And.Contain("POST");
    }

    [Fact]
    public async Task ValidateAsync_WithMatchingHttpMethod_CaseInsensitive_Succeeds()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, htm: "post");
        var context = CreateContext(proof, httpMethod: "POST");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region HTTP URI (htu) Validation

    [Fact]
    public async Task ValidateAsync_WithMismatchedScheme_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256,
            htu: "http://example.com/token");
        var context = CreateContext(proof, httpUri: "https://example.com/token");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("htu");
    }

    [Fact]
    public async Task ValidateAsync_WithMismatchedHost_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256,
            htu: "https://attacker.com/token");
        var context = CreateContext(proof, httpUri: "https://example.com/token");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("htu");
    }

    [Fact]
    public async Task ValidateAsync_WithMismatchedPort_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256,
            htu: "https://example.com:8443/token");
        var context = CreateContext(proof, httpUri: "https://example.com:443/token");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithMismatchedPath_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256,
            htu: "https://example.com/other");
        var context = CreateContext(proof, httpUri: "https://example.com/token");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_IgnoresQueryString_Succeeds()
    {
        // Per RFC 9449, query string should be ignored
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256,
            htu: "https://example.com/token");
        var context = CreateContext(proof, httpUri: "https://example.com/token?foo=bar");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Time Validation (iat)

    [Fact]
    public async Task ValidateAsync_WithFutureIat_ReturnsFail()
    {
        var futureIat = DateTimeOffset.UtcNow.AddMinutes(10);
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256,
            iat: futureIat.ToUnixTimeSeconds());
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("future");
    }

    [Fact]
    public async Task ValidateAsync_WithExpiredIat_ReturnsFail()
    {
        var oldIat = DateTimeOffset.UtcNow.AddMinutes(-10); // Older than 60s lifetime
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256,
            iat: oldIat.ToUnixTimeSeconds());
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("expired");
    }

    [Fact]
    public async Task ValidateAsync_WithIatWithinClockSkew_Succeeds()
    {
        // 3 seconds in the future should be OK with 5s clock skew
        var slightlyFuture = DateTimeOffset.UtcNow.AddSeconds(3);
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256,
            iat: slightlyFuture.ToUnixTimeSeconds());
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region JTI Replay Protection

    [Fact]
    public async Task ValidateAsync_WithReusedJti_ReturnsFail()
    {
        _nonceStore.Setup(x => x.ValidateJtiAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // JTI already used

        var proof = CreateValidDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        (result.ErrorDescription?.Contains("jti") == true || result.ErrorDescription?.Contains("already been used") == true)
            .Should().BeTrue("error should mention jti or replay");
    }

    #endregion

    #region Nonce Validation

    [Fact]
    public async Task ValidateAsync_WhenNonceRequired_WithMissingNonce_ReturnsNonceRequired()
    {
        _nonceStore.Setup(x => x.IsNonceRequired).Returns(true);
        _nonceStore.Setup(x => x.GenerateNonceAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-nonce-value");

        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, nonce: null);
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.RequiresNonce.Should().BeTrue();
        result.ServerNonce.Should().Be("new-nonce-value");
    }

    [Fact]
    public async Task ValidateAsync_WhenNonceRequired_WithInvalidNonce_ReturnsNonceRequired()
    {
        _nonceStore.Setup(x => x.IsNonceRequired).Returns(true);
        _nonceStore.Setup(x => x.ValidateNonceAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _nonceStore.Setup(x => x.GenerateNonceAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-nonce-value");

        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, nonce: "invalid-nonce");
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.RequiresNonce.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WhenNonceRequired_WithValidNonce_Succeeds()
    {
        _nonceStore.Setup(x => x.IsNonceRequired).Returns(true);
        _nonceStore.Setup(x => x.ValidateNonceAsync("valid-nonce", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, nonce: "valid-nonce");
        var context = CreateContext(proof);

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Access Token Hash (ath) Validation

    [Fact]
    public async Task ValidateAsync_WithExpectedAth_ButMissingInProof_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, ath: null);
        var context = CreateContext(proof, expectedAccessTokenHash: "expected-hash");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("ath");
    }

    [Fact]
    public async Task ValidateAsync_WithMismatchedAth_ReturnsFail()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, ath: "wrong-hash");
        var context = CreateContext(proof, expectedAccessTokenHash: "expected-hash");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("ath");
    }

    [Fact]
    public async Task ValidateAsync_WithMatchingAth_Succeeds()
    {
        var proof = CreateDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256, ath: "correct-hash");
        var context = CreateContext(proof, expectedAccessTokenHash: "correct-hash");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region JWK Thumbprint (Key Binding) Validation

    [Fact]
    public async Task ValidateAsync_WithExpectedThumbprint_ButMismatch_ReturnsFail()
    {
        var proof = CreateValidDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256);
        var context = CreateContext(proof, expectedJwkThumbprint: "wrong-thumbprint");

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("bound key");
    }

    [Fact]
    public async Task ValidateAsync_ReturnsConsistentThumbprint()
    {
        var proof1 = CreateValidDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256);
        var proof2 = CreateValidDPoPProof(_ecKey, _ecJwk, SecurityAlgorithms.EcdsaSha256);

        var result1 = await _validator.ValidateAsync(CreateContext(proof1));
        var result2 = await _validator.ValidateAsync(CreateContext(proof2));

        result1.JwkThumbprint.Should().Be(result2.JwkThumbprint);
    }

    #endregion

    #region Access Token Hash Computation

    [Fact]
    public void ComputeAccessTokenHash_ProducesConsistentResults()
    {
        var accessToken = "test-access-token";

        var hash1 = _validator.ComputeAccessTokenHash(accessToken);
        var hash2 = _validator.ComputeAccessTokenHash(accessToken);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeAccessTokenHash_DifferentTokens_ProduceDifferentHashes()
    {
        var hash1 = _validator.ComputeAccessTokenHash("token1");
        var hash2 = _validator.ComputeAccessTokenHash("token2");

        hash1.Should().NotBe(hash2);
    }

    #endregion

    #region Helper Methods

    private string CreateValidDPoPProof(object key, JsonWebKey jwk, string algorithm)
    {
        return CreateDPoPProof(key, jwk, algorithm);
    }

    // Sentinel value to indicate a field should be omitted entirely
    private const string OmitField = "__OMIT__";

    private string CreateDPoPProof(
        object key,
        JsonWebKey jwk,
        string algorithm,
        string? typHeader = "dpop+jwt",
        string? jti = "generate",
        string? htm = "POST",
        string? htu = "https://example.com/token",
        long? iat = -1,  // -1 means generate current time
        string? nonce = null,
        string? ath = null)
    {
        var securityKey = key switch
        {
            ECDsa ec => (SecurityKey)new ECDsaSecurityKey(ec),
            RSA rsa => new RsaSecurityKey(rsa),
            SecurityKey sk => sk,
            _ => throw new ArgumentException("Unsupported key type")
        };

        var credentials = new SigningCredentials(securityKey, algorithm);

        var header = new JwtHeader(credentials);
        if (typHeader != null)
            header["typ"] = typHeader;

        // Serialize JWK to a dictionary that can be embedded in the JWT header
        var jwkDict = new Dictionary<string, object> { ["kty"] = jwk.Kty };
        if (!string.IsNullOrEmpty(jwk.Crv)) jwkDict["crv"] = jwk.Crv;
        if (!string.IsNullOrEmpty(jwk.X)) jwkDict["x"] = jwk.X;
        if (!string.IsNullOrEmpty(jwk.Y)) jwkDict["y"] = jwk.Y;
        if (!string.IsNullOrEmpty(jwk.D)) jwkDict["d"] = jwk.D; // Private key material (for testing rejection)
        if (!string.IsNullOrEmpty(jwk.N)) jwkDict["n"] = jwk.N;
        if (!string.IsNullOrEmpty(jwk.E)) jwkDict["e"] = jwk.E;
        header["jwk"] = jwkDict;

        var payload = new JwtPayload();

        // jti: "generate" means create new GUID, OmitField or null means omit
        if (jti != null && jti != OmitField)
            payload["jti"] = jti == "generate" ? Guid.NewGuid().ToString() : jti;

        // htm: null or OmitField means omit
        if (htm != null && htm != OmitField)
            payload["htm"] = htm;

        // htu: null or OmitField means omit
        if (htu != null && htu != OmitField)
            payload["htu"] = htu;

        // iat: -1 means current time, null means omit
        if (iat.HasValue && iat.Value != long.MinValue)
            payload["iat"] = iat.Value == -1 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : iat.Value;

        if (nonce != null)
            payload["nonce"] = nonce;
        if (ath != null)
            payload["ath"] = ath;

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string CreateDPoPProofWithAlgorithm(string alg)
    {
        // Create a JWT string that claims to use the specified algorithm
        // This tests algorithm validation before signature verification
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"typ\":\"dpop+jwt\",\"alg\":\"{alg}\",\"jwk\":{{\"kty\":\"oct\",\"k\":\"test\"}}}}"))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"jti\":\"{Guid.NewGuid()}\",\"htm\":\"POST\",\"htu\":\"https://example.com/token\",\"iat\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}"))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var signature = Convert.ToBase64String(new byte[32])
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        return $"{header}.{payload}.{signature}";
    }

    private string CreateDPoPProofWithoutJwk()
    {
        var credentials = new SigningCredentials(new ECDsaSecurityKey(_ecKey), SecurityAlgorithms.EcdsaSha256);

        var header = new JwtHeader(credentials);
        header["typ"] = "dpop+jwt";
        // No jwk header

        var payload = new JwtPayload
        {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = "POST",
            ["htu"] = "https://example.com/token",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static DPoPValidationContext CreateContext(
        string proof,
        string httpMethod = "POST",
        string httpUri = "https://example.com/token",
        string? expectedAccessTokenHash = null,
        string? expectedJwkThumbprint = null,
        string? clientId = "test-client")
    {
        return new DPoPValidationContext
        {
            Proof = proof,
            HttpMethod = httpMethod,
            HttpUri = httpUri,
            ExpectedAccessTokenHash = expectedAccessTokenHash,
            ExpectedJwkThumbprint = expectedJwkThumbprint,
            ClientId = clientId
        };
    }

    #endregion
}
