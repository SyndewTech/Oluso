using FluentAssertions;
using Oluso.Enterprise.Scim;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests;

public class ScimOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveReasonableDefaults()
    {
        // Arrange & Act
        var options = new ScimOptions();

        // Assert
        options.BasePath.Should().Be("/scim/v2");
        options.MaxResults.Should().Be(200);
        options.MaxBulkOperations.Should().Be(1000);
        options.SoftDeleteUsers.Should().BeTrue();
        options.LogRequestBodies.Should().BeFalse();
        options.LogRetention.Should().Be(TimeSpan.FromDays(90));
    }

    [Fact]
    public void BasePath_ShouldBeConfigurable()
    {
        // Arrange
        var options = new ScimOptions
        {
            BasePath = "/api/scim"
        };

        // Assert
        options.BasePath.Should().Be("/api/scim");
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void MaxResults_ShouldBeConfigurable(int maxResults)
    {
        // Arrange
        var options = new ScimOptions
        {
            MaxResults = maxResults
        };

        // Assert
        options.MaxResults.Should().Be(maxResults);
    }

    [Fact]
    public void LogRetention_ShouldAcceptNull()
    {
        // Arrange
        var options = new ScimOptions
        {
            LogRetention = null
        };

        // Assert
        options.LogRetention.Should().BeNull();
    }
}
