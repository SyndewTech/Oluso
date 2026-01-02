using FluentAssertions;
using Oluso.Enterprise.Scim.Entities;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests.Entities;

public class ScimClientTests
{
    [Fact]
    public void NewScimClient_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var client = new ScimClient();

        // Assert
        client.Id.Should().NotBeNullOrEmpty();
        client.IsEnabled.Should().BeTrue();
        client.RateLimitPerMinute.Should().Be(60);
        client.CanCreateUsers.Should().BeTrue();
        client.CanUpdateUsers.Should().BeTrue();
        client.CanDeleteUsers.Should().BeTrue();
        client.CanManageGroups.Should().BeTrue();
        client.SuccessCount.Should().Be(0);
        client.ErrorCount.Should().Be(0);
        client.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ScimClient_ShouldAllowCustomPermissions()
    {
        // Arrange
        var client = new ScimClient
        {
            TenantId = "tenant1",
            Name = "Azure AD Provisioning",
            CanCreateUsers = true,
            CanUpdateUsers = true,
            CanDeleteUsers = false, // Read-only for deletes
            CanManageGroups = false // No group management
        };

        // Assert
        client.CanCreateUsers.Should().BeTrue();
        client.CanUpdateUsers.Should().BeTrue();
        client.CanDeleteUsers.Should().BeFalse();
        client.CanManageGroups.Should().BeFalse();
    }

    [Fact]
    public void ScimClient_ShouldSupportTokenExpiration()
    {
        // Arrange
        var expiresAt = DateTime.UtcNow.AddDays(30);
        var client = new ScimClient
        {
            TokenExpiresAt = expiresAt
        };

        // Assert
        client.TokenExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public void ScimClient_ShouldSupportIpRestrictions()
    {
        // Arrange
        var client = new ScimClient
        {
            AllowedIpRanges = "10.0.0.0/8,192.168.1.0/24"
        };

        // Assert
        client.AllowedIpRanges.Should().Contain("10.0.0.0/8");
        client.AllowedIpRanges.Should().Contain("192.168.1.0/24");
    }
}

public class ScimProvisioningLogTests
{
    [Fact]
    public void NewProvisioningLog_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var log = new ScimProvisioningLog();

        // Assert
        log.Id.Should().NotBeNullOrEmpty();
        log.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ProvisioningLog_ShouldCaptureRequestDetails()
    {
        // Arrange
        var log = new ScimProvisioningLog
        {
            TenantId = "tenant1",
            ScimClientId = "client1",
            Method = "POST",
            Path = "/scim/v2/Users",
            ResourceType = "User",
            Operation = "Create",
            StatusCode = 201,
            Success = true,
            DurationMs = 150,
            ClientIp = "192.168.1.100"
        };

        // Assert
        log.Method.Should().Be("POST");
        log.Path.Should().Be("/scim/v2/Users");
        log.ResourceType.Should().Be("User");
        log.Operation.Should().Be("Create");
        log.StatusCode.Should().Be(201);
        log.Success.Should().BeTrue();
        log.DurationMs.Should().Be(150);
        log.ClientIp.Should().Be("192.168.1.100");
    }

    [Fact]
    public void ProvisioningLog_ShouldCaptureErrors()
    {
        // Arrange
        var log = new ScimProvisioningLog
        {
            Method = "POST",
            Path = "/scim/v2/Users",
            Operation = "Create",
            StatusCode = 400,
            Success = false,
            ErrorMessage = "Username already exists"
        };

        // Assert
        log.Success.Should().BeFalse();
        log.StatusCode.Should().Be(400);
        log.ErrorMessage.Should().Be("Username already exists");
    }
}

public class ScimResourceMappingTests
{
    [Fact]
    public void NewResourceMapping_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var mapping = new ScimResourceMapping();

        // Assert
        mapping.Id.Should().NotBeNullOrEmpty();
        mapping.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ResourceMapping_ShouldMapExternalToInternalIds()
    {
        // Arrange
        var mapping = new ScimResourceMapping
        {
            TenantId = "tenant1",
            ScimClientId = "azure-ad",
            ResourceType = "User",
            ExternalId = "azure-user-12345",
            InternalId = "oluso-user-abc123"
        };

        // Assert
        mapping.ExternalId.Should().Be("azure-user-12345");
        mapping.InternalId.Should().Be("oluso-user-abc123");
        mapping.ResourceType.Should().Be("User");
    }
}
