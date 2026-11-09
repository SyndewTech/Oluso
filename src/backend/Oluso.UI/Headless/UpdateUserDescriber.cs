using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>Journey/_UpdateUser: the profile fields the step edits, and the current password when it asks for it.</summary>
public sealed class UpdateUserDescriber : StepFormDescriber<UpdateUserViewModel>
{
    private static readonly HashSet<string> InputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "email", "password", "number", "date", "tel", "url"
    };

    public UpdateUserDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_UpdateUser"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, UpdateUserViewModel? model)
    {
        var fields = new List<JourneyFormField> { Hidden("_submitted", "true") };
        fields.AddRange((model?.Fields ?? []).Select(Field));

        if (model?.RequireCurrentPassword == true)
        {
            fields.Add(new JourneyFormField
            {
                Name = "currentPassword",
                Type = "password",
                Label = T("update_user_current_password_label"),
                Required = true,
                Autocomplete = "current-password",
                Error = NullIfEmpty(model.CurrentPasswordError)
            });
        }

        return new JourneyStepForm
        {
            Title = model?.Title ?? T("update_user_default_title"),
            Description = NullIfEmpty(model?.Description),
            Error = NullIfEmpty(model?.FormError),
            Fields = fields,
            Actions = [SubmitFields(model?.SubmitButtonText ?? T("update_user_button"))]
        };
    }

    private JourneyFormField Field(UpdateFieldViewModel field)
    {
        var described = new JourneyFormField
        {
            Name = field.Name,
            Label = field.Label,
            Placeholder = NullIfEmpty(field.Placeholder),
            Required = field.Required,
            Value = field.Value,
            Error = NullIfEmpty(field.Error)
        };

        if (field.Options?.Any() == true)
        {
            described.Type = "select";
            described.Placeholder = T("update_user_select_default");
            described.Options = field.Options.Select(o => new JourneyFormOption { Value = o.Value, Label = o.Label }).ToList();
        }
        else if (field.Type == "textarea")
        {
            described.Type = "textarea";
        }
        else
        {
            described.Type = InputTypes.Contains(field.Type) ? field.Type.ToLowerInvariant() : "text";
            described.Autocomplete = described.Type switch
            {
                "email" => "email",
                "tel" => "tel",
                _ => null
            };
        }

        return described;
    }
}
