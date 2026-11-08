using FluentAssertions;
using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.Enterprise.Verification.Headless;
using Oluso.Enterprise.Verification.Journey;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>
/// The business verification views described as forms: the form posts action=submit with the same field names (and the
/// documents as files), the waiting page polls and posts its own actions, and a rejection offers start_over.
/// </summary>
public class BusinessVerificationDescriberTests
{
    private static readonly IStringLocalizerFactory Localizers = new KeyLocalizerFactory();

    private static JourneyStepForm Describe(IJourneyStepFormDescriber describer, string view, object? model) =>
        describer.Describe(new JourneyStepDescription("kyb", BusinessVerificationStepHandler.StepTypeName, view, model, null));

    private static BusinessVerificationViewModel Form(bool documents = false, string? error = null) => new()
    {
        Countries = [new BusinessVerificationCountry("NG", "Nigeria", "🇳🇬"), new BusinessVerificationCountry("GH", "Ghana", "🇬🇭")],
        Country = "ng",
        RegistrationType = "rc",
        RegistrationNumber = "123456",
        BusinessName = "Acme Ltd",
        RequireBusinessName = true,
        ErrorKey = error,
        ShowDocuments = documents,
        DocumentsRequired = documents,
        DocumentsAccept = ".pdf,image/*",
        MaxDocuments = 3,
        MaxDocumentSizeMb = 10
    };

    [Fact]
    public void Form_posts_the_business_fields_with_action_submit()
    {
        var form = Describe(new BusinessVerificationDescriber(Localizers), BusinessVerificationStepHandler.FormView,
            Form(error: "business_verification_error_invalid_number"));

        form.Title.Should().Be("business_verification_title");
        form.Error.Should().Be("business_verification_error_invalid_number:10,3");
        form.Fields.Select(f => f.Name).Should().Equal("country", "registrationType", "registrationNumber", "businessName");
        var country = form.Fields[0];
        country.Should().BeEquivalentTo(new { Type = "select", Value = "NG", Required = true, Placeholder = "business_verification_country_placeholder" });
        country.Options!.Select(o => o.Value).Should().Equal("NG", "GH");
        form.Fields.Single(f => f.Name == "businessName").Should().BeEquivalentTo(new { Value = "Acme Ltd", Required = true, MaxLength = 300 });
        form.Fields.Single(f => f.Name == "registrationNumber").Should().BeEquivalentTo(new { Value = "123456", Required = true, MaxLength = 40 });
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "action", Value = "submit", PostsFields = true });
    }

    [Fact]
    public void Registration_type_shows_only_for_its_country_and_can_be_left_to_the_number()
    {
        var form = Describe(new BusinessVerificationDescriber(Localizers), BusinessVerificationStepHandler.FormView, Form());

        var type = form.Fields.Single(f => f.Name == "registrationType");
        type.ShowWhen.Should().BeEquivalentTo(new { Field = "country", Operator = "eq", Value = "NG" });
        type.Options![0].Should().BeEquivalentTo(new { Value = "", Label = "business_verification_type_from_number" });
        type.Options.Skip(1).Select(o => o.Value).Should().Equal(BusinessVerificationViewModel.RegistrationTypes["NG"]);
        type.Hint.Should().Be("business_verification_type_help_ng");
    }

    [Fact]
    public void Registration_type_is_absent_when_no_offered_country_uses_prefixes()
    {
        var model = Form();
        var ghanaOnly = new BusinessVerificationViewModel { Countries = [model.Countries[1]], RegistrationNumber = "1" };

        var form = Describe(new BusinessVerificationDescriber(Localizers), BusinessVerificationStepHandler.FormView, ghanaOnly);

        form.Fields.Should().NotContain(f => f.Name == "registrationType");
        form.Fields[0].Placeholder.Should().BeNull();
        form.Fields[0].Value.Should().Be("GH");
    }

    [Fact]
    public void Documents_are_posted_as_files_under_the_handlers_field()
    {
        var form = Describe(new BusinessVerificationDescriber(Localizers), BusinessVerificationStepHandler.FormView, Form(documents: true));

        form.Fields.Last().Should().BeEquivalentTo(new
        {
            Name = BusinessVerificationStepHandler.DocumentsField,
            Type = "file",
            Required = true,
            Accept = ".pdf,image/*",
            MaxFiles = 3,
            MaxFileSizeMb = 10,
            Capture = "environment",
            Hint = "journey_upload_limits:10,3"
        });
    }

    [Fact]
    public void Waiting_polls_and_offers_resume_refresh_and_email()
    {
        var form = Describe(new BusinessVerificationWaitingDescriber(Localizers), BusinessVerificationStepHandler.WaitingView,
            new BusinessVerificationWaitingViewModel
            {
                JourneyId = "j", StepId = "kyb", BusinessName = "Acme Ltd", ReasonKey = "business_verification_reason_in_review",
                CanResumeHosted = true, CanEmail = true, Email = "jo@a.test", WaitSeconds = 60
            });

        form.Description.Should().Be("business_verification_waiting_description:Acme Ltd");
        form.Fields.Should().Contain(f => f.Type == "content" && f.Value == "business_verification_reason_in_review");
        form.Fields.Single(f => f.Name == "email").Should().BeEquivalentTo(new { Type = "email", Value = "jo@a.test", Required = true, MaxLength = 320 });
        form.Actions.Select(a => (a.Name, a.Value, a.PostsFields)).Should().Equal(
            ("action", "resume_hosted", false), ("action", "refresh", false), ("action", "notify", true));
        form.Widget!.Type.Should().Be("poll");
        form.Widget.Data!.Value.GetProperty("intervalSeconds").GetInt32().Should().Be(3);
        form.Widget.Data!.Value.GetProperty("maxSeconds").GetInt32().Should().Be(1800);
    }

    [Fact]
    public void Waiting_after_the_email_was_asked_for_confirms_it()
    {
        var form = Describe(new BusinessVerificationWaitingDescriber(Localizers), BusinessVerificationStepHandler.WaitingView,
            new BusinessVerificationWaitingViewModel
            {
                JourneyId = "j", StepId = "kyb", BusinessName = "Acme Ltd", ReasonKey = "r", CanEmail = true, EmailRequested = true, Email = "jo@a.test"
            });

        form.Notice.Should().Be("business_verification_waiting_email_confirmed:jo@a.test");
        form.Fields.Should().NotContain(f => f.Name == "email");
        form.Actions.Should().ContainSingle().Which.Value.Should().Be("refresh");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rejected_shows_the_reason_and_starts_over_when_allowed(bool allowRetry)
    {
        var form = Describe(new BusinessVerificationRejectedDescriber(Localizers), BusinessVerificationStepHandler.RejectedView,
            new BusinessVerificationRejectedViewModel { BusinessName = "Acme Ltd", ReasonKey = "business_verification_reason_name_mismatch", AllowRetry = allowRetry });

        form.Error.Should().Be("business_verification_reason_name_mismatch");
        form.Fields.Should().Contain(f => f.Value == "Acme Ltd");
        if (allowRetry)
        {
            form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "action", Value = "start_over" });
        }
        else
        {
            form.Actions.Should().BeEmpty();
            form.Fields.Should().Contain(f => f.Value == "business_verification_rejected_contact");
        }
    }

    [Fact]
    public void Hosted_runs_the_providers_widget_and_returns_through_the_callback_action()
    {
        var form = Describe(new BusinessVerificationHostedDescriber(Localizers), BusinessVerificationStepHandler.HostedView,
            new BusinessVerificationHostedViewModel
            {
                JourneyId = "j",
                ScriptUrl = "https://widget.provider.test/connect.js",
                Options = new Dictionary<string, object> { ["reference_id"] = "ref-1", ["widget_id"] = "w1" },
                BusinessName = "Acme Ltd"
            });

        form.Description.Should().Be("business_verification_hosted_description:Acme Ltd");
        form.Actions.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "action", Value = "refresh", PostsFields = false });
        form.Widget!.Type.Should().Be("verification_connect");
        form.Widget.ResultField.Should().BeNull();
        form.Widget.Submit.Should().BeEquivalentTo(new { Name = (string?)null, Label = "business_verification_hosted_open", Kind = "link" });
        var data = form.Widget.Data!.Value;
        data.GetProperty("scriptUrl").GetString().Should().Be("https://widget.provider.test/connect.js");
        data.GetProperty("global").GetString().Should().Be("Connect");
        data.GetProperty("options").GetProperty("reference_id").GetString().Should().Be("ref-1");
        data.GetProperty("options").GetProperty("widget_id").GetString().Should().Be("w1");
        data.GetProperty("referenceField").GetString().Should().Be("reference_id");
        data.GetProperty("referenceOption").GetString().Should().Be("reference_id");
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
