using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers;
using Oluso.Admin.Controllers.Service;
using Oluso.Admin.Dto.Clients;
using Oluso.Admin.Dto.Permissions;
using Oluso.Admin.Dto.Scopes;
using Oluso.Admin.Services;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Interceptors;
using Oluso.EntityFramework.Services;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// <c>users.delete</c> and its Service API counterpart <c>oluso:users:delete</c>
/// are granted by name only: no wildcard, no SuperAdmin shortcut, no default
/// role, no service client, and a client gets the scope only from an admin who
/// holds the permission themselves.
/// </summary>
public class UsersDeleteExplicitGrantTests
{
    private const string TenantId = "t1";

    static UsersDeleteExplicitGrantTests()
    {
        PermissionRegistry.Instance.RegisterPermissions(AdminPermissions.GetCorePermissions());
    }

    [Fact]
    public void Users_delete_requires_an_explicit_grant()
    {
        PermissionRegistry.Instance.RequiresExplicitGrant(AdminPermissions.UsersDelete).Should().BeTrue();
        PermissionRegistry.Instance.RequiresExplicitGrant(AdminPermissions.UsersWrite).Should().BeFalse();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("users.*")]
    public void Wildcards_never_expand_to_users_delete(string wildcard)
    {
        var expanded = PermissionExpander.Expand(new[] { wildcard });

        expanded.Should().Contain(AdminPermissions.UsersRead).And.NotContain(AdminPermissions.UsersDelete);
    }

    // ---------------------------------------------------------------- runtime check

    private static async Task<bool> HandlerGrantsAsync(ClaimsPrincipal user, IReadOnlyCollection<string>? databasePermissions = null)
    {
        var roles = new Mock<IEffectiveRoleResolver>();
        roles.Setup(r => r.GetEffectivePermissionsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(databasePermissions ?? Array.Empty<string>());
        var handler = new PermissionAuthorizationHandler(
            roles.Object, new TestTenantContext { TenantId = TenantId }, NullLogger<PermissionAuthorizationHandler>.Instance);

        var requirement = new PermissionRequirement(AdminPermissions.UsersDelete);
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal WithTokenPermissions(params string[] permissions) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", "admin-1"),
        new Claim("tenant_id", TenantId),
        new Claim("permissions", JsonSerializer.Serialize(permissions)),
    }, "test"));

    [Theory]
    [InlineData("*")]
    [InlineData("users.*")]
    public async Task A_wildcard_in_the_token_does_not_grant_users_delete(string wildcard)
    {
        (await HandlerGrantsAsync(WithTokenPermissions(wildcard, AdminPermissions.AdminAccess))).Should().BeFalse();
    }

    [Fact]
    public async Task A_superadmin_holding_only_the_wildcard_does_not_get_users_delete()
    {
        var superAdmin = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "root"),
            new Claim("super_admin", "true"),
            new Claim("role", "SuperAdmin"),
            new Claim("permissions", JsonSerializer.Serialize(new[] { "*" })),
        }, "test"));

        (await HandlerGrantsAsync(superAdmin)).Should().BeFalse();
    }

    [Fact]
    public async Task Roles_resolved_from_the_database_need_the_permission_by_name()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "admin-1") }, "test"));

        (await HandlerGrantsAsync(principal, new[] { "*", "users.*" })).Should().BeFalse();
        (await HandlerGrantsAsync(principal, new[] { AdminPermissions.UsersDelete })).Should().BeTrue();
    }

    [Fact]
    public async Task Granted_by_name_it_is_held()
    {
        (await HandlerGrantsAsync(WithTokenPermissions(AdminPermissions.UsersDelete))).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_permission_check_endpoint_agrees(bool superAdmin)
    {
        var admin = new OlusoRole { Id = "r-admin", Name = "Admin", NormalizedName = "ADMIN", TenantId = TenantId };
        var roles = new Mock<IRoleStore>();
        roles.Setup(r => r.GetUserRolesInTenantAsync("admin-1", TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { admin });
        roles.Setup(r => r.GetRoleClaimsAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new RoleClaim { Type = OlusoRole.PermissionClaimType, Value = "*" },
            new RoleClaim { Type = OlusoRole.PermissionClaimType, Value = "users.*" },
        });
        var claims = new List<Claim> { new("sub", "admin-1"), new("tenant_id", TenantId) };
        if (superAdmin) claims.Add(new Claim("super_admin", "true"));
        var tenant = new TestTenantContext { TenantId = TenantId };
        var controller = new PermissionsController(tenant, roles.Object, new EffectiveRoleResolver(roles.Object, Mock.Of<ITenantStore>()),
            Mock.Of<IFeatureGate>(), NullLogger<PermissionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
            },
        };

        var deleteCheck = (PermissionCheckResult)((OkObjectResult)(await controller.CheckPermission(AdminPermissions.UsersDelete, default)).Result!).Value!;
        var writeCheck = (PermissionCheckResult)((OkObjectResult)(await controller.CheckPermission(AdminPermissions.UsersWrite, default)).Result!).Value!;

        deleteCheck.HasPermission.Should().BeFalse();
        writeCheck.HasPermission.Should().BeTrue();
    }

    // ---------------------------------------------------------------- default roles

    [Fact]
    public async Task The_default_tenant_roles_do_not_include_users_delete()
    {
        var options = new DbContextOptionsBuilder<OlusoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ReservedAuthInterceptor())
            .Options;
        await using var context = new OlusoDbContext(options);
        var roles = await new TenantDefaultRoleSeeder(context, NullLogger<TenantDefaultRoleSeeder>.Instance).SeedAsync(TenantId);

        foreach (var role in new[] { roles.Admin, roles.User })
        {
            var stored = await context.RoleClaims
                .Where(c => c.RoleId == role.Id && c.ClaimType == OlusoRole.PermissionClaimType)
                .Select(c => c.ClaimValue!)
                .ToListAsync();

            PermissionExpander.Expand(stored).Should().NotContain(AdminPermissions.UsersDelete, $"{role.Name} is seeded by default");
        }
    }

    // ---------------------------------------------------------------- service clients

    [Fact]
    public void A_service_client_cannot_put_users_delete_on_a_role()
    {
        RolePermissionValidator.Validate(new[] { AdminPermissions.UsersDelete }, RolePermissionGrantor.ServiceClient)
            .Should().Contain(AdminPermissions.UsersDelete);
        RolePermissionValidator.Validate(new[] { AdminPermissions.UsersRead }, RolePermissionGrantor.ServiceClient)
            .Should().BeNull();
    }

    [Fact]
    public void An_admin_can_assign_users_delete_to_a_role_by_name()
    {
        RolePermissionValidator.Validate(new[] { AdminPermissions.UsersDelete }, new RolePermissionGrantor(IsSuperAdmin: false, IsOrgAdmin: false))
            .Should().BeNull();
    }

    [Fact]
    public void The_service_api_catalog_flags_the_delete_scope_as_sensitive()
    {
        var catalog = (ServiceApiCatalogDto)((OkObjectResult)new ServiceApiController(new TestTenantContext { TenantId = TenantId })
            .GetScopes().Result!).Value!;

        var delete = catalog.Scopes.Single(s => s.Name == ServiceScopes.UsersDelete);
        delete.RequiresExplicitGrant.Should().BeTrue();
        delete.RequiredAdminPermission.Should().Be(AdminPermissions.UsersDelete);
        catalog.Scopes.Where(s => s.Name != ServiceScopes.UsersDelete).Should().OnlyContain(s => !s.RequiresExplicitGrant);
    }

    // ---------------------------------------------------------------- client saves

    private static (ClientsController Controller, Mock<IClientStore> Clients) ClientsApi(bool adminHoldsUsersDelete)
    {
        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.AddClientAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client c, CancellationToken _) => c);
        clients.Setup(c => c.UpdateClientAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client c, CancellationToken _) => c);

        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetProtocolSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProtocolSettings());

        var authorization = new Mock<IAuthorizationService>();
        authorization
            .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string policy) =>
                policy == PermissionPolicyConstants.PolicyPrefix + AdminPermissions.UsersDelete && adminHoldsUsersDelete
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed());

        var controller = new ClientsController(
            new TestTenantContext { TenantId = TenantId }, clients.Object, settings.Object, Mock.Of<IIssuerResolver>(),
            Mock.Of<IOlusoEventService>(), Mock.Of<IServiceApiScopeProvisioner>(), authorization.Object,
            NullLogger<ClientsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", "admin-1"), new Claim("tenant_id", TenantId),
                    }, "test")),
                },
            },
        };
        return (controller, clients);
    }

    private static CreateClientRequest NewServiceClient(params string[] scopes) => new()
    {
        ClientId = $"svc-{Guid.NewGuid():N}",
        ClientName = "Integration",
        AllowedGrantTypes = new List<string> { "client_credentials" },
        AllowedScopes = new List<string> { ServiceScopes.ServiceApi }.Concat(scopes).ToList(),
    };

    [Fact]
    public async Task Giving_a_client_the_delete_scope_requires_the_admin_to_hold_users_delete()
    {
        var (controller, clients) = ClientsApi(adminHoldsUsersDelete: false);

        var result = await controller.CreateClient(NewServiceClient(ServiceScopes.UsersRead, ServiceScopes.UsersDelete), default);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        clients.Verify(c => c.AddClientAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_admin_holding_users_delete_can_give_a_client_the_delete_scope()
    {
        var (controller, clients) = ClientsApi(adminHoldsUsersDelete: true);

        var result = await controller.CreateClient(NewServiceClient(ServiceScopes.UsersDelete), default);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        clients.Verify(c => c.AddClientAsync(
            It.Is<Client>(x => x.AllowedScopes.Any(s => s.Scope == ServiceScopes.UsersDelete)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Other_service_scopes_need_no_extra_permission()
    {
        var (controller, _) = ClientsApi(adminHoldsUsersDelete: false);

        var result = await controller.CreateClient(NewServiceClient(ServiceScopes.UsersRead, ServiceScopes.UsersWrite), default);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    private static Client ExistingServiceClient(params string[] scopes) => new()
    {
        Id = 7,
        ClientId = "svc-existing",
        ClientName = "Existing",
        TenantId = TenantId,
        AllowedGrantTypes = new List<ClientGrantType> { new() { GrantType = "client_credentials" } },
        AllowedScopes = new[] { ServiceScopes.ServiceApi }.Concat(scopes).Select(s => new ClientScope { Scope = s }).ToList(),
    };

    [Fact]
    public async Task Adding_the_delete_scope_to_an_existing_client_requires_users_delete()
    {
        var (controller, clients) = ClientsApi(adminHoldsUsersDelete: false);
        clients.Setup(c => c.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingServiceClient(ServiceScopes.UsersRead));

        var result = await controller.UpdateClient(7, new UpdateClientRequest
        {
            AllowedScopes = new List<string> { ServiceScopes.ServiceApi, ServiceScopes.UsersRead, ServiceScopes.UsersDelete },
        }, default);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        clients.Verify(c => c.UpdateClientAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_client_that_already_has_the_delete_scope_can_still_be_edited()
    {
        var (controller, clients) = ClientsApi(adminHoldsUsersDelete: false);
        clients.Setup(c => c.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingServiceClient(ServiceScopes.UsersDelete));

        var result = await controller.UpdateClient(7, new UpdateClientRequest { ClientName = "Renamed" }, default);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
