using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.Enterprise.Verification.Journey;
using Oluso.UserJourneys.Headless;

namespace Oluso.Enterprise.Verification.Headless;

/// <summary>
/// Journey/_BusinessVerification: the business's country, registration type (for registries whose numbers carry a
/// type prefix, shown for that country only), registration number, name and supporting documents.
/// </summary>
public sealed class BusinessVerificationDescriber : StepFormDescriber<BusinessVerificationViewModel>
{
    public BusinessVerificationDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = [BusinessVerificationStepHandler.FormView];

    protected override JourneyStepForm Describe(JourneyStepDescription step, BusinessVerificationViewModel? model)
    {
        model ??= new BusinessVerificationViewModel();
        var fields = new List<JourneyFormField>
        {
            new()
            {
                Name = "country",
                Type = "select",
                Label = T("business_verification_country_label"),
                Placeholder = model.Countries.Count > 1 ? T("business_verification_country_placeholder") : null,
                Required = true,
                Value = model.Countries.FirstOrDefault(c => string.Equals(c.Code, model.Country, StringComparison.OrdinalIgnoreCase))?.Code
                    ?? (model.Countries.Count == 1 ? model.Countries[0].Code : null),
                Options = model.Countries
                    .Select(c => new JourneyFormOption { Value = c.Code, Label = string.IsNullOrEmpty(c.Flag) ? c.Name : $"{c.Flag} {c.Name}" })
                    .ToList()
            }
        };

        foreach (var registry in BusinessVerificationViewModel.RegistrationTypes.Where(r => model.Countries.Any(c => c.Code == r.Key)))
        {
            var key = registry.Key.ToLowerInvariant();
            var options = new List<JourneyFormOption> { new() { Value = string.Empty, Label = T("business_verification_type_from_number") } };
            options.AddRange(registry.Value.Select(type => new JourneyFormOption
            {
                Value = type,
                Label = T($"business_verification_type_{key}_{type.ToLowerInvariant()}")
            }));
            fields.Add(new JourneyFormField
            {
                Name = "registrationType",
                Type = "select",
                Label = T("business_verification_type_label"),
                Hint = T($"business_verification_type_help_{key}"),
                Value = registry.Value.FirstOrDefault(t => string.Equals(t, model.RegistrationType, StringComparison.OrdinalIgnoreCase)) ?? string.Empty,
                Options = options,
                ShowWhen = new JourneyFormCondition { Field = "country", Operator = "eq", Value = registry.Key }
            });
        }

        fields.Add(new JourneyFormField
        {
            Name = "registrationNumber",
            Type = "text",
            Label = T("business_verification_number_label"),
            Placeholder = T("business_verification_number_placeholder"),
            Value = model.RegistrationNumber,
            Required = true,
            MaxLength = 40,
            Autocomplete = "off",
            Autofocus = true
        });
        fields.Add(new JourneyFormField
        {
            Name = "businessName",
            Type = "text",
            Label = T("business_verification_name_label"),
            Placeholder = T("business_verification_name_placeholder"),
            Hint = model.RequireBusinessName ? T("business_verification_name_help") : null,
            Value = model.BusinessName,
            Required = model.RequireBusinessName,
            MaxLength = 300,
            Autocomplete = "organization"
        });

        if (model.ShowDocuments)
        {
            fields.Add(new JourneyFormField
            {
                Name = BusinessVerificationStepHandler.DocumentsField,
                Type = "file",
                Label = T("business_verification_documents_label"),
                Description = T("business_verification_documents_help"),
                Hint = T("journey_upload_limits", model.MaxDocumentSizeMb, model.MaxDocuments),
                Required = model.DocumentsRequired,
                Accept = NullIfEmpty(model.DocumentsAccept),
                MaxFiles = model.MaxDocuments,
                MaxFileSizeMb = model.MaxDocumentSizeMb,
                Capture = "environment"
            });
        }

        return new JourneyStepForm
        {
            Title = T("business_verification_title"),
            Description = T("business_verification_description"),
            Error = string.IsNullOrEmpty(model.ErrorKey) ? null : T(model.ErrorKey, model.MaxDocumentSizeMb, model.MaxDocuments),
            Fields = fields,
            Actions = [Submit(T("business_verification_button"), "action", "submit")]
        };
    }
}

/// <summary>
/// Journey/_BusinessVerificationWaiting: the check is pending; the page re-checks until the outcome arrives, and offers
/// to reopen the provider's session or email the outcome.
/// </summary>
public sealed class BusinessVerificationWaitingDescriber : StepFormDescriber<BusinessVerificationWaitingViewModel>
{
    public BusinessVerificationWaitingDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = [BusinessVerificationStepHandler.WaitingView];

    protected override JourneyStepForm Describe(JourneyStepDescription step, BusinessVerificationWaitingViewModel? model)
    {
        if (model == null)
        {
            return new JourneyStepForm { Title = T("business_verification_waiting_title") };
        }

        var fields = new List<JourneyFormField>
        {
            Content("reason", T(model.ReasonKey)),
            Content("checking", T("business_verification_waiting_checking"))
        };
        var actions = new List<JourneyFormAction>();
        if (model.CanResumeHosted)
        {
            actions.Add(Alone(Submit(T("business_verification_waiting_resume"), "action", "resume_hosted")));
        }
        actions.Add(Alone(Secondary(T("business_verification_waiting_refresh"), "action", "refresh")));

        var offerEmail = !model.EmailRequested && model.CanEmail;
        if (offerEmail)
        {
            fields.Add(new JourneyFormField
            {
                Name = "email",
                Type = "email",
                Label = T("business_verification_waiting_email_label"),
                Description = T("business_verification_waiting_email_offer"),
                Value = model.Email,
                Required = true,
                MaxLength = 320,
                Autocomplete = "email"
            });
            actions.Add(Submit(T("business_verification_waiting_email_button"), "action", "notify"));
        }

        return new JourneyStepForm
        {
            Title = T("business_verification_waiting_title"),
            Description = T("business_verification_waiting_description", model.BusinessName),
            Notice = model.EmailRequested ? T("business_verification_waiting_email_confirmed", model.Email ?? string.Empty) : null,
            Fields = fields,
            Actions = actions,
            Widget = Widget("poll", new { intervalSeconds = 3, maxSeconds = 30 * 60 })
        };
    }
}

/// <summary>
/// Journey/_BusinessVerificationHosted: the provider's own widget runs the check. Its success or error returns to the
/// step as the "callback" action (success with the session's reference_id), as the hosted page's callback does.
/// </summary>
public sealed class BusinessVerificationHostedDescriber : StepFormDescriber<BusinessVerificationHostedViewModel>
{
    public BusinessVerificationHostedDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = [BusinessVerificationStepHandler.HostedView];

    protected override JourneyStepForm Describe(JourneyStepDescription step, BusinessVerificationHostedViewModel? model) => new()
    {
        Title = T("business_verification_hosted_title"),
        Description = model == null ? null : T("business_verification_hosted_description", model.BusinessName),
        Actions = [Alone(Secondary(T("business_verification_hosted_done"), "action", "refresh"))],
        Widget = model == null ? null : Widget("verification_connect", new
        {
            scriptUrl = model.ScriptUrl,
            global = "Connect",
            options = System.Text.Json.JsonSerializer.SerializeToElement(model.Options),
            referenceField = "reference_id",
            referenceOption = "reference_id"
        }, submit: new JourneyFormAction { Label = T("business_verification_hosted_open"), Kind = "link", SkipValidation = true })
    };
}

/// <summary>Journey/_BusinessVerificationRejected: why the business wasn't verified, and a retry when allowed.</summary>
public sealed class BusinessVerificationRejectedDescriber : StepFormDescriber<BusinessVerificationRejectedViewModel>
{
    public BusinessVerificationRejectedDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = [BusinessVerificationStepHandler.RejectedView];

    protected override JourneyStepForm Describe(JourneyStepDescription step, BusinessVerificationRejectedViewModel? model)
    {
        var fields = new List<JourneyFormField>();
        if (!string.IsNullOrEmpty(model?.BusinessName))
        {
            fields.Add(Content("business", model.BusinessName));
        }
        if (model is { AllowRetry: false })
        {
            fields.Add(Content("contact", T("business_verification_rejected_contact")));
        }

        return new JourneyStepForm
        {
            Title = T("business_verification_rejected_title"),
            Error = model == null ? null : T(model.ReasonKey),
            Fields = fields,
            Actions = model?.AllowRetry == true
                ? [Submit(T("business_verification_rejected_retry"), "action", "start_over")]
                : []
        };
    }
}
