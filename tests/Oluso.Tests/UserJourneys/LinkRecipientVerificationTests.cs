using System.Text.Json;
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
/// A journey opened from a link sent to someone can check the person opening it is that someone: the code goes to the
/// address the link went out on, whatever is typed, and verifying it signs nobody in.
/// </summary>
public class LinkRecipientVerificationTests
{
    private readonly Dictionary<string, object> _journeyData = new();
    private readonly List<string> _emailedTo = new();
    private readonly List<string> _textedTo = new();
    private readonly Mock<IOlusoUserService> _users = new();

    private void SentTo(string? email, string? phone)
    {
        var context = new Dictionary<string, string> { ["emr.patientId"] = "p1" };
        if (email != null) context[JourneyLaunchData.RecipientEmailKey] = email;
        if (phone != null) context[JourneyLaunchData.RecipientPhoneKey] = phone;
        _journeyData[JourneyLaunchData.ContextDataKey] = JsonSerializer.Serialize(context);
    }

    private StepExecutionContext Context(string type, Dictionary<string, object> input)
    {
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string _, string _, CancellationToken _) => _emailedTo.Add(to))
            .ReturnsAsync(EmailResult.Succeeded());
        var sms = new Mock<ISmsSender>();
        sms.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string _, CancellationToken _) => _textedTo.Add(to))
            .ReturnsAsync(SmsResult.Succeeded());

        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("acme.id.test");

        return new StepExecutionContext
        {
            JourneyId = "journey-1",
            StepId = "verify",
            Configuration = new JourneyStepConfiguration
            {
                Id = "verify",
                Type = type,
                Settings = new Dictionary<string, object> { ["toLinkRecipient"] = true },
            },
            ServiceProvider = new ServiceCollection()
                .AddLogging()
                .AddSingleton(_users.Object)
                .AddSingleton(email.Object)
                .AddSingleton(sms.Object)
                .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http })
                .BuildServiceProvider(),
            JourneyData = _journeyData,
            Input = new JourneyStepInput { StepId = "verify", Values = input },
        };
    }

    private Task<StepHandlerResult> Email(Dictionary<string, object> input) =>
        new EmailVerificationStepHandler().ExecuteAsync(Context("email_verification", input));

    private async Task<(StepHandlerResult Result, StepExecutionContext Context)> Sms(Dictionary<string, object> input)
    {
        var context = Context("passwordless_sms", input);
        return (await new PasswordlessSmsStepHandler().ExecuteAsync(context), context);
    }

    [Fact]
    public async Task The_code_goes_to_the_address_the_link_was_sent_to_on_arrival()
    {
        SentTo("Pat@Clinic.test", null);

        var shown = await Email(new());

        _emailedTo.Should().Equal("pat@clinic.test");
        shown.StepResult!.ViewName.Should().Be("Journey/_EmailVerificationConfirm");
        shown.StepResult.ViewModel.Should().BeOfType<EmailVerificationConfirmViewModel>().Which.CanChangeEmail.Should().BeFalse();
    }

    [Fact]
    public async Task Another_address_typed_or_a_change_asked_for_is_ignored()
    {
        SentTo("pat@clinic.test", null);
        await Email(new());

        await Email(new() { ["email"] = "someone@else.test" });
        await Email(new() { ["change_email"] = "true" });
        await Email(new() { ["resend"] = "true" });

        _emailedTo.Should().Equal("pat@clinic.test", "pat@clinic.test");
    }

    [Fact]
    public async Task The_right_code_verifies_the_recipients_address()
    {
        SentTo("pat@clinic.test", null);
        await Email(new());
        var code = (string)_journeyData["email_verify_code"];

        var done = await Email(new() { ["code"] = code });

        done.Outcome.Should().Be(StepOutcome.Continue);
        done.OutputData!["email"].Should().Be("pat@clinic.test");
        done.OutputData["email_verified"].Should().Be(true);
    }

    [Fact]
    public async Task A_run_not_opened_from_a_sent_link_skips_the_check()
    {
        (await Email(new())).Outcome.Should().Be(StepOutcome.Skip);
        (await Sms(new())).Result.Outcome.Should().Be(StepOutcome.Skip);
        _emailedTo.Should().BeEmpty();
        _textedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_sent_without_that_kind_of_address_fails_rather_than_skipping()
    {
        SentTo(null, "+2348031234567");
        (await Email(new())).Error.Should().Be("recipient_unverifiable");

        SentTo("pat@clinic.test", null);
        (await Sms(new())).Result.Error.Should().Be("recipient_unverifiable");
    }

    [Fact]
    public async Task A_texted_code_verifies_the_recipients_phone_without_signing_anyone_in()
    {
        SentTo(null, "+234 803 123 4567");

        var (shown, _) = await Sms(new() { ["phone"] = "+447700900123" });
        _textedTo.Should().Equal("+2348031234567");
        shown.StepResult!.ViewName.Should().Be("Journey/_PasswordlessSmsVerify");

        var (again, _) = await Sms(new());
        again.StepResult!.ViewName.Should().Be("Journey/_PasswordlessSmsVerify");
        _textedTo.Should().HaveCount(1);

        var (done, context) = await Sms(new() { ["code"] = (string)_journeyData["passwordless_code"] });
        done.Outcome.Should().Be(StepOutcome.Continue);
        done.OutputData!["phone_number"].Should().Be("+2348031234567");
        context.UserId.Should().BeNullOrEmpty();
        _users.Verify(u => u.FindByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_verify_only_text_step_is_never_completed_by_a_signed_in_session()
    {
        var handler = new PasswordlessSmsStepHandler();

        handler.SignsIn(Context("passwordless_sms", new())).Should().BeFalse();
    }
}
