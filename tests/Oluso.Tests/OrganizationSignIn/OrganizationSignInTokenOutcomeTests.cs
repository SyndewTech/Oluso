using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Protocols;
using Xunit;

namespace Oluso.Tests.OrganizationSignIn;

/// <summary>
/// The mail-server scenario end to end on the platform side: a platform client's
/// authorize request, an organisation sign-in through the tenant, then the
/// authorize callback on the platform host. The authentication result carries the
/// tenant's user with <c>tenant_id</c> = that tenant, and tokens minted on the
/// platform host carry the platform issuer.
/// </summary>
public class OrganizationSignInTokenOutcomeTests
{
    [Fact]
    public async Task The_platform_callback_yields_the_tenant_user_under_the_platform_issuer()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var correlation = await rig.StartPlatformAuthorizeAsync();
        rig.AtPlatform();

        var platform = rig.PlatformRequest();
        var begin = await rig.Service.BeginAsync(platform, "acme", OrganizationSignInRig.CallbackFor(correlation), null);
        rig.AcceptCookies(platform);
        var requestId = OrganizationSignInRig.RequestIdFrom(begin.RedirectUrl!);

        var tenant = rig.TenantRequest();
        var lookup = await rig.Service.GetRequestForTenantHostAsync(tenant, requestId, Core.Events.OrganizationSignInEvent.StageComplete);
        var handoff = await rig.Service.IssueHandoffAsync(tenant, lookup.Request!, new OrganizationAuthentication("u1", "AcmeEntra", Array.Empty<string>()));
        rig.AtPlatform();

        var redeem = await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(handoff));
        redeem.Succeeded.Should().BeTrue(redeem.Error);

        // The session the callback endpoint issues for a standalone platform login.
        var session = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "u1"),
            new Claim("tenant_id", rig.Tenant.Id),
            new Claim("idp", "AcmeEntra"),
            new Claim("amr", "external"),
        }, "Oluso"));

        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(session, IdentityConstants.ApplicationScheme)));
        var callbackRequest = rig.PlatformRequest();
        callbackRequest.RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider();
        var accessor = new HttpContextAccessor { HttpContext = callbackRequest };

        var coordinator = new AuthenticationCoordinator(
            Mock.Of<IJourneyOrchestrator>(), Mock.Of<IJourneyPolicyStore>(), rig.Journeys, rig.ProtocolState,
            accessor, rig.Users.Object, Options.Create(rig.Endpoints), NullLogger<AuthenticationCoordinator>.Instance);

        var correlationOnReturn = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(redeem.Request!.ReturnUrl[redeem.Request.ReturnUrl.IndexOf('?')..])["correlation_id"].ToString();
        correlationOnReturn.Should().Be(correlation);

        var result = await coordinator.HandleCallbackAsync(correlationOnReturn, journeyId: null);

        result.Succeeded.Should().BeTrue();
        result.UserId.Should().Be("u1");
        result.Claims["tenant_id"].Should().Be(rig.Tenant.Id);

        var issuer = new IssuerResolver(
            rig.TenantContext,
            Mock.Of<ITenantSettingsProvider>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Oluso:IssuerUri"] = OrganizationSignInRig.PlatformIssuer }).Build(),
            accessor);
        (await issuer.GetIssuerAsync()).Should().Be(OrganizationSignInRig.PlatformIssuer);
    }
}
