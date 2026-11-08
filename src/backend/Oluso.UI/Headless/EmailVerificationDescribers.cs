using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>Journey/_EmailVerification: the address to send a verification code or link to.</summary>
public sealed class EmailVerificationDescriber : StepFormDescriber<EmailVerificationViewModel>
{
    public EmailVerificationDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_EmailVerification"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, EmailVerificationViewModel? model)
    {
        var magicLink = model?.Mode == "magic-link";
        return new JourneyStepForm
        {
            Title = T("email_verification_title"),
            Description = magicLink ? T("email_verification_magic_link_description") : T("email_verification_code_description"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields =
            [
                new JourneyFormField
                {
                    Name = "email",
                    Type = "email",
                    Label = T("email_verification_email_label"),
                    Placeholder = T("email_verification_email_placeholder"),
                    Value = model?.Email,
                    Required = true,
                    Autocomplete = "email",
                    Autofocus = true
                }
            ],
            Actions = [SubmitFields(magicLink ? T("email_verification_send_link_button") : T("email_verification_send_code_button"))]
        };
    }
}

/// <summary>
/// Journey/_EmailVerificationConfirm: the emailed code (or, for a magic link, waiting for the click), with resend and
/// change-address. Email verification and the profile update's email change both show it.
/// </summary>
public sealed class EmailVerificationConfirmDescriber : StepFormDescriber<EmailVerificationConfirmViewModel>
{
    /// <summary>How often the hosted view checks whether the journey moved on (a link clicked on another device).</summary>
    public const int PollIntervalSeconds = 3;

    /// <summary>How long the hosted view keeps checking before it gives up.</summary>
    public const int PollMaxSeconds = 180;

    public EmailVerificationConfirmDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_EmailVerificationConfirm"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, EmailVerificationConfirmViewModel? model)
    {
        var magicLink = model?.Mode == "magic-link";
        var fields = new List<JourneyFormField>();
        var actions = new List<JourneyFormAction>();

        if (!magicLink)
        {
            fields.Add(OtpCodeFields.Describe("code", model?.CodeLength ?? 6, T("email_verification_confirm_code_label"),
                T("email_verification_confirm_expires", model?.ExpirationMinutes ?? 0)));
            actions.Add(SubmitFields(T("email_verification_confirm_button")));
        }

        actions.Add(Alone(Link(magicLink ? T("email_verification_confirm_resend_link") : T("email_verification_confirm_resend_code"), "resend", "true")));
        if (model?.CanChangeEmail != false)
            actions.Add(Alone(Link(T("email_verification_confirm_change_email"), "change_email", "true")));

        return new JourneyStepForm
        {
            Title = T("email_verification_confirm_title"),
            Description = magicLink
                ? T("email_verification_confirm_magic_link_msg", model?.Email ?? string.Empty)
                : T("email_verification_confirm_code_msg", model?.Email ?? string.Empty),
            Error = NullIfEmpty(model?.ErrorMessage),
            Notice = NullIfEmpty(model?.SuccessMessage),
            Fields = fields,
            Actions = actions,
            Widget = !string.IsNullOrEmpty(model?.JourneyId) && !string.IsNullOrEmpty(model.CurrentStepId)
                ? Widget("poll", new { intervalSeconds = PollIntervalSeconds, maxSeconds = PollMaxSeconds })
                : null
        };
    }
}
