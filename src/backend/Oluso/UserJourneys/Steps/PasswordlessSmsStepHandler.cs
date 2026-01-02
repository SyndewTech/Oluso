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
/// </remarks>
public class PasswordlessSmsStepHandler : IStepHandler
{
    public string StepType => "passwordless_sms";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var smsService = context.ServiceProvider.GetService<ISmsService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<PasswordlessSmsStepHandler>>();

        var codeLength = context.GetConfig("codeLength", 6);
        var expirationMinutes = context.GetConfig("expirationMinutes", 10);
        var allowSignUp = context.GetConfig("allowSignUp", false);

        // Handle phone number submission
        var phoneNumber = context.GetInput("phone");
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

            var user = await userService.FindByPhoneAsync(phoneNumber, cancellationToken);

            // If user doesn't exist and sign-up is not allowed
            if (user == null && !allowSignUp)
            {
                // For security, don't reveal that the user doesn't exist
                logger.LogWarning("Passwordless SMS login attempted for non-existent phone");
                context.SetData("passwordless_phone", phoneNumber);
                context.SetData("passwordless_sent_at", DateTime.UtcNow.ToString("O"));

                return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
                {
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
            if (smsService != null)
            {
                var message = $"Your verification code is: {code}. It expires in {expirationMinutes} minutes.";
                await smsService.SendAsync(phoneNumber, message, cancellationToken);
            }

            logger.LogInformation("Passwordless SMS sent to {Phone}", MaskPhone(phoneNumber));

            return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
            {
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
                    PhoneNumber = MaskPhone(storedPhone),
                    ExpirationMinutes = expirationMinutes,
                    ErrorMessage = "Code has expired. Please request a new one."
                });
            }

            // Verify code
            var isValid = string.Equals(storedCode, submittedCode, StringComparison.OrdinalIgnoreCase);

            if (!isValid)
            {
                logger.LogWarning("Invalid passwordless SMS code submitted");
                return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
                {
                    PhoneNumber = MaskPhone(storedPhone),
                    ExpirationMinutes = expirationMinutes,
                    ErrorMessage = "Invalid code. Please try again."
                });
            }

            // Code is valid
            context.SetData("phone_verified", "true");

            var user = await userService.FindByPhoneAsync(storedPhone, cancellationToken);
            if (user != null)
            {
                // Existing user - authenticate
                context.UserId = user.Id;
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
                if (smsService != null)
                {
                    var message = $"Your verification code is: {code}. It expires in {expirationMinutes} minutes.";
                    await smsService.SendAsync(storedPhone, message, cancellationToken);
                }

                logger.LogInformation("Passwordless SMS code resent to {Phone}", MaskPhone(storedPhone));

                return StepHandlerResult.ShowUi("Journey/_PasswordlessSmsVerify", new PasswordlessSmsVerifyViewModel
                {
                    PhoneNumber = MaskPhone(storedPhone),
                    ExpirationMinutes = expirationMinutes,
                    SuccessMessage = "A new code has been sent to your phone."
                });
            }
        }

        // Initial state - show phone input
        return StepHandlerResult.ShowUi("Journey/_PasswordlessSms", new PasswordlessSmsViewModel
        {
            AllowSignUp = allowSignUp
        });
    }

    private static void ClearPasswordlessState(StepExecutionContext context)
    {
        context.JourneyData.Remove("passwordless_phone");
        context.JourneyData.Remove("passwordless_code");
        context.JourneyData.Remove("passwordless_sent_at");
        context.JourneyData.Remove("passwordless_user_exists");
    }

    private static string GenerateCode(int length)
    {
        var random = new Random();
        return string.Concat(Enumerable.Range(0, length).Select(_ => random.Next(0, 10).ToString()));
    }

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
    public int ExpirationMinutes { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

#endregion
