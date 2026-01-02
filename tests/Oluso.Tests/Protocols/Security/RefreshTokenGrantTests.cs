using System.Text.Json;
using FluentAssertions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;
using Oluso.Protocols.Grants;
using Xunit;

namespace Oluso.Tests.Protocols.Security;

/// <summary>
/// Security tests for the Refresh Token grant handler.
/// These tests validate:
/// - Token replay protection (OneTimeOnly usage)
/// - Token rotation security
/// - Client binding
/// - Expiration handling (absolute and sliding)
/// - User access revocation
/// </summary>
public class RefreshTokenGrantTests
{
    private readonly Mock<IPersistedGrantStore> _grantStore;
    private readonly Mock<IProfileService> _profileService;
    private readonly Mock<IClientStore> _clientStore;
    private readonly Mock<IOlusoUserService> _userService;
    private readonly RefreshTokenGrantHandler _handler;

    private const string TestRefreshToken = "refresh-token-value";
    private const string TestClientId = "test-client";
    private const string TestSubjectId = "user-123";
    private const string TestSessionId = "session-456";

    public RefreshTokenGrantTests()
    {
        _grantStore = new Mock<IPersistedGrantStore>();
        _profileService = new Mock<IProfileService>();
        _clientStore = new Mock<IClientStore>();
        _userService = new Mock<IOlusoUserService>();

        // Default setup: user is active
        _profileService.Setup(x => x.IsActiveAsync(It.IsAny<IsActiveContext>()))
            .Returns(Task.CompletedTask);
        _profileService.Setup(x => x.GetProfileDataAsync(It.IsAny<ProfileDataRequestContext>()))
            .Returns(Task.CompletedTask);

        // Default setup: client allows all users
        _clientStore.Setup(x => x.FindClientByIdAsync(TestClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client
            {
                ClientId = TestClientId,
                AllowedUsers = new List<ClientAllowedUser>(),
                AllowedRoles = new List<ClientAllowedRole>()
            });

        _handler = new RefreshTokenGrantHandler(
            _grantStore.Object,
            _profileService.Object,
            _clientStore.Object,
            _userService.Object);
    }

    #region Token Replay Protection Tests

    [Fact]
    public async Task HandleAsync_OneTimeOnly_WhenTokenAlreadyConsumed_ReturnsError()
    {
        var grant = CreateValidGrant();
        grant.ConsumedTime = DateTime.UtcNow.AddMinutes(-5); // Already consumed!

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest(refreshTokenUsage: TokenUsage.OneTimeOnly);

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("already been used");
    }

    [Fact]
    public async Task HandleAsync_OneTimeOnly_WhenTokenAlreadyConsumed_RevokesAllSessionTokens()
    {
        var grant = CreateValidGrant();
        grant.ConsumedTime = DateTime.UtcNow.AddMinutes(-5); // Already consumed!
        grant.SessionId = TestSessionId;

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest(refreshTokenUsage: TokenUsage.OneTimeOnly);

        await _handler.HandleAsync(request);

        // Should revoke ALL tokens for this session (security measure)
        _grantStore.Verify(x => x.RemoveAllAsync(
            It.Is<PersistedGrantFilter>(f =>
                f.SubjectId == TestSubjectId &&
                f.ClientId == TestClientId &&
                f.SessionId == TestSessionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OneTimeOnly_MarksTokenAsConsumed()
    {
        var grant = CreateValidGrant();
        grant.ConsumedTime = null; // Not yet consumed

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest(refreshTokenUsage: TokenUsage.OneTimeOnly);

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();

        // Token should be marked as consumed
        _grantStore.Verify(x => x.StoreAsync(
            It.Is<PersistedGrant>(g => g.ConsumedTime.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ReUse_AllowsMultipleUses()
    {
        var grant = CreateValidGrant();
        grant.ConsumedTime = DateTime.UtcNow.AddMinutes(-5); // Previously used

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest(refreshTokenUsage: TokenUsage.ReUse);

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue(); // ReUse allows multiple uses
    }

    #endregion

    #region Client Binding Tests

    [Fact]
    public async Task HandleAsync_WithMissingToken_ReturnsError()
    {
        var request = new TokenRequest
        {
            RefreshToken = null,
            Client = CreateValidatedClient()
        };

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("missing");
    }

    [Fact]
    public async Task HandleAsync_WithNonExistentToken_ReturnsError()
    {
        _grantStore.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedGrant?)null);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("Invalid");
    }

    [Fact]
    public async Task HandleAsync_WithDifferentClient_ReturnsError()
    {
        var grant = CreateValidGrant();
        grant.ClientId = "original-client"; // Different from request client

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("different client");
    }

    [Fact]
    public async Task HandleAsync_WithWrongGrantType_ReturnsError()
    {
        var grant = CreateValidGrant();
        grant.Type = "authorization_code"; // Wrong type!

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("Invalid token type");
    }

    #endregion

    #region Expiration Tests

    [Fact]
    public async Task HandleAsync_WithExpiredToken_ReturnsError()
    {
        var grant = CreateValidGrant();
        grant.Expiration = DateTime.UtcNow.AddMinutes(-1); // Expired!

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("expired");
    }

    [Fact]
    public async Task HandleAsync_WithExpiredToken_RemovesToken()
    {
        var grant = CreateValidGrant();
        grant.Expiration = DateTime.UtcNow.AddMinutes(-1); // Expired!

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest();

        await _handler.HandleAsync(request);

        _grantStore.Verify(x => x.RemoveAsync(TestRefreshToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SlidingExpiration_WhenInactiveTooLong_ReturnsError()
    {
        var grant = CreateValidGrant();
        grant.ConsumedTime = DateTime.UtcNow.AddHours(-25); // Last used 25 hours ago

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest(
            refreshTokenExpiration: TokenExpiration.Sliding,
            slidingRefreshTokenLifetime: 3600 * 24); // 24 hours sliding window

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("inactivity");
    }

    #endregion

    #region User State Tests

    [Fact]
    public async Task HandleAsync_UserNoLongerActive_ReturnsError()
    {
        var grant = CreateValidGrant();

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        // User is no longer active
        _profileService.Setup(x => x.IsActiveAsync(It.IsAny<IsActiveContext>()))
            .Callback<IsActiveContext>(ctx => ctx.IsActive = false)
            .Returns(Task.CompletedTask);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("not active");
    }

    [Fact]
    public async Task HandleAsync_UserNoLongerActive_RemovesToken()
    {
        var grant = CreateValidGrant();

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        _profileService.Setup(x => x.IsActiveAsync(It.IsAny<IsActiveContext>()))
            .Callback<IsActiveContext>(ctx => ctx.IsActive = false)
            .Returns(Task.CompletedTask);

        var request = CreateValidTokenRequest();

        await _handler.HandleAsync(request);

        _grantStore.Verify(x => x.RemoveAsync(TestRefreshToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UserNoLongerInAllowedUsers_ReturnsAccessDenied()
    {
        var grant = CreateValidGrant();

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        // Client now restricts access to specific users (not including our test user)
        _clientStore.Setup(x => x.FindClientByIdAsync(TestClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client
            {
                ClientId = TestClientId,
                AllowedUsers = new List<ClientAllowedUser>
                {
                    new() { SubjectId = "other-user" }
                },
                AllowedRoles = new List<ClientAllowedRole>()
            });

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.AccessDenied);
    }

    #endregion

    #region Scope Reduction Tests

    [Fact]
    public async Task HandleAsync_RequestingReducedScopes_Succeeds()
    {
        var grant = CreateValidGrant(scopes: new[] { "openid", "profile", "email" });

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest();
        request.RequestedScopes = new List<string> { "openid", "profile" }; // Subset

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();
        result.Scopes.Should().BeEquivalentTo(new[] { "openid", "profile" });
    }

    [Fact]
    public async Task HandleAsync_RequestingExpandedScopes_ReturnsError()
    {
        var grant = CreateValidGrant(scopes: new[] { "openid", "profile" });

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest();
        request.RequestedScopes = new List<string> { "openid", "profile", "email" }; // Includes email not in original

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidScope);
    }

    #endregion

    #region Token Rotation Signal Tests

    [Fact]
    public async Task HandleAsync_OneTimeOnly_SignalsRotation()
    {
        var grant = CreateValidGrant();

        _grantStore.Setup(x => x.GetAsync(TestRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);

        var request = CreateValidTokenRequest(refreshTokenUsage: TokenUsage.OneTimeOnly);

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();
        result.CustomData.Should().ContainKey("rotate_refresh_token");
        result.CustomData["rotate_refresh_token"].Should().Be(true);
    }

    #endregion

    #region Helpers

    private static ValidatedClient CreateValidatedClient(
        TokenUsage refreshTokenUsage = TokenUsage.OneTimeOnly,
        TokenExpiration refreshTokenExpiration = TokenExpiration.Absolute,
        int slidingRefreshTokenLifetime = 3600)
    {
        return new ValidatedClient
        {
            ClientId = TestClientId,
            RefreshTokenUsage = (int)refreshTokenUsage,
            RefreshTokenExpiration = (int)refreshTokenExpiration,
            SlidingRefreshTokenLifetime = slidingRefreshTokenLifetime,
            AbsoluteRefreshTokenLifetime = 86400
        };
    }

    private static TokenRequest CreateValidTokenRequest(
        TokenUsage refreshTokenUsage = TokenUsage.OneTimeOnly,
        TokenExpiration refreshTokenExpiration = TokenExpiration.Absolute,
        int slidingRefreshTokenLifetime = 3600)
    {
        return new TokenRequest
        {
            RefreshToken = TestRefreshToken,
            Client = CreateValidatedClient(refreshTokenUsage, refreshTokenExpiration, slidingRefreshTokenLifetime),
            RequestedScopes = new List<string>()
        };
    }

    private static PersistedGrant CreateValidGrant(string[]? scopes = null)
    {
        var tokenData = new RefreshTokenData
        {
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            Scopes = scopes ?? new[] { "openid", "profile" },
            Claims = null
        };

        return new PersistedGrant
        {
            Key = TestRefreshToken,
            Type = "refresh_token",
            ClientId = TestClientId,
            SubjectId = TestSubjectId,
            SessionId = TestSessionId,
            CreationTime = DateTime.UtcNow.AddHours(-1),
            Expiration = DateTime.UtcNow.AddDays(7),
            ConsumedTime = null,
            Data = JsonSerializer.Serialize(tokenData)
        };
    }

    #endregion
}
