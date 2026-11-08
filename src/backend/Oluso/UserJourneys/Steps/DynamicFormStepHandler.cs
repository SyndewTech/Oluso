using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Oluso.Core.Conditions;
using Oluso.Core.Locale;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;
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
/// Field types: text, email, phone, number, date, textarea, select, radio, checkbox, hidden, file
/// <para>File fields accept the kinds in fileTypes (pdf, jpeg, png, gif, webp, heic; checked by
/// content), up to maxFileSizeMb each and maxFiles in all, and store them as attachments of
/// the journey; the field's value is the comma-separated attachment ids. capture asks
/// phones to open the camera.</para>
/// </remarks>
public class DynamicFormStepHandler : IStepHandler
{
    public string StepType => "dynamic_form";

    public const string FileFieldType = "file";
    public static readonly string[] DefaultFileTypes = { FileContentSniffer.Pdf, FileContentSniffer.Jpeg, FileContentSniffer.Png };
    public const int DefaultMaxFileSizeMb = 10;
    public const int DefaultMaxFiles = 1;

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var fields = configuration is not null && configuration.TryGetValue("fields", out var raw) ? raw : null;
        if (!StepConfigurationHelpers.IsNonEmptyList(fields))
        {
            return Task.FromResult(StepConfigurationValidationResult.Invalid("At least one form field is required"));
        }
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<DynamicFormStepHandler>>();

        var config = ParseConfiguration(context);
        var culture = GetCurrentCulture(context);
        var input = NormalizePhoneInput(context.UserInput, config.Fields, out var phoneErrors);

        // Check if user submitted the form
        if (!string.IsNullOrEmpty(context.GetInput("__submitted")))
        {
            var validationErrors = ValidateInput(input, config.Fields, culture);
            foreach (var phoneError in phoneErrors)
            {
                validationErrors[phoneError.Key] = phoneError.Value;
            }

            var uploads = await ValidateFilesAsync(context, config.Fields, input, validationErrors, cancellationToken);

            if (validationErrors.Count > 0)
            {
                return StepHandlerResult.ShowUi(config.ViewName, new DynamicFormViewModel
                {
                    Title = GetLocalizedText(config.Title, config.LocalizedTitles, culture),
                    Description = GetLocalizedText(config.Description, config.LocalizedDescriptions, culture),
                    Fields = BuildFieldViewModels(config.Fields, input, validationErrors, culture),
                    SubmitButtonText = GetLocalizedText(config.SubmitButtonText, config.LocalizedSubmitButtonTexts, culture) ?? "Continue",
                    CancelButtonText = GetLocalizedText(config.CancelButtonText, config.LocalizedCancelButtonTexts, culture),
                    ShowCancel = config.AllowCancel
                });
            }

            // Store collected values in journey data
            var outputData = new Dictionary<string, object>();
            foreach (var field in config.Fields)
            {
                // Answers to fields the user couldn't see (their condition isn't met) aren't kept.
                if (input.TryGetValue(field.Name, out var value) && IsShown(field, input))
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
            if (validationError == null && uploads.Count > 0)
            {
                await SaveFilesAsync(context, config.Fields, uploads, outputData, cancellationToken);
            }
            if (validationError != null)
            {
                logger.LogInformation("Pre-completion validation failed: {Error}", validationError);
                return StepHandlerResult.ShowUi(config.ViewName, new DynamicFormViewModel
                {
                    Title = GetLocalizedText(config.Title, config.LocalizedTitles, culture),
                    Description = GetLocalizedText(config.Description, config.LocalizedDescriptions, culture),
                    Fields = BuildFieldViewModels(config.Fields, input, null, culture),
                    SubmitButtonText = GetLocalizedText(config.SubmitButtonText, config.LocalizedSubmitButtonTexts, culture) ?? "Continue",
                    CancelButtonText = GetLocalizedText(config.CancelButtonText, config.LocalizedCancelButtonTexts, culture),
                    ShowCancel = config.AllowCancel,
                    FormError = validationError
                });
            }

            // Persist fields marked with persistToUser to user properties
            var fieldsToSave = config.Fields.Where(f => f.PersistToUser && IsShown(f, input)).ToList();
            if (fieldsToSave.Any() && string.IsNullOrEmpty(context.UserId))
            {
                logger.LogInformation(
                    "Form step {StepId} has fields to save to the user, but no one is signed in yet; they stay in the journey",
                    context.StepId);
            }
            if (fieldsToSave.Any() && !string.IsNullOrEmpty(context.UserId))
            {
                var userService = context.ServiceProvider.GetService<IOlusoUserService>();
                if (userService != null)
                {
                    // Seed customProperties from the user's existing map so
                    // we merge rather than replace — UpdateUserAsync swaps
                    // the entire bag when CustomProperties is non-null.
                    var existingUser = await userService.FindByIdAsync(context.UserId, cancellationToken);
                    string? firstName = null, lastName = null, phoneNumber = null, picture = null;
                    var customProperties = existingUser?.CustomProperties != null
                        ? new Dictionary<string, string>(existingUser.CustomProperties, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var updatedFields = new List<string>();

                    foreach (var field in fieldsToSave)
                    {
                        if (input.TryGetValue(field.Name, out var value))
                        {
                            var stringValue = value?.ToString();
                            if (!string.IsNullOrEmpty(stringValue))
                            {
                                var propertyKey = field.UserPropertyName ?? field.ClaimType ?? field.Name;

                                // Map to standard user properties if matching known fields
                                switch (propertyKey.ToLowerInvariant())
                                {
                                    case "firstname":
                                    case "given_name":
                                    case "givenname":
                                        firstName = stringValue;
                                        updatedFields.Add("FirstName");
                                        break;
                                    case "lastname":
                                    case "family_name":
                                    case "familyname":
                                        lastName = stringValue;
                                        updatedFields.Add("LastName");
                                        break;
                                    case "phonenumber":
                                    case "phone":
                                    case "phone_number":
                                        phoneNumber = stringValue;
                                        updatedFields.Add("PhoneNumber");
                                        break;
                                    case "picture":
                                    case "photo":
                                    case "avatar":
                                        picture = stringValue;
                                        updatedFields.Add("Picture");
                                        break;
                                    default:
                                        // Store as custom property
                                        customProperties[propertyKey] = stringValue;
                                        updatedFields.Add($"Custom:{propertyKey}");
                                        break;
                                }
                            }
                        }
                    }

                    if (updatedFields.Any())
                    {
                        var updateRequest = new UpdateUserRequest
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            PhoneNumber = phoneNumber,
                            Picture = picture,
                            CustomProperties = customProperties.Any() ? customProperties : null
                        };

                        var updateResult = await userService.UpdateUserAsync(
                            context.UserId,
                            updateRequest,
                            cancellationToken);

                        if (updateResult.Succeeded)
                        {
                            logger.LogInformation(
                                "Persisted {Count} properties to user {UserId}: {Fields}",
                                updatedFields.Count, context.UserId, string.Join(", ", updatedFields));
                        }
                        else
                        {
                            logger.LogWarning(
                                "Failed to persist properties to user {UserId}: {Error}",
                                context.UserId, updateResult.ErrorDescription);
                            return StepHandlerResult.ShowUi(config.ViewName, new DynamicFormViewModel
                            {
                                Title = GetLocalizedText(config.Title, config.LocalizedTitles, culture),
                                Description = GetLocalizedText(config.Description, config.LocalizedDescriptions, culture),
                                Fields = BuildFieldViewModels(config.Fields, input, null, culture),
                                SubmitButtonText = GetLocalizedText(config.SubmitButtonText, config.LocalizedSubmitButtonTexts, culture) ?? "Continue",
                                CancelButtonText = GetLocalizedText(config.CancelButtonText, config.LocalizedCancelButtonTexts, culture),
                                ShowCancel = config.AllowCancel,
                                FormError = updateResult.MessageOr("We couldn't save your details. Please try again.")
                            });
                        }
                    }
                }
            }

            logger.LogInformation("Dynamic form collected {Count} values", outputData.Count);

            return StepHandlerResult.Success(outputData);
        }

        // Handle cancel
        if (!string.IsNullOrEmpty(context.GetInput("__cancel")) && config.AllowCancel)
        {
            return StepHandlerResult.Fail("user_cancelled", "User cancelled the form");
        }

        // Lock source-driven values (query / session) into the journey
        // data bag on first render so they survive step transitions and
        // any post submission missing the hidden input.
        foreach (var fieldConfig in config.Fields.Where(IsSourceDriven))
        {
            var dataKey = fieldConfig.ClaimType ?? fieldConfig.Name;
            if (!string.IsNullOrEmpty(context.GetData<string>(dataKey))) continue;
            var resolved = ResolveSourceValue(fieldConfig, context);
            if (!string.IsNullOrEmpty(resolved))
            {
                context.SetData(dataKey, resolved);
            }
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

    private static bool IsSourceDriven(FormFieldConfig f) =>
        !string.IsNullOrEmpty(f.Source) &&
        !string.Equals(f.Source, "user_input", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveSourceValue(FormFieldConfig field, StepExecutionContext context)
    {
        var key = string.IsNullOrEmpty(field.SourceKey) ? field.Name : field.SourceKey!;
        return field.Source?.ToLowerInvariant() switch
        {
            "query" => context.GetInput(key),
            "session" or "cookie" => context.GetData<string>(key),
            _ => null,
        };
    }

    private DynamicFormConfig ParseConfiguration(StepExecutionContext context)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<DynamicFormStepHandler>>();

        var config = new DynamicFormConfig
        {
            ViewName = context.GetConfig("viewName", "Journey/_DynamicForm")!,
            Title = context.GetLocalizedConfig("title"),
            Description = context.GetLocalizedConfig("description"),
            SubmitButtonText = context.GetLocalizedConfig("submitButtonText"),
            CancelButtonText = context.GetLocalizedConfig("cancelButtonText"),
            AllowCancel = context.GetConfig("allowCancel", false)
        };

        // Parse fields
        var fieldsConfig = context.GetConfig<List<JsonElement>?>("fields", null);
        if (fieldsConfig != null)
        {
            foreach (var fieldElement in fieldsConfig)
            {
                var field = ParseField(fieldElement, context.ResolvedLocale);
                if (field != null)
                {
                    config.Fields.Add(field);
                }
            }
        }

        // Validate no duplicate user property names for persistToUser fields
        var persistFields = config.Fields.Where(f => f.PersistToUser).ToList();
        if (persistFields.Count > 0)
        {
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in persistFields)
            {
                var propertyKey = field.UserPropertyName ?? field.ClaimType ?? field.Name;
                if (!propertyNames.Add(propertyKey))
                {
                    logger.LogWarning(
                        "Duplicate user property name '{PropertyKey}' detected in dynamic form config. " +
                        "Field '{FieldName}' will overwrite previous value.",
                        propertyKey, field.Name);
                }
            }
        }

        return config;
    }

    /// <summary>
    /// A form described in the same shape as this step's settings (title, description,
    /// submitButtonText, fields), for other steps (e.g. plugins) that need input.
    /// </summary>
    public static DynamicFormViewModel FromDefinition(JsonElement definition, string locale, IDictionary<string, object>? values = null)
    {
        var fields = definition.TryGetPropertyIgnoreCase("fields", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(f => ParseField(f, locale)).OfType<FormFieldConfig>().ToList()
            : [];

        return new DynamicFormViewModel
        {
            Title = ResolveLocalizedProp(definition, "title", locale),
            Description = ResolveLocalizedProp(definition, "description", locale),
            Fields = BuildFieldViewModels(fields, values, null, locale),
            SubmitButtonText = ResolveLocalizedProp(definition, "submitButtonText", locale) ?? "Continue",
            FormError = definition.GetStringSetting("error"),
        };
    }

    private static FormFieldConfig? ParseField(JsonElement element, string locale)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var field = new FormFieldConfig
        {
            Name = element.GetStringSetting("name") ?? "",
            Type = element.GetStringSetting("type") ?? "text",
            Label = ResolveLocalizedProp(element, "label", locale),
            Placeholder = ResolveLocalizedProp(element, "placeholder", locale),
            Description = ResolveLocalizedProp(element, "description", locale),
            Required = element.GetBoolSetting("required"),
            // Blank means unset: the field's own name is used.
            ClaimType = Blank(element.GetStringSetting("claimType")),
            DefaultValue = element.GetStringSetting("defaultValue"),
            Pattern = element.GetStringSetting("pattern"),
            PatternError = element.GetStringSetting("patternError"),
            MinLength = element.GetIntSetting("minLength"),
            MaxLength = element.GetIntSetting("maxLength"),
            Min = element.GetStringSetting("min"),
            Max = element.GetStringSetting("max"),
            Rows = element.GetIntSetting("rows"),
            ReadOnly = element.GetBoolSetting("readOnly"),
            Hidden = element.GetBoolSetting("hidden"),
            Group = element.GetStringSetting("group"),
            PersistToUser = element.GetBoolSetting("persistToUser"),
            UserPropertyName = Blank(element.GetStringSetting("userPropertyName")),
            Source = element.GetStringSetting("source"),
            SourceKey = element.GetStringSetting("sourceKey"),
            PhoneFormat = element.GetStringSetting("phoneFormat"),
            PhoneDefaultCountry = element.GetStringSetting("phoneDefaultCountry"),
            MaxFileSizeMb = element.GetIntSetting("maxFileSizeMb"),
            MaxFiles = element.GetIntSetting("maxFiles"),
            Capture = element.GetBoolSetting("capture")
        };

        if (element.TryGetPropertyIgnoreCase("fileTypes", out var fileTypes) && fileTypes.ValueKind == JsonValueKind.Array)
        {
            field.FileTypes = fileTypes.EnumerateArray()
                .Select(JsonLookup.AsString)
                .Where(t => !string.IsNullOrWhiteSpace(t) && FileContentSniffer.Types.ContainsKey(t!))
                .Select(t => t!.ToLowerInvariant())
                .Distinct()
                .ToList();
        }

        // Parse options for select/radio/checkbox fields
        if (element.TryGetPropertyIgnoreCase("options", out var optionsProp) && optionsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var optionElement in optionsProp.EnumerateArray())
            {
                if (optionElement.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    var value = JsonLookup.AsString(optionElement) ?? "";
                    field.Options.Add(new FormFieldOption { Value = value, Label = value });
                }
                else if (optionElement.ValueKind == JsonValueKind.Object)
                {
                    field.Options.Add(new FormFieldOption
                    {
                        Value = optionElement.GetStringSetting("value") ?? "",
                        Label = ResolveLocalizedProp(optionElement, "label", locale) ?? ""
                    });
                }
            }
        }

        // Parse conditional visibility
        if (element.TryGetPropertyIgnoreCase("showWhen", out var showWhenProp) && showWhenProp.ValueKind == JsonValueKind.Object)
        {
            field.ShowWhen = new FormFieldCondition
            {
                Field = showWhenProp.GetStringSetting("field") ?? "",
                Operator = showWhenProp.GetStringSetting("operator") ?? "equals",
                Value = showWhenProp.GetStringSetting("value") ?? ""
            };
        }

        return field;
    }

    /// <summary>Whether a field's showWhen condition is met by the answers given (always, without one).</summary>
    private static bool IsShown(FormFieldConfig field, IDictionary<string, object> input)
    {
        if (field.ShowWhen == null || string.IsNullOrEmpty(field.ShowWhen.Field)) return true;
        var answer = input.TryGetValue(field.ShowWhen.Field, out var value) ? value : null;
        return ConditionOperators.Evaluate(field.ShowWhen.Operator, ConditionOperators.ToValues(answer), field.ShowWhen.Value);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsFileField(FormFieldConfig field) =>
        field.Type.Equals(FileFieldType, StringComparison.OrdinalIgnoreCase);

    internal static JourneyUploadPolicy UploadPolicy(FormFieldConfig field) => new()
    {
        AllowedKinds = field.FileTypes is { Count: > 0 } kinds ? kinds : DefaultFileTypes,
        MaxBytes = (long)(field.MaxFileSizeMb is > 0 ? field.MaxFileSizeMb.Value : DefaultMaxFileSizeMb) * 1024 * 1024,
        MaxFiles = field.MaxFiles is > 0 ? field.MaxFiles.Value : DefaultMaxFiles,
    };

    /// <summary>
    /// Checks the files posted to each shown file field. A required field is met by new files
    /// or by files kept from an earlier submission in this journey.
    /// </summary>
    private static async Task<Dictionary<string, List<AcceptedJourneyFile>>> ValidateFilesAsync(
        StepExecutionContext context, List<FormFieldConfig> fields, IDictionary<string, object> input,
        Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        var accepted = new Dictionary<string, List<AcceptedJourneyFile>>(StringComparer.OrdinalIgnoreCase);
        var fileFields = fields.Where(f => IsFileField(f) && !f.Hidden && !f.ReadOnly && IsShown(f, input)).ToList();
        if (fileFields.Count == 0) return accepted;

        var uploads = context.ServiceProvider.GetRequiredService<IJourneyFileUploadService>();
        foreach (var field in fileFields)
        {
            var files = context.GetInputFiles(field.Name);
            if (files.Count == 0)
            {
                if (field.Required && string.IsNullOrEmpty(context.GetDataString(field.ClaimType ?? field.Name)))
                    errors[field.Name] = $"{field.Label ?? field.Name} is required";
                continue;
            }

            var policy = UploadPolicy(field);
            var ok = new List<AcceptedJourneyFile>();
            var error = await uploads.ValidateAsync(files, policy, ok, cancellationToken);
            if (error != null)
            {
                errors[field.Name] = UiStringDefaults.Resolve($"journey_upload_error_{error}",
                    policy.MaxBytes / (1024 * 1024), policy.MaxFiles);
                continue;
            }
            accepted[field.Name] = ok;
        }
        return accepted;
    }

    private static async Task SaveFilesAsync(
        StepExecutionContext context, List<FormFieldConfig> fields, Dictionary<string, List<AcceptedJourneyFile>> uploads,
        IDictionary<string, object> outputData, CancellationToken cancellationToken)
    {
        var service = context.ServiceProvider.GetRequiredService<IJourneyFileUploadService>();
        foreach (var field in fields.Where(f => uploads.ContainsKey(f.Name)))
        {
            var ids = new List<string>();
            foreach (var file in uploads[field.Name])
            {
                var saved = await service.SaveAsync(new JourneyUploadRequest
                {
                    TenantId = context.TenantId ?? throw new InvalidOperationException("File fields need an organisation"),
                    JourneyId = context.JourneyId,
                    UserId = context.UserId,
                    File = file,
                    EntityType = JourneyUploads.EntityType,
                    EntityId = context.JourneyId,
                    SubEntityType = "field",
                    SubEntityId = field.Name,
                }, cancellationToken);
                ids.Add(saved.Id);
            }

            var dataKey = field.ClaimType ?? field.Name;
            var value = string.Join(',', ids);
            context.SetData(dataKey, value);
            outputData[dataKey] = value;
        }
    }

    private static bool IsInternationalPhone(FormFieldConfig field) =>
        (field.Type.Equals("tel", StringComparison.OrdinalIgnoreCase) || field.Type.Equals("phone", StringComparison.OrdinalIgnoreCase))
        && PhoneNumberInput.IsInternational(field.PhoneFormat);

    /// <summary>
    /// Fold each international phone field's country + national inputs into one
    /// E.164 value under the field's own name, so validation, journey data, claims,
    /// and persisted user properties all see the canonical number.
    /// </summary>
    private static IDictionary<string, object> NormalizePhoneInput(
        IDictionary<string, object> userInput,
        List<FormFieldConfig> fields,
        out Dictionary<string, string> errors)
    {
        errors = new Dictionary<string, string>();
        var input = new Dictionary<string, object>(userInput);

        foreach (var field in fields.Where(IsInternationalPhone))
        {
            var national = input.TryGetValue(field.Name, out var raw) ? raw?.ToString() : null;
            if (string.IsNullOrWhiteSpace(national))
            {
                continue;
            }

            var country = input.TryGetValue(field.Name + PhoneNumberInput.CountryInputSuffix, out var c) ? c?.ToString() : null;
            if (PhoneNumberInput.TryCompose(country, national, out var e164, out var error))
            {
                input[field.Name] = e164;
            }
            else
            {
                errors[field.Name] = error;
            }
        }

        return input;
    }

    private static string? ResolvePhoneCountry(FormFieldConfig field, IDictionary<string, object>? values, string culture)
    {
        if (values != null && values.TryGetValue(field.Name + PhoneNumberInput.CountryInputSuffix, out var chosen)
            && SupportedCountries.GetByCode(chosen?.ToString()) is { } picked)
        {
            return picked.Code;
        }

        var current = values != null && values.TryGetValue(field.Name, out var v) ? v?.ToString() : field.DefaultValue;
        var (fromValue, _) = PhoneNumberInput.Split(current);
        if (fromValue != null)
        {
            return fromValue.Code;
        }

        return SupportedCountries.GetByCode(field.PhoneDefaultCountry)?.Code
            ?? SupportedCountries.FromCulture(culture)?.Code;
    }

    private static string? ResolvePhoneNational(FormFieldConfig field, IDictionary<string, object>? values)
    {
        var current = values != null && values.TryGetValue(field.Name, out var v) ? v?.ToString() : field.DefaultValue;
        var (country, national) = PhoneNumberInput.Split(current);
        return country != null ? national : current;
    }

    private static Dictionary<string, string> ValidateInput(
        IDictionary<string, object> input,
        List<FormFieldConfig> fields,
        string culture)
    {
        var errors = new Dictionary<string, string>();

        foreach (var field in fields)
        {
            // A field shown only under a condition isn't required while it's hidden.
            if (field.Hidden || field.ReadOnly || !IsShown(field, input) || IsFileField(field))
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
                    if (PhoneNumberInput.IsInternational(field.PhoneFormat))
                    {
                        if (!PhoneNumberInput.IsValidE164(value))
                            errors[field.Name] = "Enter a valid phone number for the selected country";
                    }
                    else if (!PhoneNumberInput.IsValidLocal(value))
                    {
                        errors[field.Name] = "Invalid phone number";
                    }
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
        IDictionary<string, object>? values,
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
            PhoneFormat = f.PhoneFormat,
            PhoneCountries = IsInternationalPhone(f) ? SupportedCountries.All : null,
            PhoneCountry = IsInternationalPhone(f) ? ResolvePhoneCountry(f, values, culture) : null,
            PhoneNational = IsInternationalPhone(f) ? ResolvePhoneNational(f, values) : null,
            FileAccept = IsFileField(f) ? UploadPolicy(f).AcceptAttribute : null,
            FileCapture = IsFileField(f) && f.Capture,
            MaxFiles = IsFileField(f) ? UploadPolicy(f).MaxFiles : null,
            MaxFileSizeMb = IsFileField(f) ? f.MaxFileSizeMb ?? DefaultMaxFileSizeMb : null,
            Group = f.Group,
            ShowWhen = f.ShowWhen != null ? new DynamicFormConditionViewModel
            {
                Field = f.ShowWhen.Field,
                // The page evaluates canonical operator names only.
                Operator = ConditionOperators.Normalize(f.ShowWhen.Operator) ?? "eq",
                Value = f.ShowWhen.Value
            } : null
        }).ToList();
    }

    private static string GetCurrentCulture(StepExecutionContext context)
    {
        return CultureInfo.CurrentCulture.Name ?? "en";
    }

    private static string? ResolveLocalizedProp(JsonElement element, string propertyName, string locale)
    {
        if (!element.TryGetPropertyIgnoreCase(propertyName, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
        if (prop.ValueKind == JsonValueKind.Object)
        {
            var ls = System.Text.Json.JsonSerializer.Deserialize<Oluso.Core.Locale.LocalizedString>(prop.GetRawText());
            return ls?.Resolve(locale);
        }
        return null;
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
    /// <summary>
    /// When true, the collected value will be saved to the user's CustomProperties
    /// </summary>
    public bool PersistToUser { get; set; }
    /// <summary>
    /// The property name to use when persisting to user. Defaults to ClaimType or Name.
    /// </summary>
    public string? UserPropertyName { get; set; }

    /// <summary>
    /// Where the field's value should be auto-populated from when the form
    /// is first rendered. <c>null</c> or <c>"user_input"</c> means the user
    /// fills it. <c>"query"</c> reads from the request query string,
    /// <c>"cookie"</c> from request cookies, <c>"session"</c> from the
    /// journey-state data bag. Auto-populated fields are typically rendered
    /// <see cref="Hidden"/>.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Key in the source bag — e.g. <c>"utm_campaign"</c> when <see cref="Source"/>
    /// is <c>"query"</c>. Falls back to <see cref="Name"/> when unset.
    /// </summary>
    public string? SourceKey { get; set; }

    /// <summary>
    /// Phone fields only: <c>"international"</c> (default) shows a country picker and
    /// stores E.164; <c>"local"</c> takes the number as dialled locally, unchanged.
    /// </summary>
    public string? PhoneFormat { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country preselected in the picker; defaults to the journey culture's region.</summary>
    public string? PhoneDefaultCountry { get; set; }

    /// <summary>File fields: <see cref="FileContentSniffer"/> kinds accepted.</summary>
    public List<string>? FileTypes { get; set; }

    /// <summary>File fields: largest file accepted, in megabytes.</summary>
    public int? MaxFileSizeMb { get; set; }

    /// <summary>File fields: most files accepted.</summary>
    public int? MaxFiles { get; set; }

    /// <summary>File fields: ask phones to open the rear camera.</summary>
    public bool Capture { get; set; }
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

    /// <summary>"international" (country picker + E.164) or "local"; phone fields only.</summary>
    public string? PhoneFormat { get; set; }
    /// <summary>Countries offered by the picker, when the field is an international phone.</summary>
    public IReadOnlyList<CountryInfo>? PhoneCountries { get; set; }
    /// <summary>Selected country code for the picker.</summary>
    public string? PhoneCountry { get; set; }
    /// <summary>The number without the country code, as typed.</summary>
    public string? PhoneNational { get; set; }

    /// <summary>File fields: the input's accept attribute.</summary>
    public string? FileAccept { get; set; }

    /// <summary>File fields: open the camera on phones.</summary>
    public bool FileCapture { get; set; }

    public int? MaxFiles { get; set; }
    public int? MaxFileSizeMb { get; set; }
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
