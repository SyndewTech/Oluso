using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// The "claim" condition source reads the claims the user's session would carry, keyed by
/// the short names the admin UI suggests.
/// </summary>
public class ConditionClaimsLoadingTests
{
    [Fact]
    public async Task Claims_are_the_sessions_claims_under_their_short_names()
    {
        var user = new OlusoUser { Id = "u1", UserName = "jo" };
        var userManager = new Mock<UserManager<OlusoUser>>(Mock.Of<IUserStore<OlusoUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(m => m.FindByIdAsync("u1")).ReturnsAsync(user);
        var principalFactory = new Mock<IUserClaimsPrincipalFactory<OlusoUser>>();
        principalFactory.Setup(f => f.CreateAsync(user)).ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "u1"),
            new Claim(ClaimTypes.Name, "jo"),
            new Claim(ClaimTypes.Role, "Member"),
            new Claim(ClaimTypes.Role, "Approver"),
            new Claim("tenant_id", "acme")
        }, "cookie")));
        var services = new ServiceCollection()
            .AddSingleton(userManager.Object)
            .AddSingleton(principalFactory.Object)
            .BuildServiceProvider();

        var claims = await JourneyConditionValues.LoadClaimsAsync(services, "u1");

        claims!["sub"].Should().Equal("u1");
        claims["preferred_username"].Should().Equal("jo");
        claims["role"].Should().Equal("Member", "Approver");
        claims["TENANT_ID"].Should().Equal("acme");
    }
}
