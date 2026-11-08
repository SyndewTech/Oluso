using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Oluso.Enterprise.Verification.Controllers;
using Oluso.Enterprise.Verification.Dto;
using Oluso.Enterprise.Verification.Providers;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>Admin API: permission gates, redacted secrets, routing validation, webhook URLs and reviews.</summary>
public class AdminApiTests : IDisposable
{
    private const string Tenant = VerificationHarness.Tenant;

    private readonly VerificationHarness _h = new(new ScriptedBusinessProvider("scripted", "NG"), new ScriptedHostedProvider());
    private readonly IServiceScope _scope;

    public AdminApiTests()
    {
        _h.Settings.ByTenant.Clear();
        _scope = _h.Scope();
        _h.Provider.Answer = _ => BusinessLookupResult.Failure(BusinessLookupOutcome.ProviderUnavailable, "503");
    }

    public void Dispose()
    {
        _scope.Dispose();
        _h.Dispose();
    }

    private BusinessVerificationAdminController Controller() => Controller(_h, _scope);

    internal static BusinessVerificationAdminController Controller(VerificationHarness h, IServiceScope scope, string adminId = "admin-1")
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(Tenant);
        tenant.SetupGet(t => t.HasTenant).Returns(true);
        var tenants = new Mock<ITenantStore>();
        var acme = new Tenant { Id = Tenant, Name = "Acme", Identifier = "acme" };
        tenants.Setup(t => t.GetByIdAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(acme);
        var issuers = new Mock<IIssuerResolver>();
        issuers.Setup(i => i.GetIssuerForTenantAsync(acme, It.IsAny<CancellationToken>())).ReturnsAsync("https://acme.oluso.test/");

        return new BusinessVerificationAdminController(
            tenant.Object,
            scope.ServiceProvider.GetRequiredService<IVerificationProviderRegistry>(),
            h.Settings,
            h.Store(scope),
            h.Service(scope),
            scope.ServiceProvider.GetRequiredService<IFileAttachmentService>(),
            h.Attachments,
            h.Uploader.Mock.Object,
            h.Users.Object,
            tenants.Object,
            issuers.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", adminId) }, "test")),
                },
            },
        };
    }

    private static T Ok<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

    private static UpdateIdentityVerificationSettingsRequest Update(string? secret, bool enabled = true, string environment = "Sandbox", Dictionary<string, string>? routing = null) => new()
    {
        Providers =
        {
            new UpdateVerificationProviderRequest
            {
                ProviderId = "scripted", Enabled = enabled, Environment = environment,
                Credentials = new Dictionary<string, string?> { ["key"] = secret },
            },
        },
        BusinessRouting = routing ?? new Dictionary<string, string> { ["NG"] = "scripted" },
        BusinessReuseMaxAgeDays = 30,
    };

    [Fact]
    public void No_public_action_is_left_ungated()
    {
        typeof(BusinessVerificationAdminController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Should().OnlyContain(m => m.GetCustomAttribute<RequirePermissionAttribute>() != null || m.GetCustomAttribute<RequireAnyPermissionAttribute>() != null);
        typeof(BusinessVerificationAdminController).GetMethod(nameof(BusinessVerificationAdminController.GetCountries))!
            .GetCustomAttribute<RequireAnyPermissionAttribute>()!.Permissions
            .Should().BeEquivalentTo(VerificationPermissions.Manage, VerificationPermissions.Read, "journeys.read");
    }

    [Fact]
    public async Task Secrets_are_stored_but_never_returned()
    {
        var saved = Ok(await Controller().UpdateSettings(Update("sk_live_123"), default));

        _h.Settings.ByTenant[Tenant].Providers["scripted"].Credentials["key"].Should().Be("sk_live_123");
        saved.Providers.Single().StoredSecrets.Should().Equal("key");
        saved.Providers.Single().Credentials.Should().BeEmpty();
        Ok(await Controller().GetSettings(default)).Providers.Single().Credentials.Should().NotContainValue("sk_live_123");
    }

    [Fact]
    public async Task A_blank_secret_keeps_the_stored_one_and_switching_environment_drops_it()
    {
        await Controller().UpdateSettings(Update("sk_1"), default);
        await Controller().UpdateSettings(Update(""), default);
        _h.Settings.ByTenant[Tenant].Providers["scripted"].Credentials["key"].Should().Be("sk_1");

        (await Controller().UpdateSettings(Update("", environment: "Live"), default)).Result.Should().BeOfType<BadRequestObjectResult>();
        _h.Settings.ByTenant[Tenant].Providers["scripted"].Environment.Should().Be(VerificationEnvironment.Sandbox);
    }

    [Fact]
    public async Task Bad_settings_are_refused()
    {
        (await Controller().UpdateSettings(Update(null), default)).Result.Should().BeOfType<BadRequestObjectResult>("enabled without its key");
        (await Controller().UpdateSettings(Update(null, enabled: false), default)).Result.Should().BeOfType<BadRequestObjectResult>("routed to a disabled provider");
        (await Controller().UpdateSettings(Update("sk", routing: new() { ["GH"] = "scripted" }), default)).Result.Should().BeOfType<BadRequestObjectResult>("Ghana isn't covered");
        _h.Settings.ByTenant.Should().BeEmpty();
    }

    [Fact]
    public async Task Hosted_only_credentials_aren_t_needed_to_enable_a_provider()
    {
        using var dojah = new VerificationHarness(new DojahVerificationProvider(new HttpClient(new FakeHttp()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DojahVerificationProvider>.Instance));
        using var scope = dojah.Scope();
        var controller = Controller(dojah, scope);

        var result = await controller.UpdateSettings(new UpdateIdentityVerificationSettingsRequest
        {
            Providers =
            {
                new UpdateVerificationProviderRequest
                {
                    ProviderId = "dojah", Enabled = true, Environment = "Sandbox",
                    Credentials = new Dictionary<string, string?> { ["appId"] = "app", ["secretKey"] = "sk" },
                    Options = new Dictionary<string, string> { ["lookup"] = "advance" },
                },
            },
            BusinessRouting = new Dictionary<string, string> { ["NG"] = "dojah" },
        }, default);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Hosted_providers_show_the_webhook_url_on_the_organisation_s_host()
    {
        var providers = Ok(await Controller().GetProviders(default)).ToList();

        providers.Single(p => p.Id == "hosted").WebhookUrl.Should().Be("https://acme.oluso.test/api/verification/webhooks/hosted");
        providers.Single(p => p.Id == "hosted").HostedCountries.Should().Equal("NG");
        providers.Single(p => p.Id == "scripted").WebhookUrl.Should().BeNull();
    }

    [Fact]
    public async Task Countries_list_the_routing_and_testing_uses_the_free_check()
    {
        await Controller().UpdateSettings(Update("sk"), default);

        var country = Ok(await Controller().GetCountries(default)).Single(c => c.Code == "NG");
        country.Name.Should().Be("Nigeria");
        country.RoutedProviderId.Should().Be("scripted");

        Ok(await Controller().TestProvider("scripted", default)).Success.Should().BeTrue();
        _h.Provider.Lookups.Should().BeEmpty();
    }

    [Fact]
    public async Task Businesses_are_listed_reviewed_and_queued()
    {
        _h.Settings.ByTenant[Tenant] = VerificationHarness.Routed();
        var attempt = await _h.Service(_scope).SubmitAsync(VerificationHarness.Request());

        var list = Ok(await Controller().ListBusinesses("pending", null));
        list.Items.Should().ContainSingle().Which.ApplicantCount.Should().Be(1);
        Ok(await Controller().ListBusinesses("verified", null)).Items.Should().BeEmpty();
        Ok(await Controller().ListBusinesses(null, "1234567")).Items.Should().ContainSingle();
        (await Controller().ListBusinesses("bogus", null)).Result.Should().BeOfType<BadRequestObjectResult>();

        Ok(await Controller().ReviewQueue()).Items.Should().ContainSingle().Which.Applicant.Id.Should().Be(attempt.Applicant!.Id);

        var reviewed = Ok(await Controller().ReviewBusiness(attempt.Business!.Id, new ReviewBusinessVerificationRequest { Decision = "reject", Note = "fake certificate" }, default));
        reviewed.Status.Should().Be("rejected");
        reviewed.StatusReason.Should().Be(BusinessVerificationReasons.RejectedByReviewer);
        reviewed.ReviewedBy.Should().Be("admin-1");
        reviewed.Applicants.Single().Status.Should().Be("rejected");
        reviewed.Checks.Should().Contain(c => c.Kind == BusinessCheckKinds.Review && c.OriginKind == "admin" && c.OriginId == "admin-1");
        Ok(await Controller().ReviewQueue()).Items.Should().BeEmpty();

        (await Controller().ReviewBusiness(attempt.Business.Id, new ReviewBusinessVerificationRequest { Decision = "approve" }, default)).Result
            .Should().BeOfType<ConflictObjectResult>();
        (await Controller().ReviewBusiness("missing", new ReviewBusinessVerificationRequest { Decision = "approve" }, default)).Result
            .Should().BeOfType<NotFoundResult>();
        (await Controller().ReviewApplicant(attempt.Applicant.Id, new ReviewBusinessVerificationRequest { Decision = "maybe" }, default)).Result
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Refreshing_asks_the_provider_about_the_open_hosted_session()
    {
        var settings = VerificationHarness.Routed("hosted");
        _h.Settings.ByTenant[Tenant] = settings;
        var hosted = (ScriptedHostedProvider)_h.Providers[1];
        var attempt = await _h.Service(_scope).SubmitAsync(VerificationHarness.Request(mode: BusinessVerificationMode.Hosted));
        attempt.Outcome.Should().Be(BusinessVerificationAttemptOutcome.HostedSessionStarted);

        hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Abandoned };
        var refreshed = Ok(await Controller().RefreshApplicant(attempt.Applicant!.Id, default));

        refreshed.Status.Should().Be("failed");
        refreshed.StatusReason.Should().Be(BusinessVerificationReasons.Abandoned);
        (await Controller().RefreshApplicant(attempt.Applicant.Id, default)).Result.Should().BeOfType<ConflictObjectResult>("the session is closed");
    }
}
