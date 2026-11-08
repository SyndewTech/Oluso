using System.Security.Claims;
using Oluso.Core.Authorization;
using Xunit;

namespace Oluso.Core.Tests.Authorization;

public class ClaimsPrincipalTenantExtensionsTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void Reads_multi_value_allowed_tenants_as_issued_by_the_profile_service()
    {
        var user = Principal(
            new Claim("tenant_id", "home"),
            new Claim("allowed_tenants", "home"),
            new Claim("allowed_tenants", "other"));

        var tenants = user.GetAllowedTenantIds();

        Assert.Equal(new[] { "home", "other" }, tenants);
    }

    [Fact]
    public void Reads_a_json_array_value()
    {
        var user = Principal(new Claim("allowed_tenants", "[\"a\",\"b\"]"));

        Assert.Equal(new[] { "a", "b" }, user.GetAllowedTenantIds());
    }

    [Fact]
    public void Reads_a_single_plain_value_without_falling_back_to_tenant_id()
    {
        var user = Principal(new Claim("tenant_id", "home"), new Claim("allowed_tenants", "other"));

        Assert.Equal(new[] { "other" }, user.GetAllowedTenantIds());
    }

    [Fact]
    public void Falls_back_to_tenant_id_for_legacy_tokens()
    {
        var user = Principal(new Claim("tenant_id", "home"));

        Assert.Equal(new[] { "home" }, user.GetAllowedTenantIds());
    }

    [Fact]
    public void Deduplicates_and_ignores_blanks()
    {
        var user = Principal(
            new Claim("allowed_tenants", "a"),
            new Claim("allowed_tenants", " a "),
            new Claim("allowed_tenants", ""),
            new Claim("allowed_tenants", "b,c"));

        Assert.Equal(new[] { "a", "b", "c" }, user.GetAllowedTenantIds());
    }
}
