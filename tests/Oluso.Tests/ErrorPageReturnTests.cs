using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;
using Oluso.UI.Pages;
using Xunit;

namespace Oluso.Tests;

/// <summary>The error page's way back: a local path, or the requesting app's registered homepage.</summary>
public class ErrorPageReturnTests
{
    private static ErrorModel Page(Client? client = null, JourneyPolicy? journey = null)
    {
        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.FindClientByIdAsync("app", It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var journeys = new Mock<IJourneyPolicyStore>();
        journeys.Setup(j => j.GetBySlugAsync("feedback", It.IsAny<CancellationToken>())).ReturnsAsync(journey);
        var http = new DefaultHttpContext();
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ErrorModel(clients.Object, journeys.Object)
        {
            PageContext = new PageContext(actionContext),
            Url = new UrlHelper(actionContext),
        };
    }

    [Theory]
    [InlineData("/account/login", "/account/login")]
    [InlineData("https://evil.example/phish", null)]
    [InlineData("//evil.example", null)]
    [InlineData("javascript:alert(1)", null)]
    public async Task Only_a_local_return_url_is_followed(string returnUrl, string? expected)
    {
        var page = Page();
        page.ReturnUrl = returnUrl;

        await page.OnGetAsync(default);

        page.BackUrl.Should().Be(expected);
    }

    [Fact]
    public async Task The_app_named_by_client_id_is_the_way_back_by_its_homepage_or_redirect_address()
    {
        var withHome = Page(new Client { ClientId = "app", Enabled = true, ClientUri = "https://app.example/home" });
        withHome.ClientId = "app";
        await withHome.OnGetAsync(default);
        withHome.BackUrl.Should().Be("https://app.example/home");

        var client = new Client { ClientId = "app", Enabled = true };
        client.RedirectUris.Add(new ClientRedirectUri { RedirectUri = "https://spa.example/callback" });
        var withRedirect = Page(client);
        withRedirect.ClientId = "app";
        await withRedirect.OnGetAsync(default);
        withRedirect.BackUrl.Should().Be("https://spa.example/");
    }

    [Fact]
    public async Task An_unknown_or_disabled_app_or_a_non_web_address_gives_the_home_link()
    {
        var unknown = Page(null);
        unknown.ClientId = "app";
        await unknown.OnGetAsync(default);
        unknown.BackUrl.Should().BeNull();

        var native = new Client { ClientId = "app", Enabled = true, ClientUri = "javascript:alert(1)" };
        native.RedirectUris.Add(new ClientRedirectUri { RedirectUri = "com.example.app://callback" });
        var nativePage = Page(native);
        nativePage.ClientId = "app";
        await nativePage.OnGetAsync(default);
        nativePage.BackUrl.Should().BeNull();
    }

    [Fact]
    public async Task An_error_description_is_shown_when_there_is_no_message()
    {
        var page = Page();
        page.Error = "invalid_request";
        page.ErrorDescription = "redirect_uri is not registered";

        await page.OnGetAsync(default);

        page.Message.Should().Be("redirect_uri is not registered");
    }

    [Fact]
    public async Task A_journey_leads_back_to_its_configured_success_address()
    {
        var page = Page(journey: new JourneyPolicy { Id = "j", Slug = "feedback", Name = "Feedback", SuccessRedirectUrl = "https://site.example/thanks" });
        page.Journey = "feedback";

        await page.OnGetAsync(default);

        page.BackUrl.Should().Be("https://site.example/thanks");
    }

    private static ErrorModel BrandedPage(JourneyPolicy? journey, TenantBrandingSettings branding)
    {
        var journeys = new Mock<IJourneyPolicyStore>();
        journeys.Setup(j => j.GetBySlugAsync("add-business", It.IsAny<CancellationToken>())).ReturnsAsync(journey);
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetBrandingSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(branding);
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ErrorModel(Mock.Of<IClientStore>(), journeys.Object, settings.Object)
        {
            PageContext = new PageContext(actionContext),
            Url = new UrlHelper(actionContext),
        };
    }

    [Fact]
    public async Task A_failed_journey_shows_its_own_branding()
    {
        var journey = new JourneyPolicy { Id = "p1", Name = "Add a business", Slug = "add-business",
            Ui = new JourneyUiConfiguration { PrimaryColor = "#2563eb", LogoUrl = "https://cdn.example/logo.svg" } };
        var page = BrandedPage(journey, new TenantBrandingSettings { PrimaryColor = "#16a34a" });
        page.Journey = "add-business";

        await page.OnGetAsync(default);

        (page.Brand.PrimaryColor, page.Brand.LogoUrl).Should().Be(("#2563eb", "https://cdn.example/logo.svg"));
    }

    [Fact]
    public async Task Without_a_journey_the_tenants_branding_is_used()
    {
        var page = BrandedPage(null, new TenantBrandingSettings { PrimaryColor = "#16a34a", BackgroundColor = "#0b1020" });

        await page.OnGetAsync(default);

        (page.Brand.PrimaryColor, page.Brand.BackgroundColor).Should().Be(("#16a34a", "#0b1020"));
    }
}
