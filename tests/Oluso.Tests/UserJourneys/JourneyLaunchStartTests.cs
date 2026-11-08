using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UI.Pages.Journey;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// <c>/journey/start/{slug}?launch=</c> honours only an intact, unexpired ticket minted for
/// this journey and the tenant whose host it opens on; its context goes into the journey's
/// reserved data and its return address replaces the success redirect.
/// </summary>
public class JourneyLaunchStartTests
{
    private readonly JourneyPolicy _policy = new()
    {
        Id = "p1",
        Slug = "intake",
        Name = "Intake",
        TenantId = "t1",
        Type = JourneyType.DataCollection,
        Enabled = true,
        PersistSubmissions = true,
        SuccessRedirectUrl = "/thanks",
        Steps = new List<JourneyPolicyStep> { new() { Id = "form", Type = "dynamic_form", Order = 1 } }
    };

    private readonly InMemoryJourneyStateStore _states = new();
    private readonly JourneyLaunchTokenService _tokens;

    public JourneyLaunchStartTests()
    {
        var tenant = new Tenant { Id = "t1", Identifier = "acme", Name = "Acme" };
        var tenants = new Mock<ITenantStore>();
        tenants.Setup(t => t.GetByIdAsync("t1", It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerForTenantAsync(tenant, It.IsAny<CancellationToken>())).ReturnsAsync("https://acme.id.example");
        var origins = new Mock<ITenantAppOrigins>();
        origins.Setup(o => o.GetOriginsAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https://app.acme.example" });
        _tokens = new JourneyLaunchTokenService(new EphemeralDataProtectionProvider(), Mock.Of<IJourneyPolicyStore>(),
            tenants.Object, issuer.Object, origins.Object);
    }

    private StartModel Page(string? launch, string requestTenant = "t1")
    {
        var policies = new Mock<IJourneyPolicyStore>();
        policies.Setup(p => p.GetBySlugAsync("intake", It.IsAny<CancellationToken>())).ReturnsAsync(_policy);
        var orchestrator = new Mock<IJourneyOrchestrator>();
        orchestrator.Setup(o => o.StartJourneyAsync(_policy, It.IsAny<JourneyStartContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (JourneyPolicy _, JourneyStartContext ctx, CancellationToken _) =>
            {
                var state = new JourneyState
                {
                    Id = "j1",
                    PolicyId = "p1",
                    CurrentStepId = "form",
                    Status = JourneyStatus.InProgress,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                    TenantId = (string)ctx.Properties["TenantId"],
                    ClientId = "",
                    Data = new Dictionary<string, object> { ["success_redirect_url"] = "/thanks" }
                };
                if (ctx.Launch != null) JourneyLaunchData.Write(state.Data, ctx.Launch);
                await _states.SaveAsync(state);
                return state;
            });
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(requestTenant);

        var http = new DefaultHttpContext();
        if (launch != null) http.Request.QueryString = QueryString.Create(JourneyLaunchData.QueryParameter, launch);
        return new StartModel(orchestrator.Object, policies.Object, _states, NullLogger<StartModel>.Instance,
            launchTokens: _tokens, tenantContext: tenant.Object)
        {
            PageContext = new PageContext(new ActionContext(http, new RouteData(), new ActionDescriptor())),
            PolicyId = "intake",
        };
    }

    private string Token(string tenantId = "t1", string policyId = "p1", string? returnUrl = "https://app.acme.example/chart", TimeSpan? lifetime = null) =>
        _tokens.Protect(new JourneyLaunchTicket(tenantId, policyId, new Dictionary<string, string> { ["recordId"] = "r1" }, returnUrl,
            DateTimeOffset.UtcNow + (lifetime ?? TimeSpan.FromMinutes(10))));

    [Fact]
    public async Task A_valid_ticket_stores_its_context_and_return_address_on_the_journey()
    {
        var result = await Page(Token()).OnGetAsync();

        result.Should().BeOfType<RedirectToPageResult>().Which.PageName.Should().Be("Index");
        var state = await _states.GetAsync("j1");
        state!.TenantId.Should().Be("t1");
        JourneyLaunchData.ReadContext(state.Data).Should().Contain("recordId", "r1");
        state.Data!["success_redirect_url"].Should().Be("https://app.acme.example/chart");
    }

    [Fact]
    public async Task Without_a_ticket_the_journey_starts_as_before()
    {
        await Page(null).OnGetAsync();

        var state = await _states.GetAsync("j1");
        state!.Data!.Should().NotContainKey(JourneyLaunchData.ContextDataKey);
        state.Data!["success_redirect_url"].Should().Be("/thanks");
    }

    [Fact]
    public async Task A_tampered_ticket_is_an_error_not_ignored()
    {
        var result = await Page(Token() + "x").OnGetAsync();

        Error(result).Should().Be("This link is not valid");
        (await _states.GetAsync("j1")).Should().BeNull();
    }

    [Fact]
    public async Task An_expired_ticket_is_an_error()
    {
        var result = await Page(Token(lifetime: TimeSpan.FromSeconds(-1))).OnGetAsync();

        Error(result).Should().Be("This link has expired");
    }

    [Theory]
    [InlineData("t2", "p1", "t1")]
    [InlineData("t1", "other", "t1")]
    [InlineData("t1", "p1", "t2")]
    public async Task A_ticket_for_another_journey_or_tenant_is_an_error(string ticketTenant, string ticketPolicy, string requestTenant)
    {
        var result = await Page(Token(ticketTenant, ticketPolicy), requestTenant).OnGetAsync();

        Error(result).Should().Be("This link is not valid");
    }

    [Fact]
    public async Task A_ticket_whose_return_address_is_no_longer_registered_is_an_error()
    {
        var result = await Page(Token(returnUrl: "https://evil.example/")).OnGetAsync();

        Error(result).Should().Be("This link is not valid");
    }

    private static string? Error(IActionResult result)
    {
        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.PageName.Should().Be("/Error");
        return redirect.RouteValues!["message"] as string;
    }
}
