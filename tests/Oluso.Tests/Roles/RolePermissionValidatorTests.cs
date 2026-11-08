using FluentAssertions;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// The shared rule for which permissions may be written to a tenant role, used
/// by both the admin Roles API and the Service API.
/// </summary>
public class RolePermissionValidatorTests
{
    private static readonly IReadOnlyList<PermissionDefinition> Definitions = AdminPermissions.GetCorePermissions()
        .Append(new PermissionDefinition("hr.payroll.run", "Run payroll", "HR", "Run payroll", FeatureKey: "hr"))
        .ToList();

    private static readonly RolePermissionGrantor SuperAdmin = new(IsSuperAdmin: true, IsOrgAdmin: false);
    private static readonly RolePermissionGrantor OrgAdmin = new(IsSuperAdmin: false, IsOrgAdmin: true);
    private static readonly RolePermissionGrantor TenantAdmin = new(IsSuperAdmin: false, IsOrgAdmin: false);

    private static string? Validate(RolePermissionGrantor grantor, params string[] permissions) =>
        RolePermissionValidator.Validate(permissions, grantor, Definitions);

    [Fact]
    public void A_service_client_has_neither_platform_nor_organization_standing()
    {
        RolePermissionGrantor.ServiceClient.IsSuperAdmin.Should().BeFalse();
        RolePermissionGrantor.ServiceClient.IsOrgAdmin.Should().BeFalse();
    }

    [Fact]
    public void Core_tenant_permissions_are_accepted_from_a_service_client()
    {
        Validate(RolePermissionGrantor.ServiceClient,
                AdminPermissions.UsersRead, AdminPermissions.RolesWrite, AdminPermissions.ClientsRead)
            .Should().BeNull();
    }

    [Fact]
    public void No_permissions_is_valid()
    {
        RolePermissionValidator.Validate(null, RolePermissionGrantor.ServiceClient, Definitions).Should().BeNull();
        Validate(RolePermissionGrantor.ServiceClient).Should().BeNull();
    }

    [Theory]
    [InlineData(AdminPermissions.TenantsWrite)]
    [InlineData(AdminPermissions.TenantsDelete)]
    [InlineData(AdminPermissions.PlatformClientsWrite)]
    public void SuperAdmin_only_permissions_are_refused_unless_SuperAdmin(string permission)
    {
        Validate(RolePermissionGrantor.ServiceClient, permission).Should().Contain("require SuperAdmin");
        Validate(TenantAdmin, permission).Should().Contain("require SuperAdmin");
        Validate(OrgAdmin, permission).Should().Contain("require SuperAdmin");
        Validate(SuperAdmin, permission).Should().BeNull();
    }

    [Theory]
    [InlineData(AdminPermissions.OrganizationsRead)]
    [InlineData(AdminPermissions.OrganizationsManageMembers)]
    [InlineData(AdminPermissions.TenantsRead)]
    [InlineData(AdminPermissions.TenantsManageSettings)]
    public void Organization_level_permissions_are_refused_unless_organization_admin(string permission)
    {
        Validate(RolePermissionGrantor.ServiceClient, permission).Should().Contain("require Organization Admin");
        Validate(TenantAdmin, permission).Should().Contain("require Organization Admin");
        Validate(OrgAdmin, permission).Should().BeNull();
        Validate(SuperAdmin, permission).Should().BeNull();
    }

    [Fact]
    public void Module_permissions_are_refused_for_everyone()
    {
        Validate(RolePermissionGrantor.ServiceClient, "hr.payroll.run").Should().Contain("module permissions");
        Validate(SuperAdmin, "hr.payroll.run").Should().Contain("module permissions");
    }

    [Fact]
    public void Unknown_and_wildcard_permissions_are_refused()
    {
        Validate(SuperAdmin, "made.up").Should().Contain("not valid");
        Validate(RolePermissionGrantor.ServiceClient, "users.*").Should().Contain("not valid");
    }

    [Fact]
    public void One_forbidden_permission_refuses_the_whole_set()
    {
        Validate(RolePermissionGrantor.ServiceClient, AdminPermissions.UsersRead, AdminPermissions.TenantsWrite)
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("permissions")]
    [InlineData("role")]
    [InlineData("super_admin")]
    [InlineData("tenant_id")]
    [InlineData("module_seats")]
    [InlineData("entitlements")]
    [InlineData("sub")]
    public void Protected_claim_types_are_refused_on_roles(string claimType)
    {
        RolePermissionValidator.ValidateRoleClaims(new[] { claimType }).Should().NotBeNull();
    }

    [Fact]
    public void Ordinary_claim_types_are_accepted_on_roles()
    {
        RolePermissionValidator.ValidateRoleClaims(new[] { "department", "cost_center" }).Should().BeNull();
        RolePermissionValidator.ValidateRoleClaims(null).Should().BeNull();
    }
}
