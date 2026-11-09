using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// Entitlements are the tenant's own permissions, carried on roles and released
/// by the entitlements scope. These cover the rules that decide whether an
/// assigned one actually reaches a token.
/// </summary>
public class DefaultProfileServiceEntitlementTests
{
    private const string TenantId = "tenant-1";
    private const string ScopeName = "entitlements";

    private readonly Mock<IOlusoUserService> _userService = new() { CallBase = true };
    private readonly Mock<IRoleStore> _roleStore = new();
    private readonly Mock<IClaimsProviderRegistry> _claimsRegistry = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ITenantStore> _tenantStore = new();
    private readonly Mock<IResourceStore> _resourceStore = new();
    private readonly Mock<IEntitlementStore> _entitlementStore = new();

    private readonly DefaultProfileService _service;

    public DefaultProfileServiceEntitlementTests()
    {
        _claimsRegistry
            .Setup(x => x.GetAllClaimsAsync(It.IsAny<ClaimsProviderContext>()))
            .ReturnsAsync(new Dictionary<string, object>());
        _resourceStore
            .Setup(x => x.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ApiScope>());
        _resourceStore
            .Setup(x => x.FindResourcesByUrisAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Resource>());
        _resourceStore
            .Setup(x => x.FindIdentityResourcesByScopeNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdentityResource>());

        _tenantContext.SetupGet(x => x.TenantId).Returns(TenantId);
        _tenantContext.SetupGet(x => x.HasTenant).Returns(true);

        _userService
            .Setup(x => x.FindByIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo
            {
                Id = "user-1",
                Username = "alice",
                TenantId = TenantId,
                Roles = new[] { "Approver" }
            });

        _service = new DefaultProfileService(
            _userService.Object,
            _roleStore.Object,
            _claimsRegistry.Object,
            _tenantContext.Object,
            _tenantStore.Object,
            _resourceStore.Object,
            Mock.Of<ILogger<DefaultProfileService>>(),
            entitlementStore: _entitlementStore.Object);
    }

    [Fact]
    public async Task Assigned_entitlement_is_emitted_when_the_scope_is_granted()
    {
        GiveRoleEntitlements("invoices.approve");
        ReleaseEntitlementsScope();
        CatalogContains(("invoices.approve", true));

        var claims = await IssueAsync("openid", ScopeName);

        claims.Should().Contain(c => c.Type == "entitlements" && c.Value == "invoices.approve");
    }

    [Fact]
    public async Task Entitlement_is_withheld_without_the_scope()
    {
        GiveRoleEntitlements("invoices.approve");
        CatalogContains(("invoices.approve", true));

        var claims = await IssueAsync("openid");

        claims.Should().NotContain(c => c.Type == "entitlements");
    }

    [Fact]
    public async Task Disabled_entitlement_is_withheld_without_unassigning_it()
    {
        GiveRoleEntitlements("invoices.approve", "invoices.void");
        ReleaseEntitlementsScope();
        CatalogContains(("invoices.approve", true), ("invoices.void", false));

        var claims = await IssueAsync("openid", ScopeName);

        claims.Should().Contain(c => c.Value == "invoices.approve");
        claims.Should().NotContain(c => c.Value == "invoices.void");
    }

    [Fact]
    public async Task Entitlement_deleted_from_the_catalog_stops_being_issued()
    {
        // Role claim survives, catalog row doesn't — the claim must not outlive it.
        GiveRoleEntitlements("invoices.approve");
        ReleaseEntitlementsScope();
        CatalogContains();

        var claims = await IssueAsync("openid", ScopeName);

        claims.Should().NotContain(c => c.Type == "entitlements");
    }

    private void GiveRoleEntitlements(params string[] names)
    {
        var role = new OlusoRole { Id = "role-1", Name = "Approver", TenantId = TenantId };

        _roleStore.Setup(x => x.GetUserRolesInTenantAsync("user-1", TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { role });
        _roleStore.Setup(x => x.GetRoleClaimsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(names
                .Select(n => new RoleClaim { Type = OlusoRole.EntitlementClaimType, Value = n })
                .ToList());
    }

    private void ReleaseEntitlementsScope()
    {
        _resourceStore
            .Setup(x => x.FindApiScopesByNameAsync(
                It.Is<IEnumerable<string>>(s => s.Contains(ScopeName)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ApiScope
                {
                    Name = ScopeName,
                    UserClaims = new List<ApiScopeClaim>
                    {
                        new() { Type = OlusoRole.EntitlementClaimType }
                    }
                }
            });
    }

    private void CatalogContains(params (string Name, bool Enabled)[] entries)
    {
        _entitlementStore
            .Setup(x => x.FindByNamesAsync(TenantId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries
                .Select(e => new Entitlement { Name = e.Name, Enabled = e.Enabled, TenantId = TenantId })
                .ToList());
    }

    private async Task<List<System.Security.Claims.Claim>> IssueAsync(params string[] scopes)
    {
        var context = new ProfileDataRequestContext
        {
            SubjectId = "user-1",
            Client = new Client { ClientId = "c1" },
            RequestedScopes = scopes,
        };
        await _service.GetProfileDataAsync(context);
        return context.IssuedClaims;
    }
}
