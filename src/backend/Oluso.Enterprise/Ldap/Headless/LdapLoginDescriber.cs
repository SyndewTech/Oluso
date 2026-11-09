using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.Enterprise.Ldap.Integration;
using Oluso.UserJourneys.Headless;

namespace Oluso.Enterprise.Ldap.Headless;

/// <summary>
/// Journey/_LdapLogin: directory sign-in, and the change-password form when the step allows it. The view's text isn't
/// localized, so the same text is used here.
/// </summary>
public sealed class LdapLoginDescriber : StepFormDescriber<LdapLoginViewModel>
{
    public LdapLoginDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_LdapLogin"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, LdapLoginViewModel? model)
    {
        var form = new JourneyStepForm
        {
            Title = model?.ProviderDisplayName ?? "Corporate Login",
            Description = string.IsNullOrEmpty(model?.TenantName) ? null : $"Sign in to {model.TenantName}",
            Error = NullIfEmpty(model?.ErrorMessage),
            Notice = NullIfEmpty(model?.InfoMessage)
        };
        var help = Content("help", "Having trouble signing in? Contact your IT administrator.");

        if (model?.ChangePasswordMode == true)
        {
            form.Fields =
            [
                new JourneyFormField { Name = "username", Type = "text", Label = "Username", Value = model.Username, Required = true, Autocomplete = "username" },
                new JourneyFormField { Name = "password", Type = "password", Label = "Current password", Required = true, Autocomplete = "current-password", Autofocus = true },
                new JourneyFormField { Name = "newPassword", Type = "password", Label = "New password", Required = true, Autocomplete = "new-password" },
                new JourneyFormField { Name = "confirmPassword", Type = "password", Label = "Confirm new password", Required = true, Autocomplete = "new-password" },
                help
            ];
            form.Actions =
            [
                Submit("Change password and sign in", "action", "change_password"),
                Alone(Link("Back to sign in", "username", model.Username ?? string.Empty))
            ];
            return form;
        }

        var fields = new List<JourneyFormField>
        {
            new()
            {
                Name = "username",
                Type = "text",
                Label = "Username",
                Placeholder = "Enter your corporate username",
                Hint = "Use your Active Directory or LDAP username",
                Value = model?.Username,
                Required = true,
                Autocomplete = "username",
                Autofocus = true
            },
            new()
            {
                Name = "password",
                Type = "password",
                Label = "Password",
                Placeholder = "Enter your password",
                Required = true,
                Autocomplete = "current-password"
            }
        };
        if (model?.ShowRememberMe == true)
        {
            fields.Add(new JourneyFormField { Name = "rememberMe", Type = "checkbox", Label = "Remember me on this device" });
        }
        fields.Add(help);

        var actions = new List<JourneyFormAction> { SubmitFields("Sign In with Corporate Directory") };
        if (model?.AllowPasswordChange == true)
        {
            actions.Add(Link("Change your password", "action", "show_change_password"));
        }

        form.Fields = fields;
        form.Actions = actions;
        return form;
    }
}
