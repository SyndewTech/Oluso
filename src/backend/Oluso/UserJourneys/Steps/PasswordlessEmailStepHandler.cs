using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles passwordless authentication via email magic links or OTP codes.
/// Users receive an email with either a magic link or a one-time code to authenticate.
/// </summary>
/// <remarks>
/// Configuration options:
/// - mode: "otp" (default) or "magic-link"
/// - codeLength: number of digits (default: 6)
/// - expirationMinutes: code expiration time (default: 15)
/// - allowSignUp: allow new users to register (default: false)
/// </remarks>
public class PasswordlessEmailStepHandler : IStepHandler
{
    public string StepType => "passwordless_email";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var emailService = context.ServiceProvider.GetService<IEmailService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<PasswordlessEmailStepHandler>>();

        var mode = context.GetConfig("mode", "otp");
        var codeLength = context.GetConfig("codeLength", 6);
        var expirationMinutes = context.GetConfig("expirationMinutes", 15);
        var allowSignUp = context.GetConfig("allowSignUp", false);

        // Handle email submission
        var email = context.GetInput("email");
        if (!string.IsNullOrEmpty(email))
        {
            email = email.Trim().ToLowerInvariant();

            if (!email.Contains('@'))
            {
                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmail", new PasswordlessEmailViewModel
                {
                    Mode = mode,
                    AllowSignUp = allowSignUp,
                    ErrorMessage = "Please enter a valid email address"
                });
            }

            var user = await userService.FindByEmailAsync(email, cancellationToken);

            // If user doesn't exist and sign-up is not allowed
            if (user == null && !allowSignUp)
            {
                // For security, don't reveal that the user doesn't exist
                logger.LogWarning("Passwordless login attempted for non-existent email");
                context.SetData("passwordless_email", email);
                context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));

                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    Email = MaskEmail(email),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes
                });
            }

            // Generate code
            var code = GenerateCode(codeLength);

            // Store code and email in state
            context.SetData("passwordless_email", email);
            context.SetData("passwordless_code", code);
            context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));
            context.SetData("passwordless_user_exists", (user != null).ToString().ToLower());

            // Send email
            if (emailService != null)
            {
                var subject = mode == "magic-link"
                    ? "Sign in to your account"
                    : "Your verification code";

                var body = mode == "magic-link"
                    ? GenerateMagicLinkEmail(context, code, expirationMinutes)
                    : GenerateOtpEmail(code, expirationMinutes);

                await emailService.SendAsync(email, subject, body, cancellationToken);
            }

            logger.LogInformation("Passwordless {Mode} sent to {Email}", mode, MaskEmail(email));

            return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
            {
                Email = MaskEmail(email),
                Mode = mode,
                ExpirationMinutes = expirationMinutes
            });
        }

        // Handle code verification
        var submittedCode = context.GetInput("code");
        if (!string.IsNullOrEmpty(submittedCode))
        {
            var storedEmail = context.GetData<string>("passwordless_email");
            var storedCode = context.GetData<string>("passwordless_code");
            var sentAt = context.GetData<string>("passwordless_sent_at");
            var userExists = context.GetData<string>("passwordless_user_exists") == "true";

            if (string.IsNullOrEmpty(storedEmail) || string.IsNullOrEmpty(storedCode))
            {
                return StepHandlerResult.Fail("session_expired", "Session expired. Please try again.");
            }

            // Check expiration
            if (DateTime.TryParse(sentAt, out var sentTime) &&
                DateTime.UtcNow - sentTime > TimeSpan.FromMinutes(expirationMinutes))
            {
                logger.LogWarning("Passwordless code expired");
                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    Email = MaskEmail(storedEmail),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes,
                    ErrorMessage = "Code has expired. Please request a new one."
                });
            }

            // Verify code
            var isValid = string.Equals(storedCode, submittedCode, StringComparison.OrdinalIgnoreCase);

            if (!isValid)
            {
                logger.LogWarning("Invalid passwordless code submitted");
                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    Email = MaskEmail(storedEmail),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes,
                    ErrorMessage = "Invalid code. Please try again."
                });
            }

            // Code is valid
            context.SetData("email_verified", "true");

            var user = await userService.FindByEmailAsync(storedEmail, cancellationToken);
            if (user != null)
            {
                // Existing user - authenticate
                context.UserId = user.Id;
                context.SetData("amr", "email");

                logger.LogInformation("Passwordless login successful for user {UserId}", user.Id);

                // Clear state
                ClearPasswordlessState(context);

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["sub"] = user.Id,
                    ["email"] = storedEmail,
                    ["email_verified"] = true,
                    ["amr"] = "email"
                });
            }
            else if (allowSignUp)
            {
                // New user - store email for sign-up flow
                context.SetData("verified_email", storedEmail);

                logger.LogInformation("Email verified for sign-up: {Email}", MaskEmail(storedEmail));

                ClearPasswordlessState(context);

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["verified_email"] = storedEmail,
                    ["requires_signup"] = true
                });
            }
            else
            {
                return StepHandlerResult.Fail("user_not_found", "User not found");
            }
        }

        // Handle resend request
        if (!string.IsNullOrEmpty(context.GetInput("resend")))
        {
            var storedEmail = context.GetData<string>("passwordless_email");
            if (!string.IsNullOrEmpty(storedEmail))
            {
                // Remove old code and regenerate
                context.JourneyData.Remove("passwordless_code");
                context.JourneyData.Remove("passwordless_sent_at");

                // Generate new code
                var code = GenerateCode(codeLength);
                context.SetData("passwordless_code", code);
                context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));

                // Send email
                if (emailService != null)
                {
                    var subject = mode == "magic-link"
                        ? "Sign in to your account"
                        : "Your verification code";

                    var body = mode == "magic-link"
                        ? GenerateMagicLinkEmail(context, code, expirationMinutes)
                        : GenerateOtpEmail(code, expirationMinutes);

                    await emailService.SendAsync(storedEmail, subject, body, cancellationToken);
                }

                logger.LogInformation("Passwordless code resent to {Email}", MaskEmail(storedEmail));

                return StepHandlerResult.ShowUi("Journey/_PasswordlessEmailVerify", new PasswordlessEmailVerifyViewModel
                {
                    Email = MaskEmail(storedEmail),
                    Mode = mode,
                    ExpirationMinutes = expirationMinutes,
                    SuccessMessage = "A new code has been sent to your email."
                });
            }
        }

        // Initial state - show email input
        return StepHandlerResult.ShowUi("Journey/_PasswordlessEmail", new PasswordlessEmailViewModel
        {
            Mode = mode,
            AllowSignUp = allowSignUp
        });
    }

    private static void ClearPasswordlessState(StepExecutionContext context)
    {
        context.JourneyData.Remove("passwordless_email");
        context.JourneyData.Remove("passwordless_code");
        context.JourneyData.Remove("passwordless_sent_at");
        context.JourneyData.Remove("passwordless_user_exists");
    }

    private static string GenerateCode(int length)
    {
        var random = new Random();
        return string.Concat(Enumerable.Range(0, length).Select(_ => random.Next(0, 10).ToString()));
    }

    private static string GenerateOtpEmail(string code, int expirationMinutes)
    {
        return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <h2>Your verification code</h2>
    <p>Use this code to sign in:</p>
    <div style='font-size: 32px; font-weight: bold; letter-spacing: 8px; padding: 20px; background: #f5f5f5; text-align: center; margin: 20px 0;'>
        {code}
    </div>
    <p>This code expires in {expirationMinutes} minutes.</p>
    <p style='color: #666; font-size: 12px;'>If you didn't request this code, you can safely ignore this email.</p>
</div>";
    }

    private static string GenerateMagicLinkEmail(StepExecutionContext context, string code, int expirationMinutes)
    {
        var journeyId = context.JourneyId;
        var magicLink = $"/journey/{journeyId}?code={code}";

        return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <h2>Sign in to your account</h2>
    <p>Click the button below to sign in:</p>
    <div style='text-align: center; margin: 30px 0;'>
        <a href='{magicLink}' style='display: inline-block; padding: 15px 30px; background: #4F46E5; color: white; text-decoration: none; border-radius: 6px; font-weight: bold;'>
            Sign In
        </a>
    </div>
    <p>Or use this code: <strong>{code}</strong></p>
    <p>This link expires in {expirationMinutes} minutes.</p>
    <p style='color: #666; font-size: 12px;'>If you didn't request this, you can safely ignore this email.</p>
</div>";
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
}

#region ViewModels

public class PasswordlessEmailViewModel
{
    public string Mode { get; set; } = "otp";
    public bool AllowSignUp { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Email { get; set; }
}

public class PasswordlessEmailVerifyViewModel
{
    public string Email { get; set; } = null!;
    public string Mode { get; set; } = "otp";
    public int ExpirationMinutes { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

#endregion
