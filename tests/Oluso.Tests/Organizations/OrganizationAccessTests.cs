using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Moq;
using Oluso.Admin.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Xunit;

namespace Oluso.Tests.Organizations;

/// <summary>
/// Tests for organization-based tenant access validation
/// </summary>
public class OrganizationAccessTests
{
    private readonly Mock<ITenantContext> _tenantContextMock;
    private readonly Mock<ILogger<TenantAdminAuthorizationHandler>> _loggerMock;

    public OrganizationAccessTests()
    {
        _tenantContextMock = new Mock<ITenantContext>();
        _loggerMock = new Mock<ILogger<TenantAdminAuthorizationHandler>>();
    }

    #region SuperAdmin Tests

    [Fact]
    public async Task SuperAdmin_WithSuperAdminClaim_ShouldSucceed()
    {
        // Arrange
        var handler = CreateHandler();
        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "superadmin-user"),
            new Claim("super_admin", "true")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdmin_WithSuperAdminRole_AndNoTenantId_ShouldSucceed()
    {
        // Arrange
        var handler = CreateHandler();
        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "superadmin-user"),
            new Claim(ClaimTypes.Role, "SuperAdmin")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdmin_CanAccessAnyTenant()
    {
        // Arrange
        var handler = CreateHandler();
        SetupTenantContext("tenant-123");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "superadmin-user"),
            new Claim("super_admin", "true")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdmin_CanAccessWithoutTenantContext()
    {
        // Arrange
        var handler = CreateHandler();
        SetupNoTenantContext();

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "superadmin-user"),
            new Claim("super_admin", "true")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SuperAdminPolicy_RequiresSuperAdminRole()
    {
        // Arrange
        var handler = CreateHandler();
        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "regular-user"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("tenant_id", "tenant-123")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: true));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    #endregion

    #region Organization Admin Tests

    [Fact]
    public async Task OrgAdmin_WithCurrentOrgRole_ShouldSucceed()
    {
        // Arrange
        var handler = CreateHandler();
        SetupTenantContext("tenant-123");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "org-admin-user"),
            new Claim("is_org_admin", "true"),
            new Claim("current_org_id", "org-456"),
            new Claim("current_org_role", "admin")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task OrgOwner_WithCurrentOrgRole_ShouldSucceed()
    {
        // Arrange
        var handler = CreateHandler();
        SetupTenantContext("tenant-123");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "org-owner-user"),
            new Claim("is_org_admin", "true"),
            new Claim("current_org_id", "org-456"),
            new Claim("current_org_role", "owner")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task OrgMember_WithCurrentOrgRole_ShouldSucceed()
    {
        // Arrange - members validated by middleware should also pass
        var handler = CreateHandler();
        SetupTenantContext("tenant-123");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "org-member-user"),
            new Claim("current_org_id", "org-456"),
            new Claim("current_org_role", "member")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task OrgAdmin_WithoutTenantContext_ShouldSucceed()
    {
        // Arrange - org admins can access non-tenant-scoped endpoints (like /api/admin/organizations)
        var handler = CreateHandler();
        SetupNoTenantContext();

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "org-admin-user"),
            new Claim("is_org_admin", "true"),
            new Claim("org_id", "org-456")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task OrgAdmin_WithTenantContext_ButNoCurrentOrgRole_ShouldFail()
    {
        // Arrange - middleware didn't set current_org_role, meaning access was denied
        var handler = CreateHandler();
        SetupTenantContext("tenant-123");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "org-admin-user"),
            new Claim("is_org_admin", "true"),
            new Claim("org_id", "org-456") // Member of an org, but not for this tenant
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    #endregion

    #region Legacy TenantAdmin Tests

    [Fact]
    public async Task TenantAdmin_WithMatchingTenantId_ShouldSucceed()
    {
        // Arrange
        var handler = CreateHandler();
        SetupTenantContext("tenant-123");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "tenant-admin-user"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("tenant_id", "tenant-123")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task TenantAdmin_WithMismatchedTenantId_ShouldFail()
    {
        // Arrange
        var handler = CreateHandler();
        SetupTenantContext("tenant-456");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "tenant-admin-user"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("tenant_id", "tenant-123")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task TenantAdmin_WithoutTenantContext_ShouldFail()
    {
        // Arrange
        var handler = CreateHandler();
        SetupNoTenantContext();

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "tenant-admin-user"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("tenant_id", "tenant-123")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    #endregion

    #region Unauthenticated/No Role Tests

    [Fact]
    public async Task UnauthenticatedUser_ShouldFail()
    {
        // Arrange
        var handler = CreateHandler();
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // Not authenticated
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task UserWithNoAdminRole_ShouldFail()
    {
        // Arrange
        var handler = CreateHandler();
        SetupTenantContext("tenant-123");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "regular-user"),
            new Claim("tenant_id", "tenant-123")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    #endregion

    #region Multi-Organization Role Scoping Tests

    [Fact]
    public async Task User_InMultipleOrgs_ShouldUseCurrentOrgRole()
    {
        // Arrange - User is owner in org-1 but member in org-2
        // When accessing a tenant in org-2, they should have member role
        var handler = CreateHandler();
        SetupTenantContext("tenant-in-org-2");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "multi-org-user"),
            new Claim("is_org_admin", "true"),
            new Claim("org_id", "org-1"),
            new Claim("org_id", "org-2"),
            new Claim("org_role", "org-1:owner"),
            new Claim("org_role", "org-2:member"),
            // Middleware sets current context based on tenant's org
            new Claim("current_org_id", "org-2"),
            new Claim("current_org_role", "member")
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert - Should succeed because middleware validated access
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task User_InMultipleOrgs_CannotAccessUnauthorizedTenant()
    {
        // Arrange - User tries to access tenant in org they don't belong to
        var handler = CreateHandler();
        SetupTenantContext("tenant-in-org-3");

        var user = CreateClaimsPrincipal(new[]
        {
            new Claim("sub", "multi-org-user"),
            new Claim("is_org_admin", "true"),
            new Claim("org_id", "org-1"),
            new Claim("org_id", "org-2"),
            new Claim("org_role", "org-1:owner"),
            new Claim("org_role", "org-2:member")
            // No current_org_role - middleware rejected access
        });
        var context = CreateAuthorizationContext(user, new TenantAdminRequirement(requireSuperAdmin: false));

        // Act
        await handler.HandleAsync(context);

        // Assert - Should fail because no current_org_role and no matching tenant_id
        context.HasSucceeded.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private TenantAdminAuthorizationHandler CreateHandler()
    {
        return new TenantAdminAuthorizationHandler(
            _tenantContextMock.Object,
            _loggerMock.Object);
    }

    private void SetupTenantContext(string tenantId)
    {
        _tenantContextMock.Setup(x => x.HasTenant).Returns(true);
        _tenantContextMock.Setup(x => x.TenantId).Returns(tenantId);
        _tenantContextMock.Setup(x => x.Tenant).Returns(new Tenant
        {
            Id = tenantId,
            Name = $"Test Tenant {tenantId}",
            Identifier = tenantId
        });
    }

    private void SetupNoTenantContext()
    {
        _tenantContextMock.Setup(x => x.HasTenant).Returns(false);
        _tenantContextMock.Setup(x => x.TenantId).Returns((string?)null);
        _tenantContextMock.Setup(x => x.Tenant).Returns((Tenant?)null);
    }

    private static ClaimsPrincipal CreateClaimsPrincipal(Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static AuthorizationHandlerContext CreateAuthorizationContext(
        ClaimsPrincipal user,
        TenantAdminRequirement requirement)
    {
        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            resource: null);
    }

    #endregion
}
