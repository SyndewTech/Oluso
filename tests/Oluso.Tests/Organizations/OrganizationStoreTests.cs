using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Stores;
using Xunit;

namespace Oluso.Tests.Organizations;

/// <summary>
/// Tests for Organization store implementations
/// </summary>
public class OrganizationStoreTests : IDisposable
{
    private readonly OlusoDbContext _context;
    private readonly OrganizationStore _organizationStore;
    private readonly OrganizationMembershipStore _membershipStore;
    private readonly OrganizationInvitationStore _invitationStore;

    public OrganizationStoreTests()
    {
        var options = new DbContextOptionsBuilder<OlusoDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new OlusoDbContext(options);
        _organizationStore = new OrganizationStore(_context);
        _membershipStore = new OrganizationMembershipStore(_context);
        _invitationStore = new OrganizationInvitationStore(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region Organization CRUD Tests

    [Fact]
    public async Task CreateOrganization_ShouldGenerateIdAndTimestamp()
    {
        // Arrange
        var org = new Organization
        {
            Name = "Test Org",
            Slug = "test-org"
        };

        // Act
        var created = await _organizationStore.CreateAsync(org);

        // Assert
        created.Id.Should().NotBeNullOrEmpty();
        created.Created.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        created.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetBySlug_ShouldReturnOrganization()
    {
        // Arrange
        var org = await CreateTestOrganization("slug-test", "Slug Test Org");

        // Act
        var found = await _organizationStore.GetBySlugAsync("slug-test");

        // Assert
        found.Should().NotBeNull();
        found!.Name.Should().Be("Slug Test Org");
    }

    [Fact]
    public async Task SlugExists_ShouldReturnTrue_WhenSlugExists()
    {
        // Arrange
        await CreateTestOrganization("existing-slug", "Existing Org");

        // Act
        var exists = await _organizationStore.SlugExistsAsync("existing-slug");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SlugExists_ShouldReturnFalse_WhenSlugDoesNotExist()
    {
        // Act
        var exists = await _organizationStore.SlugExistsAsync("nonexistent-slug");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetAll_ShouldExcludeDisabled_ByDefault()
    {
        // Arrange
        await CreateTestOrganization("enabled-org", "Enabled Org", enabled: true);
        var disabledOrg = await CreateTestOrganization("disabled-org", "Disabled Org", enabled: true);
        disabledOrg.Enabled = false;
        await _organizationStore.UpdateAsync(disabledOrg);

        // Act
        var orgs = await _organizationStore.GetAllAsync(includeDisabled: false);

        // Assert
        orgs.Should().HaveCount(1);
        orgs.First().Slug.Should().Be("enabled-org");
    }

    [Fact]
    public async Task GetAll_ShouldIncludeDisabled_WhenRequested()
    {
        // Arrange
        await CreateTestOrganization("enabled-org-2", "Enabled Org", enabled: true);
        var disabledOrg = await CreateTestOrganization("disabled-org-2", "Disabled Org", enabled: true);
        disabledOrg.Enabled = false;
        await _organizationStore.UpdateAsync(disabledOrg);

        // Act
        var orgs = await _organizationStore.GetAllAsync(includeDisabled: true);

        // Assert
        orgs.Should().HaveCount(2);
    }

    #endregion

    #region Membership Tests

    [Fact]
    public async Task CreateMembership_ShouldGenerateIdAndTimestamp()
    {
        // Arrange
        var org = await CreateTestOrganization("membership-test", "Membership Test Org");
        var membership = new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = "user-123",
            Role = OrganizationRole.Member
        };

        // Act
        var created = await _membershipStore.CreateAsync(membership);

        // Assert
        created.Id.Should().NotBeNullOrEmpty();
        created.JoinedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetByUserAndOrganization_ShouldReturnMembership()
    {
        // Arrange
        var org = await CreateTestOrganization("user-org-test", "User Org Test");
        await CreateTestMembership(org.Id, "user-456", OrganizationRole.Admin);

        // Act
        var membership = await _membershipStore.GetByUserAndOrganizationAsync("user-456", org.Id);

        // Assert
        membership.Should().NotBeNull();
        membership!.Role.Should().Be(OrganizationRole.Admin);
    }

    [Fact]
    public async Task GetByUser_ShouldReturnAllMemberships()
    {
        // Arrange
        var org1 = await CreateTestOrganization("org-1", "Org 1");
        var org2 = await CreateTestOrganization("org-2", "Org 2");
        await CreateTestMembership(org1.Id, "multi-org-user", OrganizationRole.Owner);
        await CreateTestMembership(org2.Id, "multi-org-user", OrganizationRole.Member);

        // Act
        var memberships = await _membershipStore.GetByUserAsync("multi-org-user");

        // Assert
        memberships.Should().HaveCount(2);
    }

    [Fact]
    public async Task IsOwner_ShouldReturnTrue_ForOwner()
    {
        // Arrange
        var org = await CreateTestOrganization("owner-test", "Owner Test Org");
        await CreateTestMembership(org.Id, "owner-user", OrganizationRole.Owner);

        // Act
        var isOwner = await _membershipStore.IsOwnerAsync("owner-user", org.Id);

        // Assert
        isOwner.Should().BeTrue();
    }

    [Fact]
    public async Task IsOwner_ShouldReturnFalse_ForAdmin()
    {
        // Arrange
        var org = await CreateTestOrganization("admin-owner-test", "Admin Owner Test Org");
        await CreateTestMembership(org.Id, "admin-user", OrganizationRole.Admin);

        // Act
        var isOwner = await _membershipStore.IsOwnerAsync("admin-user", org.Id);

        // Assert
        isOwner.Should().BeFalse();
    }

    [Fact]
    public async Task IsAdmin_ShouldReturnTrue_ForOwner()
    {
        // Arrange
        var org = await CreateTestOrganization("owner-admin-test", "Owner Admin Test");
        await CreateTestMembership(org.Id, "owner-user-2", OrganizationRole.Owner);

        // Act
        var isAdmin = await _membershipStore.IsAdminAsync("owner-user-2", org.Id);

        // Assert
        isAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task IsAdmin_ShouldReturnTrue_ForAdmin()
    {
        // Arrange
        var org = await CreateTestOrganization("admin-admin-test", "Admin Admin Test");
        await CreateTestMembership(org.Id, "admin-user-2", OrganizationRole.Admin);

        // Act
        var isAdmin = await _membershipStore.IsAdminAsync("admin-user-2", org.Id);

        // Assert
        isAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task IsAdmin_ShouldReturnFalse_ForMember()
    {
        // Arrange
        var org = await CreateTestOrganization("member-admin-test", "Member Admin Test");
        await CreateTestMembership(org.Id, "member-user", OrganizationRole.Member);

        // Act
        var isAdmin = await _membershipStore.IsAdminAsync("member-user", org.Id);

        // Assert
        isAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllowedTenantIds_ForOwner_ShouldReturnAllOrgTenants()
    {
        // Arrange
        var org = await CreateTestOrganization("tenant-access-test", "Tenant Access Test");
        await CreateTestMembership(org.Id, "owner-for-tenants", OrganizationRole.Owner);

        // Create tenants for the organization
        await CreateTestTenant(org.Id, "tenant-1", TenantEnvironment.Production);
        await CreateTestTenant(org.Id, "tenant-2", TenantEnvironment.Staging);

        // Act
        var allowedTenants = await _membershipStore.GetAllowedTenantIdsAsync("owner-for-tenants", org.Id);

        // Assert
        allowedTenants.Should().HaveCount(2);
        allowedTenants.Should().Contain("tenant-1");
        allowedTenants.Should().Contain("tenant-2");
    }

    [Fact]
    public async Task GetAllowedTenantIds_ForMemberWithRestriction_ShouldReturnOnlyAllowed()
    {
        // Arrange
        var org = await CreateTestOrganization("restricted-tenant-test", "Restricted Tenant Test");
        var membership = await CreateTestMembership(org.Id, "restricted-member", OrganizationRole.Member);

        // Create tenants
        await CreateTestTenant(org.Id, "allowed-tenant", TenantEnvironment.Production);
        await CreateTestTenant(org.Id, "restricted-tenant", TenantEnvironment.Staging);

        // Set restriction on membership
        membership.AllowedTenantIdsJson = "[\"allowed-tenant\"]";
        await _membershipStore.UpdateAsync(membership);

        // Act
        var allowedTenants = await _membershipStore.GetAllowedTenantIdsAsync("restricted-member", org.Id);

        // Assert
        allowedTenants.Should().HaveCount(1);
        allowedTenants.Should().Contain("allowed-tenant");
        allowedTenants.Should().NotContain("restricted-tenant");
    }

    #endregion

    #region Invitation Tests

    [Fact]
    public async Task CreateInvitation_ShouldGenerateTokenAndTimestamp()
    {
        // Arrange
        var org = await CreateTestOrganization("invite-test", "Invite Test Org");
        var invitation = new OrganizationInvitation
        {
            OrganizationId = org.Id,
            Email = "test@example.com",
            Role = OrganizationRole.Member,
            InvitedByUserId = "inviter-user",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        // Act
        var created = await _invitationStore.CreateAsync(invitation);

        // Assert
        created.Id.Should().NotBeNullOrEmpty();
        created.Token.Should().NotBeNullOrEmpty();
        created.Token.Should().HaveLength(43); // Base64 URL encoded 32 bytes
        created.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        created.Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task GetByToken_ShouldReturnInvitation()
    {
        // Arrange
        var org = await CreateTestOrganization("token-test", "Token Test Org");
        var invitation = await CreateTestInvitation(org.Id, "token@example.com");

        // Act
        var found = await _invitationStore.GetByTokenAsync(invitation.Token);

        // Assert
        found.Should().NotBeNull();
        found!.Email.Should().Be("token@example.com");
    }

    [Fact]
    public async Task GetPendingByEmailAndOrganization_ShouldReturnPendingInvitation()
    {
        // Arrange
        var org = await CreateTestOrganization("pending-test", "Pending Test Org");
        await CreateTestInvitation(org.Id, "pending@example.com");

        // Act
        var found = await _invitationStore.GetPendingByEmailAndOrganizationAsync(
            "pending@example.com", org.Id);

        // Assert
        found.Should().NotBeNull();
        found!.Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task GetPendingByEmailAndOrganization_ShouldNotReturnExpired()
    {
        // Arrange
        var org = await CreateTestOrganization("expired-test", "Expired Test Org");
        var invitation = await CreateTestInvitation(org.Id, "expired@example.com");
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(-1); // Already expired
        await _invitationStore.UpdateAsync(invitation);

        // Act
        var found = await _invitationStore.GetPendingByEmailAndOrganizationAsync(
            "expired@example.com", org.Id);

        // Assert
        found.Should().BeNull();
    }

    [Fact]
    public async Task CleanupExpired_ShouldMarkExpiredInvitationsAsExpired()
    {
        // Arrange
        var org = await CreateTestOrganization("cleanup-test", "Cleanup Test Org");
        var invitation = await CreateTestInvitation(org.Id, "cleanup@example.com");
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(-1); // Already expired
        await _invitationStore.UpdateAsync(invitation);

        // Act
        var cleanedCount = await _invitationStore.CleanupExpiredAsync();

        // Assert
        cleanedCount.Should().Be(1);

        var updated = await _invitationStore.GetByIdAsync(invitation.Id);
        updated!.Status.Should().Be(InvitationStatus.Expired);
    }

    #endregion

    #region Helper Methods

    private async Task<Organization> CreateTestOrganization(string slug, string name, bool enabled = true)
    {
        var org = new Organization
        {
            Name = name,
            Slug = slug,
            Enabled = enabled
        };
        return await _organizationStore.CreateAsync(org);
    }

    private async Task<OrganizationMembership> CreateTestMembership(
        string organizationId,
        string userId,
        OrganizationRole role)
    {
        var membership = new OrganizationMembership
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role
        };
        return await _membershipStore.CreateAsync(membership);
    }

    private async Task<OrganizationInvitation> CreateTestInvitation(
        string organizationId,
        string email)
    {
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organizationId,
            Email = email,
            Role = OrganizationRole.Member,
            InvitedByUserId = "test-inviter",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        return await _invitationStore.CreateAsync(invitation);
    }

    private async Task<Tenant> CreateTestTenant(
        string organizationId,
        string tenantId,
        TenantEnvironment environment)
    {
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = $"Test Tenant {tenantId}",
            Identifier = tenantId,
            OrganizationId = organizationId,
            Environment = environment
        };
        _context.Set<Tenant>().Add(tenant);
        await _context.SaveChangesAsync();
        return tenant;
    }

    #endregion
}
