using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles password reset flow for users who forgot their password.
/// Emails a reset code (the <see cref="PlatformTemplateNames.PasswordReset"/> template)
/// and, once the code is verified, sets a new password.
///
/// The response never reveals whether the address has an account: an unknown address
/// gets the same screens and the same "invalid code" answers as a real one.
/// </summary>
public class PasswordResetStepHandler : IStepHandler
{
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);

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
        var requireCurrentPassword = context.GetConfig("requireCurrentPassword", false);

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

            await IssueCodeAsync(context, userService, email, tokenExpirationMinutes, logger, cancellationToken);

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
            var sentAtStr = context.GetDataString("reset_sent_at");
            var storedEmail = context.GetData<string>("reset_email");

            if (string.IsNullOrEmpty(storedEmail))
            {
                return StepHandlerResult.Fail("session_expired", "Reset session expired. Please try again.");
            }

            // Check expiration
            if (DateTime.TryParse(sentAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sentAt) &&
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

            // An address without an account has no stored code and fails here exactly like a wrong code.
            // Each miss counts towards the step's attempt limit, which the orchestrator enforces.
            if (string.IsNullOrEmpty(storedCode) || !FixedTimeEquals(code.Trim(), storedCode))
            {
                logger.LogWarning("Invalid password reset code submitted for {Email}", MaskEmail(storedEmail));
                return StepHandlerResult.FailedAttemptUi("Journey/_PasswordResetVerify", new PasswordResetVerifyViewModel
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
                RequireNonAlphanumeric = requireNonAlphanumeric,
                RequireCurrentPassword = requireCurrentPassword,
                Username = storedEmail
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
            var resetEmail = context.GetData<string>("reset_email");

            if (!codeVerified || string.IsNullOrEmpty(storedUserId) || string.IsNullOrEmpty(storedToken))
            {
                return StepHandlerResult.Fail("session_expired", "Reset session expired. Please try again.");
            }

            // Optional current-password gate. Rare for forgot-password flows
            // (the user is asking to reset *because* they don't remember it),
            // but enabled when admins repurpose this step for a change-while-
            // logged-in flow.
            if (requireCurrentPassword)
            {
                var currentPassword = context.GetInput("current_password");
                if (string.IsNullOrEmpty(currentPassword))
                {
                    return StepHandlerResult.ShowUi("Journey/_PasswordResetNewPassword", new PasswordResetNewPasswordViewModel
                    {
                        MinLength = minLength,
                        RequireDigit = requireDigit,
                        RequireLowercase = requireLowercase,
                        RequireUppercase = requireUppercase,
                        RequireNonAlphanumeric = requireNonAlphanumeric,
                        RequireCurrentPassword = true,
                        Username = resetEmail,
                        ErrorMessage = "Please enter your current password"
                    });
                }

                var resettingUser = await userService.FindByIdAsync(storedUserId, cancellationToken);
                if (resettingUser != null)
                {
                    var credCheck = await userService.ValidateCredentialsAsync(resettingUser.Username, currentPassword, resettingUser.TenantId, cancellationToken);
                    if (!credCheck.CredentialsVerified)
                    {
                        logger.LogWarning("Current-password check failed during password reset for user {UserId}", storedUserId);
                        return StepHandlerResult.ShowUi("Journey/_PasswordResetNewPassword", new PasswordResetNewPasswordViewModel
                        {
                            MinLength = minLength,
                            RequireDigit = requireDigit,
                            RequireLowercase = requireLowercase,
                            RequireUppercase = requireUppercase,
                            RequireNonAlphanumeric = requireNonAlphanumeric,
                            RequireCurrentPassword = true,
                            Username = resetEmail,
                            ErrorMessage = "Current password is incorrect"
                        });
                    }
                }
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
                    RequireCurrentPassword = requireCurrentPassword,
                    Username = resetEmail,
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
                    RequireCurrentPassword = requireCurrentPassword,
                    Username = resetEmail,
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
                    RequireCurrentPassword = requireCurrentPassword,
                    Username = resetEmail,
                    ErrorMessage = result.MessageOr("Password reset failed")
                });
            }

            logger.LogInformation("Password reset successful for user {UserId}", storedUserId);

            // Clear state and scrub credentials out of input — both the
            // submitted new password and the optional current-password
            // gate value.
            ClearResetState(context);
            SensitiveInputs.Scrub(context.Input?.Values);

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
                string? notice = null;
                if (DateTime.TryParse(context.GetDataString("reset_sent_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastSent)
                    && DateTime.UtcNow - lastSent < ResendCooldown)
                {
                    notice = "A code was just sent. Please wait a moment before requesting another.";
                }
                else
                {
                    await IssueCodeAsync(context, userService, storedEmail, tokenExpirationMinutes, logger, cancellationToken);
                }

                return StepHandlerResult.ShowUi("Journey/_PasswordResetVerify", new PasswordResetVerifyViewModel
                {
                    Email = MaskEmail(storedEmail),
                    ExpirationMinutes = tokenExpirationMinutes,
                    ErrorMessage = notice
                });
            }
        }

        // Initial state - show email request form
        return StepHandlerResult.ShowUi("Journey/_PasswordResetRequest", new PasswordResetRequestViewModel());
    }

    public static void ClearResetState(StepExecutionContext context)
    {
        context.JourneyData.Remove("reset_email");
        context.JourneyData.Remove("reset_user_id");
        context.JourneyData.Remove("reset_token");
        context.JourneyData.Remove("reset_code");
        context.JourneyData.Remove("reset_sent_at");
        context.JourneyData.Remove("reset_code_verified");
    }

    /// <summary>
    /// Starts (or restarts) a reset for <paramref name="email"/>: a fresh reset token and
    /// code for an existing account, emailed with the password-reset template. Unknown
    /// addresses get the same session state minus the code, so the flow looks identical.
    /// </summary>
    private static async Task IssueCodeAsync(
        StepExecutionContext context,
        IOlusoUserService userService,
        string email,
        int expirationMinutes,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ClearResetState(context);
        context.SetData("reset_email", email);
        context.SetData("reset_sent_at", DateTime.UtcNow.ToString("O"));

        var user = await userService.FindByEmailAsync(email, cancellationToken);
        if (user == null)
        {
            logger.LogWarning("Password reset requested for non-existent email: {Email}", MaskEmail(email));
            return;
        }

        // The code goes to the address on the account, never the one typed: lookups are
        // case- and Unicode-normalised, so a different string (e.g. a dotless-i lookalike)
        // can match this account while naming someone else's mailbox.
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning("Password reset not available for user {UserId}: the account has no email", user.Id);
            return;
        }

        var resetToken = await userService.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        if (string.IsNullOrEmpty(resetToken))
        {
            logger.LogWarning("Password reset not available for user {UserId}", user.Id);
            return;
        }

        var resetCode = GenerateCode();
        context.SetData("reset_user_id", user.Id);
        context.SetData("reset_token", resetToken);
        context.SetData("reset_code", resetCode);

        var emailService = context.ServiceProvider.GetService<IEmailService>()
            ?? (IEmailSender?)context.ServiceProvider.GetService<IEmailSender>();
        if (emailService == null)
        {
            logger.LogError(
                "Password reset requested for user {UserId} but no IEmailSender/IEmailService is registered. " +
                "Call AddMessaging(configuration) (or AddEmailSender) in startup so the code can be sent.", user.Id);
            return;
        }

        // Opening the link submits the code to this journey, so it works like typing it in.
        var resetLink = JourneyLinks.Absolute(context,
            $"/journey/{Uri.EscapeDataString(context.JourneyId)}?code={Uri.EscapeDataString(resetCode)}");

        var templates = context.ServiceProvider.GetService<IPlatformEmailTemplateService>();
        var rendered = templates == null ? null : await templates.RenderAsync(
            PlatformTemplateNames.PasswordReset,
            new Dictionary<string, object>
            {
                [PlatformTemplateScopeKeys.OtpCode] = resetCode,
                [PlatformTemplateScopeKeys.ResetLink] = resetLink,
                [PlatformTemplateScopeKeys.ExpirationMinutes] = expirationMinutes,
                [PlatformTemplateScopeKeys.ExpiresIn] = Durations.Describe(TimeSpan.FromMinutes(expirationMinutes)),
                [PlatformTemplateScopeKeys.UserEmail] = user.Email,
                [PlatformTemplateScopeKeys.UserName] = DisplayName(user),
            },
            cancellationToken);

        var subject = rendered?.Subject ?? "Reset your password";
        var body = rendered?.HtmlBody ?? FallbackBody(resetCode, resetLink, expirationMinutes);

        var result = await emailService.SendAsync(user.Email, subject, body, cancellationToken);
        if (result.Success)
            logger.LogInformation("Password reset code sent to user {UserId}", user.Id);
        else
            logger.LogWarning("Failed to send password reset code to user {UserId}: {Error}", user.Id, result.Error);
    }

    private static string DisplayName(OlusoUserInfo user) =>
        !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName!
        : string.Join(' ', new[] { user.FirstName, user.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } full ? full
        : user.Username;

    private static string FallbackBody(string code, string link, int expirationMinutes) => BrandedEmailLayout.Document(
        title: "Reset your password",
        preheader: "Here's your password reset code.",
        accent: BrandedEmailLayout.DefaultAccent,
        brand: "",
        eyebrow: "Password reset",
        heading: "Reset your password",
        rows:
            BrandedEmailLayout.Lead("We received a request to reset your password.") +
            BrandedEmailLayout.Button(link, "Choose a new password &rarr;", BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Note("Or enter this code where you asked for the reset:") +
            BrandedEmailLayout.Code(code, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Note($"The link and code expire in <strong>{Durations.Describe(TimeSpan.FromMinutes(expirationMinutes))}</strong>.") +
            BrandedEmailLayout.Divider() +
            BrandedEmailLayout.LinkFallback(link, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Paragraph("Didn't ask to reset your password? You can safely ignore this email &mdash; your password won't change."),
        footnote: "This is an automated security email.");

    /// <summary>A uniformly random 6-digit code from the OS CSPRNG.</summary>
    /// <summary>Digits in a reset code.</summary>
    public const int CodeLength = 6;

    private static string GenerateCode() => RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, CodeLength)).ToString($"D{CodeLength}");

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

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
    public bool RequireCurrentPassword { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The address the reset was requested for, rendered as a visually hidden username field
    /// so password managers save the new password against the right login.
    /// </summary>
    public string? Username { get; set; }
}

#endregion
