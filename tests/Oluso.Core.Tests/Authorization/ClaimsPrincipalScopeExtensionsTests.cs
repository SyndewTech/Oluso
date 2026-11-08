using System.Security.Claims;
using Oluso.Core.Authorization;
using Xunit;

namespace Oluso.Core.Tests.Authorization;

public class ClaimsPrincipalScopeExtensionsTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void Reads_a_single_space_separated_scope_claim()
    {
        var user = Principal(new Claim("scope", "service users.read"));

        Assert.Equal(new[] { "service", "users.read" }, user.GetGrantedScopes());
        Assert.True(user.HasGrantedScope("users.read"));
    }

    [Fact]
    public void Reads_one_scope_claim_per_value_including_space_separated_ones_after_the_first()
    {
        var user = Principal(new Claim("scope", "service"), new Claim("scope", "users.read roles.read"));

        Assert.True(user.HasGrantedScope("service"));
        Assert.True(user.HasGrantedScope("roles.read"));
    }

    [Fact]
    public void Reads_scp_claims()
    {
        var user = Principal(new Claim("scp", "service users.write"));

        Assert.True(user.HasGrantedScope("users.write"));
    }

    [Fact]
    public void A_scope_is_matched_whole_and_case_sensitively()
    {
        var user = Principal(new Claim("scope", "users.readonly Service"));

        Assert.False(user.HasGrantedScope("users.read"));
        Assert.False(user.HasGrantedScope("service"));
    }
}
