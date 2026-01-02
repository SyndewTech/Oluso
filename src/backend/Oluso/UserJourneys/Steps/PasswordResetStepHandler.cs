using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles password reset flow for users who forgot their password.
/// Sends reset code via email and allows setting a new password.
/// </summary>
public class PasswordResetStepHandler : IStepHandler
{
    public string StepType => "password_reset";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<PasswordResetStepHandler>>();

        var tokenExpirationMinutes = context.GetConfig("tokenExpirationMinutes", 60);
        var minLength = context.GetConfig("minLength", 8);
        var requireDigit = context.GetConfig("requireDigit", true);
        var requireLowercase = context.GetConfig("requireLowercase", true);
        var requireUppercase = context.GetConfig("requireUppercase", true);
        var requireNonAlphanumeric = context.GetConfig("requireNonAlphanumeric", false);

        // Phase 1: Handle email submission (request reset)
        var email = context.GetInput("email");
        var hasCode = !string.IsNullOrEmpty(context.GetInput("code"));
        var hasNewPassword = !string.IsNullOrEmpty(context.GetInput("new_password"));

        if (!string.IsNullOrEmpty(email) && !hasCode && !hasNewPassword)
        {
            email = email.Trim().ToLowerInvariant();

            if (!email.Contains('@'))
            {
                return StepHandlerResult.ShowUi("Journey/_PasswordResetRequest", new PasswordResetRequestViewModel
                {
                    ErrorMessage = "Please enter a valid email address"
                });
            }

            var user = await userService.FindByEmailAsync(email, cancellationToken);

            // Always show success for security (don't reveal if email exists)
            context.SetData("reset_email", email);
            context.SetData("reset_sent_at", DateTime.UtcNow);

            if (user != null)
            {
                // Generate reset token
                var resetToken = await userService.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
                if (!string.IsNullOrEmpty(resetToken))
                {
                    context.SetData("reset_user_id", user.Id);
                    context.SetData("reset_token", resetToken);

                    // Generate short code for email
                    var resetCode = GenerateShortCode();
                    context.SetData("reset_code", resetCode);

                    // TODO: Send email with reset code
                    logger.LogInformation("Password reset code generated for user {UserId}: {Code}", user.Id, resetCode);
                }
            }
            else
            {
                logger.LogWarning("Password reset requested for non-existent email: {Email}", email);
            }

            return StepHandlerResult.ShowUi("Journey/_PasswordResetVerify", new PasswordResetVerifyViewModel
            {
                Email = MaskEmail(email),
                ExpirationMinutes = tokenExpirationMinutes
            });
        }

        // Phase 2: Handle code verification
        var code = context.GetInput("code");
        if (!string.IsNullOrEmpty(code) && !hasNewPassword)
        {
            var storedCode = context.GetData<string>("reset_code");
            var sentAtStr = context.GetData<string>("reset_sent_at");
            var storedEmail = context.GetData<string>("reset_email") ?? "";

            if (string.IsNullOrEmpty(storedCode))
            {
                return StepHandlerResult.Fail("session_expired", "Reset session expired. Please try again.");
            }

            // Check expiration
            if (DateTime.TryParse(sentAtStr, out var sentAt) &&
                DateTime.UtcNow - sentAt > TimeSpan.FromMinutes(tokenExpirationMinutes))
            {
                logger.LogWarning("Password reset code expired for {Email}", MaskEmail(storedEmail));
                return StepHandlerResult.ShowUi("Journey/_PasswordResetVerify", new PasswordResetVerifyViewModel
                {
                    Email = MaskEmail(storedEmail),
                    ExpirationMinutes = tokenExpirationMinutes,
                    ErrorMessage = "Code has expired. Please request a new reset."
                });
            }

            // Verify code
            if (!string.Equals(code, storedCode, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Invalid password reset code submitted for {Email}", MaskEmail(storedEmail));
                return StepHandlerResult.ShowUi("Journey/_PasswordResetVerify", new PasswordResetVerifyViewModel
                {
                    Email = MaskEmail(storedEmail),
                    ExpirationMinutes = tokenExpirationMinutes,
                    ErrorMessage = "Invalid code. Please try again."
                });
            }

            // Code valid, show new password form
            context.SetData("reset_code_verified", true);

            return StepHandlerResult.ShowUi("Journey/_PasswordResetNewPassword", new PasswordResetNewPasswordViewModel
            {
                MinLength = minLength,
                RequireDigit = requireDigit,
                RequireLowercase = requireLowercase,
                RequireUppercase = requireUppercase,
                RequireNonAlphanumeric = requireNonAlphanumeric
            });
        }

        // Phase 3: Handle new password submission
        var newPassword = context.GetInput("new_password");
        if (!string.IsNullOrEmpty(newPassword))
        {
            var confirmPassword = context.GetInput("confirm_password") ?? "";
            var storedUserId = context.GetData<string>("reset_user_id");
            var storedToken = context.GetData<string>("reset_token");
            var codeVerified = context.GetData<bool>("reset_code_verified");

            if (!codeVerified || string.IsNullOrEmpty(storedUserId) || string.IsNullOrEmpty(storedToken))
            {
                return StepHandlerResult.Fail("session_expired", "Reset session expired. Please try again.");
            }

            // Validate password
            var passwordErrors = ValidatePassword(newPassword, minLength, requireDigit,
                requireLowercase, requireUppercase, requireNonAlphanumeric);

            if (passwordErrors.Count > 0)
            {
                return StepHandlerResult.ShowUi("Journey/_PasswordResetNewPassword", new PasswordResetNewPasswordViewModel
                {
                    MinLength = minLength,
                    RequireDigit = requireDigit,
                    RequireLowercase = requireLowercase,
                    RequireUppercase = requireUppercase,
                    RequireNonAlphanumeric = requireNonAlphanumeric,
                    ErrorMessage = string.Join(" ", passwordErrors)
                });
            }

            // Confirm password match
            if (newPassword != confirmPassword)
            {
                return StepHandlerResult.ShowUi("Journey/_PasswordResetNewPassword", new PasswordResetNewPasswordViewModel
                {
                    MinLength = minLength,
                    RequireDigit = requireDigit,
                    RequireLowercase = requireLowercase,
                    RequireUppercase = requireUppercase,
                    RequireNonAlphanumeric = requireNonAlphanumeric,
                    ErrorMessage = "Passwords do not match"
                });
            }

            // Reset password
            var result = await userService.ResetPasswordAsync(storedUserId, newPassword, storedToken, cancellationToken);

            if (!result.Succeeded)
            {
                logger.LogWarning("Password reset failed for user {UserId}: {Error}", storedUserId, result.Error);
                return StepHandlerResult.ShowUi("Journey/_PasswordResetNewPassword", new PasswordResetNewPasswordViewModel
                {
                    MinLength = minLength,
                    RequireDigit = requireDigit,
                    RequireLowercase = requireLowercase,
                    RequireUppercase = requireUppercase,
                    RequireNonAlphanumeric = requireNonAlphanumeric,
                    ErrorMessage = result.ErrorDescription ?? "Password reset failed"
                });
            }

            logger.LogInformation("Password reset successful for user {UserId}", storedUserId);

            // Clear state
            ClearResetState(context);

            // Authenticate the user after successful password reset
            context.UserId = storedUserId;
            context.SetData("authenticated_at", DateTime.UtcNow);
            context.SetData("auth_method", "pwd_reset");

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["sub"] = storedUserId,
                ["password_reset"] = true
            });
        }

        // Handle resend request
        var resend = context.GetInput("resend");
        if (!string.IsNullOrEmpty(resend))
        {
            var storedEmail = context.GetData<string>("reset_email");
            if (!string.IsNullOrEmpty(storedEmail))
            {
                // Clear old tokens
                context.JourneyData.Remove("reset_token");
                context.JourneyData.Remove("reset_code");
                context.JourneyData.Remove("reset_sent_at");
                context.JourneyData.Remove("reset_code_verified");

                // Redirect back to verify screen which will regenerate the code
                return StepHandlerResult.ShowUi("Journey/_PasswordResetVerify", new PasswordResetVerifyViewModel
                {
                    Email = MaskEmail(storedEmail),
                    ExpirationMinutes = context.GetConfig("tokenExpirationMinutes", 60)
                });
            }
        }

        // Initial state - show email request form
        return StepHandlerResult.ShowUi("Journey/_PasswordResetRequest", new PasswordResetRequestViewModel());
    }

    private void ClearResetState(StepExecutionContext context)
    {
        context.JourneyData.Remove("reset_email");
        context.JourneyData.Remove("reset_user_id");
        context.JourneyData.Remove("reset_token");
        context.JourneyData.Remove("reset_code");
        context.JourneyData.Remove("reset_sent_at");
        context.JourneyData.Remove("reset_code_verified");
    }

    private static string GenerateShortCode()
    {
        var random = new Random();
        return string.Concat(Enumerable.Range(0, 6).Select(_ => random.Next(0, 10).ToString()));
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "";
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        if (localPart.Length <= 2)
            return localPart[0] + "***" + domain;

        return localPart[0] + "***" + localPart[^1] + domain;
    }

    private static List<string> ValidatePassword(string password, int minLength, bool requireDigit,
        bool requireLowercase, bool requireUppercase, bool requireNonAlphanumeric)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required.");
            return errors;
        }

        if (password.Length < minLength)
            errors.Add($"Password must be at least {minLength} characters.");

        if (requireDigit && !password.Any(char.IsDigit))
            errors.Add("Password must contain at least one digit.");

        if (requireLowercase && !password.Any(char.IsLower))
            errors.Add("Password must contain at least one lowercase letter.");

        if (requireUppercase && !password.Any(char.IsUpper))
            errors.Add("Password must contain at least one uppercase letter.");

        if (requireNonAlphanumeric && password.All(char.IsLetterOrDigit))
            errors.Add("Password must contain at least one special character.");

        return errors;
    }
}

#region ViewModels

public class PasswordResetRequestViewModel
{
    public string? ErrorMessage { get; set; }
}

public class PasswordResetVerifyViewModel
{
    public string Email { get; set; } = null!;
    public int ExpirationMinutes { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PasswordResetNewPasswordViewModel
{
    public int MinLength { get; set; } = 8;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; }
    public string? ErrorMessage { get; set; }
}

#endregion
