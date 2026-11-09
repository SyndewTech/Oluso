using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UI.Pages.Account;
using Oluso.UI.Pages.Journey;
using Xunit;

namespace Oluso.Tests.OrganizationSignIn;

/// <summary>
/// The "Sign in with your organisation" entry point appears on the platform host
/// only: on the standalone login page, and on journey login steps (handed to the
/// step partials through ViewData). On a tenant host neither shows it.
/// </summary>
public class OrganizationSignInPagesTests
{
    private static void Attach(PageModel page, HttpContext http)
    {
        var services = new ServiceCollection()
            .AddSingleton(Mock.Of<IExternalProviderConfigStore>(s =>
                s.GetEnabledProvidersAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<ExternalProviderDefinition>>(Array.Empty<ExternalProviderDefinition>())))
            .BuildServiceProvider();
        http.RequestServices = services;
        page.PageContext = new PageContext
        {
            HttpContext = http,
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
        };
    }

    private static LoginModel Login(OrganizationSignInRig rig, HttpContext http, string returnUrl)
    {
        var page = new LoginModel(Mock.Of<IOlusoUserService>(), rig.ProtocolState, NullLogger<LoginModel>.Instance,
            tenantContext: rig.TenantContext, organizationSignIn: rig.Service)
        {
            ReturnUrl = returnUrl,
        };
        Attach(page, http);
        return page;
    }

    [Fact]
    public async Task The_standalone_login_page_offers_the_option_on_the_platform_host_only()
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var callback = OrganizationSignInRig.CallbackFor(correlation);

        var platformPage = Login(rig, rig.PlatformRequest(), callback);
        (await platformPage.OnGetAsync()).Should().BeOfType<PageResult>();
        platformPage.OrgSignInUrl.Should().StartWith($"{OrganizationSignInDefaults.EntryPath}?returnUrl=");

        var tenantPage = Login(rig, rig.TenantRequest(), callback);
        (await tenantPage.OnGetAsync()).Should().BeOfType<PageResult>();
        tenantPage.OrgSignInUrl.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Journey_login_steps_get_the_option_on_the_platform_host_only(bool onPlatform)
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var journey = await rig.StartPlatformJourneyAsync(correlation);

        var orchestrator = new Mock<IJourneyOrchestrator>();
        orchestrator.Setup(o => o.ContinueJourneyAsync(journey.Id, It.IsAny<JourneyStepInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JourneyResult
            {
                JourneyId = journey.Id,
                Status = JourneyStatus.InProgress,
                CurrentStep = new JourneyStepResult { StepId = "login", StepType = "composite_login", ViewName = "Journey/_CompositeLogin" },
            });

        var http = onPlatform ? rig.PlatformRequest() : rig.TenantRequest();
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
        var services = new ServiceCollection().AddSingleton<IOrganizationSignInService>(rig.Service).BuildServiceProvider();

        var page = new IndexModel(orchestrator.Object, rig.Journeys, Mock.Of<IJourneyPolicyStore>(), services,
            NullLogger<IndexModel>.Instance, new ConfigurationBuilder().Build(), rig.TenantContext)
        {
            JourneyId = journey.Id,
        };
        Attach(page, http);

        (await page.OnPostAsync()).Should().BeOfType<PageResult>();

        var url = page.ViewData[IndexModel.OrgSignInUrlKey] as string;
        if (onPlatform)
        {
            url.Should().Be($"{OrganizationSignInDefaults.EntryPath}?journey={journey.Id}");
        }
        else
        {
            url.Should().BeNull();
        }
    }
}
