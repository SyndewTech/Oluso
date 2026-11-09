using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// Verifies that non-permission role claims reach tokens through the same
/// scope gate as user claims — the mechanism a tenant uses to model its own
/// application's authorization — while permission claims and reserved keys
/// stay out of them.
/// </summary>
public class DefaultProfileServiceRoleClaimsTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<IOlusoUserService> _userService = new() { CallBase = true };
    private readonly Mock<IRoleStore> _roleStore = new();
    private readonly Mock<IClaimsProviderRegistry> _claimsRegistry = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ITenantStore> _tenantStore = new();
    private readonly Mock<IResourceStore> _resourceStore = new();

    private readonly DefaultProfileService _service;

    public DefaultProfileServiceRoleClaimsTests()
    {
        _claimsRegistry
            .Setup(x => x.GetAllClaimsAsync(It.IsAny<ClaimsProviderContext>()))
            .ReturnsAsync(new Dictionary<string, object>());
        _resourceStore
            .Setup(x => x.FindIdentityResourcesByScopeNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdentityResource>());
        _resourceStore
            .Setup(x => x.FindResourcesByUrisAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Resource>());
        _resourceStore
            .Setup(x => x.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ApiScope>());

        _tenantContext.SetupGet(x => x.TenantId).Returns(TenantId);

        _userService
            .Setup(x => x.FindByIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo
            {
                Id = "user-1",
                Username = "alice",
                TenantId = TenantId,
                Roles = new[] { "Dispatcher" }
            });

        _service = new DefaultProfileService(
            _userService.Object,
            _roleStore.Object,
            _claimsRegistry.Object,
            _tenantContext.Object,
            _tenantStore.Object,
            _resourceStore.Object,
            Mock.Of<ILogger<DefaultProfileService>>());
    }

    [Fact]
    public async Task Role_claim_is_emitted_when_a_granted_scope_lists_its_type()
    {
        SetupRole("Dispatcher", ("app_permissions", "invoices.approve"), ("region", "north"));
        ReleaseClaimTypesForScope("dispatch", "app_permissions");

        var claims = await IssueAsync("openid", "dispatch");

        claims.Should().Contain(c => c.Type == "app_permissions" && c.Value == "invoices.approve");
        // "region" wasn't released by any granted scope.
        claims.Should().NotContain(c => c.Type == "region");
    }

    [Fact]
    public async Task Role_claim_is_withheld_when_no_scope_releases_it()
    {
        SetupRole("Dispatcher", ("app_permissions", "invoices.approve"));

        var claims = await IssueAsync("openid");

        claims.Should().NotContain(c => c.Type == "app_permissions");
    }

    [Fact]
    public async Task Permission_claims_never_ride_the_role_claim_path()
    {
        // Even if a scope releases the "permission" type, permissions belong to
        // the permissions scope and to platform authorization — not here.
        SetupRole("Dispatcher", (OlusoRole.PermissionClaimType, "users.read"));
        ReleaseClaimTypesForScope("dispatch", OlusoRole.PermissionClaimType);

        var claims = await IssueAsync("openid", "dispatch");

        claims.Should().NotContain(c => c.Type == OlusoRole.PermissionClaimType);
    }

    [Fact]
    public async Task Reserved_claim_keys_on_a_role_are_never_emitted()
    {
        // A row predating ReservedAuthInterceptor must not become an escalation.
        SetupRole("Dispatcher",
            ("super_admin", "true"),
            ("allowed_tenants", "other-tenant"),
            ("module_seats", "[\"hr\"]"));
        ReleaseClaimTypesForScope("dispatch", "super_admin", "allowed_tenants", "module_seats");

        var claims = await IssueAsync("openid", "dispatch");

        claims.Should().NotContain(c => c.Type == "super_admin");
        claims.Should().NotContain(c => c.Type == "module_seats");
        // allowed_tenants is still emitted for the user's own tenant by
        // AddAuthorizationClaimsAsync — what must not survive is the value the
        // role tried to inject.
        claims.Should().NotContain(c => c.Type == "allowed_tenants" && c.Value == "other-tenant");
    }

    [Fact]
    public async Task Claims_come_from_the_roles_held_in_this_tenant_only()
    {
        // Role rows are read per-tenant, so a same-named role in another tenant
        // contributes nothing — resolving by name would leak its claims.
        SetupRole("Dispatcher", ("region", "north"));
        _roleStore.Setup(x => x.GetUserRolesInTenantAsync("user-1", "other-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new OlusoRole { Id = "other-role", Name = "Dispatcher", TenantId = "other-tenant" } });
        _roleStore.Setup(x => x.GetRoleClaimsAsync("other-role", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RoleClaim { Type = "region", Value = "south" } });

        ReleaseClaimTypesForScope("dispatch", "region");

        var claims = await IssueAsync("openid", "dispatch");

        claims.Should().Contain(c => c.Type == "region" && c.Value == "north");
        claims.Should().NotContain(c => c.Value == "south");
    }

    private void SetupRole(string roleName, params (string Type, string Value)[] claims)
    {
        var role = new OlusoRole { Id = $"{roleName}-id", Name = roleName, TenantId = TenantId };

        _roleStore.Setup(x => x.GetUserRolesInTenantAsync("user-1", TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { role });
        _roleStore.Setup(x => x.GetRoleClaimsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claims.Select(c => new RoleClaim { Type = c.Type, Value = c.Value }).ToList());
    }

    private void ReleaseClaimTypesForScope(string scope, params string[] claimTypes)
    {
        _resourceStore
            .Setup(x => x.FindApiScopesByNameAsync(
                It.Is<IEnumerable<string>>(s => s.Contains(scope)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ApiScope
                {
                    Name = scope,
                    UserClaims = claimTypes.Select(t => new ApiScopeClaim { Type = t }).ToList()
                }
            });
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
