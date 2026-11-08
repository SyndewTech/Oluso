using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>Journey/_DynamicForm: data-collection forms, module forms and plugin forms.</summary>
public sealed class DynamicFormDescriber : StepFormDescriber<DynamicFormViewModel>
{
    private static readonly HashSet<string> NativeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "textarea", "select", "radio", "checkbox", "date", "number", "email", "tel", "file", "url"
    };

    public DynamicFormDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_DynamicForm"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, DynamicFormViewModel? model)
    {
        model ??= new DynamicFormViewModel();
        var actions = new List<JourneyFormAction>();
        if (model.ShowCancel)
        {
            actions.Add(Cancel(model.CancelButtonText ?? T("dynamic_form_cancel_button"), "__cancel", "true"));
        }
        actions.Add(Submit(model.SubmitButtonText));

        return new JourneyStepForm
        {
            Title = NullIfEmpty(model.Title),
            Description = NullIfEmpty(model.Description),
            Error = NullIfEmpty(model.FormError),
            Fields = model.Fields.Select(Field).ToList(),
            Actions = actions
        };
    }

    private JourneyFormField Field(DynamicFormFieldViewModel field)
    {
        if (field.Hidden) return Hidden(field.Name, field.Value);

        var type = field.Type.ToLowerInvariant() switch
        {
            "phone" => "tel",
            var t when NativeTypes.Contains(t) => t,
            _ => "text"
        };

        var described = new JourneyFormField
        {
            Name = field.Name,
            Type = type,
            Label = field.Label,
            Placeholder = NullIfEmpty(field.Placeholder),
            Description = NullIfEmpty(field.Description),
            Required = field.Required,
            ReadOnly = field.ReadOnly,
            Value = field.Value,
            Error = NullIfEmpty(field.Error),
            Pattern = NullIfEmpty(field.Pattern),
            MinLength = field.MinLength,
            MaxLength = field.MaxLength,
            Min = NullIfEmpty(field.Min),
            Max = NullIfEmpty(field.Max),
            Rows = field.Rows,
            Group = NullIfEmpty(field.Group),
            ShowWhen = field.ShowWhen == null ? null : new JourneyFormCondition
            {
                Field = field.ShowWhen.Field,
                Operator = field.ShowWhen.Operator,
                Value = field.ShowWhen.Value
            }
        };

        switch (type)
        {
            case "select" or "radio":
                described.Options = field.Options.Select(o => new JourneyFormOption { Value = o.Value, Label = o.Label }).ToList();
                if (type == "select") described.Placeholder ??= T("dynamic_form_select_default");
                break;

            case "email":
                described.Autocomplete = "email";
                break;

            case "tel":
                described.Autocomplete = "tel";
                if (field.PhoneCountries is { Count: > 0 })
                {
                    described.Countries = field.PhoneCountries
                        .Select(c => new JourneyFormCountry { Code = c.Code, Name = c.Name, DialCode = c.DialCode, Flag = c.Flag })
                        .ToList();
                    described.Country = field.PhoneCountry;
                    described.Value = field.PhoneNational ?? field.Value;
                }
                break;

            case "file":
                // The value is the uploads already kept for the field, which the server holds on to; only new files are posted.
                var kept = string.IsNullOrEmpty(field.Value) ? 0 : field.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
                described.Value = null;
                described.Required = field.Required && kept == 0;
                described.Accept = NullIfEmpty(field.FileAccept);
                described.MaxFiles = field.MaxFiles ?? 1;
                described.MaxFileSizeMb = field.MaxFileSizeMb;
                described.Capture = field.FileCapture ? "environment" : null;
                var hint = T("journey_upload_limits", field.MaxFileSizeMb ?? 0, field.MaxFiles ?? 1);
                described.Hint = kept > 0 ? $"{hint} {T("journey_upload_kept", kept)}" : hint;
                break;
        }

        return described;
    }
}
