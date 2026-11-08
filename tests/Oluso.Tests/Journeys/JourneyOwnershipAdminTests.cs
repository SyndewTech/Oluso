using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Journeys;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Tests.Journeys;

/// <summary>Only an administrator of a module can give it a journey, take one from it, or change one it owns.</summary>
public class JourneyOwnershipAdminTests
{
    private const string Tenant = "acme";
    private const string Module = "ownership-test-module";
    private const string SilentModule = "ownership-test-silent";

    static JourneyOwnershipAdminTests()
    {
        foreach (var key in new[] { Module, SilentModule })
        {
            var descriptor = new Mock<IModuleDescriptor>();
            descriptor.Setup(d => d.FeatureKey).Returns(key);
            descriptor.Setup(d => d.DisplayName).Returns(key == Module ? "Clinic" : "Silent");
            ModuleRegistry.Instance.Register(descriptor.Object);
        }
    }

    private sealed class Sink : IJourneyResultSink
    {
        public string Module => JourneyOwnershipAdminTests.Module;
        public Task DeliverAsync(JourneyAnswers answers, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_module_administrator_can_give_a_journey_to_a_module_that_receives_answers()
    {
        var (store, journey) = Store(Platform());

        var result = await Controller(store, administers: [Module]).UpdatePolicy(journey.Id,
            new UpdatePolicyRequest { OwnerModule = Module, Sharing = "Shared" });

        result.Result.Should().BeOfType<OkObjectResult>();
        journey.OwnerModule.Should().Be(Module);
        journey.Sharing.Should().Be(JourneySharing.Shared);
    }

    [Fact]
    public async Task A_module_that_receives_no_answers_cannot_own_a_journey()
    {
        var (store, journey) = Store(Platform());

        var result = await Controller(store, administers: [Module, SilentModule]).UpdatePolicy(journey.Id,
            new UpdatePolicyRequest { OwnerModule = SilentModule });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        journey.OwnerModule.Should().BeNull();
    }

    [Fact]
    public async Task Without_the_modules_admin_permission_its_journey_can_be_neither_taken_nor_changed()
    {
        var (store, journey) = Store(Platform(owner: Module));
        var itAdmin = Controller(store, administers: []);

        (await itAdmin.UpdatePolicy(journey.Id, new UpdatePolicyRequest { OwnerModule = "" })).Result.Should().BeOfType<ForbidResult>();
        (await itAdmin.UpdatePolicy(journey.Id, new UpdatePolicyRequest { Name = "Renamed" })).Result.Should().BeOfType<ForbidResult>();
        (await itAdmin.DeletePolicy(journey.Id)).Should().BeOfType<ForbidResult>();
        journey.OwnerModule.Should().Be(Module);
        journey.Name.Should().Be("Patient intake");
    }

    [Fact]
    public async Task Saving_a_platform_journey_as_the_builder_does_today_still_works()
    {
        var (store, journey) = Store(Platform());

        var result = await Controller(store, administers: []).UpdatePolicy(journey.Id,
            new UpdatePolicyRequest { Name = "Waitlist 2", OwnerModule = "", Sharing = "Private" });

        result.Result.Should().BeOfType<OkObjectResult>();
        journey.Name.Should().Be("Waitlist 2");
        journey.OwnerModule.Should().BeNull();
    }

    [Fact]
    public async Task Owner_choices_are_the_receiving_modules_the_admin_administers()
    {
        var (store, _) = Store(Platform());

        var result = await Controller(store, administers: [Module, SilentModule]).GetOwnerModules(CancellationToken.None);

        ((IEnumerable<JourneyOwnerModuleDto>)((OkObjectResult)result.Result!).Value!)
            .Select(m => m.Key).Should().Equal(Module);
    }

    private static JourneyPolicy Platform(string? owner = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Slug = "intake",
        Name = "Patient intake",
        TenantId = Tenant,
        Type = JourneyType.DataCollection,
        Enabled = true,
        OwnerModule = owner,
        Steps = new List<JourneyPolicyStep> { new() { Id = "form", Type = "dynamic_form", Order = 1 } },
    };

    private static (IJourneyPolicyStore Store, JourneyPolicy Journey) Store(JourneyPolicy journey)
    {
        var store = new Mock<IJourneyPolicyStore>();
        store.Setup(s => s.GetAsync(journey.Id, It.IsAny<CancellationToken>())).ReturnsAsync(journey);
        store.Setup(s => s.SaveAsync(It.IsAny<JourneyPolicy>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (store.Object, journey);
    }

    private static UserJourneyPoliciesController Controller(IJourneyPolicyStore store, string[] administers)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Tenant);
        var sanitizer = new Mock<IHtmlSanitizationService>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string?>())).Returns((string? html) => html ?? "");

        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string policy) =>
                administers.Any(m => policy == $"{PermissionPolicyConstants.PolicyPrefix}{m}.admin")
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed());

        var services = new ServiceCollection();
        services.AddSingleton<IJourneyResultSink, Sink>();

        return new UserJourneyPoliciesController(
            store, tenant.Object, sanitizer.Object, Mock.Of<IIssuerResolver>(), Mock.Of<ITenantConfigurationService>(),
            NullLogger<UserJourneyPoliciesController>.Instance, authorizationService: authorization.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "admin-1") }, "test")),
                    RequestServices = services.BuildServiceProvider(),
                },
            },
        };
    }
}
