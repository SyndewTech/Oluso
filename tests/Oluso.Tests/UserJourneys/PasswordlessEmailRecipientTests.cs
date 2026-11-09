using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// A passwordless code signs someone in, so it must reach the account's own mailbox: never a
/// differently-written address that the normalised lookup happens to match to the account.
/// </summary>
public class PasswordlessEmailRecipientTests
{
    private static readonly OlusoUserInfo Jo = new() { Id = "u1", Username = "jo", Email = "jo@x.test" };
    private const string Lookalike = "joı@x.test";

    private readonly Dictionary<string, object> _journeyData = new();
    private readonly List<string> _sentTo = new();

    private StepExecutionContext Context(Dictionary<string, object> input, bool allowSignUp = false)
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByEmailAsync("jo@x.test", It.IsAny<CancellationToken>())).ReturnsAsync(Jo);
        users.Setup(u => u.FindByEmailAsync(Lookalike, It.IsAny<CancellationToken>())).ReturnsAsync(Jo);

        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string _, string _, CancellationToken _) => _sentTo.Add(to))
            .ReturnsAsync(EmailResult.Succeeded());

        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("acme.id.test");

        return new StepExecutionContext
        {
            JourneyId = "journey-1",
            StepId = "pl",
            Configuration = new JourneyStepConfiguration
            {
                Id = "pl",
                Type = "passwordless_email",
                Settings = new Dictionary<string, object> { ["allowSignUp"] = allowSignUp },
            },
            ServiceProvider = new ServiceCollection()
                .AddLogging()
                .AddSingleton(users.Object)
                .AddSingleton(email.Object)
                .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http })
                .BuildServiceProvider(),
            JourneyData = _journeyData,
            Input = new JourneyStepInput { StepId = "pl", Values = input },
        };
    }

    private Task<StepHandlerResult> Run(Dictionary<string, object> input, bool allowSignUp = false) =>
        new PasswordlessEmailStepHandler().ExecuteAsync(Context(input, allowSignUp));

    [Fact]
    public async Task An_existing_accounts_code_goes_to_its_own_address_not_the_one_typed()
    {
        await Run(new() { ["email"] = Lookalike });
        _sentTo.Should().Equal("jo@x.test");

        await Run(new() { ["resend"] = "true" });
        _sentTo.Should().Equal("jo@x.test", "jo@x.test");
    }

    [Fact]
    public async Task A_new_sign_up_gets_its_code_at_the_address_it_gave()
    {
        await Run(new() { ["email"] = "new@x.test" }, allowSignUp: true);

        _sentTo.Should().Equal("new@x.test");
    }

    [Fact]
    public async Task An_unknown_address_with_sign_up_off_gets_nothing_but_the_same_screen()
    {
        var first = await Run(new() { ["email"] = "nobody@x.test" });
        var resend = await Run(new() { ["resend"] = "true" });

        _sentTo.Should().BeEmpty();
        first.StepResult!.ViewName.Should().Be("Journey/_PasswordlessEmailVerify");
        resend.StepResult!.ViewName.Should().Be("Journey/_PasswordlessEmailVerify");
    }
}
