using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers.Service;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Xunit;

namespace Oluso.Tests.Admin;

/// <summary>
/// The Service API policy and the per-endpoint scope checks read the same
/// claims, so a client the policy admits is not then refused by the endpoint.
/// </summary>
public class ServiceApiScopeConsistencyTests
{
    private sealed class ProbeController : ServiceBaseController
    {
        public ProbeController(ClaimsPrincipal user) : base(Mock.Of<ITenantContext>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        }

        public bool Has(string scope) => HasScope(scope);
        public IEnumerable<string> Scopes() => GetScopes();
    }

    public static TheoryData<string, string> Shapes => new()
    {
        { "scope", $"{ServiceScopes.ServiceApi} {ServiceScopes.UsersRead}" },
        { "scp", $"{ServiceScopes.ServiceApi} {ServiceScopes.UsersRead}" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task Policy_and_endpoint_agree_on_the_granted_scopes(string claimType, string value)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("client_id", "svc"),
            new Claim(claimType, value),
        }, "test"));

        var requirement = new ServiceApiRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);
        await new ServiceApiAuthorizationHandler(NullLogger<ServiceApiAuthorizationHandler>.Instance).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        var controller = new ProbeController(user);
        controller.Has(ServiceScopes.UsersRead).Should().BeTrue();
        controller.Scopes().Should().Contain(ServiceScopes.ServiceApi);
    }

    [Fact]
    public void Endpoint_reads_scopes_from_every_scope_claim()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("scope", ServiceScopes.ServiceApi),
            new Claim("scope", $"{ServiceScopes.RolesRead} {ServiceScopes.UsersRead}"),
        }, "test"));

        new ProbeController(user).Has(ServiceScopes.UsersRead).Should().BeTrue();
    }
}
