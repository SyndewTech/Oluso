using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles dynamic form-based data collection steps (also known as Claims Collection).
/// Allows defining custom fields for collecting user information during the journey.
/// </summary>
/// <remarks>
/// Configuration options:
/// - viewName: custom view name (default: "Journey/_DynamicForm")
/// - title: form title
/// - description: form description
/// - fields: array of field definitions
/// - submitButtonText: submit button label
/// - cancelButtonText: cancel button label
/// - allowCancel: show cancel button (default: false)
///
/// Field types: text, email, phone, number, date, textarea, select, radio, checkbox, hidden
/// </remarks>
public class DynamicFormStepHandler : IStepHandler
{
    public string StepType => "dynamic_form";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<DynamicFormStepHandler>>();

        var config = ParseConfiguration(context);
        var culture = GetCurrentCulture(context);

        // Check if user submitted the form
        if (!string.IsNullOrEmpty(context.GetInput("__submitted")))
        {
            var validationErrors = ValidateInput(context.UserInput, config.Fields, culture);

            if (validationErrors.Count > 0)
            {
                return StepHandlerResult.ShowUi(config.ViewName, new DynamicFormViewModel
                {
                    Title = GetLocalizedText(config.Title, config.LocalizedTitles, culture),
                    Description = GetLocalizedText(config.Description, config.LocalizedDescriptions, culture),
                    Fields = BuildFieldViewModels(config.Fields, context.UserInput, validationErrors, culture),
                    SubmitButtonText = GetLocalizedText(config.SubmitButtonText, config.LocalizedSubmitButtonTexts, culture) ?? "Continue",
                    CancelButtonText = GetLocalizedText(config.CancelButtonText, config.LocalizedCancelButtonTexts, culture),
                    ShowCancel = config.AllowCancel
                });
            }

            // Store collected values in journey data
            var outputData = new Dictionary<string, object>();
            foreach (var field in config.Fields)
            {
                if (context.UserInput.TryGetValue(field.Name, out var value))
                {
                    var stringValue = value?.ToString();
                    if (!string.IsNullOrEmpty(stringValue))
                    {
                        var dataKey = field.ClaimType ?? field.Name;
                        context.SetData(dataKey, stringValue);
                        outputData[dataKey] = stringValue;
                    }
                }
            }

            // Run pre-completion validators (e.g., duplicate check)
            var validationError = await context.ValidateBeforeCompletionAsync(outputData, cancellationToken);
            if (validationError != null)
            {
                logger.LogInformation("Pre-completion validation failed: {Error}", validationError);
                return StepHandlerResult.ShowUi(config.ViewName, new DynamicFormViewModel
                {
                    Title = GetLocalizedText(config.Title, config.LocalizedTitles, culture),
                    Description = GetLocalizedText(config.Description, config.LocalizedDescriptions, culture),
                    Fields = BuildFieldViewModels(config.Fields, context.UserInput, null, culture),
                    SubmitButtonText = GetLocalizedText(config.SubmitButtonText, config.LocalizedSubmitButtonTexts, culture) ?? "Continue",
                    CancelButtonText = GetLocalizedText(config.CancelButtonText, config.LocalizedCancelButtonTexts, culture),
                    ShowCancel = config.AllowCancel,
                    FormError = validationError
                });
            }

            logger.LogInformation("Dynamic form collected {Count} values", outputData.Count);

            return StepHandlerResult.Success(outputData);
        }

        // Handle cancel
        if (!string.IsNullOrEmpty(context.GetInput("__cancel")) && config.AllowCancel)
        {
            return StepHandlerResult.Fail("user_cancelled", "User cancelled the form");
        }

        // Show the form
        var viewModel = new DynamicFormViewModel
        {
            Title = GetLocalizedText(config.Title, config.LocalizedTitles, culture),
            Description = GetLocalizedText(config.Description, config.LocalizedDescriptions, culture),
            Fields = BuildFieldViewModels(config.Fields, null, null, culture),
            SubmitButtonText = GetLocalizedText(config.SubmitButtonText, config.LocalizedSubmitButtonTexts, culture) ?? "Continue",
            CancelButtonText = GetLocalizedText(config.CancelButtonText, config.LocalizedCancelButtonTexts, culture),
            ShowCancel = config.AllowCancel
        };

        // Pre-populate with existing data if available
        foreach (var field in viewModel.Fields)
        {
            var existingValue = context.GetData<string>(field.ClaimType ?? field.Name);
            if (!string.IsNullOrEmpty(existingValue))
            {
                field.Value = existingValue;
            }
        }

        return StepHandlerResult.ShowUi(config.ViewName, viewModel);
    }

    private DynamicFormConfig ParseConfiguration(StepExecutionContext context)
    {
        var config = new DynamicFormConfig
        {
            ViewName = context.GetConfig("viewName", "Journey/_DynamicForm"),
            Title = context.GetConfig<string?>("title", null),
            Description = context.GetConfig<string?>("description", null),
            SubmitButtonText = context.GetConfig<string?>("submitButtonText", null),
            CancelButtonText = context.GetConfig<string?>("cancelButtonText", null),
            AllowCancel = context.GetConfig("allowCancel", false),
            LocalizedTitles = context.GetConfig<Dictionary<string, string>?>("localizedTitles", null),
            LocalizedDescriptions = context.GetConfig<Dictionary<string, string>?>("localizedDescriptions", null),
            LocalizedSubmitButtonTexts = context.GetConfig<Dictionary<string, string>?>("localizedSubmitButtonTexts", null),
            LocalizedCancelButtonTexts = context.GetConfig<Dictionary<string, string>?>("localizedCancelButtonTexts", null)
        };

        // Parse fields
        var fieldsConfig = context.GetConfig<List<JsonElement>?>("fields", null);
        if (fieldsConfig != null)
        {
            foreach (var fieldElement in fieldsConfig)
            {
                var field = ParseField(fieldElement);
                if (field != null)
                {
                    config.Fields.Add(field);
                }
            }
        }

        return config;
    }

    private static FormFieldConfig? ParseField(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var field = new FormFieldConfig
        {
            Name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
            Type = element.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "text" : "text",
            Label = element.TryGetProperty("label", out var labelProp) ? labelProp.GetString() : null,
            Placeholder = element.TryGetProperty("placeholder", out var placeholderProp) ? placeholderProp.GetString() : null,
            Description = element.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
            Required = element.TryGetProperty("required", out var requiredProp) && requiredProp.GetBoolean(),
            ClaimType = element.TryGetProperty("claimType", out var claimProp) ? claimProp.GetString() : null,
            DefaultValue = element.TryGetProperty("defaultValue", out var defaultProp) ? defaultProp.GetString() : null,
            Pattern = element.TryGetProperty("pattern", out var patternProp) ? patternProp.GetString() : null,
            PatternError = element.TryGetProperty("patternError", out var patternErrProp) ? patternErrProp.GetString() : null,
            MinLength = element.TryGetProperty("minLength", out var minLenProp) ? minLenProp.GetInt32() : null,
            MaxLength = element.TryGetProperty("maxLength", out var maxLenProp) ? maxLenProp.GetInt32() : null,
            Min = element.TryGetProperty("min", out var minProp) ? minProp.GetString() : null,
            Max = element.TryGetProperty("max", out var maxProp) ? maxProp.GetString() : null,
            Rows = element.TryGetProperty("rows", out var rowsProp) ? rowsProp.GetInt32() : null,
            ReadOnly = element.TryGetProperty("readOnly", out var readOnlyProp) && readOnlyProp.GetBoolean(),
            Hidden = element.TryGetProperty("hidden", out var hiddenProp) && hiddenProp.GetBoolean(),
            Group = element.TryGetProperty("group", out var groupProp) ? groupProp.GetString() : null
        };

        // Parse options for select/radio/checkbox fields
        if (element.TryGetProperty("options", out var optionsProp) && optionsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var optionElement in optionsProp.EnumerateArray())
            {
                if (optionElement.ValueKind == JsonValueKind.String)
                {
                    var value = optionElement.GetString() ?? "";
                    field.Options.Add(new FormFieldOption { Value = value, Label = value });
                }
                else if (optionElement.ValueKind == JsonValueKind.Object)
                {
                    field.Options.Add(new FormFieldOption
                    {
                        Value = optionElement.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "",
                        Label = optionElement.TryGetProperty("label", out var l) ? l.GetString() ?? "" : ""
                    });
                }
            }
        }

        // Parse conditional visibility
        if (element.TryGetProperty("showWhen", out var showWhenProp) && showWhenProp.ValueKind == JsonValueKind.Object)
        {
            field.ShowWhen = new FormFieldCondition
            {
                Field = showWhenProp.TryGetProperty("field", out var f) ? f.GetString() ?? "" : "",
                Operator = showWhenProp.TryGetProperty("operator", out var op) ? op.GetString() ?? "equals" : "equals",
                Value = showWhenProp.TryGetProperty("value", out var val) ? val.GetString() ?? "" : ""
            };
        }

        return field;
    }

    private static Dictionary<string, string> ValidateInput(
        IDictionary<string, object?> input,
        List<FormFieldConfig> fields,
        string culture)
    {
        var errors = new Dictionary<string, string>();

        foreach (var field in fields)
        {
            if (field.Hidden || field.ReadOnly)
                continue;

            var value = input.TryGetValue(field.Name, out var v) ? v?.ToString() : null;

            // Required validation
            if (field.Required && string.IsNullOrWhiteSpace(value))
            {
                errors[field.Name] = $"{field.Label ?? field.Name} is required";
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Min/Max length
            if (field.MinLength.HasValue && value.Length < field.MinLength.Value)
            {
                errors[field.Name] = $"Must be at least {field.MinLength} characters";
                continue;
            }

            if (field.MaxLength.HasValue && value.Length > field.MaxLength.Value)
            {
                errors[field.Name] = $"Must be at most {field.MaxLength} characters";
                continue;
            }

            // Pattern validation
            if (!string.IsNullOrEmpty(field.Pattern))
            {
                try
                {
                    if (!Regex.IsMatch(value, field.Pattern))
                    {
                        errors[field.Name] = field.PatternError ?? "Invalid format";
                        continue;
                    }
                }
                catch
                {
                    // Invalid regex - skip
                }
            }

            // Type-specific validation
            switch (field.Type.ToLowerInvariant())
            {
                case "email":
                    if (!IsValidEmail(value))
                        errors[field.Name] = "Invalid email address";
                    break;

                case "number":
                    if (!double.TryParse(value, out var numValue))
                    {
                        errors[field.Name] = "Must be a valid number";
                    }
                    else
                    {
                        if (field.Min != null && double.TryParse(field.Min, out var min) && numValue < min)
                            errors[field.Name] = $"Must be at least {min}";
                        else if (field.Max != null && double.TryParse(field.Max, out var max) && numValue > max)
                            errors[field.Name] = $"Must be at most {max}";
                    }
                    break;

                case "date":
                    if (!DateTime.TryParse(value, out _))
                        errors[field.Name] = "Invalid date";
                    break;

                case "tel":
                case "phone":
                    if (!Regex.IsMatch(value, @"^[\d\s\-\+\(\)]+$"))
                        errors[field.Name] = "Invalid phone number";
                    break;

                case "url":
                    if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                        errors[field.Name] = "Invalid URL";
                    break;
            }
        }

        return errors;
    }

    private static List<DynamicFormFieldViewModel> BuildFieldViewModels(
        List<FormFieldConfig> fields,
        IDictionary<string, object?>? values,
        Dictionary<string, string>? errors,
        string culture)
    {
        return fields.Select(f => new DynamicFormFieldViewModel
        {
            Name = f.Name,
            Type = f.Type,
            Label = f.Label ?? f.Name,
            Placeholder = f.Placeholder,
            Description = f.Description,
            Required = f.Required,
            ClaimType = f.ClaimType,
            Value = values?.TryGetValue(f.Name, out var v) == true ? v?.ToString() : f.DefaultValue,
            Error = errors?.TryGetValue(f.Name, out var e) == true ? e : null,
            Options = f.Options.Select(o => new DynamicFormOptionViewModel
            {
                Value = o.Value,
                Label = o.Label ?? o.Value
            }).ToList(),
            Pattern = f.Pattern,
            MinLength = f.MinLength,
            MaxLength = f.MaxLength,
            Min = f.Min,
            Max = f.Max,
            Rows = f.Rows,
            ReadOnly = f.ReadOnly,
            Hidden = f.Hidden,
            Group = f.Group,
            ShowWhen = f.ShowWhen != null ? new DynamicFormConditionViewModel
            {
                Field = f.ShowWhen.Field,
                Operator = f.ShowWhen.Operator,
                Value = f.ShowWhen.Value
            } : null
        }).ToList();
    }

    private static string GetCurrentCulture(StepExecutionContext context)
    {
        return CultureInfo.CurrentCulture.Name ?? "en";
    }

    private static string? GetLocalizedText(
        string? defaultText,
        Dictionary<string, string>? localizations,
        string culture)
    {
        if (localizations == null || string.IsNullOrEmpty(culture))
            return defaultText;

        if (localizations.TryGetValue(culture, out var localized))
            return localized;

        if (culture.Contains('-'))
        {
            var baseCulture = culture.Split('-')[0];
            if (localizations.TryGetValue(baseCulture, out localized))
                return localized;
        }

        return defaultText;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}

#region Configuration Models

internal class DynamicFormConfig
{
    public string ViewName { get; set; } = "Journey/_DynamicForm";
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? SubmitButtonText { get; set; }
    public string? CancelButtonText { get; set; }
    public bool AllowCancel { get; set; }
    public List<FormFieldConfig> Fields { get; set; } = new();
    public Dictionary<string, string>? LocalizedTitles { get; set; }
    public Dictionary<string, string>? LocalizedDescriptions { get; set; }
    public Dictionary<string, string>? LocalizedSubmitButtonTexts { get; set; }
    public Dictionary<string, string>? LocalizedCancelButtonTexts { get; set; }
}

internal class FormFieldConfig
{
    public string Name { get; set; } = null!;
    public string Type { get; set; } = "text";
    public string? Label { get; set; }
    public string? Placeholder { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
    public string? ClaimType { get; set; }
    public string? DefaultValue { get; set; }
    public string? Pattern { get; set; }
    public string? PatternError { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }
    public int? Rows { get; set; }
    public bool ReadOnly { get; set; }
    public bool Hidden { get; set; }
    public string? Group { get; set; }
    public List<FormFieldOption> Options { get; set; } = new();
    public FormFieldCondition? ShowWhen { get; set; }
}

internal class FormFieldOption
{
    public string Value { get; set; } = null!;
    public string Label { get; set; } = null!;
}

internal class FormFieldCondition
{
    public string Field { get; set; } = null!;
    public string Operator { get; set; } = "equals";
    public string Value { get; set; } = null!;
}

#endregion

#region View Models

public class DynamicFormViewModel
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<DynamicFormFieldViewModel> Fields { get; set; } = new();
    public string SubmitButtonText { get; set; } = "Continue";
    public string? CancelButtonText { get; set; }
    public bool ShowCancel { get; set; }
    /// <summary>
    /// Global form-level error message (e.g., duplicate submission)
    /// </summary>
    public string? FormError { get; set; }
}

public class DynamicFormFieldViewModel
{
    public string Name { get; set; } = null!;
    public string Type { get; set; } = "text";
    public string Label { get; set; } = null!;
    public string? Placeholder { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
    public string? ClaimType { get; set; }
    public string? Value { get; set; }
    public string? Error { get; set; }
    public List<DynamicFormOptionViewModel> Options { get; set; } = new();
    public string? Pattern { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }
    public int? Rows { get; set; }
    public bool ReadOnly { get; set; }
    public bool Hidden { get; set; }
    public string? Group { get; set; }
    public DynamicFormConditionViewModel? ShowWhen { get; set; }
}

public class DynamicFormOptionViewModel
{
    public string Value { get; set; } = null!;
    public string Label { get; set; } = null!;
}

public class DynamicFormConditionViewModel
{
    public string Field { get; set; } = null!;
    public string Operator { get; set; } = "equals";
    public string Value { get; set; } = null!;
}

#endregion
