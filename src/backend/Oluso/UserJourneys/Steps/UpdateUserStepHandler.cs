using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles user profile update during user journeys.
/// Can be used for progressive profiling, profile completion, or profile editing flows.
/// </summary>
public class UpdateUserStepHandler : IStepHandler
{
    private const string PendingEmailKey = "email_change_email";
    private const string CodeKey = "email_change_code";
    private const string SentAtKey = "email_change_sent_at";
    private const string AttemptsKey = "email_change_attempts";
    private const int CodeLength = 6;
    private const int MaxCodeAttempts = 3;

    public string StepType => "update_user";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<UpdateUserStepHandler>>();

        // Require authenticated user
        if (string.IsNullOrEmpty(context.UserId))
        {
            return StepHandlerResult.Fail("not_authenticated", "User must be authenticated to update profile");
        }

        var user = await userService.FindByIdAsync(context.UserId, cancellationToken);
        if (user == null)
        {
            return StepHandlerResult.Fail("user_not_found", "User not found");
        }

        // Resolve the field set. Precedence:
        //  1. Explicit `fields` config (full UpdateFieldConfig list)
        //  2. `editableFields` shorthand — names only, mapped to defaults
        //  3. Built-in defaults (firstName/lastName/email/phone)
        var fields = context.GetConfig<List<UpdateFieldConfig>>("fields", null);
        if (fields == null)
        {
            var editableNames = context.GetConfig<List<string>>("editableFields", null);
            fields = editableNames != null && editableNames.Count > 0
                ? editableNames.Select(BuildFieldConfigForName).ToList()
                : GetDefaultFields();
        }
        var requiredFields = fields.Where(f => f.Required).Select(f => f.Name).ToList();
        var requireCurrentPassword = context.GetConfig("requireCurrentPassword", false);

        // An email change waiting on proof of the new address: the code entry
        // screen owns the step until the change is confirmed or abandoned.
        var pendingEmail = context.GetData<string>(PendingEmailKey);
        if (!string.IsNullOrEmpty(pendingEmail))
        {
            return await ContinueEmailChangeAsync(
                context, userService, user, pendingEmail, fields, tenantContext, requireCurrentPassword, logger, cancellationToken);
        }

        // Check if form was submitted
        var submitted = context.GetInput("_submitted");
        if (submitted != "true")
        {
            // Show profile form with current values
            return ShowProfileForm(context, user, fields, tenantContext, null, requireCurrentPassword);
        }

        // When configured, the user must re-enter their current password
        // before any edit lands. Verifies via ValidateCredentialsAsync — if
        // the user has no local password (external-only), skip the check
        // (IOlusoUserService.HasPasswordAsync) since there's nothing to
        // verify against.
        if (requireCurrentPassword)
        {
            var hasPassword = await userService.HasPasswordAsync(user.Id, cancellationToken);
            if (hasPassword)
            {
                var currentPassword = context.GetInput("currentPassword");
                if (string.IsNullOrEmpty(currentPassword))
                {
                    return ShowProfileForm(context, user, fields, tenantContext,
                        new Dictionary<string, string> { ["currentPassword"] = "Please enter your current password to save changes" },
                        requireCurrentPassword);
                }

                var credCheck = await userService.ValidateCredentialsAsync(user.Username, currentPassword, tenantContext.TenantId, cancellationToken);
                if (!credCheck.CredentialsVerified)
                {
                    logger.LogWarning("Current-password check failed for user {UserId} on profile update", user.Id);
                    // Drop the rejected password from input before returning
                    // the form — avoids it sitting in memory across the
                    // re-render and forces the user to retype.
                    SensitiveInputs.Scrub(context.Input?.Values);
                    return ShowProfileForm(context, user, fields, tenantContext,
                        new Dictionary<string, string> { ["currentPassword"] = "Current password is incorrect" },
                        requireCurrentPassword);
                }
            }

            // Password validated and accepted — drop it from input so the
            // remainder of the handler (and any downstream observability)
            // can't surface it.
            SensitiveInputs.Scrub(context.Input?.Values);
        }

        // Validate required fields
        var validationErrors = new Dictionary<string, string>();
        foreach (var field in requiredFields)
        {
            var value = context.GetInput(field);
            if (string.IsNullOrEmpty(value))
            {
                validationErrors[field] = $"{field} is required";
            }
        }

        // Validate email format if provided
        var email = context.GetInput("email");
        if (!string.IsNullOrEmpty(email) && !IsValidEmail(email))
        {
            validationErrors["email"] = "Invalid email format";
        }

        // Validate phone format if provided
        var phone = context.GetInput("phone");
        if (!string.IsNullOrEmpty(phone) && !IsValidPhone(phone))
        {
            validationErrors["phone"] = "Invalid phone format";
        }

        if (validationErrors.Any())
        {
            return ShowProfileForm(context, user, fields, tenantContext, validationErrors, requireCurrentPassword);
        }

        // Build custom properties for fields. UpdateUserAsync replaces
        // the stored map when CustomProperties is non-null, so merge with
        // the user's existing properties before saving — otherwise this
        // step would wipe properties set by other handlers (terms
        // acceptance, sign-up claimMappings) when an unrelated profile
        // edit lands.
        var customProps = user.CustomProperties != null
            ? new Dictionary<string, string>(user.CustomProperties, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.Where(f => f.IsCustomClaim))
        {
            var value = context.GetInput(field.Name);
            if (!string.IsNullOrEmpty(value))
            {
                customProps[field.ClaimType ?? field.Name] = value;
            }
        }

        // A different mailbox waits for the user to prove they own it; the old
        // address stays on the account until then. A casing-only edit is the
        // same mailbox and applies directly.
        string? newEmail = null;
        string? emailToVerify = null;
        if (!string.IsNullOrEmpty(email) && email != user.Email)
        {
            var sameMailbox = string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase);
            if (!sameMailbox && context.GetConfig("requireEmailVerification", true))
            {
                var owner = await userService.FindByEmailAsync(email, cancellationToken);
                if (owner != null && owner.Id != user.Id)
                {
                    return ShowProfileForm(context, user, fields, tenantContext,
                        new Dictionary<string, string> { ["email"] = "This email address is already in use" },
                        requireCurrentPassword);
                }

                emailToVerify = email;
            }
            else
            {
                newEmail = email;
            }
        }

        // Build update request with available properties
        var updateRequest = new UpdateUserRequest
        {
            Email = newEmail,
            FirstName = context.GetInput("firstName") ?? user.FirstName,
            LastName = context.GetInput("lastName") ?? user.LastName,
            PhoneNumber = context.GetInput("phone") ?? user.PhoneNumber,
            Picture = context.GetInput("picture") ?? user.Picture,
            CustomProperties = customProps.Any() ? customProps : null
        };

        // Perform update
        var result = await userService.UpdateUserAsync(user.Id, updateRequest, cancellationToken);

        if (!result.Succeeded)
        {
            logger.LogError("Failed to update user {UserId}: {Error}", user.Id, result.Error);
            return ShowProfileForm(context, user, fields, tenantContext,
                new Dictionary<string, string> { ["_form"] = result.MessageOr("Failed to update profile") },
                requireCurrentPassword);
        }

        logger.LogInformation("User profile updated for {UserId}", user.Id);

        if (emailToVerify != null)
        {
            context.JourneyData.Remove(AttemptsKey);
            await SendEmailChangeCodeAsync(context, user, emailToVerify, logger, cancellationToken);
            return await ShowCodeEntryAsync(context, user, emailToVerify, cancellationToken);
        }

        return await CompleteAsync(context, userService, cancellationToken);
    }

    private static async Task<StepHandlerResult> CompleteAsync(
        StepExecutionContext context, IOlusoUserService userService, CancellationToken cancellationToken)
    {
        var user = await userService.FindByIdAsync(context.UserId!, cancellationToken);

        var outputData = new Dictionary<string, object>
        {
            ["profile_updated"] = true,
            ["updated_at"] = DateTime.UtcNow
        };

        if (user != null)
        {
            outputData["name"] = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim();
            if (!string.IsNullOrEmpty(user.FirstName))
                outputData["given_name"] = user.FirstName;
            if (!string.IsNullOrEmpty(user.LastName))
                outputData["family_name"] = user.LastName;
            if (!string.IsNullOrEmpty(user.Email))
            {
                outputData["email"] = user.Email;
                outputData["email_verified"] = user.EmailVerified;
            }
            if (!string.IsNullOrEmpty(user.PhoneNumber))
                outputData["phone_number"] = user.PhoneNumber;
        }

        return StepHandlerResult.Success(outputData);
    }

    /// <summary>
    /// The code-entry screen of a pending email change: verifies the code and
    /// then applies the new address as verified, resends, or abandons the change.
    /// </summary>
    private static async Task<StepHandlerResult> ContinueEmailChangeAsync(
        StepExecutionContext context,
        IOlusoUserService userService,
        OlusoUserInfo user,
        string pendingEmail,
        List<UpdateFieldConfig> fields,
        ITenantContext tenantContext,
        bool requireCurrentPassword,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(context.GetInput("change_email")))
        {
            ClearEmailChange(context);
            return ShowProfileForm(context, user, fields, tenantContext, null, requireCurrentPassword);
        }

        if (!string.IsNullOrEmpty(context.GetInput("resend")))
        {
            await SendEmailChangeCodeAsync(context, user, pendingEmail, logger, cancellationToken);
            return await ShowCodeEntryAsync(context, user, pendingEmail, cancellationToken,
                successMessage: "A new code has been sent to your email.");
        }

        var submittedCode = context.GetInput("code");
        if (string.IsNullOrEmpty(submittedCode))
        {
            return await ShowCodeEntryAsync(context, user, pendingEmail, cancellationToken);
        }

        var storedCode = context.GetData<string>(CodeKey);
        var sentAt = context.GetData<string>(SentAtKey);
        var lifetime = await CodeLifetimeAsync(context, user, cancellationToken);
        if (string.IsNullOrEmpty(storedCode)
            || !DateTime.TryParse(sentAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var sentTime)
            || DateTime.UtcNow - sentTime > lifetime)
        {
            return await ShowCodeEntryAsync(context, user, pendingEmail, cancellationToken,
                errorMessage: "Code has expired. Please request a new one.");
        }

        if (!EmailVerificationStepHandler.FixedTimeEqualsString(storedCode, submittedCode.Trim()))
        {
            var attempts = (context.GetData<int?>(AttemptsKey) ?? 0) + 1;
            context.SetData(AttemptsKey, attempts);
            if (attempts >= MaxCodeAttempts)
            {
                logger.LogWarning("Email change for user {UserId} abandoned after {Attempts} invalid codes", user.Id, attempts);
                ClearEmailChange(context);
                return StepHandlerResult.Fail("too_many_attempts", "Too many invalid attempts. Your email address was not changed.");
            }

            return await ShowCodeEntryAsync(context, user, pendingEmail, cancellationToken,
                errorMessage: $"Invalid code. {MaxCodeAttempts - attempts} attempt(s) remaining.");
        }

        ClearEmailChange(context);

        var result = await userService.UpdateUserAsync(user.Id, new UpdateUserRequest
        {
            Email = pendingEmail,
            EmailVerified = true
        }, cancellationToken);

        if (!result.Succeeded)
        {
            logger.LogError("Failed to apply verified email change for user {UserId}: {Error}", user.Id, result.Error);
            return ShowProfileForm(context, user, fields, tenantContext,
                new Dictionary<string, string> { ["email"] = result.MessageOr("Your email address could not be changed") },
                requireCurrentPassword);
        }

        logger.LogInformation("User {UserId} changed their email after verifying the new address", user.Id);
        return await CompleteAsync(context, userService, cancellationToken);
    }

    /// <summary>
    /// Issues a fresh code for <paramref name="email"/> and sends it there, the
    /// address being claimed — never the current one.
    /// </summary>
    private static async Task SendEmailChangeCodeAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        string email,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var code = EmailVerificationStepHandler.GenerateCode(CodeLength);
        context.SetData(PendingEmailKey, email);
        context.SetData(CodeKey, code);
        context.SetData(SentAtKey, DateTime.UtcNow.ToString("O"));

        var emailService = context.ServiceProvider.GetService<IEmailService>()
            ?? (IEmailSender?)context.ServiceProvider.GetService<IEmailSender>();
        if (emailService == null)
        {
            logger.LogError(
                "Email change for user {UserId} needs a verification code but no IEmailSender/IEmailService is registered",
                user.Id);
            return;
        }

        var lifetime = await CodeLifetimeAsync(context, user, cancellationToken);
        var (subject, body) = await EmailVerificationStepHandler.BuildMessageAsync(
            context.ServiceProvider.GetService<IPlatformEmailTemplateService>(), "otp", context, code,
            magicToken: string.Empty, (int)lifetime.TotalMinutes, email, customTemplateName: null, cancellationToken);

        var result = await emailService.SendAsync(email, subject, body, cancellationToken);
        if (!result.Success)
        {
            logger.LogError("Email change code for user {UserId} could not be sent: {Error}", user.Id, result.Error);
        }
    }

    private static async Task<StepHandlerResult> ShowCodeEntryAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        string pendingEmail,
        CancellationToken cancellationToken,
        string? errorMessage = null,
        string? successMessage = null)
    {
        var lifetime = await CodeLifetimeAsync(context, user, cancellationToken);
        return StepHandlerResult.ShowUi("Journey/_EmailVerificationConfirm", new EmailVerificationConfirmViewModel
        {
            Email = EmailVerificationStepHandler.MaskEmail(pendingEmail),
            Mode = "otp",
            ExpirationMinutes = (int)lifetime.TotalMinutes,
            CodeLength = CodeLength,
            ErrorMessage = errorMessage,
            SuccessMessage = successMessage,
            JourneyId = context.JourneyId,
            CurrentStepId = context.StepId
        });
    }

    /// <summary>The account's tenant decides how long an emailed code stays valid.</summary>
    private static async Task<TimeSpan> CodeLifetimeAsync(
        StepExecutionContext context, OlusoUserInfo user, CancellationToken cancellationToken)
    {
        var settings = context.ServiceProvider.GetRequiredService<ITenantSettingsProvider>();
        var accountEmails = string.IsNullOrEmpty(user.TenantId)
            ? await settings.GetAccountEmailSettingsAsync(cancellationToken)
            : await settings.GetAccountEmailSettingsAsync(user.TenantId, cancellationToken);
        return TimeSpan.FromMinutes(accountEmails.MfaCodeLifetimeMinutes);
    }

    private static void ClearEmailChange(StepExecutionContext context)
    {
        context.JourneyData.Remove(PendingEmailKey);
        context.JourneyData.Remove(CodeKey);
        context.JourneyData.Remove(SentAtKey);
        context.JourneyData.Remove(AttemptsKey);
    }

    private static StepHandlerResult ShowProfileForm(
        StepExecutionContext context,
        OlusoUserInfo user,
        List<UpdateFieldConfig> fields,
        ITenantContext tenantContext,
        Dictionary<string, string>? errors,
        bool requireCurrentPassword)
    {
        var viewModel = new UpdateUserViewModel
        {
            Fields = fields.Select(f => new UpdateFieldViewModel
            {
                Name = f.Name,
                Label = f.Label ?? f.Name,
                Type = f.Type ?? "text",
                Required = f.Required,
                Placeholder = f.Placeholder,
                Value = GetFieldValue(context, user, f.Name),
                Error = errors?.GetValueOrDefault(f.Name),
                Options = f.Options
            }).ToList(),
            FormError = errors?.GetValueOrDefault("_form"),
            TenantName = tenantContext.Tenant?.Name,
            SubmitButtonText = context.GetLocalizedConfig("submitButtonText") ?? Oluso.Core.Locale.UiStringDefaults.Resolve("update_user_button"),
            Title = context.GetLocalizedConfig("title") ?? Oluso.Core.Locale.UiStringDefaults.Resolve("update_user_default_title"),
            Description = context.GetLocalizedConfig("description"),
            RequireCurrentPassword = requireCurrentPassword,
            CurrentPasswordError = errors?.GetValueOrDefault("currentPassword"),
            Username = string.IsNullOrWhiteSpace(user.Email) ? user.Username : user.Email
        };

        return StepHandlerResult.ShowUi("Journey/_UpdateUser", viewModel);
    }

    private static UpdateFieldConfig BuildFieldConfigForName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower switch
        {
            "firstname" or "given_name" => new UpdateFieldConfig { Name = "firstName", Label = "First Name" },
            "lastname" or "family_name" => new UpdateFieldConfig { Name = "lastName", Label = "Last Name" },
            "displayname" or "name" => new UpdateFieldConfig { Name = "displayName", Label = "Display Name" },
            "email" => new UpdateFieldConfig { Name = "email", Label = "Email", Type = "email" },
            "phone" or "phonenumber" or "phone_number" => new UpdateFieldConfig { Name = "phone", Label = "Phone", Type = "tel" },
            "picture" or "profilepictureurl" => new UpdateFieldConfig { Name = "picture", Label = "Profile Picture URL", Type = "url" },
            _ => new UpdateFieldConfig { Name = name, Label = name, IsCustomClaim = true, ClaimType = name }
        };
    }

    private static string? GetFieldValue(StepExecutionContext context, OlusoUserInfo user, string fieldName)
    {
        // First check for submitted value
        var submittedValue = context.GetInput(fieldName);
        if (submittedValue != null)
            return submittedValue;

        // Fall back to user's current value
        return fieldName.ToLowerInvariant() switch
        {
            "firstname" or "given_name" => user.FirstName,
            "lastname" or "family_name" => user.LastName,
            "displayname" or "name" => user.DisplayName,
            "email" => user.Email,
            "phone" or "phonenumber" or "phone_number" => user.PhoneNumber,
            "picture" or "profilepictureurl" => user.Picture,
            _ => user.CustomProperties?.TryGetValue(fieldName, out var value) == true ? value : null
        };
    }

    private static List<UpdateFieldConfig> GetDefaultFields()
    {
        return new List<UpdateFieldConfig>
        {
            new() { Name = "firstName", Label = "First Name", Required = true },
            new() { Name = "lastName", Label = "Last Name", Required = true },
            new() { Name = "email", Label = "Email", Type = "email", Required = true },
            new() { Name = "phone", Label = "Phone", Type = "tel" }
        };
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

    private static bool IsValidPhone(string phone)
    {
        // Basic phone validation - allows digits, spaces, dashes, parentheses, and plus sign
        return System.Text.RegularExpressions.Regex.IsMatch(phone, @"^[\d\s\-\(\)\+]+$");
    }
}

public class UpdateFieldConfig
{
    public required string Name { get; init; }
    public string? Label { get; init; }
    public string? Type { get; init; }
    public bool Required { get; init; }
    public string? Placeholder { get; init; }
    public bool IsCustomClaim { get; init; }
    public string? ClaimType { get; init; }
    public List<SelectOption>? Options { get; init; }
}

public class UpdateUserViewModel
{
    public List<UpdateFieldViewModel> Fields { get; set; } = new();
    public string? FormError { get; set; }
    public string? TenantName { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? SubmitButtonText { get; set; }
    public bool RequireCurrentPassword { get; set; }
    public string? CurrentPasswordError { get; set; }

    /// <summary>What the user signs in with, for the form's hidden username field.</summary>
    public string? Username { get; set; }
}

public class UpdateFieldViewModel
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string Type { get; init; } = "text";
    public bool Required { get; init; }
    public string? Placeholder { get; init; }
    public string? Value { get; set; }
    public string? Error { get; set; }
    public List<SelectOption>? Options { get; init; }
}

public class SelectOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}
