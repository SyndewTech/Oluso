using FluentAssertions;
using Oluso.Core.Protocols.Models;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Validation;

/// <summary>
/// Unit tests for redirect URI validation (RFC 6749, RFC 8252).
/// </summary>
public class RedirectUriValidatorTests
{
    private readonly RedirectUriValidator _validator = new();

    #region ValidateAsync - Basic Tests

    [Fact]
    public async Task ValidateAsync_WithNullUri_ReturnsFail()
    {
        var result = await _validator.ValidateAsync(null, new[] { "https://example.com/callback" });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("redirect_uri is required");
    }

    [Fact]
    public async Task ValidateAsync_WithEmptyUri_ReturnsFail()
    {
        var result = await _validator.ValidateAsync("", new[] { "https://example.com/callback" });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidUri_ReturnsFail()
    {
        var result = await _validator.ValidateAsync("not-a-valid-uri", new[] { "https://example.com/callback" });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("not a valid URI");
    }

    [Fact]
    public async Task ValidateAsync_WithRelativeUri_ReturnsFail()
    {
        var result = await _validator.ValidateAsync("/callback", new[] { "https://example.com/callback" });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
    }

    #endregion

    #region ValidateAsync - Exact Match Tests

    [Fact]
    public async Task ValidateAsync_WithExactMatch_ReturnsSuccess()
    {
        var uri = "https://example.com/callback";

        var result = await _validator.ValidateAsync(uri, new[] { uri });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithExactMatchFromMultiple_ReturnsSuccess()
    {
        var uri = "https://example.com/callback2";
        var allowed = new[]
        {
            "https://example.com/callback1",
            "https://example.com/callback2",
            "https://example.com/callback3"
        };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithNoMatch_ReturnsFail()
    {
        var uri = "https://example.com/wrong";
        var allowed = new[] { "https://example.com/callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("not registered");
    }

    [Fact]
    public async Task ValidateAsync_IsCaseSensitive()
    {
        var uri = "https://example.com/Callback"; // Capital C
        var allowed = new[] { "https://example.com/callback" }; // lowercase c

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithQueryString_RequiresExactMatch()
    {
        var uri = "https://example.com/callback?foo=bar";
        var allowed = new[] { "https://example.com/callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithQueryString_ExactMatchSucceeds()
    {
        var uri = "https://example.com/callback?foo=bar";
        var allowed = new[] { "https://example.com/callback?foo=bar" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateAsync - Fragment Tests (Implicit/Hybrid)

    [Fact]
    public async Task ValidateAsync_ImplicitFlow_WithFragment_ReturnsFail()
    {
        var uri = "https://example.com/callback#fragment";
        var allowed = new[] { "https://example.com/callback#fragment" };

        var result = await _validator.ValidateAsync(uri, allowed, isImplicitOrHybridFlow: true);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("fragment");
    }

    [Fact]
    public async Task ValidateAsync_CodeFlow_WithFragment_ReturnsSuccess()
    {
        var uri = "https://example.com/callback#fragment";
        var allowed = new[] { "https://example.com/callback#fragment" };

        var result = await _validator.ValidateAsync(uri, allowed, isImplicitOrHybridFlow: false);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateAsync - Loopback Tests (RFC 8252)

    [Fact]
    public async Task ValidateAsync_Loopback127001_WithDifferentPort_ReturnsSuccess()
    {
        var uri = "http://127.0.0.1:54321/callback";
        var allowed = new[] { "http://127.0.0.1:12345/callback" }; // Different port

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_LoopbackLocalhost_WithDifferentPort_ReturnsSuccess()
    {
        var uri = "http://localhost:54321/callback";
        var allowed = new[] { "http://localhost:12345/callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_LoopbackIPv6_WithDifferentPort_ReturnsSuccess()
    {
        var uri = "http://[::1]:54321/callback";
        var allowed = new[] { "http://[::1]:12345/callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_Loopback_WithDifferentPath_ReturnsFail()
    {
        var uri = "http://127.0.0.1:54321/wrong-path";
        var allowed = new[] { "http://127.0.0.1:12345/callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_Loopback_WithDifferentScheme_ReturnsFail()
    {
        var uri = "https://127.0.0.1:54321/callback";
        var allowed = new[] { "http://127.0.0.1:12345/callback" }; // http vs https

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NonLoopback_WithDifferentPort_ReturnsFail()
    {
        var uri = "https://example.com:8080/callback";
        var allowed = new[] { "https://example.com:443/callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region ValidateAsync - Custom Scheme Tests

    [Fact]
    public async Task ValidateAsync_CustomScheme_WithExactMatch_ReturnsSuccess()
    {
        var uri = "myapp://callback";
        var allowed = new[] { "myapp://callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CustomScheme_CaseInsensitive_ReturnsSuccess()
    {
        var uri = "MyApp://callback";
        var allowed = new[] { "myapp://callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CustomScheme_WithMismatch_ReturnsFail()
    {
        var uri = "myapp://callback";
        var allowed = new[] { "otherapp://callback" };

        var result = await _validator.ValidateAsync(uri, allowed);

        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region ValidatePostLogoutAsync Tests

    [Fact]
    public async Task ValidatePostLogoutAsync_WithNullUri_ReturnsSuccess()
    {
        var result = await _validator.ValidatePostLogoutAsync(null, new[] { "https://example.com/logout" });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePostLogoutAsync_WithEmptyUri_ReturnsSuccess()
    {
        var result = await _validator.ValidatePostLogoutAsync("", new[] { "https://example.com/logout" });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePostLogoutAsync_WithInvalidUri_ReturnsFail()
    {
        var result = await _validator.ValidatePostLogoutAsync("not-valid", new[] { "https://example.com/logout" });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
    }

    [Fact]
    public async Task ValidatePostLogoutAsync_WithExactMatch_ReturnsSuccess()
    {
        var uri = "https://example.com/logout";

        var result = await _validator.ValidatePostLogoutAsync(uri, new[] { uri });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatePostLogoutAsync_WithNoMatch_ReturnsFail()
    {
        var uri = "https://example.com/wrong";
        var allowed = new[] { "https://example.com/logout" };

        var result = await _validator.ValidatePostLogoutAsync(uri, allowed);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("not registered");
    }

    #endregion

    #region IsNativeClient Tests

    [Fact]
    public void IsNativeClient_WithLoopback127001_ReturnsTrue()
    {
        var result = _validator.IsNativeClient("http://127.0.0.1:12345/callback");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsNativeClient_WithLoopbackLocalhost_ReturnsTrue()
    {
        var result = _validator.IsNativeClient("http://localhost:12345/callback");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsNativeClient_WithLoopbackIPv6_ReturnsTrue()
    {
        var result = _validator.IsNativeClient("http://[::1]:12345/callback");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsNativeClient_WithCustomScheme_ReturnsTrue()
    {
        var result = _validator.IsNativeClient("myapp://callback");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsNativeClient_WithHttps_ReturnsFalse()
    {
        var result = _validator.IsNativeClient("https://example.com/callback");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsNativeClient_WithInvalidUri_ReturnsFalse()
    {
        var result = _validator.IsNativeClient("not-a-uri");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsNativeClient_WithUrn_ReturnsFalse()
    {
        // URN is not considered a native client scheme
        var result = _validator.IsNativeClient("urn:ietf:wg:oauth:2.0:oob");

        result.Should().BeFalse();
    }

    #endregion
}
