using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
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
    private readonly Mock<ITenantSettingsProvider> _tenantSettingsMock;
    private readonly Mock<IClaimsProviderRegistry> _claimsProviderRegistryMock;
    private readonly Mock<IIssuerResolver> _issuerResolverMock;
    private readonly Mock<ILogger<TokenService>> _loggerMock;
    private readonly Mock<ITenantContext> _tenantContextMock = new();
    private readonly Mock<ITenantStore> _tenantStoreMock = new();
    private readonly TokenService _tokenService;
    private readonly SigningCredentials _signingCredentials;

    public TokenServiceTests()
    {
        _signingCredentialStoreMock = new Mock<ISigningCredentialStore>();
        _grantStoreMock = new Mock<IPersistedGrantStore>();
        _resourceStoreMock = new Mock<IResourceStore>();
        _tenantSettingsMock = new Mock<ITenantSettingsProvider>();
        _claimsProviderRegistryMock = new Mock<IClaimsProviderRegistry>();
        _issuerResolverMock = new Mock<IIssuerResolver>();
        _loggerMock = new Mock<ILogger<TokenService>>();

        // Setup default signing credentials
        var rsa = RSA.Create(2048);
        var securityKey = new RsaSecurityKey(rsa) { KeyId = "test-key-id" };
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        _signingCredentialStoreMock
            .Setup(x => x.GetSigningCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_signingCredentials);

        // Setup default issuer
        _issuerResolverMock
            .Setup(x => x.GetIssuerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://test.oluso.io");

        // Setup default tenant settings
        _tenantSettingsMock
            .Setup(x => x.GetTokenSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantTokenSettings
            {
                DefaultAccessTokenLifetime = 3600,
                DefaultIdentityTokenLifetime = 300,
                DefaultRefreshTokenLifetime = 2592000
            });

        // Setup empty claims provider
        _claimsProviderRegistryMock
            .Setup(x => x.GetAllClaimsAsync(It.IsAny<ClaimsProviderContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        _tokenService = new TokenService(
            _signingCredentialStoreMock.Object,
            _grantStoreMock.Object,
            _tenantSettingsMock.Object,
            _claimsProviderRegistryMock.Object,
            _issuerResolverMock.Object,
            new IdentityScopeClaimResolver(_resourceStoreMock.Object),
            new AudienceResolver(_resourceStoreMock.Object, _tenantSettingsMock.Object),
            _tenantContextMock.Object,
            _tenantStoreMock.Object,
            _loggerMock.Object);
    }

    private static TokenCreationRequest TokenFor(string? tenantId)
    {
        var request = new TokenCreationRequest { ClientId = "a-client", Scopes = new List<string> { "api" }, Lifetime = 3600 };
        if (tenantId != null) request.Claims["tenant_id"] = tenantId;
        return request;
    }

    private SigningCredentials NewCredentials(string kid) =>
        new(new RsaSecurityKey(RSA.Create(2048)) { KeyId = kid }, SecurityAlgorithms.RsaSha256);

    [Fact]
    public async Task A_platform_token_requested_through_a_tenant_is_signed_and_issued_by_the_platform()
    {
        _tenantContextMock.Setup(x => x.TenantId).Returns("default");
        var platform = new Tenant { Id = Oluso.Core.PlatformConstants.TenantId, Identifier = Oluso.Core.PlatformConstants.TenantId };
        _tenantStoreMock.Setup(x => x.GetByIdAsync(Oluso.Core.PlatformConstants.TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(platform);
        _signingCredentialStoreMock
            .Setup(x => x.GetSigningCredentialsAsync(Oluso.Core.PlatformConstants.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewCredentials("platform-key"));
        _issuerResolverMock.Setup(x => x.GetIssuerForTenantAsync(platform, It.IsAny<CancellationToken>())).ReturnsAsync("https://platform.oluso.io");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(await _tokenService.CreateAccessTokenAsync(TokenFor(Oluso.Core.PlatformConstants.TenantId)));

        jwt.Header.Kid.Should().Be("platform-key");
        jwt.Issuer.Should().Be("https://platform.oluso.io");
    }

    [Theory]
    [InlineData("default", "default")]
    [InlineData(null, "default")]
    [InlineData("_platform", "default")]
    [InlineData(null, null)]
    public async Task A_token_for_the_routed_tenant_or_routed_to_the_platform_uses_the_routed_signer(string? routed, string? tokenTenant)
    {
        _tenantContextMock.Setup(x => x.TenantId).Returns(routed);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(await _tokenService.CreateAccessTokenAsync(TokenFor(tokenTenant)));

        jwt.Header.Kid.Should().Be("test-key-id");
        jwt.Issuer.Should().Be("https://test.oluso.io");
    }

    [Fact]
    public async Task A_token_for_one_tenant_is_never_signed_through_another()
    {
        _tenantContextMock.Setup(x => x.TenantId).Returns("tenant-b");

        var act = () => _tokenService.CreateAccessTokenAsync(TokenFor("tenant-a"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("tenant-a");
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
        // Arrange — tenant_id flows through claims (from profile service or grant handler),
        // NOT from _tenantContext which is the request routing context.
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "openid" },
            Lifetime = 3600,
            Claims = new Dictionary<string, object> { ["tenant_id"] = "tenant-xyz" }
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
        // Arrange — tenant flows through claims (from profile service or grant handler),
        // NOT from _tenantContext routing context.
        var request = new TokenCreationRequest
        {
            SubjectId = "user-123",
            ClientId = "test-client",
            Scopes = new List<string> { "offline_access" },
            Claims = new Dictionary<string, object> { ["tenant_id"] = "tenant-xyz" }
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

    [Fact]
    public async Task The_id_token_from_a_code_carries_how_the_user_signed_in()
    {
        _resourceStoreMock.Setup(x => x.FindResourcesByScopeNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Resource>());
        _tenantSettingsMock.Setup(x => x.GetProtocolSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProtocolSettings());
        var grant = new Oluso.Core.Protocols.GrantResult
        {
            SubjectId = "user-123",
            SessionId = "session-1",
            Scopes = new List<string> { "openid" },
            Amr = new List<string> { "fido2", "hwk" },
            Acr = "urn:oluso:passkey"
        };
        var request = new Oluso.Core.Protocols.Models.TokenRequest
        {
            GrantType = "authorization_code",
            Client = new Oluso.Core.Protocols.Models.ValidatedClient { ClientId = "spa", AccessTokenLifetime = 3600, IdentityTokenLifetime = 300 }
        };

        var response = await _tokenService.CreateTokenResponseAsync(grant, request);

        var idToken = new JwtSecurityTokenHandler().ReadJwtToken(response.IdToken);
        idToken.Claims.Where(c => c.Type == "amr").Select(c => c.Value).Should().Equal("fido2", "hwk");
        idToken.Claims.Should().Contain(c => c.Type == "acr" && c.Value == "urn:oluso:passkey");
        idToken.Claims.Should().Contain(c => c.Type == "sid" && c.Value == "session-1");
    }

    [Fact]
    public async Task A_refresh_token_keeps_how_the_user_signed_in()
    {
        _resourceStoreMock.Setup(x => x.FindResourcesByScopeNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Resource>());
        _tenantSettingsMock.Setup(x => x.GetProtocolSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProtocolSettings());
        PersistedGrant? stored = null;
        _grantStoreMock.Setup(x => x.StoreAsync(It.IsAny<PersistedGrant>(), It.IsAny<CancellationToken>()))
            .Callback<PersistedGrant, CancellationToken>((g, _) => stored = g)
            .Returns(Task.CompletedTask);

        await _tokenService.CreateTokenResponseAsync(
            new Oluso.Core.Protocols.GrantResult
            {
                SubjectId = "user-123",
                Scopes = new List<string> { "openid", "offline_access" },
                Amr = new List<string> { "fido2", "hwk" },
                Acr = "urn:oluso:passkey"
            },
            new Oluso.Core.Protocols.Models.TokenRequest
            {
                GrantType = "authorization_code",
                Client = new Oluso.Core.Protocols.Models.ValidatedClient
                {
                    ClientId = "spa", AccessTokenLifetime = 3600, IdentityTokenLifetime = 300, AllowOfflineAccess = true,
                    AbsoluteRefreshTokenLifetime = 86400, SlidingRefreshTokenLifetime = 3600
                }
            });

        stored.Should().NotBeNull();
        var data = System.Text.Json.JsonSerializer.Deserialize<Oluso.Protocols.Grants.RefreshTokenData>(stored!.Data)!;
        data.Amr.Should().Equal("fido2", "hwk");
        data.Acr.Should().Be("urn:oluso:passkey");
    }

    private async Task<Oluso.Core.Protocols.Models.TokenResponse> RespondAsync(
        IEnumerable<string> scopes, IEnumerable<string> clientGrantTypes, string grantType = "authorization_code",
        TenantProtocolSettings? protocol = null, bool allowOfflineAccess = false)
    {
        _resourceStoreMock.Setup(x => x.FindResourcesByScopeNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Resource>());
        _tenantSettingsMock.Setup(x => x.GetProtocolSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(protocol ?? new TenantProtocolSettings());
        _grantStoreMock.Setup(x => x.StoreAsync(It.IsAny<PersistedGrant>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return await _tokenService.CreateTokenResponseAsync(
            new Oluso.Core.Protocols.GrantResult { SubjectId = "user-123", SessionId = "sid-1", Scopes = scopes.ToList() },
            new Oluso.Core.Protocols.Models.TokenRequest
            {
                GrantType = grantType,
                Client = new Oluso.Core.Protocols.Models.ValidatedClient
                {
                    ClientId = "matrix-client", AccessTokenLifetime = 300, IdentityTokenLifetime = 300,
                    AllowOfflineAccess = allowOfflineAccess, AllowedGrantTypes = clientGrantTypes.ToList(),
                    AbsoluteRefreshTokenLifetime = 86400, SlidingRefreshTokenLifetime = 3600
                }
            });
    }

    [Theory]
    [InlineData("urn:matrix:client:api:*", "authorization_code")]
    [InlineData("urn:matrix:client:api:*", "urn:ietf:params:oauth:grant-type:device_code")]
    [InlineData("urn:matrix:org.matrix.msc2967.client:api:*", "authorization_code")]
    public async Task A_matrix_api_scope_earns_a_refresh_token_without_offline_access(string apiScope, string grantType)
    {
        var response = await RespondAsync(
            new[] { "openid", apiScope, "urn:matrix:client:device:ABCDEF" },
            new[] { grantType, "refresh_token" }, grantType);

        response.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_matrix_api_scope_earns_no_refresh_token_when_the_client_may_not_refresh()
    {
        var response = await RespondAsync(new[] { "openid", "urn:matrix:client:api:*" }, new[] { "authorization_code" });

        response.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task A_matrix_api_scope_earns_no_refresh_token_when_the_tenant_turns_it_off()
    {
        var response = await RespondAsync(
            new[] { "openid", "urn:matrix:client:api:*" }, new[] { "authorization_code", "refresh_token" },
            protocol: new TenantProtocolSettings { RefreshImplyingScopes = new List<string>() });

        response.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task A_tenant_can_name_its_own_refresh_implying_scopes()
    {
        var response = await RespondAsync(
            new[] { "openid", "chat" }, new[] { "authorization_code", "refresh_token" },
            protocol: new TenantProtocolSettings { RefreshImplyingScopes = new List<string> { "chat" } });

        response.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Other_scopes_still_need_offline_access_for_a_refresh_token()
    {
        var without = await RespondAsync(new[] { "openid", "api" }, new[] { "authorization_code", "refresh_token" });
        var with = await RespondAsync(new[] { "openid", "api", "offline_access" }, new[] { "authorization_code", "refresh_token" },
            allowOfflineAccess: true);

        without.RefreshToken.Should().BeNull();
        with.RefreshToken.Should().NotBeNullOrEmpty();
    }
}
