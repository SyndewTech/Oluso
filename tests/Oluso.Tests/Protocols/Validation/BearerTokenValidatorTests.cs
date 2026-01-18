using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.DPoP;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Validation;

/// <summary>
/// Tests for BearerTokenValidator which validates both JWT and reference tokens
/// with support for DPoP sender-constrained access.
/// </summary>
public class BearerTokenValidatorTests : IAsyncLifetime
{
    private readonly Mock<ISigningCredentialStore> _signingCredentialStore;
    private readonly Mock<IPersistedGrantStore> _grantStore;
    private readonly Mock<IIssuerResolver> _issuerResolver;
    private readonly Mock<IDPoPProofValidator> _dpopValidator;
    private readonly BearerTokenValidator _validator;

    private readonly RSA _rsaKey;
    private readonly SecurityKey _securityKey;
    private readonly SigningCredentials _signingCredentials;

    private const string Issuer = "https://auth.example.com";
    private const string TestClientId = "test-client";

    public BearerTokenValidatorTests()
    {
        _signingCredentialStore = new Mock<ISigningCredentialStore>();
        _grantStore = new Mock<IPersistedGrantStore>();
        _issuerResolver = new Mock<IIssuerResolver>();
        _dpopValidator = new Mock<IDPoPProofValidator>();

        // Generate RSA key for signing
        _rsaKey = RSA.Create(2048);
        _securityKey = new RsaSecurityKey(_rsaKey) { KeyId = "test-key-id" };
        _signingCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256);

        // Setup default mocks
        _issuerResolver.Setup(x => x.GetIssuerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Issuer);

        _signingCredentialStore.Setup(x => x.GetValidationKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SecurityKeyInfo { Key = _securityKey } });

        _validator = new BearerTokenValidator(
            _signingCredentialStore.Object,
            _grantStore.Object,
            _issuerResolver.Object,
            _dpopValidator.Object,
            NullLogger<BearerTokenValidator>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _rsaKey.Dispose();
        return Task.CompletedTask;
    }

    #region JWT Token Validation

    [Fact]
    public async Task ValidateAsync_WithValidJwt_ReturnsSuccess()
    {
        var token = CreateJwtToken(TestClientId, ["openid", "profile"], "user-123");

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeTrue();
        result.ClientId.Should().Be(TestClientId);
        result.SubjectId.Should().Be("user-123");
        result.Scopes.Should().Contain("openid");
        result.Scopes.Should().Contain("profile");
        result.TokenType.Should().Be("jwt");
    }

    [Fact]
    public async Task ValidateAsync_WithExpiredJwt_ReturnsFailure()
    {
        // Create token that was valid in the past but has now expired
        var token = CreateJwtToken(TestClientId, ["openid"],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1));

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidToken);
        result.ErrorDescription.Should().Contain("expired");
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidSignature_ReturnsFailure()
    {
        // Create token with different key
        using var differentKey = RSA.Create(2048);
        var differentSigningCredentials = new SigningCredentials(
            new RsaSecurityKey(differentKey), SecurityAlgorithms.RsaSha256);

        var token = CreateJwtToken(TestClientId, ["openid"], signingCredentials: differentSigningCredentials);

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidToken);
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidIssuer_ReturnsFailure()
    {
        var token = CreateJwtToken(TestClientId, ["openid"], issuer: "https://wrong-issuer.com");

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidToken);
    }

    [Fact]
    public async Task ValidateAsync_WithRequiredScopes_ValidatesPresence()
    {
        var token = CreateJwtToken(TestClientId, ["openid", "profile"]);
        var context = new BearerTokenValidationContext
        {
            Token = token,
            RequiredScopes = ["openid", "email"],
            RequireAllScopes = true
        };

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InsufficientScope);
        result.ErrorDescription.Should().Contain("email");
    }

    [Fact]
    public async Task ValidateAsync_WithAnyRequiredScope_PassesIfOnePresent()
    {
        var token = CreateJwtToken(TestClientId, ["openid", "profile"]);
        var context = new BearerTokenValidationContext
        {
            Token = token,
            RequiredScopes = ["email", "profile"], // profile is present
            RequireAllScopes = false
        };

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ExtractsSessionId()
    {
        var token = CreateJwtToken(TestClientId, ["openid"], sessionId: "session-123");

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeTrue();
        result.SessionId.Should().Be("session-123");
    }

    #endregion

    #region Reference Token Validation

    [Fact]
    public async Task ValidateAsync_WithValidReferenceToken_ReturnsSuccess()
    {
        var token = "ref_token_12345";
        var grant = CreateReferenceTokenGrant(token, TestClientId, ["openid", "profile"], "user-456");

        _grantStore.Setup(x => x.GetAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeTrue();
        result.ClientId.Should().Be(TestClientId);
        result.SubjectId.Should().Be("user-456");
        result.TokenType.Should().Be("reference");
    }

    [Fact]
    public async Task ValidateAsync_WithConsumedReferenceToken_ReturnsFailure()
    {
        var token = "ref_token_consumed";
        var grant = CreateReferenceTokenGrant(token, TestClientId, ["openid"]);
        grant.ConsumedTime = DateTime.UtcNow.AddMinutes(-5);

        _grantStore.Setup(x => x.GetAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidToken);
        result.ErrorDescription.Should().Contain("revoked");
    }

    [Fact]
    public async Task ValidateAsync_WithExpiredReferenceToken_ReturnsFailure()
    {
        var token = "ref_token_expired";
        var grant = CreateReferenceTokenGrant(token, TestClientId, ["openid"]);
        grant.Expiration = DateTime.UtcNow.AddHours(-1);

        _grantStore.Setup(x => x.GetAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidToken);
        result.ErrorDescription.Should().Contain("expired");
    }

    #endregion

    #region DPoP Validation

    [Fact]
    public async Task ValidateAsync_WithDPoPBoundJwt_RequiresProof()
    {
        var dpopJkt = "test-jkt-thumbprint";
        var token = CreateJwtToken(TestClientId, ["openid"], dpopJkt: dpopJkt);
        var context = new BearerTokenValidationContext
        {
            Token = token,
            // No DPoP proof provided
            HttpMethod = "POST",
            HttpUri = "https://example.com/resource"
        };

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidToken);
        result.ErrorDescription.Should().Contain("DPoP proof is required");
    }

    [Fact]
    public async Task ValidateAsync_WithDPoPBoundJwt_ValidatesProof()
    {
        var dpopJkt = "test-jkt-thumbprint";
        var token = CreateJwtToken(TestClientId, ["openid"], dpopJkt: dpopJkt);
        var dpopProof = "valid-dpop-proof";

        _dpopValidator.Setup(x => x.ComputeAccessTokenHash(token))
            .Returns("computed-ath");
        _dpopValidator.Setup(x => x.ValidateAsync(It.IsAny<DPoPValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DPoPValidationResult.Success(dpopJkt, new JsonWebKey()));

        var context = new BearerTokenValidationContext
        {
            Token = token,
            DPoPProof = dpopProof,
            HttpMethod = "POST",
            HttpUri = "https://example.com/resource"
        };

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
        result.DPoPKeyThumbprint.Should().Be(dpopJkt);

        _dpopValidator.Verify(x => x.ValidateAsync(
            It.Is<DPoPValidationContext>(c =>
                c.Proof == dpopProof &&
                c.HttpMethod == "POST" &&
                c.ExpectedJwkThumbprint == dpopJkt),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidDPoPProof_ReturnsFailure()
    {
        var dpopJkt = "test-jkt-thumbprint";
        var token = CreateJwtToken(TestClientId, ["openid"], dpopJkt: dpopJkt);

        _dpopValidator.Setup(x => x.ComputeAccessTokenHash(It.IsAny<string>()))
            .Returns("computed-ath");
        _dpopValidator.Setup(x => x.ValidateAsync(It.IsAny<DPoPValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DPoPValidationResult.Failure("invalid_dpop_proof", "Signature validation failed"));

        var context = new BearerTokenValidationContext
        {
            Token = token,
            DPoPProof = "invalid-proof",
            HttpMethod = "POST",
            HttpUri = "https://example.com/resource"
        };

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_dpop_proof");
    }

    [Fact]
    public async Task ValidateAsync_WithDPoPBoundReferenceToken_ValidatesProof()
    {
        var token = "ref_dpop_bound";
        var dpopJkt = "reference-token-jkt";
        var grant = CreateReferenceTokenGrant(token, TestClientId, ["openid"], dpopJkt: dpopJkt);

        _grantStore.Setup(x => x.GetAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);
        _dpopValidator.Setup(x => x.ComputeAccessTokenHash(token))
            .Returns("computed-ath");
        _dpopValidator.Setup(x => x.ValidateAsync(It.IsAny<DPoPValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DPoPValidationResult.Success(dpopJkt, new JsonWebKey()));

        var context = new BearerTokenValidationContext
        {
            Token = token,
            DPoPProof = "valid-proof",
            HttpMethod = "GET",
            HttpUri = "https://example.com/api"
        };

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeTrue();
        result.DPoPKeyThumbprint.Should().Be(dpopJkt);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ValidateAsync_WithEmptyToken_ReturnsFailure()
    {
        var result = await _validator.ValidateAsync("");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidToken);
    }

    [Fact]
    public async Task ValidateAsync_WithNullToken_ReturnsFailure()
    {
        var context = new BearerTokenValidationContext { Token = null! };

        var result = await _validator.ValidateAsync(context);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithMalformedJwt_ReturnsFailure()
    {
        var result = await _validator.ValidateAsync("not.a.valid.jwt");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidToken);
    }

    [Fact]
    public async Task ValidateAsync_FallsBackToJwtIfNotReferenceToken()
    {
        var token = CreateJwtToken(TestClientId, ["openid"]);

        // Grant store returns null (not a reference token)
        _grantStore.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedGrant?)null);

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeTrue();
        result.TokenType.Should().Be("jwt");
    }

    [Fact]
    public async Task ValidateAsync_WithNoValidationKeys_ReturnsFailure()
    {
        _signingCredentialStore.Setup(x => x.GetValidationKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityKeyInfo>());

        var token = CreateJwtToken(TestClientId, ["openid"]);

        var result = await _validator.ValidateAsync(token);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("unavailable");
    }

    #endregion

    #region Helper Methods

    private string CreateJwtToken(
        string clientId,
        IEnumerable<string> scopes,
        string? subjectId = null,
        string? sessionId = null,
        string? dpopJkt = null,
        string issuer = Issuer,
        DateTime? notBefore = null,
        DateTime? expires = null,
        SigningCredentials? signingCredentials = null)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new("client_id", clientId)
        };

        if (subjectId != null)
            claims.Add(new System.Security.Claims.Claim("sub", subjectId));

        if (sessionId != null)
            claims.Add(new System.Security.Claims.Claim("sid", sessionId));

        foreach (var scope in scopes)
            claims.Add(new System.Security.Claims.Claim("scope", scope));

        // Add DPoP binding via cnf claim
        if (dpopJkt != null)
        {
            var cnf = JsonSerializer.Serialize(new { jkt = dpopJkt });
            claims.Add(new System.Security.Claims.Claim("cnf", cnf));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: clientId,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: signingCredentials ?? _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static PersistedGrant CreateReferenceTokenGrant(
        string key,
        string clientId,
        IEnumerable<string> scopes,
        string? subjectId = null,
        string? dpopJkt = null)
    {
        var data = new
        {
            SubjectId = subjectId,
            ClientId = clientId,
            Scopes = scopes.ToList(),
            DPoPKeyThumbprint = dpopJkt,
            CreatedAt = DateTime.UtcNow
        };

        return new PersistedGrant
        {
            Key = key,
            Type = "reference_token",
            ClientId = clientId,
            SubjectId = subjectId,
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddHours(1),
            Data = JsonSerializer.Serialize(data)
        };
    }

    #endregion
}
