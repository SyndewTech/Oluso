using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UI.Headless;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys.Headless;

/// <summary>
/// The emailed and texted code views (email verification, passwordless email and SMS) described as forms: the code
/// is one value under the field name the hosted view posts, and resend and change-address post the view's own values.
/// </summary>
public class CodesDescriberTests
{
    private static readonly IStringLocalizerFactory Localizers = new KeyLocalizerFactory();

    private static JourneyStepForm Describe(IJourneyStepFormDescriber describer, string view, object? model) =>
        describer.Describe(new JourneyStepDescription("step", "step", view, model, null));

    [Theory]
    [InlineData("otp", "email_verification_code_description", "email_verification_send_code_button")]
    [InlineData("magic-link", "email_verification_magic_link_description", "email_verification_send_link_button")]
    public void Email_verification_asks_for_the_address(string mode, string description, string button)
    {
        var form = Describe(new EmailVerificationDescriber(Localizers), "Journey/_EmailVerification",
            new EmailVerificationViewModel { Mode = mode, Email = "jo@a.test", ErrorMessage = "Please enter a valid email address" });

        form.Title.Should().Be("email_verification_title");
        form.Description.Should().Be(description);
        form.Error.Should().Be("Please enter a valid email address");
        var email = form.Fields.Should().ContainSingle().Subject;
        email.Should().BeEquivalentTo(new { Name = "email", Type = "email", Value = "jo@a.test", Required = true });
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = (string?)null, Value = (string?)null, Label = button, PostsFields = true });
    }

    [Fact]
    public void Email_verification_confirm_takes_a_code_of_the_configured_length()
    {
        var form = Describe(new EmailVerificationConfirmDescriber(Localizers), "Journey/_EmailVerificationConfirm",
            new EmailVerificationConfirmViewModel
            {
                Email = "j***o@a.test", Mode = "otp", CodeLength = 8, ExpirationMinutes = 15,
                ErrorMessage = "Invalid code. 2 attempt(s) remaining.", SuccessMessage = "A new code has been sent to your email."
            });

        form.Description.Should().Be("email_verification_confirm_code_msg:j***o@a.test");
        form.Error.Should().Be("Invalid code. 2 attempt(s) remaining.");
        form.Notice.Should().Be("A new code has been sent to your email.");
        var code = form.Fields.Should().ContainSingle().Subject;
        code.Should().BeEquivalentTo(new
        {
            Name = "code", Type = "otp", MaxLength = 8, Autocomplete = "one-time-code", Required = true,
            Hint = "email_verification_confirm_expires:15"
        });
        form.Actions.Select(a => (a.Name, a.Value, a.Kind, a.SkipValidation, a.PostsFields)).Should().Equal(
            ((string?)null, (string?)null, "primary", false, true),
            ("resend", "true", "link", true, false),
            ("change_email", "true", "link", true, false));
        form.Widget.Should().BeNull();
    }

    [Fact]
    public void Email_verification_confirm_waits_for_the_magic_link_to_be_clicked()
    {
        var form = Describe(new EmailVerificationConfirmDescriber(Localizers), "Journey/_EmailVerificationConfirm",
            new EmailVerificationConfirmViewModel { Email = "j***o@a.test", Mode = "magic-link", JourneyId = "j1", CurrentStepId = "verify" });

        form.Description.Should().Be("email_verification_confirm_magic_link_msg:j***o@a.test");
        form.Fields.Should().BeEmpty();
        form.Actions.Select(a => (a.Name, a.Label)).Should().Equal(
            ("resend", "email_verification_confirm_resend_link"),
            ("change_email", "email_verification_confirm_change_email"));
        form.Widget!.Type.Should().Be("poll");
        form.Widget.Data!.Value.GetProperty("intervalSeconds").GetInt32().Should().Be(3);
        form.Widget.Data!.Value.GetProperty("maxSeconds").GetInt32().Should().Be(180);
        form.Actions.Should().OnlyContain(a => !a.PostsFields);
    }

    [Fact]
    public void Passwordless_email_asks_for_the_address_and_says_new_addresses_get_an_account_when_sign_up_is_on()
    {
        var describer = new PasswordlessEmailDescriber(Localizers);

        var open = Describe(describer, "Journey/_PasswordlessEmail",
            new PasswordlessEmailViewModel { Mode = "magic-link", AllowSignUp = true, ErrorMessage = "Please enter a valid email address" });
        open.Description.Should().Be("passwordless_email_magic_link_description");
        open.Error.Should().Be("Please enter a valid email address");
        open.Fields.Select(f => (f.Name, f.Type)).Should().Equal(("email", "email"), ("signup_notice", "content"));
        open.Actions.Should().ContainSingle().Which.Label.Should().Be("passwordless_email_send_magic_button");

        var closed = Describe(describer, "Journey/_PasswordlessEmail", new PasswordlessEmailViewModel());
        closed.Fields.Should().ContainSingle().Which.Name.Should().Be("email");
        closed.Actions.Should().ContainSingle().Which.Label.Should().Be("passwordless_email_send_code_button");
    }

    [Fact]
    public void Passwordless_email_verify_takes_the_code_or_resends_or_changes_the_address()
    {
        var describer = new PasswordlessEmailVerifyDescriber(Localizers);

        var otp = Describe(describer, "Journey/_PasswordlessEmailVerify",
            new PasswordlessEmailVerifyViewModel { Email = "j***o@a.test", CodeLength = 6, ExpirationMinutes = 10, SuccessMessage = "Sent" });
        otp.Description.Should().Be("passwordless_email_verify_code_msg:j***o@a.test");
        otp.Notice.Should().Be("Sent");
        otp.Fields.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Name = "code", Type = "otp", MaxLength = 6, Hint = "passwordless_email_verify_expires:10"
        });
        otp.Actions.Select(a => (a.Name, a.Value, a.PostsFields)).Should().Equal(
            ((string?)null, (string?)null, true), ("resend", "true", false), ("change_email", "true", false));

        var link = Describe(describer, "Journey/_PasswordlessEmailVerify",
            new PasswordlessEmailVerifyViewModel { Email = "j***o@a.test", Mode = "magic-link" });
        link.Fields.Should().BeEmpty();
        link.Actions.Select(a => a.Label).Should().Equal("passwordless_email_verify_resend_magic", "passwordless_email_verify_change_email");
        link.Widget.Should().BeNull();
    }

    [Fact]
    public void Passwordless_sms_asks_for_the_phone_number()
    {
        var form = Describe(new PasswordlessSmsDescriber(Localizers), "Journey/_PasswordlessSms",
            new PasswordlessSmsViewModel { PhoneNumber = "+447700900123", AllowSignUp = true });

        var phone = form.Fields[0];
        phone.Should().BeEquivalentTo(new
        {
            Name = "phone", Type = "tel", Value = "+447700900123", Autocomplete = "tel", Required = true,
            Hint = "passwordless_sms_help", Placeholder = "passwordless_sms_placeholder"
        });
        form.Fields.Should().Contain(f => f.Type == "content" && f.Value == "passwordless_sms_signup_notice");
        form.Actions.Should().ContainSingle().Which.Label.Should().Be("passwordless_sms_button");
    }

    [Fact]
    public void Passwordless_sms_verify_takes_the_texted_code_or_resends_it()
    {
        var form = Describe(new PasswordlessSmsVerifyDescriber(Localizers), "Journey/_PasswordlessSmsVerify",
            new PasswordlessSmsVerifyViewModel { PhoneNumber = "***0123", CodeLength = 4, ExpirationMinutes = 5, ErrorMessage = "Invalid code" });

        form.Description.Should().Be("passwordless_sms_verify_description:***0123");
        form.Error.Should().Be("Invalid code");
        form.Fields.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "code", Type = "otp", MaxLength = 4 });
        form.Actions.Select(a => (a.Name, a.Value, a.Kind, a.PostsFields)).Should().Equal(
            ((string?)null, (string?)null, "primary", true), ("resend", "true", "link", false));
    }

    [Fact]
    public async Task Changing_the_address_while_verifying_returns_to_the_address_and_forgets_the_old_code()
    {
        var data = new Dictionary<string, object>
        {
            ["email_verify_email"] = "old@a.test",
            ["email_verify_code"] = "123456",
            ["email_verify_magic_token"] = "token",
            ["email_verify_sent_at"] = DateTime.UtcNow.ToString("O")
        };
        var context = new StepExecutionContext
        {
            JourneyId = "j1",
            PolicyId = "p1",
            StepId = "verify",
            Configuration = new JourneyStepConfiguration { Id = "verify", Type = "email_verification", Settings = new Dictionary<string, object>() },
            ServiceProvider = new ServiceCollection().AddLogging().BuildServiceProvider(),
            JourneyData = data,
            Input = new JourneyStepInput { StepId = "verify", Values = new Dictionary<string, object> { ["change_email"] = "true" } }
        };

        var result = await new EmailVerificationStepHandler().ExecuteAsync(context);

        result.StepResult!.ViewName.Should().Be("Journey/_EmailVerification");
        data.Should().NotContainKeys("email_verify_email", "email_verify_code", "email_verify_magic_token");
    }

    private sealed class KeyLocalizerFactory : IStringLocalizerFactory
    {
        public IStringLocalizer Create(Type resourceSource) => new KeyLocalizer();
        public IStringLocalizer Create(string baseName, string location) => new KeyLocalizer();
    }

    private sealed class KeyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, $"{name}:{string.Join(",", arguments)}");

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
