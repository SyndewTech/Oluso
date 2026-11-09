using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// A token's tenant_id is the tenant the user is in when they are entitled to
/// it — membership, or Owner/Admin of the organization that owns it — and their
/// home tenant otherwise. tenant_id is an assertion, so it is never taken on the
/// request's word alone.
/// </summary>
public class DefaultProfileServiceTokenTenantTests
{
    private const string Home = "tenant-home";
    private const string OrgTenant = "tenant-org";
    private const string OrgId = "org-1";

    private readonly Mock<IOlusoUserService> _users = new() { CallBase = true };
    private readonly Mock<IClaimsProviderRegistry> _claims = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ITenantStore> _tenants = new();
    private readonly Mock<IResourceStore> _resources = new();
    private readonly Mock<IOrganizationMembershipStore> _orgMemberships = new();
    private readonly Mock<ITenantMembershipStore> _tenantMemberships = new();

    public DefaultProfileServiceTokenTenantTests()
    {
        _claims.Setup(x => x.GetAllClaimsAsync(It.IsAny<ClaimsProviderContext>())).ReturnsAsync(new Dictionary<string, object>());
        _resources.Setup(x => x.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ApiScope>());
        _resources.Setup(x => x.FindResourcesByUrisAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Resource>());
        _resources.Setup(x => x.FindIdentityResourcesByScopeNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdentityResource>());

        _users.Setup(x => x.FindByIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "user-1", Username = "alice", TenantId = Home, Roles = Array.Empty<string>() });

        _tenants.Setup(t => t.GetByIdAsync(OrgTenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = OrgTenant, Name = "Org tenant", OrganizationId = OrgId, Enabled = true });
        _tenants.Setup(t => t.GetByIdAsync(Home, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = Home, Name = "Home", Enabled = true });

        _orgMemberships.Setup(m => m.GetByUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OrganizationMembership>());
        _tenantMemberships.Setup(m => m.GetByUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantMembership>());
    }

    private async Task<string?> TokenTenantAsync(string ambientTenant)
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(ambientTenant);
        _tenantContext.SetupGet(x => x.HasTenant).Returns(true);

        var service = new DefaultProfileService(
            _users.Object, Mock.Of<IRoleStore>(), _claims.Object, _tenantContext.Object, _tenants.Object,
            _resources.Object, Mock.Of<ILogger<DefaultProfileService>>(),
            organizationMembershipStore: _orgMemberships.Object,
            tenantMembershipStore: _tenantMemberships.Object);

        var context = new ProfileDataRequestContext
        {
            SubjectId = "user-1",
            Client = new Client { ClientId = "c1" },
            RequestedScopes = new[] { "openid" },
        };
        await service.GetProfileDataAsync(context);
        return context.IssuedClaims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
    }

    [Fact]
    public async Task An_organization_owner_or_admin_gets_the_tenant_they_are_in()
    {
        _orgMemberships.Setup(m => m.IsAdminAsync("user-1", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await TokenTenantAsync(OrgTenant)).Should().Be(OrgTenant);
    }

    [Fact]
    public async Task A_plain_organization_member_without_membership_falls_back_to_home()
    {
        _orgMemberships.Setup(m => m.IsAdminAsync("user-1", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        (await TokenTenantAsync(OrgTenant)).Should().Be(Home);
    }

    [Fact]
    public async Task A_tenant_member_gets_the_tenant_they_are_in()
    {
        _tenantMemberships.Setup(m => m.GetByTenantAndUserAsync(OrgTenant, "user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembership { TenantId = OrgTenant, UserId = "user-1" });

        (await TokenTenantAsync(OrgTenant)).Should().Be(OrgTenant);
    }
}
