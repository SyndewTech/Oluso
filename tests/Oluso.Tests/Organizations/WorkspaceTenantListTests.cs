using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Account.Controllers;
using Oluso.Account.Dto.Tenants;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Organizations;

/// <summary>
/// The workspace tenant switcher lists exactly what the switch allows: home and
/// member tenants, plus every tenant of an organization the user owns or
/// administers — not organizations where they are only a member.
/// </summary>
public class WorkspaceTenantListTests
{
    private readonly Mock<ITenantStore> _tenants = new();

    private TenantsController CreateController(params Claim[] claims)
    {
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetBrandingSettingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantBrandingSettings());

        return new TenantsController(
            Mock.Of<ITenantContext>(),
            _tenants.Object,
            settings.Object,
            Mock.Of<IProfileService>(),
            Mock.Of<IClientStore>(),
            Mock.Of<ITokenService>(),
            NullLogger<TenantsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims.Prepend(new Claim("sub", "user-1")), "test")),
                },
            },
        };
    }

    private void Tenant(string id, string? organizationId, bool enabled = true)
    {
        var tenant = new Tenant { Id = id, Name = id, Identifier = id, OrganizationId = organizationId, Enabled = enabled };
        _tenants.Setup(t => t.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
    }

    private void OrgTenants(string organizationId, params string[] ids) =>
        _tenants.Setup(t => t.GetByOrganizationAsync(organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.Select(id => new Tenant { Id = id, Name = id, Identifier = id, OrganizationId = organizationId, Enabled = id != "t-disabled" }).ToList());

    [Fact]
    public async Task Lists_home_and_member_tenants_plus_tenants_of_led_organizations()
    {
        Tenant("t-home", "org-home");
        OrgTenants("org-owned", "t-owned-1", "t-owned-2", "t-disabled");
        OrgTenants("org-member", "t-member-only");
        var controller = CreateController(
            new Claim("allowed_tenants", "t-home"),
            new Claim("org_roles", """{"org-owned":"owner","org-member":"member"}"""));

        var result = await controller.GetUserTenants(CancellationToken.None);

        var list = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<TenantListDto>().Subject;
        list.Tenants.Select(t => t.Id).Should().BeEquivalentTo(new[] { "t-home", "t-owned-1", "t-owned-2" });
        list.IsMultiTenant.Should().BeTrue();
    }

    [Fact]
    public async Task A_tenant_reachable_two_ways_is_listed_once()
    {
        Tenant("t-owned-1", "org-owned");
        OrgTenants("org-owned", "t-owned-1");
        var controller = CreateController(
            new Claim("allowed_tenants", "t-owned-1"),
            new Claim("org_roles", """{"org-owned":"admin"}"""));

        var result = await controller.GetUserTenants(CancellationToken.None);

        var list = (TenantListDto)((OkObjectResult)result.Result!).Value!;
        list.Tenants.Should().ContainSingle(t => t.Id == "t-owned-1");
    }
}
