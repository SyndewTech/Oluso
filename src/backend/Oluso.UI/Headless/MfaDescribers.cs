using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>Journey/_MfaVerify: the code from the chosen method, or a switch to a recovery code.</summary>
public sealed class MfaVerifyDescriber : StepFormDescriber<MfaVerifyViewModel>
{
    public MfaVerifyDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_MfaVerify"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, MfaVerifyViewModel? model)
    {
        var provider = model?.Provider?.ToLower();
        var subtitle = provider switch
        {
            "totp" or "authenticator" => T("mfa_verify_totp_subtitle"),
            "email" => T("mfa_verify_email_subtitle"),
            "sms" or "phone" => T("mfa_verify_sms_subtitle"),
            _ => T("mfa_verify_generic_subtitle")
        };

        var fields = new List<JourneyFormField> { OtpCodeFields.Describe("code", 6, T("mfa_verify_code_label")) };
        if (model?.CanRememberDevice == true)
        {
            fields.Add(new JourneyFormField { Name = "remember_device", Type = "checkbox", Label = T("mfa_verify_remember_device") });
        }

        return new JourneyStepForm
        {
            Title = T("mfa_verify_title"),
            Description = subtitle,
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions =
            [
                Submit(T("mfa_verify_button"), "provider", model?.Provider ?? string.Empty),
                Alone(Link(T("mfa_verify_recovery_link"), "use_recovery", "true"), T("mfa_verify_recovery_prompt"))
            ]
        };
    }
}

/// <summary>Journey/_MfaSetup: choosing the method to set up.</summary>
public sealed class MfaSetupDescriber : StepFormDescriber<MfaSetupViewModel>
{
    public MfaSetupDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_MfaSetup"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, MfaSetupViewModel? model)
    {
        var methods = model?.AvailableMethods;
        var actions = new List<JourneyFormAction>();
        if (methods?.Contains("totp") == true)
        {
            actions.Add(Alone(Submit(T("mfa_setup_totp_heading"), "setup_method", "totp"), T("mfa_setup_totp_description")));
        }
        if (methods?.Contains("email") == true)
        {
            actions.Add(Alone(Submit(T("mfa_setup_email_heading"), "setup_method", "email"), T("mfa_setup_email_description")));
        }
        if (methods?.Contains("sms") == true || methods?.Contains("phone") == true)
        {
            actions.Add(Alone(Submit(T("mfa_setup_sms_heading"), "setup_method", "sms"), T("mfa_setup_sms_description")));
        }

        return new JourneyStepForm
        {
            Title = T("mfa_setup_title"),
            Description = T("mfa_setup_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Actions = actions
        };
    }
}

/// <summary>Journey/_MfaTotpSetup: the authenticator QR code and shared key, then the first code.</summary>
public sealed class MfaTotpSetupDescriber : StepFormDescriber<MfaTotpSetupViewModel>
{
    public MfaTotpSetupDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_MfaTotpSetup"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, MfaTotpSetupViewModel? model)
    {
        return new JourneyStepForm
        {
            Title = T("mfa_totp_setup_title"),
            Description = T("mfa_totp_setup_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields =
            [
                Content("qr_instructions", T("mfa_totp_setup_qr_instructions")),
                OtpCodeFields.Describe("setup_totp_code", 6, T("mfa_totp_setup_code_label"))
            ],
            Actions = [SubmitFields(T("mfa_totp_setup_button"))],
            Widget = Widget("qr", new
            {
                value = NullIfEmpty(model?.AuthenticatorUri),
                image = NullIfEmpty(model?.QrCodeImage),
                secret = NullIfEmpty(model?.SharedKey),
                secretLabel = T("mfa_totp_setup_manual_label")
            })
        };
    }
}

/// <summary>Journey/_MfaEmailSetup: the code emailed to confirm the address, or a new one.</summary>
public sealed class MfaEmailSetupDescriber : StepFormDescriber<MfaEmailSetupViewModel>
{
    public MfaEmailSetupDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_MfaEmailSetup"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, MfaEmailSetupViewModel? model)
    {
        return new JourneyStepForm
        {
            Title = T("mfa_email_setup_title"),
            Description = T("mfa_email_setup_subtitle", model?.Email ?? string.Empty),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = [OtpCodeFields.Describe("setup_email_code", 6, T("mfa_email_setup_code_label"))],
            Actions =
            [
                SubmitFields(T("mfa_email_setup_button")),
                Alone(Link(T("mfa_email_setup_resend_button"), "setup_email", "resend"), T("mfa_email_setup_resend_prompt"))
            ]
        };
    }
}

/// <summary>Journey/_MfaSmsSetup: the phone number, then the code texted to it.</summary>
public sealed class MfaSmsSetupDescriber : StepFormDescriber<MfaSmsSetupViewModel>
{
    public MfaSmsSetupDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_MfaSmsSetup"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, MfaSmsSetupViewModel? model)
    {
        var back = Alone(Link(T("mfa_sms_setup_back_link"), "setup_method_back", "true"));
        var form = new JourneyStepForm
        {
            Title = T("mfa_sms_setup_title"),
            Description = T("mfa_sms_setup_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage)
        };

        if (model?.CodeSent == true)
        {
            form.Notice = T("mfa_sms_setup_code_sent", model.MaskedPhone ?? string.Empty);
            form.Fields = [OtpCodeFields.Describe("setup_sms_code", 6, T("mfa_sms_setup_code_label"))];
            form.Actions =
            [
                Submit(T("mfa_sms_setup_verify_button"), "setup_sms_verify", "true"),
                Alone(Link(T("mfa_sms_setup_resend_button"), "setup_sms_resend", "true"), T("mfa_sms_setup_resend_prompt")),
                back
            ];
            return form;
        }

        form.Fields =
        [
            new JourneyFormField
            {
                Name = "phone_number",
                Type = "tel",
                Label = T("mfa_sms_setup_phone_label"),
                Placeholder = T("mfa_sms_setup_phone_placeholder"),
                Hint = T("mfa_sms_setup_phone_help"),
                Value = model?.PhoneNumber,
                Autocomplete = "tel",
                Autofocus = true,
                Required = true
            }
        ];
        form.Actions = [Submit(T("mfa_sms_setup_phone_button"), "setup_sms_phone", "true"), back];
        return form;
    }
}

/// <summary>Journey/_MfaRecoveryCodes: the new recovery codes, to keep before finishing set-up.</summary>
public sealed class MfaRecoveryCodesDescriber : StepFormDescriber<MfaRecoveryCodesViewModel>
{
    public MfaRecoveryCodesDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_MfaRecoveryCodes"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, MfaRecoveryCodesViewModel? model)
    {
        var warning = Content("recovery_codes_warning", string.Join("\n",
            T("mfa_recovery_codes_warning_item1"),
            T("mfa_recovery_codes_warning_item2"),
            T("mfa_recovery_codes_warning_item3")));
        warning.Label = T("mfa_recovery_codes_warning_header");

        return new JourneyStepForm
        {
            Title = T("mfa_recovery_codes_title"),
            Description = T("mfa_recovery_codes_subtitle", model?.Provider ?? string.Empty),
            Fields = [warning],
            Actions = [Submit(T("mfa_recovery_codes_confirm_button"), "recovery_codes_confirmed", "true")],
            Widget = Widget("codes", new { codes = model?.RecoveryCodes ?? [] })
        };
    }
}

/// <summary>Journey/_MfaRecoveryVerify: signing in with a recovery code.</summary>
public sealed class MfaRecoveryVerifyDescriber : StepFormDescriber<MfaRecoveryVerifyViewModel>
{
    public MfaRecoveryVerifyDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_MfaRecoveryVerify"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, MfaRecoveryVerifyViewModel? model)
    {
        return new JourneyStepForm
        {
            Title = T("mfa_recovery_verify_title"),
            Description = T("mfa_recovery_verify_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields =
            [
                new JourneyFormField
                {
                    Name = "recovery_code",
                    Type = "text",
                    Label = T("mfa_recovery_verify_code_label"),
                    Placeholder = T("mfa_recovery_verify_code_placeholder"),
                    Hint = T("mfa_recovery_verify_code_help"),
                    Autocomplete = "off",
                    Autofocus = true,
                    Required = true
                }
            ],
            Actions =
            [
                Submit(T("mfa_recovery_verify_button"), "use_recovery_code", "true"),
                Alone(Link(T("mfa_recovery_verify_back_link"), "use_recovery", "false"))
            ]
        };
    }
}
