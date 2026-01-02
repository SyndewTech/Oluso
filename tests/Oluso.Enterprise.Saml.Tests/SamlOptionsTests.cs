using FluentAssertions;
using Oluso.Enterprise.Saml.Configuration;
using Xunit;

namespace Oluso.Enterprise.Saml.Tests;

public class SamlSpOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveReasonableDefaults()
    {
        // Arrange & Act
        var options = new SamlSpOptions();

        // Assert
        options.AssertionConsumerServicePath.Should().Be("/saml/acs");
        options.SingleLogoutServicePath.Should().Be("/saml/slo");
        options.MetadataPath.Should().Be("/saml/metadata");
        options.RequireSignedResponses.Should().BeTrue();
        options.RequireSignedAssertions.Should().BeTrue();
    }

    [Fact]
    public void EntityId_ShouldBeConfigurable()
    {
        // Arrange
        var options = new SamlSpOptions
        {
            EntityId = "https://sp.example.com/saml"
        };

        // Assert
        options.EntityId.Should().Be("https://sp.example.com/saml");
    }

    [Fact]
    public void BaseUrl_ShouldBeConfigurable()
    {
        // Arrange
        var options = new SamlSpOptions
        {
            BaseUrl = "https://sp.example.com"
        };

        // Assert
        options.BaseUrl.Should().Be("https://sp.example.com");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void SigningOptions_ShouldBeConfigurable(bool requireSignedResponses, bool requireSignedAssertions)
    {
        // Arrange
        var options = new SamlSpOptions
        {
            RequireSignedResponses = requireSignedResponses,
            RequireSignedAssertions = requireSignedAssertions
        };

        // Assert
        options.RequireSignedResponses.Should().Be(requireSignedResponses);
        options.RequireSignedAssertions.Should().Be(requireSignedAssertions);
    }
}

public class SamlIdpOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveReasonableDefaults()
    {
        // Arrange & Act
        var options = new SamlIdpOptions();

        // Assert
        options.Enabled.Should().BeFalse();
        options.SingleSignOnServicePath.Should().Be("/saml/sso");
        options.SingleLogoutServicePath.Should().Be("/saml/idp/slo");
        options.MetadataPath.Should().Be("/saml/idp/metadata");
        options.AssertionLifetimeMinutes.Should().Be(5);
    }

    [Fact]
    public void EntityId_ShouldBeConfigurable()
    {
        // Arrange
        var options = new SamlIdpOptions
        {
            EntityId = "https://idp.example.com/saml"
        };

        // Assert
        options.EntityId.Should().Be("https://idp.example.com/saml");
    }

    [Fact]
    public void AssertionLifetimeMinutes_ShouldBeConfigurable()
    {
        // Arrange
        var options = new SamlIdpOptions
        {
            AssertionLifetimeMinutes = 10
        };

        // Assert
        options.AssertionLifetimeMinutes.Should().Be(10);
    }

    [Fact]
    public void NameIdFormats_ShouldHaveDefaults()
    {
        // Arrange & Act
        var options = new SamlIdpOptions();

        // Assert
        options.NameIdFormats.Should().Contain("urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress");
        options.NameIdFormats.Should().Contain("urn:oasis:names:tc:SAML:2.0:nameid-format:persistent");
    }
}

public class SamlSpConfigTests
{
    [Fact]
    public void DefaultConfig_ShouldHaveReasonableDefaults()
    {
        // Arrange & Act
        var config = new SamlSpConfig();

        // Assert
        config.Enabled.Should().BeTrue();
        config.SignResponses.Should().BeTrue();
        config.SignAssertions.Should().BeTrue();
        config.RequireSignedAuthnRequests.Should().BeFalse();
        config.SsoBinding.Should().Be("POST");
    }

    [Fact]
    public void EntityId_ShouldBeConfigurable()
    {
        // Arrange
        var config = new SamlSpConfig
        {
            EntityId = "https://sp.example.com"
        };

        // Assert
        config.EntityId.Should().Be("https://sp.example.com");
    }
}
