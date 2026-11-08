using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.Enterprise.Fido2.Headless;
using Oluso.Enterprise.Fido2.Services;
using Oluso.Enterprise.Fido2.Steps;
using Xunit;

namespace Oluso.Enterprise.Fido2.Tests;

/// <summary>
/// The passkey views described as forms: the credential JSON goes under the name each view posts it, with the same
/// hidden values (challenge, assertion and registration ids) and the same button names and values.
/// </summary>
public class Fido2DescriberTests
{
    private static readonly IStringLocalizerFactory Localizers = new KeyLocalizerFactory();

    private static JourneyStepForm Describe(IJourneyStepFormDescriber describer, string view, object? model) =>
        describer.Describe(new JourneyStepDescription("step", "webauthn", view, model, null));

    private static Fido2AssertionOptions AssertionOptions() => new()
    {
        AssertionId = "assert-1",
        Challenge = "Y2hhbGxlbmdl",
        RpId = "auth.example.test",
        AllowCredentials = [new Fido2CredentialDescriptor { Type = "public-key", Id = "Y3JlZA", Transports = ["internal"] }]
    };

    [Fact]
    public void Webauthn_login_posts_the_username_and_gives_the_passkey_button_the_journeys_options()
    {
        var form = Describe(new Fido2LoginDescriber(Localizers), "Journey/_Fido2Login",
            new Fido2LoginViewModel { Username = "jo", ErrorMessage = "User not found", Options = StepOptions() });

        form.Title.Should().Be("fido2_login_title");
        form.Error.Should().Be("User not found");
        form.Fields.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "username", Value = "jo", Required = true, Autocomplete = "username webauthn" });
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = (string?)null, Value = (string?)null, Label = "fido2_login_continue", PostsFields = true });

        form.Widget!.Type.Should().Be("webauthn_get");
        form.Widget.ResultField.Should().Be("fido2_response");
        form.Widget.Submit.Should().BeEquivalentTo(new { Name = (string?)null, PostsFields = false, SkipValidation = true, Label = "fido2_login_use_passkey" });
        var data = form.Widget.Data!.Value;
        // The options the step issued for this journey: the step accepts only their challenge.
        data.TryGetProperty("optionsUrl", out _).Should().BeFalse();
        data.GetProperty("publicKey").GetProperty("challenge").GetString().Should().Be("step-challenge");
        data.GetProperty("publicKey").GetProperty("rpId").GetString().Should().Be("auth.example.test");
        data.GetProperty("format").GetString().Should().Be("with_attachment");
    }

    private static Services.AssertionOptions StepOptions() => new()
    {
        Challenge = "step-challenge",
        Timeout = 60000,
        RpId = "auth.example.test",
        UserVerification = "preferred"
    };

    [Fact]
    public void Webauthn_login_in_passkey_only_mode_has_no_username()
    {
        var form = Describe(new Fido2LoginDescriber(Localizers), "Journey/_Fido2Login", new Fido2LoginViewModel { AllowPasskeyOnly = true, Options = StepOptions() });

        form.Fields.Should().NotContain(f => f.Name == "username");
        form.Actions.Should().BeEmpty();
        form.Widget!.Submit!.Label.Should().Be("fido2_login_passkey_button");
    }

    [Fact]
    public void Fido2_login_step_model_posts_passkey_login_with_an_optional_username()
    {
        var form = Describe(new Fido2LoginDescriber(Localizers), "Journey/_Fido2Login",
            new Steps.Fido2LoginViewModel { Title = "Sign in", Description = "With a passkey", AllowUsernameless = true });

        form.Title.Should().Be("Sign in");
        form.Description.Should().Be("With a passkey");
        form.Fields.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "username", Required = false });
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "action", Value = "passkey_login" });
        form.Widget.Should().BeNull();
    }

    [Fact]
    public void Assert_posts_the_response_with_the_options_challenge()
    {
        var form = Describe(new Fido2AssertDescriber(Localizers), "Journey/_Fido2Assert", new Fido2AssertViewModel
        {
            Options = new AssertionOptions { Challenge = "Y2hhbGxlbmdl", Timeout = 60000, RpId = "auth.example.test", UserVerification = "preferred" },
            Username = "jo@a.test",
            DisplayName = "Jo",
            ErrorMessage = "Try again"
        });

        form.Title.Should().Be("Jo");
        form.Error.Should().Be("Try again");
        form.Fields.Should().Contain(f => f.Type == "content" && f.Value == "jo@a.test");
        form.Fields.Should().ContainSingle(f => f.Type == "hidden").Which.Should().BeEquivalentTo(new { Name = "fido2_challenge", Value = "Y2hhbGxlbmdl" });
        form.Widget!.ResultField.Should().Be("fido2_response");
        form.Widget.Submit!.PostsFields.Should().BeFalse();
        var publicKey = form.Widget.Data!.Value.GetProperty("publicKey");
        publicKey.GetProperty("challenge").GetString().Should().Be("Y2hhbGxlbmdl");
        publicKey.GetProperty("rpId").GetString().Should().Be("auth.example.test");
        publicKey.TryGetProperty("allowCredentials", out _).Should().BeFalse();
    }

    [Fact]
    public void Assert_without_options_shows_no_ceremony()
    {
        var form = Describe(new Fido2AssertDescriber(Localizers), "Journey/_Fido2Assert", new Fido2AssertViewModel { IsPasskeyOnly = true });

        form.Title.Should().Be("passkey_assert_title");
        form.Widget.Should().BeNull();
        form.Notice.Should().Be("passkey_assert_not_supported");
    }

    [Fact]
    public void Assertion_posts_assertion_response_and_id_for_either_view_model()
    {
        var options = AssertionOptions();
        var core = Describe(new Fido2AssertionDescriber(Localizers), "Journey/_Fido2Assertion",
            new Oluso.Core.Services.Fido2AssertionViewModel { Options = options, AssertionId = options.AssertionId });
        var own = Describe(new Fido2AssertionDescriber(Localizers), "Journey/_Fido2Assertion",
            new Steps.Fido2AssertionViewModel { Options = options, AssertionId = options.AssertionId });

        foreach (var form in new[] { core, own })
        {
            form.Fields.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "assertionId", Type = "hidden", Value = "assert-1" });
            form.Widget!.ResultField.Should().Be("assertionResponse");
            form.Widget.Submit!.PostsFields.Should().BeFalse();
            var publicKey = form.Widget.Data!.Value.GetProperty("publicKey");
            publicKey.GetProperty("timeout").GetInt32().Should().Be(60000);
            publicKey.GetProperty("userVerification").GetString().Should().Be("preferred");
            var credential = publicKey.GetProperty("allowCredentials")[0];
            credential.GetProperty("id").GetString().Should().Be("Y3JlZA");
            credential.GetProperty("transports")[0].GetString().Should().Be("internal");
        }
    }

    [Fact]
    public void Registration_prompt_registers_or_skips()
    {
        var form = Describe(new Fido2RegistrationPromptDescriber(Localizers), "Journey/_Fido2RegistrationPrompt",
            new Fido2RegistrationPromptViewModel { Title = "Add a passkey", ExistingCredentialCount = 2, ErrorMessage = "Registration failed" });

        form.Title.Should().Be("Add a passkey");
        form.Description.Should().Be("fido2_reg_prompt_default_description");
        form.Error.Should().Be("Registration failed");
        form.Fields.Should().Contain(f => f.Value == "fido2_reg_prompt_existing_many:2");
        form.Actions.Select(a => (a.Name, a.Value, a.Kind)).Should().Equal(("action", "register", "primary"), ("action", "skip", "link"));
    }

    [Fact]
    public void Registration_prompt_without_skip_only_registers()
    {
        var form = Describe(new Fido2RegistrationPromptDescriber(Localizers), "Journey/_Fido2RegistrationPrompt",
            new Fido2RegistrationPromptViewModel { AllowSkip = false });

        form.Actions.Should().ContainSingle().Which.Value.Should().Be("register");
    }

    [Fact]
    public void Registration_posts_the_attestation_with_the_registration_id()
    {
        var form = Describe(new Fido2RegistrationDescriber(Localizers), "Journey/_Fido2Registration", new Steps.Fido2RegistrationViewModel
        {
            RegistrationId = "reg-1",
            AllowSkip = true,
            Options = new Fido2RegistrationOptions
            {
                RegistrationId = "reg-1",
                Challenge = "Y2hhbGxlbmdl",
                User = new Fido2UserInfo { Id = "dXNlcg", Name = "jo", DisplayName = "Jo" },
                RelyingParty = new Fido2RelyingParty { Id = "auth.example.test", Name = "Example" },
                PublicKeyCredentialParameters = [new Fido2PublicKeyCredentialParameters { Type = "public-key", Algorithm = -7 }],
                ExcludeCredentials = [new Fido2CredentialDescriptor { Type = "public-key", Id = "b2xk" }]
            }
        });

        form.Fields.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "registrationId", Type = "hidden", Value = "reg-1" });
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "action", Value = "skip", PostsFields = false });
        form.Widget!.Type.Should().Be("webauthn_create");
        form.Widget.ResultField.Should().Be("attestationResponse");
        var data = form.Widget.Data!.Value;
        var publicKey = data.GetProperty("publicKey");
        publicKey.GetProperty("rp").GetProperty("id").GetString().Should().Be("auth.example.test");
        publicKey.GetProperty("user").GetProperty("displayName").GetString().Should().Be("Jo");
        publicKey.GetProperty("pubKeyCredParams")[0].GetProperty("alg").GetInt32().Should().Be(-7);
        publicKey.GetProperty("attestation").GetString().Should().Be("none");
        publicKey.GetProperty("excludeCredentials")[0].GetProperty("id").GetString().Should().Be("b2xk");
        data.GetProperty("format").GetString().Should().Be("with_attachment");
        var names = data.GetProperty("credentialName");
        names.GetProperty("field").GetString().Should().Be("credentialDisplayName");
        names.GetProperty("platform").GetString().Should().Be("fido2_reg_display_this_device");
        names.GetProperty("crossPlatform").GetString().Should().Be("fido2_reg_display_security_key");
        names.GetProperty("other").GetString().Should().Be("Passkey");
    }

    [Fact]
    public void Registration_success_continues()
    {
        var form = Describe(new Fido2RegistrationSuccessDescriber(Localizers), "Journey/_Fido2RegistrationSuccess",
            new Fido2RegistrationSuccessViewModel { CredentialId = "c", DisplayName = "This device" });

        form.Description.Should().Be("fido2_reg_success_detail:This device");
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "action", Value = "continue" });
    }

    [Fact]
    public void Every_passkey_view_has_a_describer()
    {
        var describers = new IJourneyStepFormDescriber[]
        {
            new Fido2LoginDescriber(Localizers), new Fido2AssertDescriber(Localizers), new Fido2AssertionDescriber(Localizers),
            new Fido2RegistrationPromptDescriber(Localizers), new Fido2RegistrationDescriber(Localizers), new Fido2RegistrationSuccessDescriber(Localizers)
        };
        var forms = new JourneyStepForms(describers);

        foreach (var view in new[] { "_Fido2Login", "_Fido2Assert", "_Fido2Assertion", "_Fido2RegistrationPrompt", "_Fido2Registration", "_Fido2RegistrationSuccess" })
        {
            forms.Describe(new JourneyStepDescription("s", "t", $"Journey/{view}", null, null)).Should().NotBeNull(view);
        }
    }

    private sealed class KeyLocalizerFactory : IStringLocalizerFactory
    {
        public IStringLocalizer Create(Type resourceSource) => new KeyLocalizer();
        public IStringLocalizer Create(string baseName, string location) => new KeyLocalizer();
    }

    private sealed class KeyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, $"{name}:{string.Join(",", arguments)}");
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
