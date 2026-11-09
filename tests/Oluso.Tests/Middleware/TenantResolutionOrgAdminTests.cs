using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Middleware;
using Xunit;

namespace Oluso.Tests.Middleware;

/// <summary>
/// Org admin is derived from organization membership (is_org_admin and
/// org_roles, emitted for an Owner/Admin membership), never from the OrgAdmin
/// role — role assignments are per tenant, so the role can't say which
/// organization it covers.
/// </summary>
public class TenantResolutionOrgAdminTests
{
    private const string HomeTenant = "org1-home";
    private const string SiblingTenant = "org1-sibling";
    private const string OtherOrgTenant = "org2-tenant";

    private readonly Mock<ITenantStore> _tenantStore = new();
    private readonly Mock<ITenantContextAccessor> _tenantAccessor = new();

    public TenantResolutionOrgAdminTests()
    {
        foreach (var (id, org) in new[] { (HomeTenant, "org-1"), (SiblingTenant, "org-1"), (OtherOrgTenant, "org-2") })
        {
            var tenant = new Tenant { Id = id, Identifier = id, Name = id, OrganizationId = org, Enabled = true };
            _tenantStore.Setup(x => x.GetByIdentifierAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
            _tenantStore.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        }
    }

    private static ClaimsPrincipal OrgOwnerByMembership() => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", "owner-1"),
        new Claim("tenant_id", HomeTenant),
        new Claim("allowed_tenants", HomeTenant),
        new Claim("is_org_admin", "true"),
        new Claim("org_ids", "org-1"),
        new Claim("org_roles", "{\"org-1\":\"owner\"}"),
    }, "TestAuth"));

    private static ClaimsPrincipal OrgAdminRoleOnly() => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", "role-holder-1"),
        new Claim("tenant_id", HomeTenant),
        new Claim("allowed_tenants", HomeTenant),
        new Claim("role", "OrgAdmin"),
    }, "TestAuth"));

    [Theory]
    [InlineData(HomeTenant)]
    [InlineData(SiblingTenant)]
    public async Task MembershipOrgAdmin_ReachesEveryTenantOfTheirOrganization(string tenantId)
    {
        var context = BuildContext(OrgOwnerByMembership(), tenantId);

        (await InvokeAsync(context)).Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task MembershipOrgAdmin_IsRefusedATenantOutsideTheirOrganization()
    {
        var context = BuildContext(OrgOwnerByMembership(), OtherOrgTenant);

        (await InvokeAsync(context)).Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task OrgAdminRoleAlone_GrantsNoOrganizationAccess()
    {
        var context = BuildContext(OrgAdminRoleOnly(), SiblingTenant);

        (await InvokeAsync(context)).Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task OrgAdminRoleAlone_DoesNotSatisfyTheSuperAdminOrOrgAdminPolicy()
    {
        var tenantContext = new Mock<ITenantContext>();
        var handler = new TenantAdminAuthorizationHandler(
            tenantContext.Object, NullLogger<TenantAdminAuthorizationHandler>.Instance);
        var requirement = new TenantAdminRequirement(allowOrgAdmin: true);

        var byRole = new AuthorizationHandlerContext(new[] { requirement }, OrgAdminRoleOnly(), null);
        await handler.HandleAsync(byRole);
        byRole.HasSucceeded.Should().BeFalse();

        var byMembership = new AuthorizationHandlerContext(new[] { requirement }, OrgOwnerByMembership(), null);
        await handler.HandleAsync(byMembership);
        byMembership.HasSucceeded.Should().BeTrue();
    }

    private static DefaultHttpContext BuildContext(ClaimsPrincipal user, string tenantId)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/admin/users";
        context.Request.Host = new HostString("localhost");
        context.Request.Headers["X-Tenant-Id"] = tenantId;
        context.Response.Body = new MemoryStream();
        context.User = user;
        return context;
    }

    private async Task<bool> InvokeAsync(DefaultHttpContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_tenantStore.Object);
        services.AddSingleton(_tenantAccessor.Object);
        services.AddSingleton<IOptions<MultiTenancyOptions>>(Options.Create(new MultiTenancyOptions()));
        context.RequestServices = services.BuildServiceProvider();

        var reachedNext = false;
        var middleware = new TenantResolutionMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            NullLogger<TenantResolutionMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        return reachedNext;
    }
}
