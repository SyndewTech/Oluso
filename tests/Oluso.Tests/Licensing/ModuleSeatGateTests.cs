using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Xunit;

namespace Oluso.Tests.Licensing;

/// <summary>
/// Workspace endpoints let in only users holding a seat in the module (a module seat row),
/// the same rule the workspace app switcher applies, whether or not the tenant caps its
/// seats. Admin and customer-facing surfaces check only that the tenant has the module.
/// </summary>
public class ModuleSeatGateTests
{
    private const string Tenant = "acme";
    private readonly HashSet<(string User, string Feature)> _seats = new();
    private bool _tenantHasModule = true;

    private async Task<IActionResult?> RunAsync(RequireFeatureAttribute attribute, string? userId)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(Tenant);
        var seatStore = new Mock<IUserModuleAccessStore>();
        seatStore.Setup(s => s.HasAccessAsync(Tenant, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string user, string feature, CancellationToken _) => _seats.Contains((user, feature)));
        var tenantFeatures = new Mock<IFeatureAvailabilityProvider>();
        tenantFeatures.Setup(p => p.IsFeatureAvailableAsync(It.IsAny<string>(), Tenant, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _tenantHasModule);

        var services = new ServiceCollection()
            .AddSingleton(tenant.Object)
            .AddSingleton(seatStore.Object)
            .AddSingleton(tenantFeatures.Object)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddScoped<IFeatureGate, FeatureGate>()
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        if (userId != null) http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)], "test"));
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

        var filter = (IAsyncAuthorizationFilter)attribute.CreateInstance(services);
        await filter.OnAuthorizationAsync(context);
        return context.Result;
    }

    [Fact]
    public async Task A_workspace_endpoint_lets_in_only_users_holding_a_seat()
    {
        _seats.Add(("alice", "approval"));
        var workspace = new RequireFeatureAttribute("approval") { RequireSeat = true };

        (await RunAsync(workspace, "alice")).Should().BeNull();
        (await RunAsync(workspace, "customer")).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        (await RunAsync(workspace, null)).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task A_seat_is_no_use_when_the_tenant_does_not_have_the_module()
    {
        _seats.Add(("alice", "approval"));
        _tenantHasModule = false;

        (await RunAsync(new RequireFeatureAttribute("approval") { RequireSeat = true }, "alice"))
            .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Admin_and_account_endpoints_check_only_the_tenant()
    {
        (await RunAsync(new RequireFeatureAttribute("emr"), "patient")).Should().BeNull();

        _tenantHasModule = false;
        (await RunAsync(new RequireFeatureAttribute("emr"), "patient")).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }
}
