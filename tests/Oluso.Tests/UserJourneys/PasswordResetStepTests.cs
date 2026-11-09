using System.Text.RegularExpressions;
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
/// The password-reset step emails a code and a link that submits it, renders the
/// password-reset template with every placeholder filled, never reveals whether the
/// address has an account, counts wrong codes as failed attempts, and only re-sends
/// after a short cooldown.
/// </summary>
public class PasswordResetStepTests
{
    private static readonly OlusoUserInfo Jo = new() { Id = "u1", Username = "jo", Email = "jo@x.test", DisplayName = "Jo Bloggs" };
    private const string Lookalike = "jo\u0131@x.test";

    private readonly Dictionary<string, object> _journeyData = new();
    private readonly List<(string To, string Subject, string Body)> _sent = new();
    private IDictionary<string, object>? _templateScope;

    private StepExecutionContext Context(Dictionary<string, object> input, bool withTemplates = true)
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByEmailAsync("jo@x.test", It.IsAny<CancellationToken>())).ReturnsAsync(Jo);
        // A different string that the normalised lookup matches to the same account (e.g. a dotless-i lookalike).
        users.Setup(u => u.FindByEmailAsync(Lookalike, It.IsAny<CancellationToken>())).ReturnsAsync(Jo);
        users.Setup(u => u.GeneratePasswordResetTokenAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync("identity-token");

        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string subject, string body, CancellationToken _) => _sent.Add((to, subject, body)))
            .ReturnsAsync(EmailResult.Succeeded());

        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("acme.id.test");

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(users.Object)
            .AddSingleton(email.Object)
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });

        if (withTemplates)
        {
            var templates = new Mock<IPlatformEmailTemplateService>();
            templates.Setup(t => t.RenderAsync(PlatformTemplateNames.PasswordReset, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback((string _, IDictionary<string, object>? scope, CancellationToken _) => _templateScope = scope)
                .ReturnsAsync(new RenderedEmail { Subject = "Reset your password", HtmlBody = "<html>rendered</html>" });
            services.AddSingleton(templates.Object);
        }

        return new StepExecutionContext
        {
            JourneyId = "journey-1",
            StepId = "reset",
            Configuration = new JourneyStepConfiguration { Id = "reset", Type = "password_reset" },
            ServiceProvider = services.BuildServiceProvider(),
            JourneyData = _journeyData,
            Input = new JourneyStepInput { StepId = "reset", Values = input }
        };
    }

    private Task<StepHandlerResult> Run(Dictionary<string, object> input, bool withTemplates = true) =>
        new PasswordResetStepHandler().ExecuteAsync(Context(input, withTemplates));

    private static Dictionary<string, object> Email(string email) => new() { ["email"] = email };
    private static Dictionary<string, object> Code(string code) => new() { ["code"] = code };

    [Fact]
    public async Task The_code_goes_to_the_address_on_the_account_not_the_one_typed()
    {
        var result = await Run(Email(Lookalike));

        result.StepResult!.ViewName.Should().Be("Journey/_PasswordResetVerify");
        _sent.Should().ContainSingle().Which.To.Should().Be("jo@x.test");
        _templateScope![PlatformTemplateScopeKeys.UserEmail].Should().Be("jo@x.test");
    }

    [Fact]
    public async Task A_reset_request_renders_the_template_with_a_secure_code_and_a_working_link()
    {
        var result = await Run(Email("jo@x.test"));

        result.StepResult!.ViewName.Should().Be("Journey/_PasswordResetVerify");
        _sent.Should().ContainSingle().Which.Should().Be(("jo@x.test", "Reset your password", "<html>rendered</html>"));

        var code = (string)_templateScope![PlatformTemplateScopeKeys.OtpCode];
        code.Should().MatchRegex("^[0-9]{6}$");
        _templateScope[PlatformTemplateScopeKeys.ResetLink].Should().Be($"https://acme.id.test/journey/journey-1?code={code}");
        _templateScope[PlatformTemplateScopeKeys.ExpirationMinutes].Should().Be(60);
        _templateScope[PlatformTemplateScopeKeys.UserEmail].Should().Be("jo@x.test");
        _templateScope[PlatformTemplateScopeKeys.UserName].Should().Be("Jo Bloggs");
    }

    [Fact]
    public async Task Without_the_template_service_the_built_in_email_still_carries_the_code_and_link()
    {
        await Run(Email("jo@x.test"), withTemplates: false);

        var body = _sent.Should().ContainSingle().Subject.Body;
        var code = Regex.Match(body, @"code=([0-9]{6})").Groups[1].Value;
        code.Should().NotBeEmpty();
        body.Should().Contain($"https://acme.id.test/journey/journey-1?code={code}");
        body.Should().Contain($">{code}</td>");
    }

    [Fact]
    public async Task Opening_the_link_or_typing_the_code_moves_on_to_the_new_password()
    {
        await Run(Email("jo@x.test"));
        var code = (string)_templateScope![PlatformTemplateScopeKeys.OtpCode];

        var result = await Run(Code(code));

        result.FailedAttempt.Should().BeFalse();
        result.StepResult!.ViewName.Should().Be("Journey/_PasswordResetNewPassword");
    }

    [Fact]
    public async Task A_wrong_code_is_a_failed_attempt()
    {
        await Run(Email("jo@x.test"));

        var result = await Run(Code("999999x"));

        result.FailedAttempt.Should().BeTrue();
        result.StepResult!.ViewName.Should().Be("Journey/_PasswordResetVerify");
    }

    [Fact]
    public async Task An_unknown_address_looks_exactly_like_a_real_one_but_gets_no_email()
    {
        var requested = await Run(Email("nobody@x.test"));
        var guessed = await Run(Code("123456"));

        _sent.Should().BeEmpty();
        requested.StepResult!.ViewName.Should().Be("Journey/_PasswordResetVerify");
        guessed.FailedAttempt.Should().BeTrue();
        guessed.StepResult!.ViewName.Should().Be("Journey/_PasswordResetVerify");
    }

    [Fact]
    public async Task Resend_waits_for_the_cooldown_then_issues_a_fresh_code()
    {
        await Run(Email("jo@x.test"));
        await Run(new Dictionary<string, object> { ["resend"] = "1" });
        _sent.Should().HaveCount(1, "a resend straight after the first email is held back");

        _journeyData["reset_sent_at"] = DateTime.UtcNow.AddMinutes(-2).ToString("O");
        await Run(new Dictionary<string, object> { ["resend"] = "1" });

        _sent.Should().HaveCount(2);
        var newCode = (string)_templateScope![PlatformTemplateScopeKeys.OtpCode];
        (await Run(Code(newCode))).StepResult!.ViewName.Should().Be("Journey/_PasswordResetNewPassword");
    }
}
