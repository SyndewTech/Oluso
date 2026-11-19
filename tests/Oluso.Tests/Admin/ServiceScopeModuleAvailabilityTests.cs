using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Controllers.Service;
using Oluso.Admin.Dto.Clients;
using Oluso.Admin.Dto.Scopes;
using Oluso.Admin.Services;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.Admin;

/// <summary>
/// A module's Service API scopes are offered to an organization, and can be given to its clients, only while it has
/// the module. A client that already has one keeps saving after the module is switched off, so it can be taken off.
/// </summary>
public class ServiceScopeModuleAvailabilityTests
{
    private const string TenantId = "t1";
    private const string Module = "availability-probe";
    private static readonly ServiceScopeDefinition ModuleScope =
        new("oluso:availability-probe:read", "Read probes", "Read the probe module's records.", "Probe", FeatureKey: Module);

    static ServiceScopeModuleAvailabilityTests()
    {
        ServiceScopes.Register(ModuleScope);
    }

    private static IFeatureGate Gate(bool moduleOn)
    {
        var gate = new Mock<IFeatureGate>();
        gate.Setup(g => g.CheckFeatureAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string? _, CancellationToken _) => key != Module || moduleOn
                ? FeatureGateResult.Allowed()
                : FeatureGateResult.Denied(FeatureDenialReason.ProviderDenied));
        return gate.Object;
    }

    private static async Task<List<ServiceApiScopeDto>> CatalogAsync(bool moduleOn)
    {
        var controller = new ServiceApiController(new TestTenantContext { TenantId = TenantId }, Gate(moduleOn));
        var result = await controller.GetScopes(default);
        return result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<ServiceApiCatalogDto>().Which.Scopes;
    }

    [Fact]
    public async Task The_catalog_marks_a_module_scope_unavailable_while_the_organization_lacks_the_module()
    {
        (await CatalogAsync(moduleOn: false)).Single(s => s.Name == ModuleScope.Name).Available.Should().BeFalse();
        (await CatalogAsync(moduleOn: true)).Single(s => s.Name == ModuleScope.Name).Available.Should().BeTrue();
    }

    [Fact]
    public async Task Platform_scopes_are_always_available()
    {
        (await CatalogAsync(moduleOn: false)).Where(s => s.Name.StartsWith("oluso:users:") || s.Name.StartsWith("oluso:roles:"))
            .Should().NotBeEmpty().And.OnlyContain(s => s.Available);
    }

    private static (ClientsController Controller, Mock<IClientStore> Clients) ClientsApi(bool moduleOn)
    {
        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.AddClientAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client c, CancellationToken _) => c);
        clients.Setup(c => c.UpdateClientAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client c, CancellationToken _) => c);

        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetProtocolSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProtocolSettings());

        var controller = new ClientsController(
            new TestTenantContext { TenantId = TenantId }, clients.Object, settings.Object, Mock.Of<IIssuerResolver>(),
            Mock.Of<IOlusoEventService>(), Mock.Of<IServiceApiScopeProvisioner>(), Mock.Of<IAuthorizationService>(),
            Mock.Of<IClientSecretProtector>(), Gate(moduleOn), NullLogger<ClientsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", "admin-1"), new Claim("tenant_id", TenantId),
                    }, "test")),
                },
            },
        };
        return (controller, clients);
    }

    private static CreateClientRequest NewServiceClient(params string[] scopes) => new()
    {
        ClientId = $"svc-{Guid.NewGuid():N}",
        ClientName = "Integration",
        AllowedGrantTypes = new List<string> { "client_credentials" },
        AllowedScopes = new List<string> { ServiceScopes.ServiceApi }.Concat(scopes).ToList(),
    };

    private static Client ExistingServiceClient(params string[] scopes) => new()
    {
        Id = 7,
        ClientId = "svc-existing",
        ClientName = "Existing",
        TenantId = TenantId,
        AllowedGrantTypes = new List<ClientGrantType> { new() { GrantType = "client_credentials" } },
        AllowedScopes = new[] { ServiceScopes.ServiceApi }.Concat(scopes).Select(s => new ClientScope { Scope = s }).ToList(),
    };

    [Fact]
    public async Task A_module_scope_cannot_be_given_without_the_module()
    {
        var (controller, clients) = ClientsApi(moduleOn: false);

        var result = await controller.CreateClient(NewServiceClient(ServiceScopes.UsersRead, ModuleScope.Name), default);

        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value!.ToString().Should().Contain(ModuleScope.Name);
        clients.Verify(c => c.AddClientAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_module_scope_can_be_given_with_the_module()
    {
        var (controller, _) = ClientsApi(moduleOn: true);

        var result = await controller.CreateClient(NewServiceClient(ModuleScope.Name), default);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task Adding_a_module_scope_to_an_existing_client_needs_the_module()
    {
        var (controller, clients) = ClientsApi(moduleOn: false);
        clients.Setup(c => c.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingServiceClient(ServiceScopes.UsersRead));

        var result = await controller.UpdateClient(7, new UpdateClientRequest
        {
            AllowedScopes = new List<string> { ServiceScopes.ServiceApi, ServiceScopes.UsersRead, ModuleScope.Name },
        }, default);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        clients.Verify(c => c.UpdateClientAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_client_that_already_has_a_module_scope_still_saves_after_the_module_is_switched_off()
    {
        var (controller, clients) = ClientsApi(moduleOn: false);
        clients.Setup(c => c.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingServiceClient(ModuleScope.Name));

        (await controller.UpdateClient(7, new UpdateClientRequest { ClientName = "Renamed" }, default))
            .Result.Should().BeOfType<OkObjectResult>();
        (await controller.UpdateClient(7, new UpdateClientRequest { AllowedScopes = new List<string> { ServiceScopes.ServiceApi } }, default))
            .Result.Should().BeOfType<OkObjectResult>("taking the scope off must work");
    }

    [Fact]
    public async Task A_provisioned_module_scope_requires_the_module_at_issuance_and_an_existing_one_is_stamped()
    {
        var resources = new Mock<IResourceStore>();
        var existing = new ApiScope { Name = ServiceScopes.ServiceApi };
        resources.Setup(r => r.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> names, CancellationToken _) =>
                names.Contains(ServiceScopes.ServiceApi) ? new[] { existing } : Array.Empty<ApiScope>());
        var added = new List<ApiScope>();
        resources.Setup(r => r.AddApiScopeAsync(It.IsAny<ApiScope>(), It.IsAny<CancellationToken>()))
            .Callback((ApiScope scope, CancellationToken _) => added.Add(scope))
            .ReturnsAsync((ApiScope scope, CancellationToken _) => scope);
        var provisioner = new ServiceApiScopeProvisioner(
            resources.Object, new TestTenantContext { TenantId = TenantId }, NullLogger<ServiceApiScopeProvisioner>.Instance);

        await provisioner.EnsureAsync(new[] { ServiceScopes.ServiceApi, ServiceScopes.UsersRead, ModuleScope.Name });

        ApiScopeFeatures.RequiredFeatures(added.Single(s => s.Name == ModuleScope.Name)).Should().Equal(Module);
        ApiScopeFeatures.RequiredFeatures(added.Single(s => s.Name == ServiceScopes.UsersRead)).Should().BeEmpty();
        new ServiceScopeFeatureSource().GetFeatureGatedScopes()
            .Should().ContainSingle(d => d.Name == ModuleScope.Name).Which.RequiredFeature.Should().Be(Module);
        new ServiceScopeFeatureSource().GetFeatureGatedScopes().Should().NotContain(d => d.Name == ServiceScopes.UsersRead);
    }
}
