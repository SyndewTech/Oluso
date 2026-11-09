using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Xunit;

namespace Oluso.Tests.Organizations;

/// <summary>
/// The admin API's tenant access is the admin.access permission; the SuperAdmin and SuperAdminOrOrgAdmin policies are
/// the role-based checks endpoints opt into.
/// </summary>
public class OrganizationAccessTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();

    [Theory]
    [InlineData("AdminApi")]
    [InlineData("TenantAdmin")]
    public void Admin_api_access_is_the_admin_access_permission_not_a_role_name(string policyName)
    {
        var services = new ServiceCollection();
        services.AddControllers().AddOlusoAdmin();
        var policy = services.BuildServiceProvider().GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy(policyName)!;

        policy.Requirements.OfType<PermissionRequirement>().Should().ContainSingle()
            .Which.Permission.Should().Be(AdminPermissions.AdminAccess);
        policy.Requirements.OfType<TenantAdminRequirement>().Should().BeEmpty();
    }

    [Theory]
    [InlineData("super_admin", "true", null)]
    [InlineData(ClaimTypes.Role, "SuperAdmin", null)]
    [InlineData(ClaimTypes.Role, "SystemAdmin", "_platform")]
    public async Task A_system_admin_passes_the_SuperAdmin_policy(string claimType, string value, string? tenantId)
    {
        var claims = new List<Claim> { new("sub", "u-1"), new(claimType, value) };
        if (tenantId != null) claims.Add(new Claim("tenant_id", tenantId));

        (await Handle(claims, new TenantAdminRequirement())).Should().BeTrue();
    }

    [Fact]
    public async Task A_tenant_admin_or_a_SuperAdmin_role_inside_a_tenant_fails_the_SuperAdmin_policy()
    {
        (await Handle([new("sub", "u-1"), new(ClaimTypes.Role, "Admin"), new("tenant_id", "tenant-123")], new TenantAdminRequirement()))
            .Should().BeFalse();
        (await Handle([new("sub", "u-1"), new(ClaimTypes.Role, "SuperAdmin"), new("tenant_id", "tenant-123")], new TenantAdminRequirement()))
            .Should().BeFalse();
    }

    [Fact]
    public async Task An_organization_admin_passes_only_the_policy_that_allows_them()
    {
        Claim[] orgAdmin = [new("sub", "u-1"), new("is_org_admin", "true"), new("current_org_role", "admin")];

        (await Handle(orgAdmin, new TenantAdminRequirement(allowOrgAdmin: true))).Should().BeTrue();
        (await Handle(orgAdmin, new TenantAdminRequirement())).Should().BeFalse();
    }

    [Fact]
    public async Task An_organization_member_who_is_not_an_admin_fails_the_SuperAdminOrOrgAdmin_policy()
    {
        (await Handle([new("sub", "u-1"), new("current_org_role", "member")], new TenantAdminRequirement(allowOrgAdmin: true)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task An_unauthenticated_caller_fails()
    {
        var context = new AuthorizationHandlerContext([new TenantAdminRequirement(allowOrgAdmin: true)], new ClaimsPrincipal(new ClaimsIdentity()), null);
        await Handler().HandleAsync(context);
        context.HasSucceeded.Should().BeFalse();
    }

    private TenantAdminAuthorizationHandler Handler() =>
        new(_tenantContext.Object, NullLogger<TenantAdminAuthorizationHandler>.Instance);

    private async Task<bool> Handle(IEnumerable<Claim> claims, TenantAdminRequirement requirement)
    {
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")), null);
        await Handler().HandleAsync(context);
        return context.HasSucceeded;
    }
}
