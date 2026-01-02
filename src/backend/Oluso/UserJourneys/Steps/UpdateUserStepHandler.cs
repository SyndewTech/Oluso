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

        // Get configurable fields
        var fields = context.GetConfig<List<UpdateFieldConfig>>("fields", null) ?? GetDefaultFields();
        var requiredFields = fields.Where(f => f.Required).Select(f => f.Name).ToList();

        // Check if form was submitted
        var submitted = context.GetInput("_submitted");
        if (submitted != "true")
        {
            // Show profile form with current values
            return ShowProfileForm(context, user, fields, tenantContext, null);
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
            return ShowProfileForm(context, user, fields, tenantContext, validationErrors);
        }

        // Build custom properties for fields
        var customProps = new Dictionary<string, string>();
        foreach (var field in fields.Where(f => f.IsCustomClaim))
        {
            var value = context.GetInput(field.Name);
            if (!string.IsNullOrEmpty(value))
            {
                customProps[field.ClaimType ?? field.Name] = value;
            }
        }

        // Handle email change (may require verification)
        string? newEmail = null;
        if (!string.IsNullOrEmpty(email) && email != user.Email)
        {
            var requireEmailVerification = context.GetConfig("requireEmailVerification", true);
            if (requireEmailVerification)
            {
                // Store pending email, actual change happens after verification
                context.SetData("pending_email", email);
                context.SetData("email_change_requested", true);
                logger.LogInformation("Email change requested for user {UserId}, verification pending", user.Id);
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
                new Dictionary<string, string> { ["_form"] = result.ErrorDescription ?? "Failed to update profile" });
        }

        logger.LogInformation("User profile updated for {UserId}", user.Id);

        // Refresh user data
        user = await userService.FindByIdAsync(context.UserId, cancellationToken);

        // Build output claims
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
                outputData["email"] = user.Email;
            if (!string.IsNullOrEmpty(user.PhoneNumber))
                outputData["phone_number"] = user.PhoneNumber;
        }

        return StepHandlerResult.Success(outputData);
    }

    private static StepHandlerResult ShowProfileForm(
        StepExecutionContext context,
        OlusoUserInfo user,
        List<UpdateFieldConfig> fields,
        ITenantContext tenantContext,
        Dictionary<string, string>? errors)
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
            SubmitButtonText = context.GetConfig<string>("submitButtonText", "Update Profile"),
            Title = context.GetConfig<string>("title", "Update Your Profile"),
            Description = context.GetConfig<string>("description", null)
        };

        return StepHandlerResult.ShowUi("Journey/_UpdateUser", viewModel);
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
