using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>Journey/_SignUp: the registration form and its "Already have an account?" switch to sign-in.</summary>
public sealed class SignUpDescriber : StepFormDescriber<SignUpViewModel>
{
    public SignUpDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_SignUp"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, SignUpViewModel? model)
    {
        var fields = new List<JourneyFormField>
        {
            new()
            {
                Name = "firstName",
                Type = "text",
                Label = T("signup_first_name_label"),
                Placeholder = T("signup_first_name_placeholder"),
                Value = model?.FirstName,
                Autocomplete = "given-name",
                Group = "name"
            },
            new()
            {
                Name = "lastName",
                Type = "text",
                Label = T("signup_last_name_label"),
                Placeholder = T("signup_last_name_placeholder"),
                Value = model?.LastName,
                Autocomplete = "family-name",
                Group = "name"
            },
            new()
            {
                Name = "email",
                Type = "email",
                Label = T("signup_email_label"),
                Placeholder = T("signup_email_placeholder"),
                Value = model?.Email,
                ReadOnly = model?.IsEmailDisabled == true,
                Required = true,
                // The address is the sign-in name unless a separate username is asked for.
                Autocomplete = model?.AskUsername == true ? "email" : "username"
            }
        };

        if (model?.AskUsername == true)
        {
            fields.Add(new JourneyFormField
            {
                Name = "username",
                Type = "text",
                Label = T("signup_username_label"),
                Placeholder = T("signup_username_placeholder"),
                Value = model.Username,
                Required = true,
                Autocomplete = "username"
            });
        }

        if (model?.RequirePassword != false)
        {
            fields.Add(new JourneyFormField
            {
                Name = "password",
                Type = "password",
                Label = T("signup_password_label"),
                Placeholder = T("signup_password_placeholder"),
                Hint = T("signup_password_help"),
                Required = true,
                Autocomplete = "new-password"
            });
            fields.Add(new JourneyFormField
            {
                Name = "confirmPassword",
                Type = "password",
                Label = T("signup_confirm_password_label"),
                Placeholder = T("signup_confirm_password_placeholder"),
                Required = true,
                Autocomplete = "new-password"
            });
        }
        else
        {
            fields.Add(Content("passkey_notice", T("signup_passkey_notice")));
        }

        var links = new List<JourneyFormLink>();
        if (model?.RequireTermsAcceptance == true)
        {
            var label = $"{T("signup_terms_prefix")} {T("signup_terms_link")}";
            if (!string.IsNullOrEmpty(model.TermsUrl))
            {
                links.Add(new JourneyFormLink { Label = T("signup_terms_link"), Url = model.TermsUrl });
            }
            if (!string.IsNullOrEmpty(model.PrivacyUrl))
            {
                label += $" {T("signup_terms_and")} {T("signup_privacy_link")}";
                links.Add(new JourneyFormLink { Label = T("signup_privacy_link"), Url = model.PrivacyUrl });
            }
            fields.Add(new JourneyFormField { Name = "acceptTerms", Type = "checkbox", Label = label, Required = true });
        }

        var actions = new List<JourneyFormAction> { SubmitFields(T("signup_button")) };
        if (model?.ShowSignInLink == true)
        {
            actions.Add(Alone(Link(T("signup_signin_link"), "action_type", "login"), T("signup_signin_prefix")));
        }

        return new JourneyStepForm
        {
            Title = T("signup_title"),
            Description = T("signup_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions = actions,
            Links = links
        };
    }
}
