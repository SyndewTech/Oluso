using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Oluso.EntityFramework.Stores;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// An Owner/Admin membership in an organization grants the OrgAdmin role row's
/// permissions in every tenant of that organization, and nowhere else — without
/// any OrgAdmin assignment rows. Checked on token minting (including the
/// tenant-switch re-mint, which calls the same GetUserPermissionsAsync) and on
/// the permission handler's database path (the header-only switch).
/// </summary>
public class OrgAdminMembershipGrantTests : IDisposable
{
    private const string OrgId = "org-1";
    private const string HomeTenant = "org1-home";
    private const string SiblingTenant = "org1-sibling";
    private const string OutsideTenant = "org2-tenant";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly RoleStore _store;
    private readonly OrganizationMembershipStore _orgMemberships;
    private readonly DefaultProfileService _profile;
    private readonly PermissionAuthorizationHandler _permissions;
    private OlusoRole _orgAdminRole = null!;
    private OlusoUser _owner = null!;

    public OrgAdminMembershipGrantTests()
    {
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _roleManager = IdentityTestFactory.CreateRoleManager(_context);
        var userManager = IdentityTestFactory.CreateUserManager(_context, _tenant);
        _store = new RoleStore(_roleManager, userManager, _context, _tenant);
        _orgMemberships = new OrganizationMembershipStore(_context);
        var tenantStore = new TenantStore(_context);
        var effectiveRoles = new EffectiveRoleResolver(_store, tenantStore, _orgMemberships);

        var signInManager = new SignInManager<OlusoUser>(
            userManager,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<OlusoUser>>(),
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<OlusoUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<OlusoUser>>());
        var userService = new IdentityUserService(
            userManager, signInManager, _tenant, Mock.Of<ITenantSettingsProvider>(), _context,
            NullLogger<IdentityUserService>.Instance);

        _profile = new DefaultProfileService(
            userService, _store, Mock.Of<IClaimsProviderRegistry>(), _tenant, tenantStore, Mock.Of<IResourceStore>(),
            NullLogger<DefaultProfileService>.Instance,
            organizationMembershipStore: _orgMemberships,
            effectiveRoles: effectiveRoles);

        _permissions = new PermissionAuthorizationHandler(
            effectiveRoles, _tenant, NullLogger<PermissionAuthorizationHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task SeedAsync(OrganizationRole membershipRole = OrganizationRole.Owner)
    {
        _tenant.TenantId = null;
        _context.Organizations.AddRange(
            new Organization { Id = OrgId, Name = "Org 1", Slug = "org-1" },
            new Organization { Id = "org-2", Name = "Org 2", Slug = "org-2" });
        _context.Tenants.AddRange(
            new Tenant { Id = HomeTenant, Name = HomeTenant, Identifier = HomeTenant, OrganizationId = OrgId },
            new Tenant { Id = SiblingTenant, Name = SiblingTenant, Identifier = SiblingTenant, OrganizationId = OrgId },
            new Tenant { Id = OutsideTenant, Name = OutsideTenant, Identifier = OutsideTenant, OrganizationId = "org-2" });
        await _context.SaveChangesAsync();

        _orgAdminRole = await IdentityTestFactory.CreateRoleAsync(
            _roleManager, EffectiveRoleResolver.OrgAdminRoleName, tenantId: null, isSystemRole: true);
        foreach (var permission in new[] { "organizations.*", "tenants.*", "users.*" })
        {
            await _store.AddRoleClaimAsync(_orgAdminRole.Id, OlusoRole.PermissionClaimType, permission);
        }

        _owner = await IdentityTestFactory.CreateUserAsync(_context, HomeTenant, "owner@org1.test");
        await _orgMemberships.CreateAsync(new OrganizationMembership
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = OrgId,
            UserId = _owner.Id,
            Role = membershipRole,
        });
    }

    /// <summary>What a token minted for <paramref name="tenantId"/> carries, incl. the switch re-mint.</summary>
    private async Task<ICollection<string>> TokenPermissionsAsync(string tenantId)
    {
        _tenant.TenantId = tenantId;
        return await _profile.GetUserPermissionsAsync(_owner.Id, tenantId);
    }

    /// <summary>
    /// A home-tenant token sent with X-Tenant-Id for another tenant: its baked-in
    /// permissions don't apply there, so the handler reads the database.
    /// </summary>
    private async Task<bool> HeaderSwitchPermittedAsync(string tenantId, string permission)
    {
        _tenant.TenantId = tenantId;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", _owner.Id),
            new Claim("tenant_id", HomeTenant),
            new Claim("permissions", JsonSerializer.Serialize(new[] { "organizations.*", "tenants.*", "users.*" })),
        }, "test"));
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);
        await _permissions.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Theory]
    [InlineData(HomeTenant)]
    [InlineData(SiblingTenant)]
    public async Task OrgOwner_HasTheOrgAdminPermissions_InEachTenantOfTheOrganization(string tenantId)
    {
        await SeedAsync();

        (await TokenPermissionsAsync(tenantId)).Should().BeEquivalentTo("organizations.*", "tenants.*", "users.*");
        (await HeaderSwitchPermittedAsync(tenantId, "users.manage_roles")).Should().BeTrue();
        (await _context.UserRoles.CountAsync()).Should().Be(0, "the grant comes from membership, not assignment rows");
    }

    [Fact]
    public async Task OrgAdminMembership_GrantsTheSameAsOwner()
    {
        await SeedAsync(OrganizationRole.Admin);

        (await TokenPermissionsAsync(SiblingTenant)).Should().Contain("tenants.*");
    }

    [Fact]
    public async Task OrgOwner_HasNoneOfIt_InATenantOutsideTheOrganization()
    {
        await SeedAsync();

        (await TokenPermissionsAsync(OutsideTenant)).Should().BeEmpty();
        (await HeaderSwitchPermittedAsync(OutsideTenant, "users.manage_roles")).Should().BeFalse();
    }

    [Fact]
    public async Task OrgOwner_CanCreateATenantInTheOrganization()
    {
        await SeedAsync();

        // Permission gate on POST api/admin/organizations/{id}/tenants, from a
        // sibling tenant the owner holds no role assignment in.
        (await HeaderSwitchPermittedAsync(SiblingTenant, AdminPermissions.OrganizationsManageTenants)).Should().BeTrue();

        // Organization gate on the same action: writes need Owner/Admin of the route's organization.
        (await OrganizationScopeAllowsAsync(OrgId, HttpMethods.Post)).Should().BeTrue();
        (await OrganizationScopeAllowsAsync("org-2", HttpMethods.Post)).Should().BeFalse();
    }

    [Fact]
    public async Task DemotingTheMembership_RemovesThePermissions_OnTheNextTokenOrRequest()
    {
        await SeedAsync();
        (await TokenPermissionsAsync(SiblingTenant)).Should().NotBeEmpty();

        var membership = await _orgMemberships.GetByUserAndOrganizationAsync(_owner.Id, OrgId);
        membership!.Role = OrganizationRole.Member;
        await _orgMemberships.UpdateAsync(membership);

        // A leftover OrgAdmin assignment row is not an input either.
        await _store.AssignToUserInTenantAsync(_orgAdminRole.Id, _owner.Id, HomeTenant, "legacy");

        (await TokenPermissionsAsync(SiblingTenant)).Should().BeEmpty();
        (await TokenPermissionsAsync(HomeTenant)).Should().BeEmpty();
        (await HeaderSwitchPermittedAsync(SiblingTenant, "users.manage_roles")).Should().BeFalse();
    }

    [Fact]
    public async Task EditingTheOrgAdminRolePermissions_ChangesWhatOrgAdminsGet()
    {
        await SeedAsync();

        await _store.AddRoleClaimAsync(_orgAdminRole.Id, OlusoRole.PermissionClaimType, "clients.read");
        await _store.RemoveRoleClaimAsync(_orgAdminRole.Id, OlusoRole.PermissionClaimType, "tenants.*");

        (await TokenPermissionsAsync(SiblingTenant)).Should().BeEquivalentTo("organizations.*", "users.*", "clients.read");
        (await HeaderSwitchPermittedAsync(SiblingTenant, "clients.read")).Should().BeTrue();
        (await HeaderSwitchPermittedAsync(SiblingTenant, "tenants.write")).Should().BeFalse();
    }

    private async Task<bool> OrganizationScopeAllowsAsync(string organizationId, string method)
    {
        var services = new ServiceCollection()
            .AddSingleton<IOrganizationMembershipStore>(_orgMemberships)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", _owner.Id) }, "test")),
        };
        httpContext.Request.Method = method;
        var routeData = new RouteData();
        routeData.Values["organizationId"] = organizationId;

        var executing = new ActionExecutingContext(
            new ActionContext(httpContext, routeData, new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());

        var reached = false;
        await new RequireOrganizationScopeAttribute().OnActionExecutionAsync(executing, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(executing, new List<IFilterMetadata>(), new object()));
        });
        return reached && executing.Result == null;
    }
}
