using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Permissions;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Licensing;
using Xunit;

namespace Oluso.Tests.Permissions;

/// <summary>
/// The permissions feed must resolve the roles a user actually holds by id.
/// A tenant row that shares its name with a platform module role (a legacy
/// "twin") must not shadow the platform row the user's membership points at,
/// or explicit-grant permissions vanish from the menu while the APIs keep working.
/// </summary>
public class PermissionsControllerTests
{
    private const string TenantId = "t1";
    private const string UserId = "u1";
    private const string RoleName = "AcctxAdmin";
    private const string AdminPermission = "acctx.administration";
    private const string ViewPermission = "acctx.gl.view";

    private static readonly OlusoRole PlatformRole = new() { Id = "r-platform", Name = RoleName, NormalizedName = RoleName.ToUpperInvariant(), TenantId = null, IsSystemRole = true };
    private static readonly OlusoRole TenantTwin = new() { Id = "r-twin", Name = RoleName, NormalizedName = RoleName.ToUpperInvariant(), TenantId = TenantId };

    static PermissionsControllerTests()
    {
        PermissionRegistry.Instance.RegisterPermissions(new[]
        {
            new PermissionDefinition(AdminPermission, "Acctx administration", "acctx", "Test module administration", RequiresExplicitGrant: true),
            new PermissionDefinition(ViewPermission, "Acctx GL view", "acctx", "Test module view"),
        });
    }

    private static (PermissionsController Controller, Mock<IRoleStore> Roles) CreateController()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        tenant.SetupGet(t => t.HasTenant).Returns(true);

        var roles = new Mock<IRoleStore>(MockBehavior.Strict);
        roles.Setup(r => r.GetUserRolesInTenantAsync(UserId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { PlatformRole });
        roles.Setup(r => r.GetRoleClaimsAsync(PlatformRole.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RoleClaim { Type = OlusoRole.PermissionClaimType, Value = AdminPermission },
                new RoleClaim { Type = OlusoRole.PermissionClaimType, Value = "acctx.*" },
            });
        // The twin exists and would win a name lookup; it must never be consulted.
        roles.Setup(r => r.GetByNameAsync(RoleName, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantTwin);
        roles.Setup(r => r.GetRoleClaimsAsync(TenantTwin.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RoleClaim { Type = OlusoRole.PermissionClaimType, Value = "acctx.*" } });

        roles.Setup(r => r.GetByNameAsync(EffectiveRoleResolver.OrgAdminRoleName, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OlusoRole?)null);

        var features = new Mock<IFeatureGate>();
        features.Setup(f => f.CheckFeatureAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FeatureGateResult.Allowed());

        var controller = new PermissionsController(tenant.Object, roles.Object, new EffectiveRoleResolver(roles.Object, Mock.Of<ITenantStore>()), features.Object, NullLogger<PermissionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", UserId),
                        new Claim("role", RoleName),
                        new Claim("tenant_id", TenantId),
                    }, "test")),
                },
            },
        };

        return (controller, roles);
    }

    [Fact]
    public async Task My_permissions_come_from_the_held_platform_row_not_the_tenant_twin()
    {
        var (controller, roles) = CreateController();

        var result = await controller.GetMyPermissions(CancellationToken.None);

        var response = (result.Result as OkObjectResult)?.Value as MyPermissionsResponse;
        response.Should().NotBeNull();
        response!.EffectivePermissions.Should().Contain(AdminPermission, "the explicit-grant permission is literally on the held row");
        response.EffectivePermissions.Should().Contain(ViewPermission, "the category wildcard expands");
        response.ExplicitGrantPermissions.Should().Contain(AdminPermission);
        response.RolePermissions.Should().ContainKey(RoleName);
        roles.Verify(r => r.GetByNameAsync(RoleName, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        roles.Verify(r => r.GetRoleClaimsAsync(TenantTwin.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Check_permission_uses_held_roles()
    {
        var (controller, roles) = CreateController();

        var result = await controller.CheckPermission(AdminPermission, CancellationToken.None);

        var check = (result.Result as OkObjectResult)?.Value as PermissionCheckResult;
        check.Should().NotBeNull();
        check!.HasPermission.Should().BeTrue();
        check.GrantedBy.Should().Contain(RoleName);
        roles.Verify(r => r.GetByNameAsync(RoleName, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task No_subject_yields_no_permissions_instead_of_an_error()
    {
        var (controller, _) = CreateController();
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await controller.GetMyPermissions(CancellationToken.None);

        var response = (result.Result as OkObjectResult)?.Value as MyPermissionsResponse;
        response.Should().NotBeNull();
        response!.EffectivePermissions.Should().BeEmpty();
    }
}
