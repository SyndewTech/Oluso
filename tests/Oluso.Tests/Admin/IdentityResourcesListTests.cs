using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Scopes;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Xunit;

namespace Oluso.Tests.Admin;

/// <summary>
/// The identity scope list is what pickers offer: the server's standard scopes, which work without
/// registration, plus the organisation's own; a registered scope of the same name replaces the built-in.
/// </summary>
public class IdentityResourcesListTests
{
    private static async Task<List<IdentityResourceDto>> ListAsync(params IdentityResource[] registered)
    {
        var store = new Mock<IResourceStore>();
        store.Setup(s => s.GetAllIdentityResourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(registered);
        var controller = new IdentityResourcesController(
            Mock.Of<ITenantContext>(), store.Object, NullLogger<IdentityResourcesController>.Instance);

        var result = await controller.GetAll(CancellationToken.None);
        return ((IEnumerable<IdentityResourceDto>)((OkObjectResult)result.Result!).Value!).ToList();
    }

    [Fact]
    public async Task Standard_scopes_are_listed_as_built_in_without_registration()
    {
        var list = await ListAsync();

        list.Select(r => r.Name).Should().Equal("openid", "profile", "email", "address", "phone");
        list.Should().OnlyContain(r => r.BuiltIn && r.Id == 0 && r.Enabled);
        list.Single(r => r.Name == "email").UserClaims.Should().Contain("email");
    }

    [Fact]
    public async Task A_registered_scope_replaces_its_built_in_and_custom_scopes_follow()
    {
        var list = await ListAsync(
            new IdentityResource { Id = 7, Name = "profile", DisplayName = "Our profile", Enabled = true },
            new IdentityResource { Id = 8, Name = "merchant", DisplayName = "Merchant", Enabled = true });

        list.Where(r => r.Name == "profile").Should().ContainSingle().Which.Should().Match<IdentityResourceDto>(r => !r.BuiltIn && r.Id == 7);
        list.Should().Contain(r => r.Name == "merchant" && !r.BuiltIn);
        list.Count(r => r.BuiltIn).Should().Be(4);
    }
}
