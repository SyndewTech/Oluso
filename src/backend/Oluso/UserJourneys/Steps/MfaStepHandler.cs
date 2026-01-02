using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using System.Text;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles multi-factor authentication step including setup and verification.
/// Supports TOTP authenticator apps, SMS, and email verification.
/// </summary>
public class MfaStepHandler : IStepHandler
{
    public string StepType => "mfa";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var mfaService = context.ServiceProvider.GetService<IMfaService>();
        var eventService = context.ServiceProvider.GetService<IOlusoEventService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<MfaStepHandler>>();

        // Get user from journey state
        var userId = context.UserId ?? context.GetData<string>("mfa_user_id");

        if (string.IsNullOrEmpty(userId))
        {
            return StepHandlerResult.Fail("mfa_no_user", "No authenticated user for MFA");
        }

        var user = await userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return StepHandlerResult.Fail("mfa_user_not_found", "User not found");
        }

        // Check configuration
        var requireMfa = context.GetConfig("required", false);
        var allowedMethods = context.GetConfig<List<string>>("methods.cshtml", new List<string> { "totp", "email" });

        // Check if user has MFA enabled
        var hasMfa = user.TwoFactorEnabled;

        // ===== SETUP FLOW =====

        // Handle setup method selection
        var setupMethod = context.GetInput("setup_method");
        if (!string.IsNullOrEmpty(setupMethod))
        {
            return await HandleSetupMethodSelectionAsync(context, user, mfaService, logger, setupMethod, allowedMethods, cancellationToken);
        }

        // Handle TOTP setup verification
        var setupTotpCode = context.GetInput("setup_totp_code");
        if (!string.IsNullOrEmpty(setupTotpCode))
        {
            return await HandleTotpSetupVerificationAsync(context, user, mfaService, logger, setupTotpCode, cancellationToken);
        }

        // Handle recovery codes confirmation
        if (!string.IsNullOrEmpty(context.GetInput("recovery_codes_confirmed")))
        {
            context.SetData("amr", "mfa");
            logger.LogInformation("MFA setup completed for user {UserId}, recovery codes confirmed", user.Id);

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["mfa_setup"] = "complete"
            });
        }

        // Handle SMS setup: phone number submission
        if (!string.IsNullOrEmpty(context.GetInput("setup_sms_phone")))
        {
            return await HandleSmsPhoneSubmissionAsync(context, user, mfaService, logger, cancellationToken);
        }

        // Handle SMS setup: verification code submission
        if (!string.IsNullOrEmpty(context.GetInput("setup_sms_verify")))
        {
            return await HandleSmsSetupVerificationAsync(context, user, mfaService, logger, cancellationToken);
        }

        // Handle SMS setup: resend code
        if (!string.IsNullOrEmpty(context.GetInput("setup_sms_resend")))
        {
            return await HandleSmsResendAsync(context, user, mfaService, logger, cancellationToken);
        }

        // Handle back to method selection
        if (!string.IsNullOrEmpty(context.GetInput("setup_method_back")))
        {
            var allowedMethodsForBack = context.GetConfig<List<string>>("methods.cshtml", new List<string> { "totp", "email", "sms" });
            return StepHandlerResult.ShowUi("Journey/_MfaSetup", new MfaSetupViewModel
            {
                UserId = user.Id,
                AvailableMethods = allowedMethodsForBack
            });
        }

        // ===== VERIFICATION FLOW =====

        // Handle provider selection
        var provider = context.GetInput("provider");
        var code = context.GetInput("code");

        if (!string.IsNullOrEmpty(provider) && string.IsNullOrEmpty(code))
        {
            // User selected a provider, show verification UI
            if (mfaService != null && (provider.Equals("email", StringComparison.OrdinalIgnoreCase) ||
                                        provider.Equals("sms", StringComparison.OrdinalIgnoreCase)))
            {
                // Send code
                await mfaService.SendVerificationCodeAsync(userId, provider, cancellationToken);
                logger.LogInformation("MFA code sent via {Provider} for user {UserId}", provider, userId);
            }

            return StepHandlerResult.ShowUi("Journey/_MfaVerify", new MfaVerifyViewModel
            {
                Provider = provider
            });
        }

        // Handle verification code submission
        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(provider))
        {
            return await HandleMfaVerificationAsync(context, user, userService, mfaService, logger, code, provider, cancellationToken);
        }

        // ===== INITIAL ROUTING =====

        // No MFA configured and not required - skip
        if (!hasMfa && !requireMfa)
        {
            return StepHandlerResult.Skip();
        }

        // MFA required but user hasn't set it up - redirect to setup
        if (!hasMfa)
        {
            if (requireMfa)
            {
                return StepHandlerResult.ShowUi("Journey/_MfaSetup", new MfaSetupViewModel
                {
                    UserId = userId,
                    AvailableMethods = allowedMethods
                });
            }
            return StepHandlerResult.Skip();
        }

        // User has MFA configured - show verification UI
        // Default to TOTP/authenticator
        return StepHandlerResult.ShowUi("Journey/_MfaVerify", new MfaVerifyViewModel
        {
            Provider = "totp"
        });
    }

    private async Task<StepHandlerResult> HandleSetupMethodSelectionAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        IMfaService? mfaService,
        ILogger logger,
        string setupMethod,
        List<string> allowedMethods,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("User {UserId} selected MFA setup method: {Method}", user.Id, setupMethod);

        switch (setupMethod.ToLower())
        {
            case "totp":
            case "authenticator":
                if (mfaService == null)
                {
                    return StepHandlerResult.Fail("mfa_service_unavailable", "MFA service not configured");
                }

                var setupResult = await mfaService.GenerateTotpSetupAsync(user.Id, cancellationToken);
                if (!setupResult.Succeeded)
                {
                    return StepHandlerResult.Fail("mfa_setup_error", "Failed to generate authenticator key");
                }

                context.SetData("mfa_setup_key", setupResult.SharedKey);

                return StepHandlerResult.ShowUi("Journey/_MfaTotpSetup", new MfaTotpSetupViewModel
                {
                    SharedKey = FormatKey(setupResult.SharedKey ?? ""),
                    AuthenticatorUri = setupResult.AuthenticatorUri ?? "",
                    UserEmail = user.Email ?? user.Username
                });

            case "email":
                if (string.IsNullOrEmpty(user.Email))
                {
                    return StepHandlerResult.ShowUi("Journey/_MfaSetup", new MfaSetupViewModel
                    {
                        UserId = user.Id,
                        AvailableMethods = allowedMethods,
                        ErrorMessage = "Email address is required for email MFA."
                    });
                }

                if (mfaService != null)
                {
                    await mfaService.SendVerificationCodeAsync(user.Id, "email", cancellationToken);
                }
                context.SetData("mfa_setup_email_pending", true);

                return StepHandlerResult.ShowUi("Journey/_MfaEmailSetup", new MfaEmailSetupViewModel
                {
                    Email = MaskEmail(user.Email)
                });

            case "sms":
            case "phone":
                // Show phone number entry form for SMS setup
                return StepHandlerResult.ShowUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
                {
                    PhoneNumber = user.PhoneNumber
                });

            default:
                return StepHandlerResult.ShowUi("Journey/_MfaSetup", new MfaSetupViewModel
                {
                    UserId = user.Id,
                    AvailableMethods = allowedMethods,
                    ErrorMessage = $"Unknown setup method: {setupMethod}"
                });
        }
    }

    private async Task<StepHandlerResult> HandleTotpSetupVerificationAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        IMfaService? mfaService,
        ILogger logger,
        string code,
        CancellationToken cancellationToken)
    {
        var storedKey = context.GetData<string>("mfa_setup_key");
        if (string.IsNullOrEmpty(storedKey))
        {
            return StepHandlerResult.Fail("mfa_setup_error", "Setup session expired. Please try again.");
        }

        if (mfaService == null)
        {
            return StepHandlerResult.Fail("mfa_service_unavailable", "MFA service not configured");
        }

        // Verify the code
        var isValid = await mfaService.VerifyTotpCodeAsync(user.Id, code, cancellationToken);

        if (!isValid)
        {
            logger.LogWarning("TOTP setup verification failed for user {UserId}", user.Id);

            var setupResult = await mfaService.GenerateTotpSetupAsync(user.Id, cancellationToken);

            return StepHandlerResult.ShowUi("Journey/_MfaTotpSetup", new MfaTotpSetupViewModel
            {
                SharedKey = FormatKey(storedKey),
                AuthenticatorUri = setupResult.AuthenticatorUri ?? "",
                UserEmail = user.Email ?? user.Username,
                ErrorMessage = "Invalid verification code. Please try again."
            });
        }

        // Enable MFA
        var enableResult = await mfaService.EnableMfaAsync(user.Id, "totp", cancellationToken);
        if (!enableResult.Succeeded)
        {
            return StepHandlerResult.Fail("mfa_enable_failed", "Failed to enable MFA");
        }

        logger.LogInformation("TOTP setup completed for user {UserId}", user.Id);

        // Clear setup state
        context.JourneyData.Remove("mfa_setup_key");

        // Show recovery codes
        return StepHandlerResult.ShowUi("Journey/_MfaRecoveryCodes", new MfaRecoveryCodesViewModel
        {
            RecoveryCodes = enableResult.RecoveryCodes?.ToList() ?? new List<string>(),
            Provider = "Authenticator"
        });
    }

    private async Task<StepHandlerResult> HandleMfaVerificationAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        IOlusoUserService userService,
        IMfaService? mfaService,
        ILogger logger,
        string code,
        string provider,
        CancellationToken cancellationToken)
    {
        bool isValid;

        if (mfaService != null)
        {
            isValid = provider.ToLower() switch
            {
                "totp" or "authenticator" => await mfaService.VerifyTotpCodeAsync(user.Id, code, cancellationToken),
                "email" => await mfaService.VerifyEmailCodeAsync(user.Id, code, cancellationToken),
                "sms" or "phone" => await mfaService.VerifySmsCodeAsync(user.Id, code, cancellationToken),
                _ => await userService.ValidateMfaCodeAsync(user.Id, code, cancellationToken)
            };
        }
        else
        {
            isValid = await userService.ValidateMfaCodeAsync(user.Id, code, cancellationToken);
        }

        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var eventService = context.ServiceProvider.GetService<IOlusoEventService>();

        if (!isValid)
        {
            logger.LogWarning("MFA verification failed for user {UserId} via {Provider}", user.Id, provider);

            // Raise MFA failed event
            if (eventService != null)
            {
                await eventService.RaiseAsync(new MfaCompletedEvent
                {
                    TenantId = tenantContext.TenantId,
                    SubjectId = user.Id,
                    MfaMethod = provider.ToLower(),
                    Success = false
                }, cancellationToken);
            }

            return StepHandlerResult.ShowUi("Journey/_MfaVerify", new MfaVerifyViewModel
            {
                Provider = provider,
                ErrorMessage = "Invalid verification code"
            });
        }

        // MFA successful
        context.SetData("amr", "mfa");
        context.SetData("mfa_provider", provider.ToLower());

        // Raise MFA success event
        if (eventService != null)
        {
            await eventService.RaiseAsync(new MfaCompletedEvent
            {
                TenantId = tenantContext.TenantId,
                SubjectId = user.Id,
                MfaMethod = provider.ToLower(),
                Success = true
            }, cancellationToken);
        }

        logger.LogInformation("MFA verification successful for user {UserId} via {Provider}", user.Id, provider);

        return StepHandlerResult.Success(new Dictionary<string, object>
        {
            ["amr"] = provider.ToLower()
        });
    }

    private async Task<StepHandlerResult> HandleSmsPhoneSubmissionAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        IMfaService? mfaService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var phoneNumber = context.GetInput("phone_number");

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return StepHandlerResult.ShowUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
            {
                PhoneNumber = phoneNumber,
                ErrorMessage = "Please enter a valid phone number."
            });
        }

        // Normalize phone number (basic cleanup)
        phoneNumber = phoneNumber.Trim();

        if (mfaService == null)
        {
            return StepHandlerResult.Fail("mfa_service_unavailable", "MFA service not configured");
        }

        // Store phone number and send verification code
        context.SetData("mfa_setup_phone", phoneNumber);

        var sendResult = await mfaService.SendVerificationCodeAsync(user.Id, "sms", cancellationToken);
        if (!sendResult)
        {
            logger.LogWarning("Failed to send SMS verification code to {Phone} for user {UserId}", MaskPhone(phoneNumber), user.Id);
            return StepHandlerResult.ShowUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
            {
                PhoneNumber = phoneNumber,
                ErrorMessage = "Failed to send verification code. Please check the phone number and try again."
            });
        }

        logger.LogInformation("SMS verification code sent to {Phone} for user {UserId}", MaskPhone(phoneNumber), user.Id);

        return StepHandlerResult.ShowUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
        {
            MaskedPhone = MaskPhone(phoneNumber),
            CodeSent = true
        });
    }

    private async Task<StepHandlerResult> HandleSmsSetupVerificationAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        IMfaService? mfaService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var code = context.GetInput("setup_sms_code");
        var storedPhone = context.GetData<string>("mfa_setup_phone");

        if (string.IsNullOrWhiteSpace(code))
        {
            return StepHandlerResult.ShowUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
            {
                MaskedPhone = MaskPhone(storedPhone ?? ""),
                CodeSent = true,
                ErrorMessage = "Please enter the verification code."
            });
        }

        if (mfaService == null)
        {
            return StepHandlerResult.Fail("mfa_service_unavailable", "MFA service not configured");
        }

        // Verify the code
        var isValid = await mfaService.VerifySmsCodeAsync(user.Id, code, cancellationToken);

        if (!isValid)
        {
            logger.LogWarning("SMS setup verification failed for user {UserId}", user.Id);
            return StepHandlerResult.ShowUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
            {
                MaskedPhone = MaskPhone(storedPhone ?? ""),
                CodeSent = true,
                ErrorMessage = "Invalid verification code. Please try again."
            });
        }

        // Enable MFA with SMS
        var enableResult = await mfaService.EnableMfaAsync(user.Id, "sms", cancellationToken);
        if (!enableResult.Succeeded)
        {
            return StepHandlerResult.Fail("mfa_enable_failed", "Failed to enable SMS MFA");
        }

        logger.LogInformation("SMS MFA setup completed for user {UserId}", user.Id);

        // Clear setup state
        context.JourneyData.Remove("mfa_setup_phone");

        // Show recovery codes
        return StepHandlerResult.ShowUi("Journey/_MfaRecoveryCodes", new MfaRecoveryCodesViewModel
        {
            RecoveryCodes = enableResult.RecoveryCodes?.ToList() ?? new List<string>(),
            Provider = "SMS"
        });
    }

    private async Task<StepHandlerResult> HandleSmsResendAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        IMfaService? mfaService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var storedPhone = context.GetData<string>("mfa_setup_phone");

        if (string.IsNullOrEmpty(storedPhone))
        {
            // No phone stored, go back to phone entry
            return StepHandlerResult.ShowUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
            {
                ErrorMessage = "Session expired. Please enter your phone number again."
            });
        }

        if (mfaService == null)
        {
            return StepHandlerResult.Fail("mfa_service_unavailable", "MFA service not configured");
        }

        var sendResult = await mfaService.SendVerificationCodeAsync(user.Id, "sms", cancellationToken);
        if (!sendResult)
        {
            logger.LogWarning("Failed to resend SMS verification code to {Phone} for user {UserId}", MaskPhone(storedPhone), user.Id);
        }
        else
        {
            logger.LogInformation("SMS verification code resent to {Phone} for user {UserId}", MaskPhone(storedPhone), user.Id);
        }

        return StepHandlerResult.ShowUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
        {
            MaskedPhone = MaskPhone(storedPhone),
            CodeSent = true
        });
    }

    private static string MaskPhone(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber)) return "";
        if (phoneNumber.Length <= 4) return "***" + phoneNumber;
        return "***" + phoneNumber[^4..];
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }
        return result.ToString().ToLowerInvariant();
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

public class MfaVerifyViewModel
{
    public string Provider { get; set; } = null!;
    public string? Hint { get; set; }
    public string? ErrorMessage { get; set; }
}

public class MfaSetupViewModel
{
    public string UserId { get; set; } = null!;
    public List<string> AvailableMethods { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class MfaTotpSetupViewModel
{
    public string SharedKey { get; set; } = null!;
    public string AuthenticatorUri { get; set; } = null!;
    public string UserEmail { get; set; } = null!;
    public string? ErrorMessage { get; set; }
}

public class MfaEmailSetupViewModel
{
    public string Email { get; set; } = null!;
    public string? ErrorMessage { get; set; }
}

public class MfaRecoveryCodesViewModel
{
    public List<string> RecoveryCodes { get; set; } = new();
    public string Provider { get; set; } = null!;
}

public class MfaSmsSetupViewModel
{
    public string? PhoneNumber { get; set; }
    public string? MaskedPhone { get; set; }
    public bool CodeSent { get; set; }
    public string? ErrorMessage { get; set; }
}

#endregion
