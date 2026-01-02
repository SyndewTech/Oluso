using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Common;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Protocols.Grants;
using Xunit;

namespace Oluso.Tests.Protocols.Security;

/// <summary>
/// Security tests for the Authorization Code grant handler.
/// These tests validate RFC 6749 Section 4.1 and security requirements:
/// - One-time use of authorization codes
/// - Proper expiration handling
/// - Client binding validation
/// - Redirect URI validation
/// - PKCE validation
/// </summary>
public class AuthorizationCodeGrantTests
{
    private readonly Mock<IAuthorizationCodeStore> _codeStore;
    private readonly Mock<IPkceValidator> _pkceValidator;
    private readonly Mock<IProfileService> _profileService;
    private readonly Mock<IPersistedGrantStore> _grantStore;
    private readonly Mock<IClientStore> _clientStore;
    private readonly AuthorizationCodeGrantHandler _handler;

    private const string TestCode = "test-authorization-code";
    private const string TestClientId = "test-client";
    private const string TestSubjectId = "user-123";
    private const string TestSessionId = "session-456";
    private const string TestRedirectUri = "https://example.com/callback";

    public AuthorizationCodeGrantTests()
    {
        _codeStore = new Mock<IAuthorizationCodeStore>();
        _pkceValidator = new Mock<IPkceValidator>();
        _profileService = new Mock<IProfileService>();
        _grantStore = new Mock<IPersistedGrantStore>();
        _clientStore = new Mock<IClientStore>();

        // Mock the actual interface methods, not the extension methods
        _profileService.Setup(x => x.IsActiveAsync(It.IsAny<IsActiveContext>()))
            .Returns(Task.CompletedTask);
        _profileService.Setup(x => x.GetProfileDataAsync(It.IsAny<ProfileDataRequestContext>()))
            .Returns(Task.CompletedTask);

        // Mock client store
        _clientStore.Setup(x => x.FindClientByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client { ClientId = TestClientId });

        _handler = new AuthorizationCodeGrantHandler(
            _codeStore.Object,
            _pkceValidator.Object,
            _profileService.Object,
            _grantStore.Object,
            _clientStore.Object,
            NullLogger<AuthorizationCodeGrantHandler>.Instance);
    }

    #region Code Reuse / Replay Prevention Tests

    [Fact]
    public async Task HandleAsync_WithMissingCode_ReturnsInvalidGrant()
    {
        var request = new TokenRequest
        {
            Code = null,
            Client = CreateValidatedClient()
        };

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("missing");
    }

    [Fact]
    public async Task HandleAsync_WithNonExistentCode_ReturnsInvalidGrant()
    {
        _codeStore.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode?)null);

        var request = new TokenRequest
        {
            Code = TestCode,
            Client = CreateValidatedClient()
        };

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("Invalid");
    }

    [Fact]
    public async Task HandleAsync_WithAlreadyConsumedCode_ReturnsInvalidGrant()
    {
        var authCode = CreateValidAuthCode();
        authCode.IsConsumed = true;

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("already been used");

        // Should remove the code to prevent further replay attempts
        _codeStore.Verify(x => x.RemoveAsync(TestCode, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithValidCode_ConsumesCodeAfterUse()
    {
        var authCode = CreateValidAuthCode();

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();

        // Code should be removed (one-time use)
        _codeStore.Verify(x => x.RemoveAsync(TestCode, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SecondRequestWithSameCode_ShouldFail()
    {
        // First request - code exists and is valid
        var authCode = CreateValidAuthCode();
        _codeStore.SetupSequence(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode)
            .ReturnsAsync((AuthorizationCode?)null); // Second request - code was removed

        var request = CreateValidTokenRequest();

        // First request should succeed
        var result1 = await _handler.HandleAsync(request);
        result1.IsValid.Should().BeTrue();

        // Second request should fail (code no longer exists)
        var result2 = await _handler.HandleAsync(request);
        result2.IsValid.Should().BeFalse();
        result2.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    /// <summary>
    /// RFC 6749 Section 4.1.2:
    /// "If an authorization code is used more than once, the authorization server MUST deny the request
    /// and SHOULD revoke (when possible) all tokens previously issued based on that authorization code."
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithAlreadyConsumedCode_RevokesAllSessionTokens()
    {
        // Setup: Code was already consumed (replayed)
        var authCode = CreateValidAuthCode();
        authCode.IsConsumed = true;
        authCode.SessionId = TestSessionId;

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        // Should reject the replay attempt
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("already been used");

        // RFC 6749: SHOULD revoke all tokens previously issued based on that authorization code
        _grantStore.Verify(x => x.RemoveAllAsync(
            It.Is<PersistedGrantFilter>(f =>
                f.SubjectId == TestSubjectId &&
                f.ClientId == TestClientId &&
                f.SessionId == TestSessionId),
            It.IsAny<CancellationToken>()), Times.Once);

        // Should also remove the compromised code
        _codeStore.Verify(x => x.RemoveAsync(TestCode, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Expiration Tests

    [Fact]
    public async Task HandleAsync_WithExpiredCode_ReturnsInvalidGrant()
    {
        var authCode = CreateValidAuthCode();
        authCode.Expiration = DateTime.UtcNow.AddMinutes(-1); // Expired

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("expired");

        // Should remove the expired code
        _codeStore.Verify(x => x.RemoveAsync(TestCode, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithCodeAboutToExpire_Succeeds()
    {
        var authCode = CreateValidAuthCode();
        authCode.Expiration = DateTime.UtcNow.AddSeconds(5); // About to expire but still valid

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Client Binding Tests

    [Fact]
    public async Task HandleAsync_WithDifferentClient_ReturnsInvalidGrant()
    {
        var authCode = CreateValidAuthCode();
        authCode.ClientId = "original-client";

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();
        request.Client = new ValidatedClient { ClientId = "different-client" }; // Wrong client!

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("different client");
    }

    [Fact]
    public async Task HandleAsync_WithNullClient_ReturnsInvalidGrant()
    {
        var authCode = CreateValidAuthCode();

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();
        request.Client = null;

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("different client");
    }

    #endregion

    #region Redirect URI Validation Tests

    [Fact]
    public async Task HandleAsync_WithMismatchedRedirectUri_ReturnsInvalidGrant()
    {
        var authCode = CreateValidAuthCode();
        authCode.RedirectUri = "https://original.com/callback";

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();
        request.RedirectUri = "https://attacker.com/callback"; // Different redirect_uri!

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("redirect_uri");
    }

    [Fact]
    public async Task HandleAsync_WithCaseDifferentRedirectUri_ReturnsInvalidGrant()
    {
        var authCode = CreateValidAuthCode();
        authCode.RedirectUri = "https://example.com/callback";

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();
        request.RedirectUri = "https://Example.com/callback"; // Case different!

        var result = await _handler.HandleAsync(request);

        // Per RFC 6749, redirect_uri comparison should be case-sensitive
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    [Fact]
    public async Task HandleAsync_WithMissingRedirectUri_WhenOriginalHadIt_ReturnsInvalidGrant()
    {
        var authCode = CreateValidAuthCode();
        authCode.RedirectUri = "https://example.com/callback";

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();
        request.RedirectUri = null; // Missing redirect_uri

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithNoOriginalRedirectUri_Succeeds()
    {
        var authCode = CreateValidAuthCode();
        authCode.RedirectUri = null; // No redirect_uri in original request

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();
        request.RedirectUri = null;

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region PKCE Validation Tests

    [Fact]
    public async Task HandleAsync_WithPkce_ValidCodeVerifier_Succeeds()
    {
        var authCode = CreateValidAuthCode();
        authCode.CodeChallenge = "challenge-value";
        authCode.CodeChallengeMethod = "S256";

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        _pkceValidator.Setup(x => x.ValidateCodeVerifier("verifier", "challenge-value", "S256"))
            .Returns(ValidationResult.Success());

        var request = CreateValidTokenRequest();
        request.CodeVerifier = "verifier";

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();
        _pkceValidator.Verify(x => x.ValidateCodeVerifier("verifier", "challenge-value", "S256"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithPkce_InvalidCodeVerifier_ReturnsError()
    {
        var authCode = CreateValidAuthCode();
        authCode.CodeChallenge = "challenge-value";
        authCode.CodeChallengeMethod = "S256";

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        _pkceValidator.Setup(x => x.ValidateCodeVerifier(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValidationResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid code verifier"));

        var request = CreateValidTokenRequest();
        request.CodeVerifier = "wrong-verifier";

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    [Fact]
    public async Task HandleAsync_WithPkce_MissingCodeVerifier_ReturnsError()
    {
        var authCode = CreateValidAuthCode();
        authCode.CodeChallenge = "challenge-value";
        authCode.CodeChallengeMethod = "S256";

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        _pkceValidator.Setup(x => x.ValidateCodeVerifier(null, It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValidationResult.Failure(OidcConstants.Errors.InvalidGrant, "code_verifier is required"));

        var request = CreateValidTokenRequest();
        request.CodeVerifier = null; // Missing!

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ClientRequiresPkce_ButNoCodeChallenge_ReturnsError()
    {
        var authCode = CreateValidAuthCode();
        authCode.CodeChallenge = null; // No PKCE was used in authorization

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var client = CreateValidatedClient();
        client.RequirePkce = true; // Client requires PKCE

        var request = CreateValidTokenRequest();
        request.Client = client;

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("PKCE");
    }

    [Fact]
    public async Task HandleAsync_NoPkceRequired_WithoutCodeVerifier_Succeeds()
    {
        var authCode = CreateValidAuthCode();
        authCode.CodeChallenge = null; // No PKCE

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();
        request.CodeVerifier = null;
        request.Client!.RequirePkce = false;

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region User State Tests

    [Fact]
    public async Task HandleAsync_UserNoLongerActive_ReturnsInvalidGrant()
    {
        var authCode = CreateValidAuthCode();

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        // Mock the interface method to set IsActive = false
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
    public async Task HandleAsync_WithNoSubject_ClientCredentialsLike_Succeeds()
    {
        var authCode = CreateValidAuthCode();
        authCode.SubjectId = null; // No user subject

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();
        result.SubjectId.Should().BeNull();
    }

    #endregion

    #region Successful Token Request Tests

    [Fact]
    public async Task HandleAsync_ValidRequest_ReturnsCorrectGrant()
    {
        var authCode = CreateValidAuthCode();
        authCode.Scopes = new List<string> { "openid", "profile", "email" };
        authCode.SessionId = "session-123";
        authCode.Nonce = "nonce-456";
        authCode.Claims = new Dictionary<string, string>
        {
            ["custom_claim"] = "custom_value"
        };

        _codeStore.Setup(x => x.GetAsync(TestCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        // Mock the interface method to add claims to the context
        _profileService.Setup(x => x.GetProfileDataAsync(It.IsAny<ProfileDataRequestContext>()))
            .Callback<ProfileDataRequestContext>(ctx =>
            {
                ctx.IssuedClaims.Add(new System.Security.Claims.Claim("name", "Test User"));
                ctx.IssuedClaims.Add(new System.Security.Claims.Claim("email", "test@example.com"));
            })
            .Returns(Task.CompletedTask);

        var request = CreateValidTokenRequest();

        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue();
        result.SubjectId.Should().Be(TestSubjectId);
        result.SessionId.Should().Be("session-123");
        result.Scopes.Should().Contain(new[] { "openid", "profile", "email" });
        result.Claims.Should().ContainKey("nonce");
        result.Claims.Should().ContainKey("name");
        result.Claims.Should().ContainKey("email");
        result.Claims.Should().ContainKey("custom_claim");
    }

    #endregion

    #region Helpers

    private static ValidatedClient CreateValidatedClient()
    {
        return new ValidatedClient
        {
            ClientId = TestClientId,
            RequirePkce = false
        };
    }

    private static AuthorizationCode CreateValidAuthCode()
    {
        return new AuthorizationCode
        {
            ClientId = TestClientId,
            SubjectId = TestSubjectId,
            SessionId = TestSessionId,
            RedirectUri = TestRedirectUri,
            Expiration = DateTime.UtcNow.AddMinutes(5),
            IsConsumed = false,
            Scopes = new List<string> { "openid", "profile" },
            CodeChallenge = null, // No PKCE by default
            CodeChallengeMethod = null
        };
    }

    private static TokenRequest CreateValidTokenRequest()
    {
        return new TokenRequest
        {
            Code = TestCode,
            Client = CreateValidatedClient(),
            RedirectUri = TestRedirectUri,
            CodeVerifier = null
        };
    }

    #endregion
}
