using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>Journey/_PasswordlessEmail: the address to send a sign-in code or link to.</summary>
public sealed class PasswordlessEmailDescriber : StepFormDescriber<PasswordlessEmailViewModel>
{
    public PasswordlessEmailDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_PasswordlessEmail"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, PasswordlessEmailViewModel? model)
    {
        var magicLink = model?.Mode == "magic-link";
        var fields = new List<JourneyFormField>
        {
            new()
            {
                Name = "email",
                Type = "email",
                Label = T("passwordless_email_label"),
                Placeholder = T("passwordless_email_placeholder"),
                Value = model?.Email,
                Required = true,
                Autocomplete = "username",
                Autofocus = true
            }
        };
        if (model?.AllowSignUp == true)
        {
            fields.Add(Content("signup_notice", T("passwordless_email_signup_notice")));
        }

        return new JourneyStepForm
        {
            Title = T("passwordless_email_title"),
            Description = magicLink ? T("passwordless_email_magic_link_description") : T("passwordless_email_code_description"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions = [SubmitFields(magicLink ? T("passwordless_email_send_magic_button") : T("passwordless_email_send_code_button"))]
        };
    }
}

/// <summary>Journey/_PasswordlessEmailVerify: the emailed sign-in code (or the link to click), with resend and change-address.</summary>
public sealed class PasswordlessEmailVerifyDescriber : StepFormDescriber<PasswordlessEmailVerifyViewModel>
{
    public PasswordlessEmailVerifyDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_PasswordlessEmailVerify"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, PasswordlessEmailVerifyViewModel? model)
    {
        var magicLink = model?.Mode == "magic-link";
        var fields = new List<JourneyFormField>();
        var actions = new List<JourneyFormAction>();

        if (!magicLink)
        {
            fields.Add(OtpCodeFields.Describe("code", model?.CodeLength ?? 6, T("passwordless_email_verify_code_label"),
                T("passwordless_email_verify_expires", model?.ExpirationMinutes ?? 0)));
            actions.Add(SubmitFields(T("passwordless_email_verify_button")));
        }

        actions.Add(Alone(Link(magicLink ? T("passwordless_email_verify_resend_magic") : T("passwordless_email_verify_resend_code"), "resend", "true")));
        actions.Add(Alone(Link(T("passwordless_email_verify_change_email"), "change_email", "true")));

        return new JourneyStepForm
        {
            Title = T("passwordless_email_verify_title"),
            Description = magicLink
                ? T("passwordless_email_verify_magic_link_msg", model?.Email ?? string.Empty)
                : T("passwordless_email_verify_code_msg", model?.Email ?? string.Empty),
            Error = NullIfEmpty(model?.ErrorMessage),
            Notice = NullIfEmpty(model?.SuccessMessage),
            Fields = fields,
            Actions = actions
        };
    }
}

/// <summary>Journey/_PasswordlessSms: the phone number to text a sign-in code to.</summary>
public sealed class PasswordlessSmsDescriber : StepFormDescriber<PasswordlessSmsViewModel>
{
    public PasswordlessSmsDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_PasswordlessSms"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, PasswordlessSmsViewModel? model)
    {
        var fields = new List<JourneyFormField>
        {
            new()
            {
                Name = "phone",
                Type = "tel",
                Label = T("passwordless_sms_label"),
                Placeholder = T("passwordless_sms_placeholder"),
                Hint = T("passwordless_sms_help"),
                Value = model?.PhoneNumber,
                Autocomplete = "tel",
                Required = true,
                Autofocus = true
            }
        };
        if (model?.AllowSignUp == true)
        {
            fields.Add(Content("signup_notice", T("passwordless_sms_signup_notice")));
        }

        return new JourneyStepForm
        {
            Title = T("passwordless_sms_title"),
            Description = T("passwordless_sms_description"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions = [SubmitFields(T("passwordless_sms_button"))]
        };
    }
}

/// <summary>Journey/_PasswordlessSmsVerify: the texted sign-in code, with resend.</summary>
public sealed class PasswordlessSmsVerifyDescriber : StepFormDescriber<PasswordlessSmsVerifyViewModel>
{
    public PasswordlessSmsVerifyDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_PasswordlessSmsVerify"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, PasswordlessSmsVerifyViewModel? model)
    {
        return new JourneyStepForm
        {
            Title = T("passwordless_sms_verify_title"),
            Description = T("passwordless_sms_verify_description", model?.PhoneNumber ?? string.Empty),
            Error = NullIfEmpty(model?.ErrorMessage),
            Notice = NullIfEmpty(model?.SuccessMessage),
            Fields =
            [
                OtpCodeFields.Describe("code", model?.CodeLength ?? 6, T("passwordless_sms_verify_code_label"),
                    T("passwordless_sms_verify_expires", model?.ExpirationMinutes ?? 0))
            ],
            Actions =
            [
                SubmitFields(T("passwordless_sms_verify_button")),
                Alone(Link(T("passwordless_sms_verify_resend_button"), "resend", "true"))
            ]
        };
    }
}
