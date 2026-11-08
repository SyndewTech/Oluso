using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>Journey/_LocalLogin: username and password, the organization choice, passkey and forgot-password.</summary>
public sealed class LocalLoginDescriber : StepFormDescriber<LocalLoginViewModel>
{
    public LocalLoginDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_LocalLogin"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, LocalLoginViewModel? model)
    {
        var fields = new List<JourneyFormField>
        {
            new()
            {
                Name = "username",
                Type = "text",
                Label = T("local_login_username_label"),
                Placeholder = T("local_login_username_placeholder"),
                Value = model?.Username,
                Required = true,
                Autocomplete = "username",
                Autofocus = true
            },
            new()
            {
                Name = "password",
                Type = "password",
                Label = T("local_login_password_label"),
                Placeholder = T("local_login_password_placeholder"),
                Required = true,
                Autocomplete = "current-password"
            }
        };
        if (model?.ShowRememberMe == true)
        {
            fields.Add(new JourneyFormField
            {
                Name = "rememberMe",
                Type = "checkbox",
                Label = T("local_login_remember_me"),
                Value = model.RememberMe ? "true" : null
            });
        }

        var actions = new List<JourneyFormAction>();
        var choosingOrganization = model?.AvailableOrganizations is { Count: > 1 };
        if (choosingOrganization)
        {
            // Each choice submits the sign-in form, so the password is asked for again with it.
            actions.AddRange(model!.AvailableOrganizations!.Select(org =>
                Secondary(org.Name, "organization", org.Slug, skipValidation: false)));
        }
        actions.Add(SubmitFields(T("local_login_button")));

        if (model?.ShowPasskey == true)
        {
            var passkey = Secondary(T("local_login_passkey_button"), "action", "passkey_login");
            passkey.Description = T("local_login_passkey_hint");
            // Without usernameless credentials the hosted page copies the typed username into the passkey form.
            actions.Add(model.PasskeyUsernameless ? Alone(passkey) : passkey);
        }
        if (model?.AllowForgotPassword == true)
        {
            actions.Add(Alone(Link(T("local_login_forgot_password"), "action", "forgot_password")));
        }

        var links = new List<JourneyFormLink>();
        if (model?.AllowRegistration == true && !string.IsNullOrEmpty(model.RegistrationUrl))
        {
            links.Add(new JourneyFormLink { Label = T("local_login_signup_link"), Url = model.RegistrationUrl });
        }

        return new JourneyStepForm
        {
            Title = T("local_login_title"),
            Description = T("local_login_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Notice = choosingOrganization ? T("login_choose_organization_subtitle") : null,
            Fields = fields,
            Actions = actions,
            Links = links
        };
    }
}

/// <summary>Journey/_Lockout: the account is locked; nothing to submit.</summary>
public sealed class LockoutDescriber : StepFormDescriber<object>
{
    public LockoutDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Lockout"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, object? model) => new()
    {
        Title = T("lockout_title"),
        Description = T("lockout_message"),
        Notice = T("lockout_instructions")
    };
}

/// <summary>
/// Journey/_CompositeLogin with its Journey/_CompositeLocalFields: password, passkey and external providers in the
/// order the step's layout gives them. Choosing a provider makes the step redirect to it.
/// </summary>
public sealed class CompositeLoginDescriber : StepFormDescriber<CompositeLoginViewModel>
{
    public CompositeLoginDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_CompositeLogin"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, CompositeLoginViewModel? model)
    {
        var methodOrder = model?.LoginLayout switch
        {
            "social-first" => new[] { "external", "local", "passkey" },
            "passkey-first" => new[] { "passkey", "local", "external" },
            "email-expandable" => new[] { "external", "passkey", "local" },
            _ => new[] { "local", "passkey", "external" }
        };
        var showLocal = model?.ShowLocalLogin == true;
        var showPasskey = model?.ShowPasskey == true;
        var showExternal = model?.ShowExternalLogin == true && model.ExternalProviders.Count > 0;

        var fields = new List<JourneyFormField>();
        var actions = new List<JourneyFormAction>();
        foreach (var method in methodOrder)
        {
            switch (method)
            {
                case "local" when showLocal:
                    fields.AddRange(LocalFields(model!));
                    actions.Add(Emphasis(SubmitFields(T("composite_login_button")), model!, "local"));
                    break;

                case "passkey" when showPasskey:
                    var passkey = Emphasis(Secondary(T("composite_login_passkey_button"), "action", "passkey_login"), model!, "passkey");
                    if (model!.PasskeyUsernameless)
                    {
                        actions.Add(Alone(passkey));
                    }
                    else if (!showLocal)
                    {
                        // The passkey form's username input, which stays empty with no sign-in form to copy it from.
                        Alone(passkey).Values = new Dictionary<string, string> { ["username"] = string.Empty };
                        actions.Add(passkey);
                    }
                    else
                    {
                        // The hosted page copies the typed username into the passkey form.
                        actions.Add(passkey);
                    }
                    break;

                case "external" when showExternal:
                    actions.AddRange(model!.ExternalProviders.Select(provider =>
                        Alone(Emphasis(Secondary(provider.DisplayName, "provider", provider.Scheme), model, "external"))));
                    break;
            }
        }

        var links = new List<JourneyFormLink>();
        if (model?.AllowForgotPassword == true && !string.IsNullOrEmpty(model.ForgotPasswordUrl))
        {
            links.Add(new JourneyFormLink { Label = T("composite_login_forgot_password"), Url = model.ForgotPasswordUrl });
        }
        if (model?.AllowRegistration == true && !string.IsNullOrEmpty(model.RegistrationUrl))
        {
            links.Add(new JourneyFormLink { Label = T("composite_login_create_account"), Url = model.RegistrationUrl });
        }

        return new JourneyStepForm
        {
            Title = T("composite_login_title"),
            Description = T("composite_login_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions = actions,
            Links = links
        };
    }

    private IEnumerable<JourneyFormField> LocalFields(CompositeLoginViewModel model)
    {
        yield return new JourneyFormField
        {
            Name = "username",
            Type = "text",
            Label = T("composite_login_username_label"),
            Value = model.Username,
            Required = true,
            Autocomplete = "username"
        };
        yield return new JourneyFormField
        {
            Name = "password",
            Type = "password",
            Label = T("composite_login_password_label"),
            Required = true,
            Autocomplete = "current-password"
        };
        if (model.AllowRememberMe)
        {
            yield return new JourneyFormField
            {
                Name = "rememberMe",
                Type = "checkbox",
                Label = T("composite_login_remember_me"),
                Value = model.RememberMe ? "true" : null
            };
        }
    }

    private static JourneyFormAction Emphasis(JourneyFormAction action, CompositeLoginViewModel model, string method)
    {
        action.Kind = model.PrimaryAction == method ? "primary" : "secondary";
        return action;
    }
}

/// <summary>Journey/_ExternalLogin: one button per identity provider; choosing one makes the step redirect to it.</summary>
public sealed class ExternalLoginDescriber : StepFormDescriber<ExternalLoginViewModel>
{
    public ExternalLoginDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_ExternalLogin"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, ExternalLoginViewModel? model) => new()
    {
        Title = T("external_login_title"),
        Error = NullIfEmpty(model?.ErrorMessage),
        Actions = (model?.Providers ?? [])
            .Select(provider => Alone(Submit(provider.DisplayName, "provider", provider.Scheme)))
            .ToList()
    };
}
