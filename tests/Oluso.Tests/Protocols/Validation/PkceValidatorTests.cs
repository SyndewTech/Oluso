using FluentAssertions;
using Oluso.Core.Protocols.Models;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Validation;

/// <summary>
/// Unit tests for PKCE (RFC 7636) validation.
/// </summary>
public class PkceValidatorTests
{
    private readonly PkceValidator _validator = new();

    #region ValidateCodeChallenge Tests

    [Fact]
    public void ValidateCodeChallenge_WithMissingChallenge_WhenRequired_ReturnsFail()
    {
        var result = _validator.ValidateCodeChallenge(null, null, pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("code_challenge is required");
    }

    [Fact]
    public void ValidateCodeChallenge_WithMissingChallenge_WhenNotRequired_ReturnsSuccess()
    {
        var result = _validator.ValidateCodeChallenge(null, null, pkceRequired: false, allowPlainText: false);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateCodeChallenge_WithTooShort_ReturnsFail()
    {
        var shortChallenge = new string('a', 42); // Min is 43

        var result = _validator.ValidateCodeChallenge(shortChallenge, CodeChallengeMethods.Sha256, pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("43");
    }

    [Fact]
    public void ValidateCodeChallenge_WithTooLong_ReturnsFail()
    {
        var longChallenge = new string('a', 129); // Max is 128

        var result = _validator.ValidateCodeChallenge(longChallenge, CodeChallengeMethods.Sha256, pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("128");
    }

    [Fact]
    public void ValidateCodeChallenge_WithInvalidCharacters_ReturnsFail()
    {
        var invalidChallenge = new string('a', 43) + "!@#"; // Invalid base64url chars

        var result = _validator.ValidateCodeChallenge(invalidChallenge, CodeChallengeMethods.Sha256, pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("invalid characters");
    }

    [Fact]
    public void ValidateCodeChallenge_WithUnsupportedMethod_ReturnsFail()
    {
        var challenge = new string('a', 43);

        var result = _validator.ValidateCodeChallenge(challenge, "sha512", pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("Unsupported");
    }

    [Fact]
    public void ValidateCodeChallenge_WithPlainMethod_WhenNotAllowed_ReturnsFail()
    {
        var challenge = new string('a', 43);

        var result = _validator.ValidateCodeChallenge(challenge, CodeChallengeMethods.Plain, pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidRequest);
        result.ErrorDescription.Should().Contain("plain").And.Contain("not allowed");
    }

    [Fact]
    public void ValidateCodeChallenge_WithPlainMethod_WhenAllowed_ReturnsSuccess()
    {
        var challenge = new string('a', 43);

        var result = _validator.ValidateCodeChallenge(challenge, CodeChallengeMethods.Plain, pkceRequired: true, allowPlainText: true);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateCodeChallenge_WithS256Method_ReturnsSuccess()
    {
        var challenge = new string('a', 43);

        var result = _validator.ValidateCodeChallenge(challenge, CodeChallengeMethods.Sha256, pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateCodeChallenge_WithValidBase64UrlCharacters_ReturnsSuccess()
    {
        // Valid base64url: A-Z, a-z, 0-9, -, _
        var challenge = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdef0123456789-_";

        var result = _validator.ValidateCodeChallenge(challenge, CodeChallengeMethods.Sha256, pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateCodeChallenge_WithNullMethod_DefaultsToPlain()
    {
        var challenge = new string('a', 43);

        // null method defaults to "plain" which should fail if plain is not allowed
        var result = _validator.ValidateCodeChallenge(challenge, null, pkceRequired: true, allowPlainText: false);

        result.IsValid.Should().BeFalse();
        result.ErrorDescription.Should().Contain("plain");
    }

    #endregion

    #region ValidateCodeVerifier Tests

    [Fact]
    public void ValidateCodeVerifier_WithNullVerifier_ReturnsFail()
    {
        var result = _validator.ValidateCodeVerifier(null, "stored_challenge", CodeChallengeMethods.Sha256);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("code_verifier is required");
    }

    [Fact]
    public void ValidateCodeVerifier_WithTooShort_ReturnsFail()
    {
        var shortVerifier = new string('a', 42);

        var result = _validator.ValidateCodeVerifier(shortVerifier, "stored_challenge", CodeChallengeMethods.Sha256);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("43");
    }

    [Fact]
    public void ValidateCodeVerifier_WithTooLong_ReturnsFail()
    {
        var longVerifier = new string('a', 129);

        var result = _validator.ValidateCodeVerifier(longVerifier, "stored_challenge", CodeChallengeMethods.Sha256);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("128");
    }

    [Fact]
    public void ValidateCodeVerifier_WithInvalidCharacters_ReturnsFail()
    {
        var invalidVerifier = new string('a', 43) + "!@#";

        var result = _validator.ValidateCodeVerifier(invalidVerifier, "stored_challenge", CodeChallengeMethods.Sha256);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("invalid characters");
    }

    [Fact]
    public void ValidateCodeVerifier_PlainMethod_WithMatchingVerifier_ReturnsSuccess()
    {
        var verifier = new string('a', 43);
        var challenge = verifier; // Plain method: challenge == verifier

        var result = _validator.ValidateCodeVerifier(verifier, challenge, CodeChallengeMethods.Plain);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateCodeVerifier_PlainMethod_WithMismatch_ReturnsFail()
    {
        var verifier = new string('a', 43);
        var challenge = new string('b', 43);

        var result = _validator.ValidateCodeVerifier(verifier, challenge, CodeChallengeMethods.Plain);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        result.ErrorDescription.Should().Contain("does not match");
    }

    [Fact]
    public void ValidateCodeVerifier_S256Method_WithValidVerifier_ReturnsSuccess()
    {
        // Generate a verifier and its corresponding S256 challenge
        var verifier = _validator.GenerateCodeVerifier();
        var challenge = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Sha256);

        var result = _validator.ValidateCodeVerifier(verifier, challenge, CodeChallengeMethods.Sha256);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateCodeVerifier_S256Method_WithWrongVerifier_ReturnsFail()
    {
        var verifier = _validator.GenerateCodeVerifier();
        var challenge = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Sha256);

        // Use a different verifier
        var wrongVerifier = _validator.GenerateCodeVerifier();

        var result = _validator.ValidateCodeVerifier(wrongVerifier, challenge, CodeChallengeMethods.Sha256);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    #endregion

    #region GenerateCodeVerifier Tests

    [Fact]
    public void GenerateCodeVerifier_GeneratesValidLength()
    {
        var verifier = _validator.GenerateCodeVerifier();

        verifier.Length.Should().BeGreaterOrEqualTo(43);
        verifier.Length.Should().BeLessOrEqualTo(128);
    }

    [Fact]
    public void GenerateCodeVerifier_GeneratesValidCharacters()
    {
        var verifier = _validator.GenerateCodeVerifier();

        verifier.Should().MatchRegex(@"^[A-Za-z0-9\-_]+$");
    }

    [Fact]
    public void GenerateCodeVerifier_GeneratesUniqueValues()
    {
        var verifiers = Enumerable.Range(0, 100)
            .Select(_ => _validator.GenerateCodeVerifier())
            .ToList();

        verifiers.Distinct().Count().Should().Be(100);
    }

    #endregion

    #region GenerateCodeChallenge Tests

    [Fact]
    public void GenerateCodeChallenge_PlainMethod_ReturnsVerifier()
    {
        var verifier = "test_verifier_with_enough_length_for_pkce";

        var challenge = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Plain);

        challenge.Should().Be(verifier);
    }

    [Fact]
    public void GenerateCodeChallenge_S256Method_ReturnsDifferentValue()
    {
        var verifier = _validator.GenerateCodeVerifier();

        var challenge = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Sha256);

        challenge.Should().NotBe(verifier);
    }

    [Fact]
    public void GenerateCodeChallenge_S256Method_IsDeterministic()
    {
        var verifier = "test_verifier_with_enough_length_for_pkce";

        var challenge1 = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Sha256);
        var challenge2 = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Sha256);

        challenge1.Should().Be(challenge2);
    }

    [Fact]
    public void GenerateCodeChallenge_S256Method_ProducesValidBase64Url()
    {
        var verifier = _validator.GenerateCodeVerifier();

        var challenge = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Sha256);

        // Should not contain padding or invalid chars
        challenge.Should().NotContain("=");
        challenge.Should().NotContain("+");
        challenge.Should().NotContain("/");
        challenge.Should().MatchRegex(@"^[A-Za-z0-9\-_]+$");
    }

    [Fact]
    public void GenerateCodeChallenge_S256_KnownVector()
    {
        // RFC 7636 Appendix B test vector
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Sha256);

        challenge.Should().Be(expectedChallenge);
    }

    #endregion

    #region End-to-End PKCE Flow Tests

    [Fact]
    public void FullPkceFlow_S256_Succeeds()
    {
        // 1. Client generates verifier
        var verifier = _validator.GenerateCodeVerifier();

        // 2. Client computes challenge and sends to /authorize
        var challenge = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Sha256);

        // 3. Server validates challenge at /authorize
        var challengeResult = _validator.ValidateCodeChallenge(challenge, CodeChallengeMethods.Sha256, pkceRequired: true, allowPlainText: false);
        challengeResult.IsValid.Should().BeTrue();

        // 4. Server stores challenge, client exchanges code with verifier at /token
        var verifierResult = _validator.ValidateCodeVerifier(verifier, challenge, CodeChallengeMethods.Sha256);
        verifierResult.IsValid.Should().BeTrue();
    }

    [Fact]
    public void FullPkceFlow_Plain_Succeeds()
    {
        var verifier = _validator.GenerateCodeVerifier();
        var challenge = _validator.GenerateCodeChallenge(verifier, CodeChallengeMethods.Plain);

        var challengeResult = _validator.ValidateCodeChallenge(challenge, CodeChallengeMethods.Plain, pkceRequired: true, allowPlainText: true);
        challengeResult.IsValid.Should().BeTrue();

        var verifierResult = _validator.ValidateCodeVerifier(verifier, challenge, CodeChallengeMethods.Plain);
        verifierResult.IsValid.Should().BeTrue();
    }

    #endregion
}
