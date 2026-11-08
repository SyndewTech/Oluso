using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// Verifies that DefaultProfileService gates identity claims by the granted
/// scopes / requested resources: standard OIDC scopes fall back to built-in
/// defaults, a configured IdentityResource overrides that default, ApiScope /
/// Resource UserClaims are honoured, and platform claims (tenant_id, role) are
/// always emitted regardless of scope.
/// </summary>
public class DefaultProfileServiceScopeClaimsTests
{
    private readonly Mock<IOlusoUserService> _userService = new() { CallBase = true };
    private readonly Mock<IRoleStore> _roleStore = new();
    private readonly Mock<IClaimsProviderRegistry> _claimsRegistry = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ITenantStore> _tenantStore = new();
    private readonly Mock<IResourceStore> _resourceStore = new();

    private readonly DefaultProfileService _service;

    public DefaultProfileServiceScopeClaimsTests()
    {
        _claimsRegistry
            .Setup(x => x.GetAllClaimsAsync(It.IsAny<ClaimsProviderContext>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // No configured DB rows by default — exercises the built-in OIDC defaults.
        _resourceStore
            .Setup(x => x.FindIdentityResourcesByScopeNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdentityResource>());
        _resourceStore
            .Setup(x => x.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ApiScope>());
        _resourceStore
            .Setup(x => x.FindResourcesByUrisAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Resource>());

        _service = new DefaultProfileService(
            _userService.Object,
            _roleStore.Object,
            _claimsRegistry.Object,
            _tenantContext.Object,
            _tenantStore.Object,
            _resourceStore.Object,
            Mock.Of<ILogger<DefaultProfileService>>());
    }

    private void SetupUser(OlusoUserInfo user)
    {
        _userService.Setup(x => x.FindByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        // The role claim carries the roles assigned in the token's tenant.
        _roleStore
            .Setup(x => x.GetUserRolesInTenantAsync(user.Id, user.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user.Roles ?? Array.Empty<string>())
                .Select(name => new OlusoRole { Id = $"role-{name}", Name = name, TenantId = user.TenantId })
                .ToList());
    }

    private static OlusoUserInfo TestUser(IDictionary<string, string>? custom = null) => new()
    {
        Id = "user-1",
        Username = "alice",
        Email = "alice@example.com",
        EmailVerified = true,
        FirstName = "Alice",
        LastName = "Smith",
        DisplayName = "Alice Smith",
        PhoneNumber = "+15550001111",
        TenantId = "tenant-1",
        Roles = new[] { "Member" },
        CustomProperties = custom,
    };

    private async Task<List<System.Security.Claims.Claim>> IssueAsync(
        IEnumerable<string> scopes, IEnumerable<string>? resources = null)
    {
        var context = new ProfileDataRequestContext
        {
            SubjectId = "user-1",
            Client = new Client { ClientId = "c1" },
            RequestedScopes = scopes,
            RequestedResources = resources,
        };
        await _service.GetProfileDataAsync(context);
        return context.IssuedClaims;
    }

    [Fact]
    public async Task Standard_profile_and_email_scopes_emit_their_default_claims()
    {
        SetupUser(TestUser());

        var claims = await IssueAsync(new[] { "openid", "profile", "email" });

        claims.Should().Contain(c => c.Type == "name" && c.Value == "Alice Smith");
        claims.Should().Contain(c => c.Type == "given_name" && c.Value == "Alice");
        claims.Should().Contain(c => c.Type == "family_name" && c.Value == "Smith");
        claims.Should().Contain(c => c.Type == "email" && c.Value == "alice@example.com");
        claims.Should().Contain(c => c.Type == "email_verified");
    }

    [Fact]
    public async Task Openid_only_withholds_profile_and_email_but_keeps_platform_claims()
    {
        SetupUser(TestUser());

        var claims = await IssueAsync(new[] { "openid" });

        claims.Should().NotContain(c => c.Type == "name");
        claims.Should().NotContain(c => c.Type == "email");
        // Platform claims are never scope-gated.
        claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == "tenant-1");
        claims.Should().Contain(c => c.Type == "role" && c.Value == "Member");
    }

    [Fact]
    public async Task Configured_identity_resource_overrides_the_standard_default()
    {
        SetupUser(TestUser());
        // A DB row for "profile" that only releases "name" replaces the built-in set.
        _resourceStore
            .Setup(x => x.FindIdentityResourcesByScopeNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new IdentityResource
                {
                    Name = "profile",
                    UserClaims = new List<IdentityResourceClaim> { new() { Type = "name" } },
                },
            });

        var claims = await IssueAsync(new[] { "openid", "profile" });

        claims.Should().Contain(c => c.Type == "name");
        claims.Should().NotContain(c => c.Type == "given_name");
        claims.Should().NotContain(c => c.Type == "family_name");
    }

    [Fact]
    public async Task ApiScope_user_claims_release_matching_custom_properties()
    {
        SetupUser(TestUser(new Dictionary<string, string> { ["department"] = "Cardiology" }));
        _resourceStore
            .Setup(x => x.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ApiScope
                {
                    Name = "emr",
                    UserClaims = new List<ApiScopeClaim> { new() { Type = "department" } },
                },
            });

        var claims = await IssueAsync(new[] { "openid", "emr" });

        claims.Should().Contain(c => c.Type == "department" && c.Value == "Cardiology");
    }

    [Fact]
    public async Task Resource_user_claims_are_honoured_for_requested_resources()
    {
        SetupUser(TestUser(new Dictionary<string, string> { ["department"] = "Cardiology" }));
        _resourceStore
            .Setup(x => x.FindResourcesByUrisAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Resource
                {
                    Uri = "https://emr.api",
                    UserClaims = new List<ResourceClaim> { new() { Type = "department" } },
                },
            });

        var claims = await IssueAsync(new[] { "openid" }, resources: new[] { "https://emr.api" });

        claims.Should().Contain(c => c.Type == "department" && c.Value == "Cardiology");
    }
}
