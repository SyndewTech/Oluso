using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles passwordless authentication via SMS OTP codes.
/// Users receive an SMS with a one-time code to authenticate.
/// </summary>
/// <remarks>
/// Configuration options:
/// - codeLength: number of digits (default: 6)
/// - expirationMinutes: code expiration time (default: 10)
/// - allowSignUp: allow new users to register (default: false)
/// - toLinkRecipient: verify the phone the journey's link was sent to, without signing anyone in; it is sent the code
///   at once and can't be changed. Runs not opened from a link sent to someone skip the step.
/// </remarks>
public class PasswordlessSmsStepHandler : IPrimaryAuthenticationStep
{
    public string StepType => "passwordless_sms";

    public bool SignsIn(StepExecutionContext context) => !context.GetConfig("toLinkRecipient", false);

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        // Prefer ISmsService (legacy alias); fall back to ISmsSender so the
        // platform Sms config is picked up regardless of which interface
        // the consumer registered against.
        var smsService = context.ServiceProvider.GetService<ISmsService>()
            ?? (ISmsSender?)context.ServiceProvider.GetService<ISmsSender>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<PasswordlessSmsStepHandler>>();

        var codeLength = context.GetConfig("codeLength", 6);
        var expirationMinutes = context.GetConfig("expirationMinutes", 10);
        var allowSignUp = context.GetConfig("allowSignUp", false);
        var maxAttempts = context.GetConfig("maxAttempts", 3);
        var messageTemplate = context.GetConfig<string?>("messageTemplate", null);

        string? recipientPhone = null;
        if (!SignsIn(context))
        {
            var recipient = JourneyLaunchData.ReadRecipient(context.JourneyData);
            if (recipient == null) return StepHandlerResult.Skip();
            if (recipient.Phone == null)
                return StepHandlerResult.Fail("recipient_unverifiable", "This link can't be verified by text message. Ask whoever sent it for a new one.");
            recipientPhone = NormalizePhoneNumber(recipient.Phone);
        }
        var pendingPhone = context.GetData<string>("passwordless_phone");

        // Handle phone number submission. The link's recipient is sent the code on arrival and can't be replaced.
        var phoneNumber = recipientPhone == null ? context.GetInput("phone") : string.IsNullOrEmpty(pendingPhone) ? recipientPhone : null;
        if (!string.IsNullOrEmpty(phoneNumber))
        {
            phoneNumber = NormalizePhoneNumber(phoneNumber);

            if (phoneNumber.Length < 10)
            {
                return StepHandlerResult.ShowUi("Journey/_PasswordlessSms", new PasswordlessSmsViewModel
                {
                    AllowSignUp = allowSignUp,
                    ErrorMessage = "Please enter a valid phone number"
                });
            }

            var user = recipientPhone == null ? await userService.FindByPhoneAsync(phoneNumber, cancellationToken) : null;

            // If user doesn't exist and sign-up is not allowed
            if (recipientPhone == null && user == null && !allowSignUp)
            {
                // For security, don't reveal that the user doesn't exist
                logger.LogWarning("Passwordless SMS login attempted for non-existent phone");
                context.SetData("passwordless_phone", phoneNumber);
                context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));

                return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
                {
                    CodeLength = codeLength,
                    PhoneNumber = MaskPhone(phoneNumber),
                    ExpirationMinutes = expirationMinutes
                });
            }

            // Generate code
            var code = GenerateCode(codeLength);

            // Store in state
            context.SetData("passwordless_phone", phoneNumber);
            context.SetData("passwordless_code", code);
            context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));
            context.SetData("passwordless_user_exists", (user != null).ToString().ToLower());

            // Send SMS
            if (smsService == null)
            {
                logger.LogError(
                    "Passwordless SMS requested for {Phone} but no ISmsSender/ISmsService is registered. " +
                    "Call AddMessaging(configuration) (or AddSmsSender) in startup.",
                    MaskPhone(phoneNumber));
            }
            else
            {
                var message = BuildSmsMessage(messageTemplate, code, expirationMinutes);
                var result = await smsService.SendAsync(phoneNumber, message, cancellationToken);
                if (!result.Success)
                {
                    logger.LogError(
                        "Passwordless SMS send failed for {Phone}: {Error}",
                        MaskPhone(phoneNumber), result.Error);
                }
                else
                {
                    logger.LogInformation("Passwordless SMS sent to {Phone}", MaskPhone(phoneNumber));
                }
            }

            return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
            {
                CodeLength = codeLength,
                PhoneNumber = MaskPhone(phoneNumber),
                ExpirationMinutes = expirationMinutes
            });
        }

        // Handle code verification
        var submittedCode = context.GetInput("code");
        if (!string.IsNullOrEmpty(submittedCode))
        {
            var storedPhone = context.GetData<string>("passwordless_phone");
            var storedCode = context.GetData<string>("passwordless_code");
            var sentAt = context.GetData<string>("passwordless_sent_at");

            if (string.IsNullOrEmpty(storedPhone) || string.IsNullOrEmpty(storedCode))
            {
                return StepHandlerResult.Fail("session_expired", "Session expired. Please try again.");
            }

            // Check expiration
            if (DateTime.TryParse(sentAt, out var sentTime) &&
                DateTime.UtcNow - sentTime > TimeSpan.FromMinutes(expirationMinutes))
            {
                logger.LogWarning("Passwordless SMS code expired");
                return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
                {
                    CodeLength = codeLength,
                    PhoneNumber = MaskPhone(storedPhone),
                    ExpirationMinutes = expirationMinutes,
                    ErrorMessage = "Code has expired. Please request a new one."
                });
            }

            // Verify code
            var isValid = CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(storedCode), System.Text.Encoding.UTF8.GetBytes(submittedCode.Trim()));

            if (!isValid)
            {
                var attempts = context.GetData<int?>("passwordless_attempts") ?? 0;
                attempts++;
                context.SetData("passwordless_attempts", attempts);

                if (maxAttempts > 0 && attempts >= maxAttempts)
                {
                    logger.LogWarning(
                        "Passwordless SMS verification exceeded max attempts ({MaxAttempts}) for {Phone}",
                        maxAttempts, MaskPhone(storedPhone));
                    ClearPasswordlessState(context);
                    return StepHandlerResult.Fail(
                        "too_many_attempts",
                        "Too many invalid attempts. Please start over.");
                }

                logger.LogWarning("Invalid passwordless SMS code submitted (attempt {Attempt}/{MaxAttempts})",
                    attempts, maxAttempts);
                return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
                {
                    CodeLength = codeLength,
                    PhoneNumber = MaskPhone(storedPhone),
                    ExpirationMinutes = expirationMinutes,
                    ErrorMessage = maxAttempts > 0
                        ? $"Invalid code. {Math.Max(0, maxAttempts - attempts)} attempt(s) remaining."
                        : "Invalid code. Please try again."
                });
            }

            // Code is valid
            context.SetData("phone_verified", "true");

            if (recipientPhone != null)
            {
                logger.LogInformation("Link recipient verified their phone {Phone}", MaskPhone(storedPhone));
                ClearPasswordlessState(context);
                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["phone_number"] = storedPhone,
                    ["phone_number_verified"] = true
                });
            }

            var user = await userService.FindByPhoneAsync(storedPhone, cancellationToken);
            if (user != null)
            {
                // Existing user - authenticate
                context.SetAuthenticated(user.Id, "sms");
                context.SetData("amr", "sms");

                logger.LogInformation("Passwordless SMS login successful for user {UserId}", user.Id);

                ClearPasswordlessState(context);

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["sub"] = user.Id,
                    ["phone_number"] = storedPhone,
                    ["phone_number_verified"] = true,
                    ["amr"] = "sms"
                });
            }
            else if (allowSignUp)
            {
                // New user - store phone for sign-up flow
                context.SetData("verified_phone", storedPhone);

                logger.LogInformation("Phone verified for sign-up: {Phone}", MaskPhone(storedPhone));

                ClearPasswordlessState(context);

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["verified_phone"] = storedPhone,
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
            var storedPhone = context.GetData<string>("passwordless_phone");
            if (!string.IsNullOrEmpty(storedPhone))
            {
                // Remove old code and regenerate
                context.JourneyData.Remove("passwordless_code");
                context.JourneyData.Remove("passwordless_sent_at");

                var code = GenerateCode(codeLength);
                context.SetData("passwordless_code", code);
                context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));

                // Send SMS
                if (smsService == null)
                {
                    logger.LogError(
                        "Passwordless SMS resend requested for {Phone} but no ISmsSender/ISmsService is registered.",
                        MaskPhone(storedPhone));
                }
                else
                {
                    var message = BuildSmsMessage(messageTemplate, code, expirationMinutes);
                    var result = await smsService.SendAsync(storedPhone, message, cancellationToken);
                    if (!result.Success)
                    {
                        logger.LogError(
                            "Passwordless SMS resend failed for {Phone}: {Error}",
                            MaskPhone(storedPhone), result.Error);
                    }
                    else
                    {
                        logger.LogInformation("Passwordless SMS code resent to {Phone}", MaskPhone(storedPhone));
                    }
                }

                return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
                {
                    CodeLength = codeLength,
                    PhoneNumber = MaskPhone(storedPhone),
                    ExpirationMinutes = expirationMinutes,
                    SuccessMessage = "A new code has been sent to your phone."
                });
            }
        }

        // Re-entry after the code went to the link's recipient: the code screen, not a number to type.
        if (recipientPhone != null && !string.IsNullOrEmpty(pendingPhone))
        {
            return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
            {
                CodeLength = codeLength,
                PhoneNumber = MaskPhone(pendingPhone),
                ExpirationMinutes = expirationMinutes
            });
        }

        // Initial state - show phone input
        return StepHandlerResult.ShowUi("Journey/_PasswordlessSms", new PasswordlessSmsViewModel
        {
            AllowSignUp = allowSignUp
        });
    }

    /// <summary>
    /// Build the SMS body. When <paramref name="template"/> is set on the
    /// step config, run a small placeholder substitution; otherwise fall
    /// back to the platform default text.
    /// </summary>
    private static string BuildSmsMessage(string? template, string code, int expirationMinutes)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return $"Your verification code is: {code}. It expires in {expirationMinutes} minutes.";
        }

        return template
            .Replace("{code}", code)
            .Replace("{expirationMinutes}", expirationMinutes.ToString());
    }

    private static void ClearPasswordlessState(StepExecutionContext context)
    {
        context.JourneyData.Remove("passwordless_phone");
        context.JourneyData.Remove("passwordless_code");
        context.JourneyData.Remove("passwordless_sent_at");
        context.JourneyData.Remove("passwordless_user_exists");
    }

    private static string GenerateCode(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString()));

    private static string NormalizePhoneNumber(string phone)
    {
        var normalized = new string(phone.Where(c => char.IsDigit(c)).ToArray());
        if (phone.StartsWith("+"))
            normalized = "+" + normalized;
        return normalized;
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4) return phone;
        return "***" + phone[^4..];
    }
}

#region ViewModels

public class PasswordlessSmsViewModel
{
    public bool AllowSignUp { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PhoneNumber { get; set; }
}

public class PasswordlessSmsVerifyViewModel
{
    public string PhoneNumber { get; set; } = null!;
    public int CodeLength { get; set; } = 6;
    public int ExpirationMinutes { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

#endregion
