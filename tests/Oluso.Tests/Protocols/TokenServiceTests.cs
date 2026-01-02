using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Protocols.Services;
using Xunit;

namespace Oluso.Tests.Protocols;

public class TokenServiceTests
{
    private readonly Mock<ISigningCredentialStore> _signingCredentialStoreMock;
    private readonly Mock<IPersistedGrantStore> _grantStoreMock;
    private readonly Mock<IResourceStore> _resourceStoreMock;
    private readonly Mock<ITenantContext> _tenantContextMock;
    private readonly Mock<ITenantSettingsProvider> _tenantSettingsMock;
    private readonly Mock<IClaimsProviderRegistry> _claimsProviderRegistryMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<TokenService>> _loggerMock;
    private readonly TokenService _tokenService;
    private readonly SigningCredentials _signingCredentials;

    public TokenServiceTests()
    {
        _signingCredentialStoreMock = new Mock<ISigningCredentialStore>();
        _grantStoreMock = new Mock<IPersistedGrantStore>();
        _resourceStoreMock = new Mock<IResourceStore>();
        _tenantContextMock = new Mock<ITenantContext>();
        _tenantSettingsMock = new Mock<ITenantSettingsProvider>();
        _claimsProviderRegistryMock = new Mock<IClaimsProviderRegistry>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<TokenService>>();

        // Setup default signing credentials
        var rsa = RSA.Create(2048);
        var securityKey = new RsaSecurityKey(rsa) { KeyId = "test-key-id" };
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        _signingCredentialStoreMock
            .Setup(x => x.GetSigningCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_signingCredentials);

        // Setup default issuer
        _configurationMock.Setup(x => x["Oluso:IssuerUri"]).Returns("https://test.oluso.io");

        // Setup default tenant settings
        _tenantSettingsMock
            .Setup(x => x.GetTokenSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantTokenSettings
            {
                DefaultAccessTokenLifetime = 3600,
                DefaultIdentityTokenLifetime = 300,
                DefaultRefreshTokenLifetime = 2592000
            });

        // Setup default no-tenant context
        _tenantContextMock.Setup(x => x.HasTenant).Returns(false);
        _tenantContextMock.Setup(x => x.TenantId).Returns((string?)null);

        // Setup empty claims provider
        _claimsProviderRegistryMock
            .Setup(x => x.GetAllClaimsAsync(It.IsAny<ClaimsProviderContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        _tokenService = new TokenService(
            _signingCredentialStoreMock.Object,
            _grantStoreMock.Object,
            _resourceStoreMock.Object,
            _tenantContextMock.Object,
            _tenantSettingsMock.Object,
            _claimsProviderRegistryMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithValidRequest_ReturnsJwtToken()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            ClientName = "Test Client",
            Scopes = new List<string> { "openid", "profile" },
            Lifetime = 3600,
            SessionId = "session-456"
        };

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        token.Should().NotBeNullOrEmpty();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Subject.Should().Be("user-123");
        jwt.Claims.Should().Contain(c => c.Type == "client_id" && c.Value == "test-client");
        jwt.Claims.Should().Contain(c => c.Type == "scope" && c.Value == "openid");
        jwt.Claims.Should().Contain(c => c.Type == "scope" && c.Value == "profile");
        jwt.Claims.Should().Contain(c => c.Type == "sid" && c.Value == "session-456");
        jwt.Issuer.Should().Be("https://test.oluso.io");
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithoutSubject_OmitsSubClaim()
    {
        // Arrange - client_credentials flow has no subject
        var request = new TokenCreationRequest
        {
            ClientId = "service-client",
            Scopes = new List<string> { "api.read" },
            Lifetime = 3600
        };

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Subject.Should().BeNullOrEmpty();
        jwt.Claims.Should().Contain(c => c.Type == "client_id" && c.Value == "service-client");
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithReferenceToken_StoresGrantAndReturnsHandle()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = 3600,
            IsReference = true
        };

        PersistedGrant? storedGrant = null;
        _grantStoreMock
            .Setup(x => x.StoreAsync(It.IsAny<PersistedGrant>(), It.IsAny<CancellationToken>()))
            .Callback<PersistedGrant, CancellationToken>((g, _) => storedGrant = g)
            .Returns(Task.CompletedTask);

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        token.Should().NotBeNullOrEmpty();
        // Reference token should not be a JWT
        token.Should().NotContain(".");

        storedGrant.Should().NotBeNull();
        storedGrant!.Type.Should().Be("reference_token");
        storedGrant.SubjectId.Should().Be("user-123");
        storedGrant.ClientId.Should().Be("test-client");
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithDPoP_IncludesCnfClaim()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = 3600,
            DPoPKeyThumbprint = "abc123thumbprint"
        };

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var cnfClaim = jwt.Claims.FirstOrDefault(c => c.Type == "cnf");
        cnfClaim.Should().NotBeNull();
        cnfClaim!.Value.Should().Contain("abc123thumbprint");
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithTenant_IncludesTenantIdClaim()
    {
        // Arrange
        _tenantContextMock.Setup(x => x.HasTenant).Returns(true);
        _tenantContextMock.Setup(x => x.TenantId).Returns("tenant-xyz");

        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = 3600
        };

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == "tenant-xyz");
    }

    [Fact]
    public async Task CreateIdTokenAsync_WithValidRequest_ReturnsJwtWithRequiredClaims()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid", "profile" },
            Nonce = "nonce-value-123",
            AuthTime = DateTime.UtcNow.AddMinutes(-5),
            SessionId = "session-456",
            Amr = new List<string> { "pwd" },
            Acr = "urn:mace:incommon:iap:silver"
        };

        // Act
        var token = await _tokenService.CreateIdTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Subject.Should().Be("user-123");
        jwt.Audiences.Should().Contain("test-client");
        jwt.Issuer.Should().Be("https://test.oluso.io");
        jwt.Claims.Should().Contain(c => c.Type == "nonce" && c.Value == "nonce-value-123");
        jwt.Claims.Should().Contain(c => c.Type == "sid" && c.Value == "session-456");
        jwt.Claims.Should().Contain(c => c.Type == "amr" && c.Value == "pwd");
        jwt.Claims.Should().Contain(c => c.Type == "acr" && c.Value == "urn:mace:incommon:iap:silver");
        jwt.Claims.Should().Contain(c => c.Type == "auth_time");
        jwt.Claims.Should().Contain(c => c.Type == "iat");
    }

    [Fact]
    public async Task CreateIdTokenAsync_WithAccessTokenHash_IncludesAtHash()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" }
        };

        // Act
        var token = await _tokenService.CreateIdTokenAsync(request, accessTokenHash: "access-token-hash-value");

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == "at_hash" && c.Value == "access-token-hash-value");
    }

    [Fact]
    public async Task CreateIdTokenAsync_WithCodeHash_IncludesCHash()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" }
        };

        // Act
        var token = await _tokenService.CreateIdTokenAsync(request, codeHash: "code-hash-value");

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == "c_hash" && c.Value == "code-hash-value");
    }

    [Fact]
    public async Task CreateIdTokenAsync_WithoutSubject_ThrowsArgumentNullException()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            ClientId = "test-client",
            Scopes = new List<string> { "openid" }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _tokenService.CreateIdTokenAsync(request));
    }

    [Fact]
    public async Task CreateRefreshTokenAsync_WithValidRequest_StoresGrantAndReturnsHandle()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid", "offline_access" },
            SessionId = "session-456"
        };

        PersistedGrant? storedGrant = null;
        _grantStoreMock
            .Setup(x => x.StoreAsync(It.IsAny<PersistedGrant>(), It.IsAny<CancellationToken>()))
            .Callback<PersistedGrant, CancellationToken>((g, _) => storedGrant = g)
            .Returns(Task.CompletedTask);

        // Act
        var token = await _tokenService.CreateRefreshTokenAsync(request);

        // Assert
        token.Should().NotBeNullOrEmpty();
        // Refresh token should not be a JWT (opaque handle)
        token.Should().NotContain("eyJ"); // Not a JWT header

        storedGrant.Should().NotBeNull();
        storedGrant!.Type.Should().Be("refresh_token");
        storedGrant.SubjectId.Should().Be("user-123");
        storedGrant.ClientId.Should().Be("test-client");
        storedGrant.SessionId.Should().Be("session-456");
    }

    [Fact]
    public async Task CreateRefreshTokenAsync_WithTenant_IncludesTenantInGrant()
    {
        // Arrange
        _tenantContextMock.Setup(x => x.HasTenant).Returns(true);
        _tenantContextMock.Setup(x => x.TenantId).Returns("tenant-xyz");

        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "offline_access" }
        };

        PersistedGrant? storedGrant = null;
        _grantStoreMock
            .Setup(x => x.StoreAsync(It.IsAny<PersistedGrant>(), It.IsAny<CancellationToken>()))
            .Callback<PersistedGrant, CancellationToken>((g, _) => storedGrant = g)
            .Returns(Task.CompletedTask);

        // Act
        await _tokenService.CreateRefreshTokenAsync(request);

        // Assert
        storedGrant.Should().NotBeNull();
        storedGrant!.TenantId.Should().Be("tenant-xyz");
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithCustomClaims_IncludesClaimsInToken()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = 3600,
            Claims = new Dictionary<string, object>
            {
                { "custom_claim", "custom_value" },
                { "role", "admin" }
            }
        };

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == "custom_claim" && c.Value == "custom_value");
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "admin");
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithAudiences_IncludesAudiencesInToken()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "api.read" },
            Lifetime = 3600,
            Audiences = new List<string> { "api1", "api2" }
        };

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Audiences.Should().Contain("api1");
        jwt.Audiences.Should().Contain("api2");
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithNoSigningCredentials_ThrowsInvalidOperationException()
    {
        // Arrange
        _signingCredentialStoreMock
            .Setup(x => x.GetSigningCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((SigningCredentials?)null);

        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = 3600
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _tokenService.CreateAccessTokenAsync(request));
    }

    [Fact]
    public async Task CreateAccessTokenAsync_TokenHasCorrectExpiration()
    {
        // Arrange
        var lifetime = 7200; // 2 hours
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = lifetime
        };

        var beforeCreation = DateTime.UtcNow;

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.ValidTo.Should().BeCloseTo(beforeCreation.AddSeconds(lifetime), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateAccessTokenAsync_TokenHasJtiClaim()
    {
        // Arrange
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = 3600
        };

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var jtiClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti);
        jtiClaim.Should().NotBeNull();
        Guid.TryParse(jtiClaim!.Value, out _).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAccessTokenAsync_WithDuplicateClaimKeys_SkipsDuplicates()
    {
        // Arrange - try to add a 'sub' claim which should already be added
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = 3600,
            Claims = new Dictionary<string, object>
            {
                { "sub", "different-user" }, // Should be ignored
                { "unique_claim", "value" }
            }
        };

        // Act
        var token = await _tokenService.CreateAccessTokenAsync(request);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Should have the original subject, not the duplicate
        jwt.Subject.Should().Be("user-123");
        jwt.Claims.Should().Contain(c => c.Type == "unique_claim" && c.Value == "value");
    }
}
