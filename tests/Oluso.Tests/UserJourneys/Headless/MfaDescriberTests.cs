using FluentAssertions;
using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UI.Headless;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys.Headless;

/// <summary>
/// The MFA views described as forms: posting the fields with an action is what the hosted view's form posts, so the
/// MFA step handler reads the same input.
/// </summary>
public class MfaDescriberTests
{
    private static readonly IStringLocalizerFactory Localizers = new KeyLocalizerFactory();

    private static JourneyStepForm Describe(IJourneyStepFormDescriber describer, string view, object? model) =>
        describer.Describe(new JourneyStepDescription("mfa", "mfa", view, model, null));

    [Fact]
    public void Verify_posts_the_code_with_the_provider_and_offers_recovery()
    {
        var form = Describe(new MfaVerifyDescriber(Localizers), "Journey/_MfaVerify",
            new MfaVerifyViewModel { Provider = "email", ErrorMessage = "Invalid code" });

        form.Title.Should().Be("mfa_verify_title");
        form.Description.Should().Be("mfa_verify_email_subtitle");
        form.Error.Should().Be("Invalid code");
        var code = form.Fields.Should().ContainSingle().Subject;
        code.Name.Should().Be("code");
        code.Type.Should().Be("otp");
        code.MaxLength.Should().Be(6);
        code.Autocomplete.Should().Be("one-time-code");
        code.Required.Should().BeTrue();

        form.Actions.Select(a => (a.Name, a.Value, a.Kind, a.PostsFields, a.Description)).Should().Equal(
            ("provider", "email", "primary", true, (string?)null),
            ("use_recovery", "true", "link", false, "mfa_verify_recovery_prompt"));
        form.Actions[1].SkipValidation.Should().BeTrue();
        form.Actions[1].Values.Should().BeNull();
    }

    [Theory]
    [InlineData("totp", "mfa_verify_totp_subtitle")]
    [InlineData("Authenticator", "mfa_verify_totp_subtitle")]
    [InlineData("phone", "mfa_verify_sms_subtitle")]
    [InlineData("webauthn", "mfa_verify_generic_subtitle")]
    public void Verify_explains_where_the_code_comes_from(string provider, string subtitle)
    {
        var form = Describe(new MfaVerifyDescriber(Localizers), "Journey/_MfaVerify", new MfaVerifyViewModel { Provider = provider });

        form.Description.Should().Be(subtitle);
    }

    [Fact]
    public void Verify_offers_remembering_the_device_only_when_allowed()
    {
        var describer = new MfaVerifyDescriber(Localizers);

        var allowed = Describe(describer, "Journey/_MfaVerify", new MfaVerifyViewModel { Provider = "totp", CanRememberDevice = true });
        var remember = allowed.Fields.Should().ContainSingle(f => f.Name == "remember_device").Subject;
        remember.Type.Should().Be("checkbox");
        remember.Label.Should().Be("mfa_verify_remember_device");

        Describe(describer, "Journey/_MfaVerify", new MfaVerifyViewModel { Provider = "totp" })
            .Fields.Should().NotContain(f => f.Name == "remember_device");
    }

    [Fact]
    public void Setup_offers_each_available_method_as_its_own_choice()
    {
        var form = Describe(new MfaSetupDescriber(Localizers), "Journey/_MfaSetup",
            new MfaSetupViewModel { AvailableMethods = ["totp", "phone"], ErrorMessage = "Pick one" });

        form.Title.Should().Be("mfa_setup_title");
        form.Error.Should().Be("Pick one");
        form.Fields.Should().BeEmpty();
        form.Actions.Select(a => (a.Name, a.Value, a.Label, a.Description, a.PostsFields)).Should().Equal(
            ("setup_method", "totp", "mfa_setup_totp_heading", "mfa_setup_totp_description", false),
            ("setup_method", "sms", "mfa_setup_sms_heading", "mfa_setup_sms_description", false));
    }

    [Fact]
    public void Totp_setup_shows_the_qr_code_rendered_on_the_server_and_takes_the_first_code()
    {
        var model = new MfaTotpSetupViewModel
        {
            SharedKey = "ABCD EFGH",
            AuthenticatorUri = "otpauth://totp/Acme:jo@a.test?secret=ABCDEFGH&issuer=Acme",
            UserEmail = "jo@a.test",
            ErrorMessage = "Wrong code"
        };

        var form = Describe(new MfaTotpSetupDescriber(Localizers), "Journey/_MfaTotpSetup", model);

        form.Error.Should().Be("Wrong code");
        form.Widget!.Type.Should().Be("qr");
        var data = form.Widget.Data!.Value;
        data.GetProperty("value").GetString().Should().Be(model.AuthenticatorUri);
        data.GetProperty("secret").GetString().Should().Be("ABCD EFGH");
        data.GetProperty("secretLabel").GetString().Should().Be("mfa_totp_setup_manual_label");
        data.GetProperty("image").GetString().Should().StartWith("data:image/png;base64,");

        var code = form.Fields.Should().ContainSingle(f => f.Type == "otp").Subject;
        code.Name.Should().Be("setup_totp_code");
        code.MaxLength.Should().Be(6);
        form.Fields.Should().Contain(f => f.Type == "content" && f.Value == "mfa_totp_setup_qr_instructions");
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = (string?)null, Label = "mfa_totp_setup_button", PostsFields = true });
    }

    [Fact]
    public void Email_setup_takes_the_emailed_code_or_resends_it()
    {
        var form = Describe(new MfaEmailSetupDescriber(Localizers), "Journey/_MfaEmailSetup",
            new MfaEmailSetupViewModel { Email = "j***o@a.test", ErrorMessage = "Invalid" });

        form.Description.Should().Be("mfa_email_setup_subtitle:j***o@a.test");
        form.Error.Should().Be("Invalid");
        form.Fields.Should().ContainSingle().Which.Name.Should().Be("setup_email_code");
        form.Actions.Select(a => (a.Name, a.Value, a.SkipValidation, a.PostsFields, a.Description)).Should().Equal(
            ((string?)null, (string?)null, false, true, (string?)null),
            ("setup_email", "resend", true, false, "mfa_email_setup_resend_prompt"));
    }

    [Fact]
    public void Sms_setup_asks_for_the_phone_number_first()
    {
        var form = Describe(new MfaSmsSetupDescriber(Localizers), "Journey/_MfaSmsSetup",
            new MfaSmsSetupViewModel { PhoneNumber = "+447700900123", ErrorMessage = "Bad number" });

        form.Error.Should().Be("Bad number");
        form.Notice.Should().BeNull();
        var phone = form.Fields.Should().ContainSingle().Subject;
        phone.Name.Should().Be("phone_number");
        phone.Type.Should().Be("tel");
        phone.Value.Should().Be("+447700900123");
        phone.Placeholder.Should().Be("mfa_sms_setup_phone_placeholder");
        phone.Hint.Should().Be("mfa_sms_setup_phone_help");
        phone.Required.Should().BeTrue();
        form.Actions.Select(a => (a.Name, a.Value, a.Kind, a.PostsFields)).Should().Equal(
            ("setup_sms_phone", "true", "primary", true),
            ("setup_method_back", "true", "link", false));
    }

    [Fact]
    public void Sms_setup_takes_the_texted_code_once_sent()
    {
        var form = Describe(new MfaSmsSetupDescriber(Localizers), "Journey/_MfaSmsSetup",
            new MfaSmsSetupViewModel { CodeSent = true, MaskedPhone = "***0123" });

        form.Notice.Should().Be("mfa_sms_setup_code_sent:***0123");
        var code = form.Fields.Should().ContainSingle().Subject;
        code.Name.Should().Be("setup_sms_code");
        code.Type.Should().Be("otp");
        form.Actions.Select(a => (a.Name, a.Value, a.Kind, a.PostsFields, a.Description)).Should().Equal(
            ("setup_sms_verify", "true", "primary", true, (string?)null),
            ("setup_sms_resend", "true", "link", false, "mfa_sms_setup_resend_prompt"),
            ("setup_method_back", "true", "link", false, (string?)null));
    }

    [Fact]
    public void Recovery_codes_are_shown_to_keep_and_confirmed()
    {
        var form = Describe(new MfaRecoveryCodesDescriber(Localizers), "Journey/_MfaRecoveryCodes",
            new MfaRecoveryCodesViewModel { Provider = "totp", RecoveryCodes = ["aaaa-1111", "bbbb-2222"] });

        form.Description.Should().Be("mfa_recovery_codes_subtitle:totp");
        form.Widget!.Type.Should().Be("codes");
        form.Widget.Data!.Value.GetProperty("codes").EnumerateArray().Select(c => c.GetString())
            .Should().Equal("aaaa-1111", "bbbb-2222");
        var warning = form.Fields.Should().ContainSingle().Subject;
        warning.Type.Should().Be("content");
        warning.Label.Should().Be("mfa_recovery_codes_warning_header");
        warning.Value.Should().Be("mfa_recovery_codes_warning_item1\nmfa_recovery_codes_warning_item2\nmfa_recovery_codes_warning_item3");
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "recovery_codes_confirmed", Value = "true" });
    }

    [Fact]
    public void Recovery_verify_posts_the_recovery_code_or_goes_back_to_the_code()
    {
        var form = Describe(new MfaRecoveryVerifyDescriber(Localizers), "Journey/_MfaRecoveryVerify",
            new MfaRecoveryVerifyViewModel { ErrorMessage = "Invalid recovery code" });

        form.Error.Should().Be("Invalid recovery code");
        var code = form.Fields.Should().ContainSingle().Subject;
        code.Name.Should().Be("recovery_code");
        code.Type.Should().Be("text");
        code.Required.Should().BeTrue();
        code.Autocomplete.Should().Be("off");
        form.Actions.Select(a => (a.Name, a.Value, a.Kind, a.SkipValidation, a.PostsFields)).Should().Equal(
            ("use_recovery_code", "true", "primary", false, true),
            ("use_recovery", "false", "link", true, false));
    }

    [Fact]
    public void The_step_forms_find_each_mfa_view_by_its_hosted_path()
    {
        var forms = new JourneyStepForms(
        [
            new MfaVerifyDescriber(Localizers), new MfaSetupDescriber(Localizers), new MfaTotpSetupDescriber(Localizers),
            new MfaEmailSetupDescriber(Localizers), new MfaSmsSetupDescriber(Localizers),
            new MfaRecoveryCodesDescriber(Localizers), new MfaRecoveryVerifyDescriber(Localizers)
        ]);

        forms.Describe(new JourneyStepDescription("mfa", "mfa", "~/Views/Shared/Journey/_MfaRecoveryVerify.cshtml",
            new MfaRecoveryVerifyViewModel(), null))!.Title.Should().Be("mfa_recovery_verify_title");
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
