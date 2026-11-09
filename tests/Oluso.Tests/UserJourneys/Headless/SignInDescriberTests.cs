using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UI.Headless;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys.Headless;

/// <summary>
/// The sign-in views described as forms post what the hosted views post: the same field names, hidden values and
/// button names and values, with the model's errors and prefilled values carried over.
/// </summary>
public class SignInDescriberTests
{
    private static readonly IStringLocalizerFactory Localizers = new KeyLocalizerFactory();

    private static JourneyStepForm Describe(IJourneyStepFormDescriber describer, object? model, string? view = null) =>
        describer.Describe(new JourneyStepDescription("step", "type", view ?? describer.Views.First(), model, null));

    private static IEnumerable<string> Names(JourneyStepForm form) => form.Fields.Select(f => f.Name);

    private static JourneyFormField Field(JourneyStepForm form, string name) => form.Fields.Single(f => f.Name == name);

    [Theory]
    [InlineData("Journey/_LocalLogin", typeof(LocalLoginDescriber))]
    [InlineData("~/Views/Shared/Journey/_Lockout.cshtml", typeof(LockoutDescriber))]
    [InlineData("Journey/_CompositeLogin", typeof(CompositeLoginDescriber))]
    [InlineData("Journey/_ExternalLogin", typeof(ExternalLoginDescriber))]
    [InlineData("Journey/_SignUp", typeof(SignUpDescriber))]
    [InlineData("Journey/_PasswordChange", typeof(PasswordChangeDescriber))]
    [InlineData("Journey/_PasswordResetRequest", typeof(PasswordResetRequestDescriber))]
    [InlineData("Journey/_PasswordResetVerify", typeof(PasswordResetVerifyDescriber))]
    [InlineData("Journey/_PasswordResetNewPassword", typeof(PasswordResetNewPasswordDescriber))]
    [InlineData("Journey/_LinkAccount", typeof(LinkAccountDescriber))]
    [InlineData("Journey/_LinkAccountConfirm", typeof(LinkAccountConfirmDescriber))]
    [InlineData("Journey/_UpdateUser", typeof(UpdateUserDescriber))]
    [InlineData("Journey/_Consent", typeof(ConsentDescriber))]
    [InlineData("Journey/_TermsAcceptance", typeof(TermsAcceptanceDescriber))]
    [InlineData("Journey/_Captcha", typeof(CaptchaDescriber))]
    public void Registered_describers_cover_each_view(string view, Type describerType)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Localizers);
        services.AddJourneyStepFormDescribers(typeof(Oluso.UI.DependencyInjection).Assembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var describers = scope.ServiceProvider.GetServices<IJourneyStepFormDescriber>().ToList();
        describers.Should().ContainSingle(d => d.GetType() == describerType);
        var forms = new JourneyStepForms(describers);
        forms.Describe(new JourneyStepDescription("s", "t", view, null, null)).Should().NotBeNull();
    }

    // ---- _LocalLogin ----

    [Fact]
    public void LocalLogin_posts_username_password_and_remember_me_with_passkey_and_forgot_password()
    {
        var form = Describe(new LocalLoginDescriber(Localizers), new LocalLoginViewModel
        {
            Username = "jo@x.test",
            ErrorMessage = "Invalid username or password",
            ShowRememberMe = true,
            RememberMe = true,
            ShowPasskey = true,
            AllowForgotPassword = true
        });

        form.Title.Should().Be("local_login_title");
        form.Error.Should().Be("Invalid username or password");
        form.Notice.Should().BeNull();
        Field(form, "username").Value.Should().Be("jo@x.test");
        Field(form, "username").Required.Should().BeTrue();
        Field(form, "password").Type.Should().Be("password");
        Field(form, "rememberMe").Type.Should().Be("checkbox");
        Field(form, "rememberMe").Value.Should().Be("true");

        form.Actions.Select(a => (a.Name, a.Value, a.Kind)).Should().Equal(
            (null, null, "primary"),
            ("action", "passkey_login", "secondary"),
            ("action", "forgot_password", "link"));
        form.Actions.Skip(1).Should().OnlyContain(a => a.SkipValidation && !a.PostsFields);
        form.Actions[0].PostsFields.Should().BeTrue();
        form.Actions[1].Description.Should().Be("local_login_passkey_hint");
        form.Links.Should().BeEmpty();
    }

    [Fact]
    public void LocalLogin_passkey_for_named_credentials_carries_the_typed_username()
    {
        var form = Describe(new LocalLoginDescriber(Localizers), new LocalLoginViewModel { ShowPasskey = true, PasskeyUsernameless = false });

        form.Actions.Single(a => a.Value == "passkey_login").PostsFields.Should().BeTrue();
    }

    [Fact]
    public void LocalLogin_leaves_out_remember_me_passkey_and_forgot_password_when_the_step_does()
    {
        var form = Describe(new LocalLoginDescriber(Localizers), new LocalLoginViewModel
        {
            ShowRememberMe = false,
            ShowPasskey = false,
            AllowForgotPassword = false
        });

        Names(form).Should().Equal("username", "password");
        form.Actions.Should().ContainSingle().Which.Name.Should().BeNull();
    }

    [Fact]
    public void LocalLogin_offers_each_organization_as_a_button_that_submits_the_sign_in_form()
    {
        var form = Describe(new LocalLoginDescriber(Localizers), new LocalLoginViewModel
        {
            Username = "jo@x.test",
            ShowRememberMe = false,
            AllowForgotPassword = true,
            AvailableOrganizations =
            [
                new OrganizationOption { Slug = "acme", Name = "Acme" },
                new OrganizationOption { Slug = "globex", Name = "Globex" }
            ]
        });

        form.Notice.Should().Be("login_choose_organization_subtitle");
        form.Actions.Take(2).Select(a => (a.Name, a.Value, a.Label)).Should().Equal(
            ("organization", "acme", "Acme"),
            ("organization", "globex", "Globex"));
        form.Actions.Take(2).Should().OnlyContain(a => !a.SkipValidation);
        Names(form).Should().Contain(["username", "password"]);
    }

    [Fact]
    public void LocalLogin_links_registration_only_when_it_has_an_address()
    {
        var describer = new LocalLoginDescriber(Localizers);

        Describe(describer, new LocalLoginViewModel { AllowRegistration = true }).Links.Should().BeEmpty();
        Describe(describer, new LocalLoginViewModel { AllowRegistration = true, RegistrationUrl = "https://x.test/register" })
            .Links.Should().ContainSingle(l => l.Url == "https://x.test/register" && l.Label == "local_login_signup_link");
    }

    [Fact]
    public void LocalLogin_without_a_model_is_the_bare_sign_in_form()
    {
        var form = Describe(new LocalLoginDescriber(Localizers), null);

        Names(form).Should().Equal("username", "password");
        form.Actions.Should().ContainSingle();
    }

    // ---- _Lockout ----

    [Fact]
    public void Lockout_has_text_and_nothing_to_submit()
    {
        var form = Describe(new LockoutDescriber(Localizers), null);

        form.Title.Should().Be("lockout_title");
        form.Description.Should().Be("lockout_message");
        form.Notice.Should().Be("lockout_instructions");
        form.Fields.Should().BeEmpty();
        form.Actions.Should().BeEmpty();
    }

    // ---- _CompositeLogin ----

    private static CompositeLoginViewModel Composite(string layout = "standard", string primary = "local") => new()
    {
        Username = "jo@x.test",
        ErrorMessage = "Invalid username or password",
        ShowLocalLogin = true,
        ShowPasskey = true,
        ShowExternalLogin = true,
        AllowRememberMe = true,
        LoginLayout = layout,
        PrimaryAction = primary,
        ExternalProviders =
        [
            new ExternalProviderViewModel { Scheme = "google", DisplayName = "Google" },
            new ExternalProviderViewModel { Scheme = "entra-acme", DisplayName = "Acme Entra" }
        ]
    };

    [Fact]
    public void CompositeLogin_posts_the_local_fields_and_offers_passkey_and_each_provider()
    {
        var form = Describe(new CompositeLoginDescriber(Localizers), Composite());

        form.Error.Should().Be("Invalid username or password");
        Names(form).Should().Equal("username", "password", "rememberMe");
        Field(form, "username").Value.Should().Be("jo@x.test");
        form.Actions.Select(a => (a.Name, a.Value, a.Kind)).Should().Equal(
            (null, null, "primary"),
            ("action", "passkey_login", "secondary"),
            ("provider", "google", "secondary"),
            ("provider", "entra-acme", "secondary"));
        form.Actions.Skip(1).Should().OnlyContain(a => a.SkipValidation && !a.PostsFields);
        form.Actions[0].SkipValidation.Should().BeFalse();
    }

    [Theory]
    [InlineData("social-first", new[] { "provider", "provider", null, "action" })]
    [InlineData("passkey-first", new[] { "action", null, "provider", "provider" })]
    [InlineData("email-expandable", new[] { "provider", "provider", "action", null })]
    public void CompositeLogin_orders_the_methods_by_the_layout(string layout, string[] order)
    {
        var form = Describe(new CompositeLoginDescriber(Localizers), Composite(layout));

        form.Actions.Select(a => a.Name).Should().Equal(order);
    }

    [Fact]
    public void CompositeLogin_makes_the_configured_method_the_primary_one()
    {
        var form = Describe(new CompositeLoginDescriber(Localizers), Composite(primary: "external"));

        form.Actions.Where(a => a.Kind == "primary").Should().OnlyContain(a => a.Name == "provider").And.HaveCount(2);
        form.Actions.Single(a => a.Name == null).Kind.Should().Be("secondary");
    }

    [Fact]
    public void CompositeLogin_without_the_local_form_posts_an_empty_username_for_a_named_passkey()
    {
        var model = Composite();
        model.ShowLocalLogin = false;
        model.PasskeyUsernameless = false;

        var form = Describe(new CompositeLoginDescriber(Localizers), model);

        form.Fields.Should().BeEmpty();
        var passkey = form.Actions.Single(a => a.Value == "passkey_login");
        passkey.PostsFields.Should().BeFalse();
        passkey.Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["username"] = "" });
        form.Actions.Should().OnlyContain(a => a.Name != null);
    }

    [Fact]
    public void CompositeLogin_hides_external_providers_when_the_step_turns_them_off_and_links_only_real_addresses()
    {
        var model = Composite();
        model.ShowExternalLogin = false;
        model.AllowRememberMe = false;
        model.AllowForgotPassword = true;
        model.AllowRegistration = true;
        model.RegistrationUrl = "https://x.test/join";

        var form = Describe(new CompositeLoginDescriber(Localizers), model);

        form.Actions.Select(a => a.Name).Should().NotContain("provider");
        Names(form).Should().Equal("username", "password");
        form.Links.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Label = "composite_login_create_account", Url = "https://x.test/join" });
    }

    // ---- _ExternalLogin ----

    [Fact]
    public void ExternalLogin_posts_the_chosen_provider()
    {
        var form = Describe(new ExternalLoginDescriber(Localizers), new ExternalLoginViewModel
        {
            ErrorMessage = "Failed",
            Providers = [new ExternalProviderViewModel { Scheme = "github", DisplayName = "GitHub" }]
        });

        form.Error.Should().Be("Failed");
        form.Fields.Should().BeEmpty();
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "provider", Value = "github", Label = "GitHub" });
    }

    // ---- _SignUp ----

    [Fact]
    public void SignUp_posts_the_registration_fields_and_the_switch_to_sign_in()
    {
        var form = Describe(new SignUpDescriber(Localizers), new SignUpViewModel
        {
            ErrorMessage = "Passwords do not match",
            Email = "jo@x.test",
            IsEmailDisabled = true,
            FirstName = "Jo",
            LastName = "Bloggs",
            AskUsername = true,
            Username = "jo",
            RequireTermsAcceptance = true,
            TermsUrl = "https://x.test/terms",
            PrivacyUrl = "https://x.test/privacy",
            ShowSignInLink = true
        });

        form.Error.Should().Be("Passwords do not match");
        Names(form).Should().Equal("firstName", "lastName", "email", "username", "password", "confirmPassword", "acceptTerms");
        Field(form, "email").Should().BeEquivalentTo(new { Value = "jo@x.test", ReadOnly = true, Required = true });
        Field(form, "firstName").Value.Should().Be("Jo");
        Field(form, "username").Value.Should().Be("jo");
        Field(form, "acceptTerms").Should().BeEquivalentTo(new
        {
            Type = "checkbox",
            Required = true,
            Label = "signup_terms_prefix signup_terms_link signup_terms_and signup_privacy_link"
        });
        form.Links.Select(l => l.Url).Should().Equal("https://x.test/terms", "https://x.test/privacy");
        form.Actions.Select(a => (a.Name, a.Value, a.Kind)).Should().Equal(
            (null, null, "primary"),
            ("action_type", "login", "link"));
    }

    [Fact]
    public void SignUp_without_a_password_shows_the_passkey_notice_instead()
    {
        var form = Describe(new SignUpDescriber(Localizers), new SignUpViewModel { RequirePassword = false });

        Names(form).Should().Equal("firstName", "lastName", "email", "passkey_notice");
        Field(form, "passkey_notice").Should().BeEquivalentTo(new { Type = "content", Value = "signup_passkey_notice" });
        form.Actions.Should().ContainSingle();
    }

    // ---- _PasswordChange ----

    [Fact]
    public void PasswordChange_asks_for_the_current_password_only_when_the_step_does()
    {
        var describer = new PasswordChangeDescriber(Localizers);

        Names(Describe(describer, new PasswordChangeViewModel { RequireCurrentPassword = true }))
            .Should().Equal("currentPassword", "newPassword", "confirmPassword");

        var expired = Describe(describer, new PasswordChangeViewModel
        {
            RequireCurrentPassword = false,
            MinPasswordLength = 12,
            RequireUppercase = true,
            RequireLowercase = false,
            RequireDigit = false,
            RequireSpecialChar = true,
            NoticeMessage = "Your password has expired.",
            ErrorMessage = "Passwords do not match",
            TenantName = "Acme"
        });

        Names(expired).Should().Equal("newPassword", "confirmPassword");
        expired.Notice.Should().Be("Your password has expired.");
        expired.Error.Should().Be("Passwords do not match");
        expired.Description.Should().Be("Acme");
        Field(expired, "newPassword").Hint.Should().Be(
            "password_change_requirements_header\npassword_change_req_length:12\npassword_change_req_upper\npassword_change_req_special");
        expired.Actions.Should().ContainSingle().Which.Name.Should().BeNull();
    }

    // ---- _PasswordReset* ----

    [Fact]
    public void PasswordResetRequest_posts_the_email_or_goes_back_to_sign_in()
    {
        var form = Describe(new PasswordResetRequestDescriber(Localizers), new PasswordResetRequestViewModel { ErrorMessage = "Please enter a valid email address" });

        form.Error.Should().Be("Please enter a valid email address");
        Field(form, "email").Should().BeEquivalentTo(new { Type = "email", Required = true });
        form.Actions.Select(a => (a.Name, a.Value, a.SkipValidation)).Should().Equal(
            (null, null, false),
            ("action", "back_to_login", true));
    }

    [Fact]
    public void PasswordResetVerify_posts_the_code_resends_or_goes_back()
    {
        var form = Describe(new PasswordResetVerifyDescriber(Localizers), new PasswordResetVerifyViewModel
        {
            Email = "j***o@x.test",
            ExpirationMinutes = 15,
            ErrorMessage = "Invalid code. Please try again."
        });

        form.Description.Should().Be("password_reset_verify_subtitle:j***o@x.test");
        form.Error.Should().Be("Invalid code. Please try again.");
        Field(form, "code").Should().BeEquivalentTo(new
        {
            Type = "otp",
            Required = true,
            MinLength = PasswordResetStepHandler.CodeLength,
            MaxLength = PasswordResetStepHandler.CodeLength,
            Hint = "password_reset_verify_expires:15"
        });
        form.Actions.Select(a => (a.Name, a.Value, a.SkipValidation)).Should().Equal(
            (null, null, false),
            ("resend", "true", true),
            ("action", "back_to_login", true));
        form.Actions.Skip(1).Should().OnlyContain(a => !a.PostsFields);
        form.Actions[1].Description.Should().Be("password_reset_verify_resend_prompt");
    }

    [Fact]
    public void PasswordResetNewPassword_posts_the_new_password_under_the_names_the_step_reads()
    {
        var form = Describe(new PasswordResetNewPasswordDescriber(Localizers), new PasswordResetNewPasswordViewModel
        {
            MinLength = 10,
            RequireDigit = true,
            RequireLowercase = false,
            RequireUppercase = false,
            RequireCurrentPassword = true,
            ErrorMessage = "Current password is incorrect"
        });

        Names(form).Should().Equal("current_password", "new_password", "confirm_password");
        form.Error.Should().Be("Current password is incorrect");
        Field(form, "new_password").Hint.Should().Be(
            "password_reset_new_requirements_header\npassword_reset_new_req_length:10\npassword_reset_new_req_digit");
        Names(Describe(new PasswordResetNewPasswordDescriber(Localizers), new PasswordResetNewPasswordViewModel()))
            .Should().Equal("new_password", "confirm_password");
    }

    // ---- _LinkAccount / _LinkAccountConfirm ----

    [Fact]
    public void LinkAccount_lists_the_linked_accounts_and_continues_with_done()
    {
        var form = Describe(new LinkAccountDescriber(Localizers), new LinkAccountViewModel
        {
            UserId = "u1",
            SuccessMessage = "Successfully linked your Google account!",
            LinkedLogins =
            [
                new LinkedLoginInfo { Provider = "Google", ProviderKey = "g1", ProviderDisplayName = "Google" },
                new LinkedLoginInfo { Provider = "GitHub", ProviderKey = "h1", ProviderDisplayName = "GitHub" }
            ]
        });

        form.Notice.Should().Be("Successfully linked your Google account!");
        Field(form, "linked_accounts").Should().BeEquivalentTo(new { Type = "content", Value = "Google\nGitHub", Label = "link_account_currently_linked" });
        form.Actions.Select(a => (a.Name, a.Value, a.Description, a.PostsFields)).Should().Equal(
            ("unlink_provider", "Google", "Google", false),
            ("unlink_provider", "GitHub", "GitHub", false),
            ("done", "true", null, false));
        form.Actions[0].Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["unlink_key"] = "g1" });
        form.Actions[1].Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["unlink_key"] = "h1" });

        Field(Describe(new LinkAccountDescriber(Localizers), new LinkAccountViewModel { UserId = "u1" }), "linked_accounts")
            .Value.Should().Be("link_account_no_linked");

        var noUnlink = Describe(new LinkAccountDescriber(Localizers), new LinkAccountViewModel
        {
            UserId = "u1",
            AllowUnlink = false,
            LinkedLogins = [new LinkedLoginInfo { Provider = "Google", ProviderKey = "g1", ProviderDisplayName = "Google" }]
        });
        noUnlink.Actions.Should().ContainSingle().Which.Name.Should().Be("done");
    }

    [Fact]
    public void LinkAccountConfirm_posts_the_external_login_it_confirms()
    {
        var form = Describe(new LinkAccountConfirmDescriber(Localizers), new LinkAccountConfirmViewModel
        {
            Provider = "Google",
            ProviderKey = "g-123",
            ProviderDisplayName = "Google Workspace"
        });

        form.Fields.Where(f => f.Type == "hidden").Select(f => (f.Name, f.Value)).Should().Equal(
            ("_external_provider", "Google"),
            ("_external_provider_key", "g-123"),
            ("_external_provider_name", "Google Workspace"),
            ("_confirmed", "true"));
        form.Title.Should().Be("link_account_confirm_title:Google Workspace");
        form.Description.Should().Be("link_account_confirm_description:Google Workspace");
        form.Actions.Select(a => (a.Name, a.Value, a.Kind, a.PostsFields)).Should().Equal(
            (null, null, "primary", true),
            ("cancel", "true", "cancel", false));
        form.Actions[0].Label.Should().Be("link_account_confirm_button:Google Workspace");
    }

    // ---- _UpdateUser ----

    [Fact]
    public void UpdateUser_posts_the_marker_the_step_waits_for_and_each_profile_field()
    {
        var form = Describe(new UpdateUserDescriber(Localizers), new UpdateUserViewModel
        {
            Title = "Complete your profile",
            Description = "A few more details",
            FormError = "Failed to update profile",
            SubmitButtonText = "Save",
            RequireCurrentPassword = true,
            CurrentPasswordError = "Current password is incorrect",
            Fields =
            [
                new UpdateFieldViewModel { Name = "firstName", Label = "First Name", Required = true, Value = "Jo" },
                new UpdateFieldViewModel { Name = "email", Label = "Email", Type = "email", Value = "jo@x.test", Error = "This email address is already in use" },
                new UpdateFieldViewModel { Name = "phone", Label = "Phone", Type = "tel" },
                new UpdateFieldViewModel { Name = "bio", Label = "Bio", Type = "textarea" },
                new UpdateFieldViewModel { Name = "colour", Label = "Colour", Type = "color" },
                new UpdateFieldViewModel
                {
                    Name = "team", Label = "Team", Value = "b",
                    Options = [new SelectOption { Value = "a", Label = "A" }, new SelectOption { Value = "b", Label = "B" }]
                }
            ]
        });

        form.Title.Should().Be("Complete your profile");
        form.Description.Should().Be("A few more details");
        form.Error.Should().Be("Failed to update profile");
        Field(form, "_submitted").Should().BeEquivalentTo(new { Type = "hidden", Value = "true" });
        form.Fields.Select(f => (f.Name, f.Type)).Should().Equal(
            ("_submitted", "hidden"), ("firstName", "text"), ("email", "email"), ("phone", "tel"),
            ("bio", "textarea"), ("colour", "text"), ("team", "select"), ("currentPassword", "password"));
        Field(form, "email").Error.Should().Be("This email address is already in use");
        Field(form, "team").Value.Should().Be("b");
        Field(form, "team").Options!.Select(o => o.Value).Should().Equal("a", "b");
        Field(form, "currentPassword").Error.Should().Be("Current password is incorrect");
        form.Actions.Should().ContainSingle().Which.Label.Should().Be("Save");
    }

    // ---- _Consent ----

    [Fact]
    public void Consent_posts_each_scope_and_the_decision()
    {
        var form = Describe(new ConsentDescriber(Localizers), new ConsentViewModel
        {
            ClientId = "app",
            ClientName = "Acme App",
            ClientUri = "https://acme.test",
            AllowRememberConsent = true,
            IdentityScopes =
            [
                new ScopeViewModel { Name = "openid", DisplayName = "Your user identifier", Required = true, Checked = true },
                new ScopeViewModel { Name = "email", DisplayName = "Email", Description = "Your email address", Checked = true }
            ],
            ApiScopes = [new ScopeViewModel { Name = "orders.read", DisplayName = "Read orders", Checked = false }]
        });

        form.Title.Should().Be("consent_title:Acme App");
        form.Fields.Where(f => f.Type == "checkbox").Select(f => (f.Name, f.Value)).Should().Equal(
            ("scope_openid", "true"), ("scope_email", "true"), ("scope_orders.read", null), ("remember_consent", null));
        Field(form, "scope_openid").Should().BeEquivalentTo(new { ReadOnly = true, Required = true, Hint = "consent_scope_required" });
        Field(form, "scope_email").Should().BeEquivalentTo(new { ReadOnly = false, Required = false, Description = "Your email address" });
        form.Actions.Select(a => (a.Name, a.Value, a.Kind)).Should().Equal(
            ("consent_action", "deny", "cancel"),
            ("consent_action", "allow", "primary"));
        form.Links.Should().ContainSingle(l => l.Url == "https://acme.test");
    }

    [Fact]
    public void Consent_leaves_out_remember_and_empty_sections()
    {
        var form = Describe(new ConsentDescriber(Localizers), new ConsentViewModel { ClientId = "app", ClientName = "App", ErrorMessage = "Oops" });

        form.Fields.Should().BeEmpty();
        form.Error.Should().Be("Oops");
    }

    // ---- _TermsAcceptance ----

    private static TermsAcceptanceViewModel Terms(bool inline = false, bool checkbox = true, bool both = true, bool canDecline = true) => new()
    {
        TermsTitle = "Terms of Service",
        PrivacyTitle = "Privacy Policy",
        TermsVersion = "2",
        PrivacyVersion = "3",
        TermsUrl = "https://x.test/terms",
        PrivacyUrl = "https://x.test/privacy",
        TermsContent = "<h2>Terms</h2><p>Be <b>nice</b> &amp; fair.</p><script>alert(1)</script><ul><li>One</li><li>Two</li></ul>",
        ShowTermsInline = inline,
        RequireCheckbox = checkbox,
        RequireBoth = both,
        AcceptButtonText = "Agree",
        DeclineButtonText = "No thanks",
        CanDecline = canDecline
    };

    [Fact]
    public void TermsAcceptance_posts_the_marker_the_checkboxes_and_decline()
    {
        var form = Describe(new TermsAcceptanceDescriber(Localizers), Terms());

        form.Title.Should().Be("Terms of Service");
        Field(form, "_submitted").Should().BeEquivalentTo(new { Type = "hidden", Value = "true" });
        Field(form, "acceptTerms").Should().BeEquivalentTo(new { Type = "checkbox", Required = true, Label = "terms_acceptance_agree_label:Terms of Service" });
        Field(form, "acceptPrivacy").Should().BeEquivalentTo(new { Type = "checkbox", Required = true, Label = "terms_acceptance_agree_label:Privacy Policy" });
        form.Links.Select(l => (l.Label, l.Url)).Should().Equal(
            ("Terms of Service (terms_acceptance_version:2)", "https://x.test/terms"),
            ("Privacy Policy (terms_acceptance_version:3)", "https://x.test/privacy"));
        form.Actions.Select(a => (a.Name, a.Value, a.Label, a.Kind)).Should().Equal(
            (null, null, "Agree", "primary"),
            ("decline", "true", "No thanks", "cancel"));
    }

    [Fact]
    public void TermsAcceptance_shows_inline_terms_as_plain_text()
    {
        var form = Describe(new TermsAcceptanceDescriber(Localizers), Terms(inline: true, both: false));

        Field(form, "terms_content").Should().BeEquivalentTo(new
        {
            Type = "content",
            Label = "Terms of Service",
            Value = "Terms\nBe nice & fair.\nOne\nTwo"
        });
        form.Links.Should().BeEmpty();
        Names(form).Should().NotContain("acceptPrivacy");
    }

    [Fact]
    public void TermsAcceptance_without_checkboxes_accepts_on_submit_and_says_so()
    {
        var form = Describe(new TermsAcceptanceDescriber(Localizers), Terms(checkbox: false, canDecline: false));

        Names(form).Should().NotContain(["acceptTerms", "acceptPrivacy"]);
        Field(form, "implicit_acceptance").Value.Should().Be(
            "terms_acceptance_implicit Terms of Service terms_acceptance_review_and Privacy Policy.");
        form.Actions.Should().ContainSingle();
    }

    // ---- _Captcha ----

    [Theory]
    [InlineData("recaptcha", "recaptcha", "g-recaptcha-response")]
    [InlineData("hcaptcha", "hcaptcha", "h-captcha-response")]
    [InlineData("turnstile", "turnstile", "cf-turnstile-response")]
    [InlineData("cloudflare", "turnstile", "cf-turnstile-response")]
    public void Captcha_asks_the_browser_for_the_providers_token(string configured, string provider, string resultField)
    {
        var form = Describe(new CaptchaDescriber(Localizers), new CaptchaViewModel
        {
            Provider = configured,
            SiteKey = "site-key",
            Theme = "dark",
            Size = "compact",
            Language = "fr",
            ErrorMessage = "CAPTCHA verification failed. Please try again."
        });

        form.Error.Should().Be("CAPTCHA verification failed. Please try again.");
        form.Widget!.Type.Should().Be("captcha");
        form.Widget.ResultField.Should().Be(resultField);
        form.Widget.Submit.Should().BeNull();
        var data = form.Widget.Data!.Value;
        data.GetProperty("provider").GetString().Should().Be(provider);
        data.GetProperty("siteKey").GetString().Should().Be("site-key");
        data.GetProperty("theme").GetString().Should().Be("dark");
        data.GetProperty("size").GetString().Should().Be("compact");
        form.Actions.Should().ContainSingle().Which.Label.Should().Be("captcha_button");
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
