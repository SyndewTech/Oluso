using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Protocols;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// Confirming recovery codes finishes MFA only right after this user set a method up; email
/// MFA setup completes; and a consent answer is honoured once, for its own user and client.
/// </summary>
public class MfaAndConsentHardeningTests
{
    private static (StepExecutionContext Context, Mock<IMfaService> Mfa) MfaContext(Dictionary<string, object> input, Dictionary<string, object>? data = null, bool twoFactor = true)
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "ada", Email = "ada@x.test", TwoFactorEnabled = twoFactor });
        var mfa = new Mock<IMfaService>();
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns("t1");
        var services = new ServiceCollection()
            .AddSingleton(users.Object)
            .AddSingleton(mfa.Object)
            .AddSingleton(tenant.Object)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();

        var context = new StepExecutionContext
        {
            JourneyId = "j1",
            StepId = "mfa",
            TenantId = "t1",
            UserId = "u1",
            Configuration = new JourneyStepConfiguration { Id = "mfa", Type = "mfa", Settings = new Dictionary<string, object> { ["methods"] = new List<string> { "totp", "email" } } },
            JourneyData = data ?? new Dictionary<string, object>(),
            Input = new JourneyStepInput { StepId = "mfa", Values = input },
            ServiceProvider = services,
        };
        return (context, mfa);
    }

    [Fact]
    public async Task Confirming_recovery_codes_does_not_pass_MFA_on_its_own()
    {
        var (context, _) = MfaContext(new() { ["recovery_codes_confirmed"] = "1" });

        var result = await new MfaStepHandler().ExecuteAsync(context);

        result.Outcome.Should().NotBe(StepOutcome.Continue);
        context.JourneyData.Should().NotContainKey("amr");
    }

    [Fact]
    public async Task Confirming_recovery_codes_right_after_setup_finishes_MFA_once()
    {
        var (context, _) = MfaContext(new() { ["recovery_codes_confirmed"] = "1" },
            new Dictionary<string, object> { [MfaStepHandler.SetupVerifiedDataKey] = "u1" });

        var result = await new MfaStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Continue);
        context.JourneyData.Should().NotContainKey(MfaStepHandler.SetupVerifiedDataKey);
    }

    [Fact]
    public async Task Email_MFA_setup_verifies_the_code_and_enables_MFA()
    {
        var (context, mfa) = MfaContext(new() { ["setup_email_code"] = "123456" },
            new Dictionary<string, object> { ["mfa_setup_email_pending"] = true }, twoFactor: false);
        mfa.Setup(m => m.VerifyEmailCodeAsync("u1", "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mfa.Setup(m => m.EnableMfaAsync("u1", "email", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaEnableResult { Succeeded = true, RecoveryCodes = ["r1"] });

        var result = await new MfaStepHandler().ExecuteAsync(context);

        result.StepResult!.ViewName.Should().Be("Journey/_MfaRecoveryCodes");
        mfa.Verify(m => m.EnableMfaAsync("u1", "email", It.IsAny<CancellationToken>()), Times.Once);
        context.GetData<string>(MfaStepHandler.SetupVerifiedDataKey).Should().Be("u1");
    }

    private static ConsentDecisions Decisions(IJourneyStateStore? journeys = null) =>
        new(new InMemoryProtocolStateStore(
                new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions())),
                Microsoft.Extensions.Options.Options.Create(new Oluso.Core.Protocols.OidcEndpointConfiguration()),
                NullLogger<InMemoryProtocolStateStore>.Instance),
            NullLogger<ConsentDecisions>.Instance, journeys);

    [Fact]
    public async Task A_recorded_consent_counts_once_and_only_for_its_user_and_client()
    {
        var decisions = Decisions();
        var id = await decisions.RecordAsync("u1", "app", ["openid", "profile"]);
        var parameters = new Dictionary<string, string> { [ConsentDecisions.DecisionParameter] = id };

        (await decisions.TakeAsync(parameters, "u2", "app")).Should().BeNull("another user can't use it");
        (await decisions.TakeAsync(parameters, "u1", "other")).Should().BeNull("another client can't use it");
        (await decisions.TakeAsync(parameters, "u1", "app")).Should().Equal("openid", "profile");
        (await decisions.TakeAsync(parameters, "u1", "app")).Should().BeNull("it's used once");
    }

    [Fact]
    public async Task A_completed_consent_journey_counts_once_for_its_user()
    {
        var journeys = new InMemoryJourneyStateStore();
        await journeys.SaveAsync(new JourneyState
        {
            Id = "j1", TenantId = "t1", ClientId = "app", PolicyId = "consent", CurrentStepId = "consent",
            UserId = "u1", Status = JourneyStatus.Completed,
            Data = new Dictionary<string, object> { [ConsentDecisions.ConsentedScopesDataKey] = "openid email" },
        });
        var decisions = Decisions(journeys);
        var parameters = new Dictionary<string, string> { [ConsentDecisions.JourneyParameter] = "j1" };

        (await decisions.TakeAsync(parameters, "u2", "app")).Should().BeNull();
        (await decisions.TakeAsync(parameters, "u1", "app")).Should().Equal("openid", "email");
        (await decisions.TakeAsync(parameters, "u1", "app")).Should().BeNull();
    }
}
