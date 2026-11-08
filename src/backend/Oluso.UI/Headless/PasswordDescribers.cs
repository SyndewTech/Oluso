using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>
/// Journey/_PasswordChange: the password-change step, and the change a sign-in step asks for when the password
/// was right but has expired.
/// </summary>
public sealed class PasswordChangeDescriber : StepFormDescriber<PasswordChangeViewModel>
{
    public PasswordChangeDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_PasswordChange"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, PasswordChangeViewModel? model)
    {
        var fields = new List<JourneyFormField>();
        if (model?.RequireCurrentPassword == true)
        {
            fields.Add(new JourneyFormField
            {
                Name = "currentPassword",
                Type = "password",
                Label = T("password_change_current_label"),
                Required = true,
                Autocomplete = "current-password"
            });
        }

        var requirements = new List<string>();
        if (model?.MinPasswordLength > 0) requirements.Add(T("password_change_req_length", model.MinPasswordLength));
        if (model?.RequireUppercase == true) requirements.Add(T("password_change_req_upper"));
        if (model?.RequireLowercase == true) requirements.Add(T("password_change_req_lower"));
        if (model?.RequireDigit == true) requirements.Add(T("password_change_req_digit"));
        if (model?.RequireSpecialChar == true) requirements.Add(T("password_change_req_special"));

        fields.Add(new JourneyFormField
        {
            Name = "newPassword",
            Type = "password",
            Label = T("password_change_new_label"),
            Hint = string.Join("\n", requirements.Prepend(T("password_change_requirements_header"))),
            Required = true,
            Autocomplete = "new-password"
        });
        fields.Add(new JourneyFormField
        {
            Name = "confirmPassword",
            Type = "password",
            Label = T("password_change_confirm_label"),
            Required = true,
            Autocomplete = "new-password"
        });

        var notices = new[] { model?.NoticeMessage, model?.SuccessMessage }.Where(m => !string.IsNullOrEmpty(m)).ToList();
        return new JourneyStepForm
        {
            Title = T("password_change_title"),
            Description = NullIfEmpty(model?.TenantName),
            Notice = notices.Count == 0 ? null : string.Join("\n", notices),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions = [SubmitFields(T("password_change_button"))]
        };
    }
}

/// <summary>Journey/_PasswordResetRequest: the email to send a reset code to.</summary>
public sealed class PasswordResetRequestDescriber : StepFormDescriber<PasswordResetRequestViewModel>
{
    public PasswordResetRequestDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_PasswordResetRequest"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, PasswordResetRequestViewModel? model) => new()
    {
        Title = T("password_reset_request_title"),
        Description = T("password_reset_request_subtitle"),
        Error = NullIfEmpty(model?.ErrorMessage),
        Fields =
        [
            new JourneyFormField
            {
                Name = "email",
                Type = "email",
                Label = T("password_reset_request_email_label"),
                Required = true,
                Autocomplete = "email",
                Autofocus = true
            }
        ],
        Actions =
        [
            SubmitFields(T("password_reset_request_button")),
            Alone(Link(T("password_reset_request_back_link"), "action", "back_to_login"))
        ]
    };
}

/// <summary>Journey/_PasswordResetVerify: the emailed reset code, with resend and back to sign-in.</summary>
public sealed class PasswordResetVerifyDescriber : StepFormDescriber<PasswordResetVerifyViewModel>
{
    public PasswordResetVerifyDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_PasswordResetVerify"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, PasswordResetVerifyViewModel? model) => new()
    {
        Title = T("password_reset_verify_title"),
        Description = T("password_reset_verify_subtitle", model?.Email ?? string.Empty),
        Error = NullIfEmpty(model?.ErrorMessage),
        Fields =
        [
            OtpCodeFields.Describe("code", PasswordResetStepHandler.CodeLength, T("password_reset_verify_code_label"),
                T("password_reset_verify_expires", model?.ExpirationMinutes ?? 0))
        ],
        Actions =
        [
            SubmitFields(T("password_reset_verify_button")),
            Alone(Link(T("password_reset_verify_resend_button"), "resend", "true"), T("password_reset_verify_resend_prompt")),
            Alone(Link(T("password_reset_request_back_link"), "action", "back_to_login"))
        ]
    };
}

/// <summary>Journey/_PasswordResetNewPassword: the new password once the reset code is verified.</summary>
public sealed class PasswordResetNewPasswordDescriber : StepFormDescriber<PasswordResetNewPasswordViewModel>
{
    public PasswordResetNewPasswordDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_PasswordResetNewPassword"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, PasswordResetNewPasswordViewModel? model)
    {
        var fields = new List<JourneyFormField>();
        if (model?.RequireCurrentPassword == true)
        {
            fields.Add(new JourneyFormField
            {
                Name = "current_password",
                Type = "password",
                Label = T("password_reset_new_current_label"),
                Required = true,
                Autocomplete = "current-password"
            });
        }

        var requirements = new List<string>
        {
            T("password_reset_new_requirements_header"),
            T("password_reset_new_req_length", model?.MinLength ?? 0)
        };
        if (model?.RequireUppercase == true) requirements.Add(T("password_reset_new_req_upper"));
        if (model?.RequireLowercase == true) requirements.Add(T("password_reset_new_req_lower"));
        if (model?.RequireDigit == true) requirements.Add(T("password_reset_new_req_digit"));
        if (model?.RequireNonAlphanumeric == true) requirements.Add(T("password_reset_new_req_special"));

        fields.Add(new JourneyFormField
        {
            Name = "new_password",
            Type = "password",
            Label = T("password_reset_new_password_label"),
            Hint = string.Join("\n", requirements),
            Required = true,
            Autocomplete = "new-password",
            Autofocus = true
        });
        fields.Add(new JourneyFormField
        {
            Name = "confirm_password",
            Type = "password",
            Label = T("password_reset_new_confirm_label"),
            Required = true,
            Autocomplete = "new-password"
        });

        return new JourneyStepForm
        {
            Title = T("password_reset_new_title"),
            Description = T("password_reset_new_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions = [SubmitFields(T("password_reset_new_button"))]
        };
    }
}
