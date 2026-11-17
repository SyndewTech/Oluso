using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// On a tenant that requires MFA, a journey's MFA step applies the standard pages' exemptions: a sign-in that already
/// has a second factor (a passkey that verified the user, the external provider's own MFA) is not asked again, and a proxy-mode identity,
/// which has no account here to hold one, is let through unless the step itself requires MFA.
/// </summary>
public class MfaExemptionTests
{
    private static StepExecutionContext Rig(Dictionary<string, object> data, string userId = "u1", bool stepRequires = false)
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@example.test", TwoFactorEnabled = false });
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.Tenant).Returns(new Tenant { Id = "t1", RequireMfa = true });
        tenant.SetupGet(t => t.TenantId).Returns("t1");

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(users.Object)
            .AddSingleton(tenant.Object)
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() })
            .BuildServiceProvider();

        return new StepExecutionContext
        {
            JourneyId = "j1",
            StepId = "mfa",
            Configuration = new JourneyStepConfiguration
            {
                Id = "mfa",
                Type = "mfa",
                Settings = stepRequires ? new Dictionary<string, object> { ["required"] = true } : null
            },
            ServiceProvider = services,
            UserId = userId,
            JourneyData = data,
        };
    }

    [Fact]
    public async Task A_password_sign_in_is_asked_to_set_up_a_second_factor()
    {
        var result = await new MfaStepHandler().ExecuteAsync(Rig(new() { ["auth_method"] = "pwd" }));

        result.Outcome.Should().Be(StepOutcome.RequireInput);
        result.StepResult!.ViewName.Should().Be("Journey/_MfaSetup");
    }

    [Theory]
    [InlineData("fido2", new[] { "hwk", "mfa" })]
    [InlineData("google", new[] { "mfa" })]
    public async Task A_sign_in_that_already_has_a_second_factor_is_not_asked_again(string method, string[] amr)
    {
        var result = await new MfaStepHandler().ExecuteAsync(Rig(new() { ["auth_method"] = method, ["amr"] = amr }));

        result.Outcome.Should().Be(StepOutcome.Skip);
    }

    [Fact]
    public async Task A_passkey_that_only_tested_presence_is_asked_for_a_second_factor()
    {
        var result = await new MfaStepHandler().ExecuteAsync(Rig(new() { ["auth_method"] = "fido2", ["amr"] = new[] { "hwk" } }));

        result.Outcome.Should().Be(StepOutcome.RequireInput);
    }

    [Fact]
    public async Task A_proxy_mode_identity_is_let_through()
    {
        var context = Rig(new() { ["auth_method"] = "partner", ["proxy_mode"] = true }, userId: "proxy:partner:42");

        var result = await new MfaStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Skip);
    }

    [Fact]
    public async Task A_step_that_requires_mfa_itself_cannot_be_met_by_a_proxy_mode_identity()
    {
        var context = Rig(new() { ["proxy_mode"] = true }, userId: "proxy:partner:42", stepRequires: true);

        var result = await new MfaStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Failed);
        result.Error.Should().Be("mfa_unavailable");
    }

    [Theory]
    [InlineData(new[] { "pwd", "mfa" }, true)]
    [InlineData(new[] { "pwd mfa" }, true)]
    [InlineData(new[] { "pwd" }, false)]
    public void The_providers_amr_tells_whether_it_did_mfa(string[] amr, bool reported)
    {
        var claims = amr.Select(v => new Claim("amr", v));

        SecondFactorRequirement.ProviderReportsMfa(claims).Should().Be(reported);
        SecondFactorRequirement.ProviderReportsMfa(
            new[] { new Claim("http://schemas.microsoft.com/claims/authnmethodsreferences", "mfa") }).Should().BeTrue();
    }
}
