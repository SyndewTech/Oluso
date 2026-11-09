using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Events;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Organizations;

/// <summary>
/// Tenant access inside an organization: leaving the organization ends access to
/// its tenants, and grants and revokes are explicit per tenant. The home tenant,
/// where the account lives, is never revoked.
/// </summary>
public class RemoveMemberTenantAccessTests
{
    private const string OrgId = "org-1";
    private const string UserId = "user-1";
    private const string HomeTenant = "t-home";
    private const string OrgTenantA = "t-a";
    private const string OrgTenantB = "t-b";

    private readonly Mock<IOrganizationMembershipStore> _memberships = new();
    private readonly Mock<ITenantStore> _tenants = new();
    private readonly Mock<ITenantMembershipStore> _tenantMemberships = new();
    private readonly Mock<IRoleStore> _roles = new();
    private readonly Mock<IOlusoUserService> _users = new();
    private readonly Mock<IRoleGrantService> _grants = new();
    private readonly Mock<IOrganizationStore> _orgStore = new();

    private OrganizationsController CreateController(string homeTenantId, OrganizationRole role = OrganizationRole.Member)
    {
        _memberships.Setup(m => m.GetByIdAsync("m-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationMembership { Id = "m-1", OrganizationId = OrgId, UserId = UserId, Role = role });
        _memberships.Setup(m => m.GetByOrganizationAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new OrganizationMembership { Id = "m-1", OrganizationId = OrgId, UserId = UserId, Role = role },
                new OrganizationMembership { Id = "m-2", OrganizationId = OrgId, UserId = "other", Role = OrganizationRole.Owner },
            });
        _memberships.Setup(m => m.GetByUserAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OrganizationMembership>());

        _tenants.Setup(t => t.GetByOrganizationAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Tenant { Id = HomeTenant, Name = "Home", OrganizationId = OrgId },
                new Tenant { Id = OrgTenantA, Name = "A", OrganizationId = OrgId },
                new Tenant { Id = OrgTenantB, Name = "B", OrganizationId = OrgId },
            });

        _users.Setup(u => u.FindByIdsAcrossTenantsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, OlusoUserInfo>
            {
                [UserId] = new() { Id = UserId, Username = "user", TenantId = homeTenantId },
            });

        _roles.Setup(r => r.GetUserRolesInTenantAsync(UserId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OlusoRole>());

        var controller = new OrganizationsController(
            _orgStore.Object,
            _memberships.Object,
            Mock.Of<IOrganizationInvitationStore>(),
            Mock.Of<IOrganizationFeatureStore>(),
            _tenants.Object,
            _tenantMemberships.Object,
            _roles.Object,
            Mock.Of<ITenantDefaultRoleSeeder>(),
            _users.Object,
            Mock.Of<IOlusoEventService>(),
            Mock.Of<IIssuerResolver>(),
            _grants.Object,
            Mock.Of<IModuleAdminAssignmentNotifier>(),
            NullLogger<OrganizationsController>.Instance,
            Mock.Of<ITenantContext>(),
            Mock.Of<ITenantSettingsProvider>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "admin-1") }, "test")),
                },
            },
        };
        return controller;
    }

    [Fact]
    public async Task Removing_a_member_revokes_the_org_tenants_but_not_the_home_tenant()
    {
        var controller = CreateController(HomeTenant);

        var result = await controller.RemoveMember(OrgId, "m-1", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _tenantMemberships.Verify(m => m.RemoveAsync(OrgTenantA, UserId, It.IsAny<CancellationToken>()), Times.Once);
        _tenantMemberships.Verify(m => m.RemoveAsync(OrgTenantB, UserId, It.IsAny<CancellationToken>()), Times.Once);
        _tenantMemberships.Verify(m => m.RemoveAsync(HomeTenant, UserId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Tenant_roles_are_revoked_through_the_grant_service_but_global_and_org_admin_roles_stay()
    {
        var controller = CreateController("t-elsewhere");
        _roles.Setup(r => r.GetUserRolesInTenantAsync(UserId, OrgTenantA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new OlusoRole { Id = "r-hr", Name = "HRAdmin", NormalizedName = "HRADMIN" },
                new OlusoRole { Id = "r-super", Name = "SuperAdmin", NormalizedName = "SUPERADMIN" },
                new OlusoRole { Id = "r-org", Name = EffectiveRoleResolver.OrgAdminRoleName, NormalizedName = "ORGADMIN" },
            });

        await controller.RemoveMember(OrgId, "m-1", CancellationToken.None);

        _grants.Verify(g => g.RevokeAsync("r-hr", UserId, OrgTenantA, It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.RevokeAsync("r-super", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Never);
        _grants.Verify(g => g.RevokeAsync("r-org", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Never);
        _tenantMemberships.Verify(m => m.RemoveAsync(HomeTenant, UserId, It.IsAny<CancellationToken>()), Times.Once,
            "this user's home is outside the organization, so every org tenant is revoked");
    }

    [Fact]
    public async Task The_last_owner_cannot_be_removed_and_nothing_is_revoked()
    {
        var controller = CreateController(HomeTenant, OrganizationRole.Owner);
        _memberships.Setup(m => m.GetByOrganizationAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new OrganizationMembership { Id = "m-1", OrganizationId = OrgId, UserId = UserId, Role = OrganizationRole.Owner } });

        var result = await controller.RemoveMember(OrgId, "m-1", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        _tenantMemberships.Verify(m => m.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupOrgAndTenant(string tenantId, string? tenantOrgId)
    {
        _orgStore.Setup(o => o.GetByIdAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = OrgId, Name = "Org", Slug = "org" });
        _memberships.Setup(m => m.GetByUserAndOrganizationAsync(UserId, OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationMembership { Id = "m-1", OrganizationId = OrgId, UserId = UserId });
        _tenants.Setup(t => t.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantOrgId == null ? null : new Tenant { Id = tenantId, Name = tenantId, OrganizationId = tenantOrgId });
    }

    [Fact]
    public async Task Revoking_the_home_tenant_is_refused()
    {
        var controller = CreateController(HomeTenant);
        SetupOrgAndTenant(HomeTenant, OrgId);

        var result = await controller.RevokeMemberTenant(OrgId, UserId, HomeTenant, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        _tenantMemberships.Verify(m => m.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoking_another_org_tenant_removes_membership_and_roles()
    {
        var controller = CreateController(HomeTenant);
        SetupOrgAndTenant(OrgTenantA, OrgId);
        _roles.Setup(r => r.GetUserRolesInTenantAsync(UserId, OrgTenantA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new OlusoRole { Id = "r-hr", Name = "HRAdmin", NormalizedName = "HRADMIN" } });

        var result = await controller.RevokeMemberTenant(OrgId, UserId, OrgTenantA, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _grants.Verify(g => g.RevokeAsync("r-hr", UserId, OrgTenantA, It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Once);
        _tenantMemberships.Verify(m => m.RemoveAsync(OrgTenantA, UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_tenant_outside_the_organization_is_refused_for_grant_and_revoke()
    {
        var controller = CreateController(HomeTenant);
        SetupOrgAndTenant("t-other-org", "org-2");

        (await controller.GrantMemberTenant(OrgId, UserId, "t-other-org", CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.RevokeMemberTenant(OrgId, UserId, "t-other-org", CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
        _tenantMemberships.Verify(m => m.AddAsync(It.IsAny<TenantMembership>(), It.IsAny<CancellationToken>()), Times.Never);
        _tenantMemberships.Verify(m => m.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Granting_adds_a_membership_only_when_missing()
    {
        var controller = CreateController(HomeTenant);
        SetupOrgAndTenant(OrgTenantA, OrgId);

        (await controller.GrantMemberTenant(OrgId, UserId, OrgTenantA, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        _tenantMemberships.Verify(m => m.AddAsync(It.Is<TenantMembership>(tm => tm.TenantId == OrgTenantA && tm.UserId == UserId), It.IsAny<CancellationToken>()), Times.Once);

        _tenantMemberships.Setup(m => m.GetByTenantAndUserAsync(OrgTenantA, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembership { TenantId = OrgTenantA, UserId = UserId });
        await controller.GrantMemberTenant(OrgId, UserId, OrgTenantA, CancellationToken.None);
        _tenantMemberships.Verify(m => m.AddAsync(It.IsAny<TenantMembership>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private OrganizationsController CreateSlugController(params Claim[] claims)
    {
        var controller = CreateController(HomeTenant);
        _orgStore.Setup(o => o.GetBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = OrgId, Name = "Acme", Slug = "acme" });
        var services = new ServiceCollection().AddSingleton(_memberships.Object).BuildServiceProvider();
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims.Prepend(new Claim("sub", "caller")), "test")),
        };
        return controller;
    }

    [Fact]
    public async Task By_slug_is_hidden_from_callers_outside_the_organization()
    {
        var controller = CreateSlugController(new Claim("role", "Admin"));
        _memberships.Setup(m => m.IsMemberAsync("caller", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await controller.GetBySlug("acme", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task By_slug_is_visible_to_members_and_super_admins()
    {
        _memberships.Setup(m => m.IsMemberAsync("caller", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        (await CreateSlugController().GetBySlug("acme", CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();

        _memberships.Setup(m => m.IsMemberAsync("caller", OrgId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await CreateSlugController(new Claim("role", "SuperAdmin")).GetBySlug("acme", CancellationToken.None))
            .Result.Should().BeOfType<OkObjectResult>();
    }
}
