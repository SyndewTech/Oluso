using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles password change for authenticated users.
/// Requires current password verification before allowing password update.
/// </summary>
public class PasswordChangeStepHandler : IStepHandler
{
    public string StepType => "password_change";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<PasswordChangeStepHandler>>();
        var eventService = context.ServiceProvider.GetService<IOlusoEventService>();

        // Require authenticated user
        if (string.IsNullOrEmpty(context.UserId))
        {
            return StepHandlerResult.Fail("not_authenticated", "User must be authenticated to change password");
        }

        var user = await userService.FindByIdAsync(context.UserId, cancellationToken);
        if (user == null)
        {
            return StepHandlerResult.Fail("user_not_found", "User not found");
        }

        // What the user signs in with, for the form's hidden username field.
        var accountName = string.IsNullOrWhiteSpace(user.Email) ? user.Username : user.Email;

        // Check if form was submitted
        var currentPassword = context.GetInput("currentPassword");
        var newPassword = context.GetInput("newPassword");
        var confirmPassword = context.GetInput("confirmPassword");

        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
        {
            // Show password change form
            var requireCurrentPassword = context.GetConfig("requireCurrentPassword", true);
            var minLength = context.GetConfig("minPasswordLength", 8);
            var requireUppercase = context.GetConfig("requireUppercase", true);
            var requireLowercase = context.GetConfig("requireLowercase", true);
            var requireDigit = context.GetConfig("requireDigit", true);
            var requireSpecialChar = context.GetConfig("requireSpecialChar", false);

            return StepHandlerResult.ShowUi("Journey/_PasswordChange", new PasswordChangeViewModel
            {
                RequireCurrentPassword = requireCurrentPassword,
                MinPasswordLength = minLength,
                RequireUppercase = requireUppercase,
                RequireLowercase = requireLowercase,
                RequireDigit = requireDigit,
                RequireSpecialChar = requireSpecialChar,
                TenantName = tenantContext.Tenant?.Name,
                Username = accountName
            });
        }

        // Validate new password matches confirmation
        if (newPassword != confirmPassword)
        {
            return StepHandlerResult.ShowUi("Journey/_PasswordChange", new PasswordChangeViewModel
            {
                ErrorMessage = "New password and confirmation do not match",
                RequireCurrentPassword = context.GetConfig("requireCurrentPassword", true),
                Username = accountName
            });
        }

        // Validate password complexity
        var validationErrors = ValidatePasswordComplexity(newPassword, context);
        if (validationErrors.Any())
        {
            return StepHandlerResult.ShowUi("Journey/_PasswordChange", new PasswordChangeViewModel
            {
                ErrorMessage = string.Join(". ", validationErrors),
                RequireCurrentPassword = context.GetConfig("requireCurrentPassword", true),
                Username = accountName
            });
        }

        // Verify current password if required
        var requireCurrent = context.GetConfig("requireCurrentPassword", true);
        if (requireCurrent)
        {
            var verifyResult = await userService.ValidateCredentialsAsync(
                user.Username,
                currentPassword,
                tenantContext.TenantId,
                cancellationToken);

            if (!verifyResult.CredentialsVerified)
            {
                logger.LogWarning("Password change failed: invalid current password for user {UserId}", user.Id);
                return StepHandlerResult.ShowUi("Journey/_PasswordChange", new PasswordChangeViewModel
                {
                    ErrorMessage = "Current password is incorrect",
                    RequireCurrentPassword = true,
                    Username = accountName
                });
            }
        }

        // Change the password
        var changeResult = await userService.ChangePasswordAsync(user.Id, currentPassword, newPassword, cancellationToken);
        if (!changeResult.Succeeded)
        {
            logger.LogError("Password change failed for user {UserId}: {Error}", user.Id, changeResult.Error);
            return StepHandlerResult.ShowUi("Journey/_PasswordChange", new PasswordChangeViewModel
            {
                ErrorMessage = changeResult.MessageOr("Failed to change password"),
                RequireCurrentPassword = context.GetConfig("requireCurrentPassword", true),
                Username = accountName
            });
        }

        logger.LogInformation("Password changed successfully for user {UserId}", user.Id);

        // Raise password changed event
        if (eventService != null)
        {
            await eventService.RaiseAsync(new UserPasswordChangedEvent
            {
                TenantId = tenantContext.TenantId,
                SubjectId = user.Id,
                ChangedByAdmin = false
            }, cancellationToken);
        }

        context.SetData("password_changed_at", DateTime.UtcNow);

        // Drop credentials from input now that they've been consumed.
        SensitiveInputs.Scrub(context.Input?.Values);

        return StepHandlerResult.Success(new Dictionary<string, object>
        {
            ["password_changed"] = true,
            ["password_changed_at"] = DateTime.UtcNow
        });
    }

    private static List<string> ValidatePasswordComplexity(string password, StepExecutionContext context)
    {
        var errors = new List<string>();

        var minLength = context.GetConfig("minPasswordLength", 8);
        var requireUppercase = context.GetConfig("requireUppercase", true);
        var requireLowercase = context.GetConfig("requireLowercase", true);
        var requireDigit = context.GetConfig("requireDigit", true);
        var requireSpecialChar = context.GetConfig("requireSpecialChar", false);

        if (password.Length < minLength)
        {
            errors.Add($"Password must be at least {minLength} characters");
        }

        if (requireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add("Password must contain at least one uppercase letter");
        }

        if (requireLowercase && !password.Any(char.IsLower))
        {
            errors.Add("Password must contain at least one lowercase letter");
        }

        if (requireDigit && !password.Any(char.IsDigit))
        {
            errors.Add("Password must contain at least one digit");
        }

        if (requireSpecialChar && !password.Any(c => !char.IsLetterOrDigit(c)))
        {
            errors.Add("Password must contain at least one special character");
        }

        return errors;
    }
}

public class PasswordChangeViewModel
{
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    /// <summary>Why the user is being asked to change their password (e.g. it expired).</summary>
    public string? NoticeMessage { get; set; }
    public bool RequireCurrentPassword { get; set; } = true;
    public int MinPasswordLength { get; set; } = 8;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireDigit { get; set; } = true;
    public bool RequireSpecialChar { get; set; }
    public string? TenantName { get; set; }

    /// <summary>
    /// The account's sign-in name, rendered as a visually hidden username field so password
    /// managers save the new password against the right login.
    /// </summary>
    public string? Username { get; set; }
}
