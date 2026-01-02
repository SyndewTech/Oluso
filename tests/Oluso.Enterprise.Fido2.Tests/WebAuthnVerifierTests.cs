using FluentAssertions;
using Oluso.Enterprise.Fido2.Configuration;
using Oluso.Enterprise.Fido2.Services;
using Xunit;

namespace Oluso.Enterprise.Fido2.Tests;

public class Fido2OptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveReasonableDefaults()
    {
        // Arrange & Act
        var options = new Fido2Options();

        // Assert
        options.Timeout.Should().Be(60000);
        options.AttestationConveyancePreference.Should().Be("none");
        options.UserVerificationRequirement.Should().Be("preferred");
        options.ResidentKeyRequirement.Should().Be("preferred");
        options.MaxCredentialsPerUser.Should().Be(10);
        options.StoreAttestationData.Should().BeFalse();
    }

    [Fact]
    public void RelyingPartyName_ShouldBeConfigurable()
    {
        // Arrange
        var options = new Fido2Options
        {
            RelyingPartyName = "My Application"
        };

        // Assert
        options.RelyingPartyName.Should().Be("My Application");
    }

    [Fact]
    public void RelyingPartyId_ShouldBeConfigurable()
    {
        // Arrange
        var options = new Fido2Options
        {
            RelyingPartyId = "example.com"
        };

        // Assert
        options.RelyingPartyId.Should().Be("example.com");
    }

    [Fact]
    public void Origins_ShouldBeConfigurable()
    {
        // Arrange
        var options = new Fido2Options();
        options.Origins.Add("https://example.com");
        options.Origins.Add("https://app.example.com");

        // Assert
        options.Origins.Should().Contain("https://example.com");
        options.Origins.Should().Contain("https://app.example.com");
    }
}

public class Fido2CredentialTests
{
    [Fact]
    public void Fido2Credential_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var credential = new Fido2Credential
        {
            CredentialId = "credential123",
            PublicKey = "publickey123",
            UserId = "user123",
            UserHandle = "handle123"
        };

        // Assert
        credential.CredentialId.Should().Be("credential123");
        credential.PublicKey.Should().Be("publickey123");
        credential.UserId.Should().Be("user123");
        credential.IsActive.Should().BeTrue();
        credential.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Fido2Credential_ShouldSupportOptionalProperties()
    {
        // Arrange
        var credential = new Fido2Credential
        {
            CredentialId = "credential123",
            PublicKey = "publickey123",
            UserId = "user123",
            UserHandle = "handle123",
            DisplayName = "My Passkey",
            AuthenticatorType = AuthenticatorType.Platform,
            AaGuid = Guid.NewGuid(),
            SignatureCounter = 5,
            IsDiscoverable = true
        };

        // Assert
        credential.DisplayName.Should().Be("My Passkey");
        credential.AuthenticatorType.Should().Be(AuthenticatorType.Platform);
        credential.SignatureCounter.Should().Be(5u);
        credential.IsDiscoverable.Should().BeTrue();
    }
}

public class Fido2RegistrationResultTests
{
    [Fact]
    public void SuccessfulResult_ShouldIndicateSuccess()
    {
        // Arrange & Act
        var result = new Fido2RegistrationResult
        {
            Success = true,
            CredentialId = "credential123"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.CredentialId.Should().NotBeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FailedResult_ShouldContainError()
    {
        // Arrange & Act
        var result = new Fido2RegistrationResult
        {
            Success = false,
            Error = "attestation_failed",
            ErrorCode = "invalid_attestation"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("attestation_failed");
        result.ErrorCode.Should().Be("invalid_attestation");
        result.CredentialId.Should().BeNull();
    }
}

public class Fido2AssertionResultTests
{
    [Fact]
    public void SuccessfulResult_ShouldIndicateSuccess()
    {
        // Arrange & Act
        var result = new Fido2AssertionResult
        {
            Success = true,
            UserId = "user123",
            CredentialId = "credential123",
            SignatureCounter = 10
        };

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().Be("user123");
        result.CredentialId.Should().Be("credential123");
        result.SignatureCounter.Should().Be(10u);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FailedResult_ShouldContainError()
    {
        // Arrange & Act
        var result = new Fido2AssertionResult
        {
            Success = false,
            Error = "invalid_signature",
            ErrorCode = "signature_mismatch"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_signature");
        result.ErrorCode.Should().Be("signature_mismatch");
    }
}

public class AuthenticatorTypeTests
{
    [Fact]
    public void AuthenticatorType_ShouldHavePlatformValue()
    {
        // Assert
        AuthenticatorType.Platform.Should().Be(AuthenticatorType.Platform);
        ((int)AuthenticatorType.Platform).Should().Be(0);
    }

    [Fact]
    public void AuthenticatorType_ShouldHaveCrossPlatformValue()
    {
        // Assert
        AuthenticatorType.CrossPlatform.Should().Be(AuthenticatorType.CrossPlatform);
        ((int)AuthenticatorType.CrossPlatform).Should().Be(1);
    }
}
