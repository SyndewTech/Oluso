using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Models;
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

    // Cookie carries a data-protected payload of "{userId}|{tenantId}|{expiryUnix}".
    // Validated against the current user/tenant — copying the cookie to a
    // different user or tenant won't replay.
    private const string TrustedDeviceCookieName = "oluso_mfa_td";
    private const string TrustedDeviceProtectorPurpose = "Oluso.Mfa.TrustedDevice.v1";

    /// <summary>Methods offered when the step doesn't list its own.</summary>
    public static readonly IReadOnlyList<string> DefaultMethods = ["totp", "email"];

    private static List<string> AllowedMethods(StepExecutionContext context) =>
        context.GetConfig<List<string>>("methods", null) is { Count: > 0 } configured ? configured : DefaultMethods.ToList();

    /// <summary>Set when this journey's user has just verified a new MFA method.</summary>
    public const string SetupVerifiedDataKey = "mfa_setup_verified_user";

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
        var allowedMethods = AllowedMethods(context);
        var allowSetup = context.GetConfig("allowSetup", true);
        var rememberDevice = context.GetConfig("rememberDevice", true);

        // Check if user has MFA enabled
        var hasMfa = user.TwoFactorEnabled;

        // A journey run for a signed-in user can accept the MFA that session already passed,
        // within the configured window. 0 (the default) always asks again.
        var reuseSessionMfaSeconds = context.GetConfig("reuseSessionMfaSeconds", 0);
        if (await IsSessionMfaFreshAsync(context, user.Id, reuseSessionMfaSeconds))
        {
            logger.LogInformation("MFA met for user {UserId} by the current session", user.Id);
            context.SetData("amr", "mfa");
            context.SetData("mfa_provider", "session");
            return StepHandlerResult.SkipOrBranch(context, "mfa_session");
        }

        // ===== SETUP FLOW =====

        // Handle setup method selection
        var setupMethod = context.GetInput("setup_method");
        if (!string.IsNullOrEmpty(setupMethod))
        {
            return await HandleSetupMethodSelectionAsync(context, user, mfaService, logger, setupMethod, allowedMethods!, cancellationToken);
        }

        // Handle TOTP setup verification
        var setupTotpCode = context.GetInput("setup_totp_code");
        if (!string.IsNullOrEmpty(setupTotpCode))
        {
            return await HandleTotpSetupVerificationAsync(context, user, mfaService, logger, setupTotpCode, cancellationToken);
        }

        // Handle recovery codes confirmation. Only completes MFA right after this user
        // verified a new method in this journey; on its own the field proves nothing.
        if (!string.IsNullOrEmpty(context.GetInput("recovery_codes_confirmed"))
            && string.Equals(context.GetData<string>(SetupVerifiedDataKey), user.Id, StringComparison.Ordinal))
        {
            context.JourneyData.Remove(SetupVerifiedDataKey);
            context.SetData("amr", "mfa");
            logger.LogInformation("MFA setup completed for user {UserId}, recovery codes confirmed", user.Id);

            var outputData = new Dictionary<string, object>
            {
                ["mfa_setup"] = "complete"
            };
            return StepHandlerResult.SuccessOrBranch(context, outputData, "mfa_setup_complete");
        }

        // Handle email setup: code submission or resend
        if (context.GetData<bool>("mfa_setup_email_pending"))
        {
            if (string.Equals(context.GetInput("setup_email"), "resend", StringComparison.OrdinalIgnoreCase))
            {
                if (mfaService != null)
                {
                    await mfaService.SendVerificationCodeAsync(user.Id, "email", cancellationToken);
                }
                return StepHandlerResult.ShowUi("Journey/_MfaEmailSetup", new MfaEmailSetupViewModel { Email = MaskEmail(user.Email ?? "") });
            }

            var setupEmailCode = context.GetInput("setup_email_code");
            if (!string.IsNullOrEmpty(setupEmailCode))
            {
                return await HandleEmailSetupVerificationAsync(context, user, mfaService, logger, setupEmailCode, cancellationToken);
            }
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
            var allowedMethodsForBack = AllowedMethods(context);
            return StepHandlerResult.ShowUi("Journey/_MfaSetup", new MfaSetupViewModel
            {
                UserId = user.Id,
                AvailableMethods = allowedMethodsForBack!
            });
        }

        // ===== VERIFICATION FLOW =====

        // Handle recovery code flow
        var useRecovery = context.GetInput("use_recovery");
        if (!string.IsNullOrEmpty(useRecovery) && useRecovery.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return StepHandlerResult.ShowUi("Journey/_MfaRecoveryVerify", new MfaRecoveryVerifyViewModel());
        }

        // Handle recovery code submission
        var useRecoveryCode = context.GetInput("use_recovery_code");
        var recoveryCode = context.GetInput("recovery_code");
        if (!string.IsNullOrEmpty(useRecoveryCode) && !string.IsNullOrEmpty(recoveryCode))
        {
            return await HandleRecoveryCodeVerificationAsync(context, user, mfaService, eventService, tenantContext, logger, recoveryCode, cancellationToken);
        }

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
            return StepHandlerResult.SkipOrBranch(context, "mfa_not_required");
        }

        // MFA required but user hasn't set it up.
        // allowSetup gates whether we offer to enrol them inline. When false
        // and the user has no MFA, we have to bounce — only an admin can
        // provision MFA for them.
        if (!hasMfa)
        {
            if (requireMfa)
            {
                if (!allowSetup)
                {
                    logger.LogWarning("MFA required but user {UserId} has no MFA and inline setup is disabled", user.Id);
                    return StepHandlerResult.Fail("mfa_setup_required",
                        "Multi-factor authentication is required for this account. Please contact your administrator to enable it.");
                }

                return StepHandlerResult.ShowUi("Journey/_MfaSetup", new MfaSetupViewModel
                {
                    UserId = userId,
                    AvailableMethods = allowedMethods!
                });
            }
            return StepHandlerResult.SkipOrBranch(context, "mfa_not_required");
        }

        // User has MFA configured. Honour trusted-device cookie if enabled —
        // skip verification when the cookie validates for this user/tenant.
        if (rememberDevice && IsTrustedDeviceValid(context, user.Id, tenantContext.TenantId, logger))
        {
            logger.LogInformation("MFA bypassed for user {UserId} via trusted device cookie", user.Id);
            context.SetData("amr", "mfa");
            context.SetData("mfa_provider", "trusted_device");
            return StepHandlerResult.SkipOrBranch(context, "mfa_trusted_device");
        }

        // User has MFA configured - show verification UI
        // Default to TOTP/authenticator
        return StepHandlerResult.ShowUi("Journey/_MfaVerify", new MfaVerifyViewModel
        {
            Provider = "totp",
            CanRememberDevice = rememberDevice
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

                context.SetData("mfa_setup_key", setupResult.SharedKey ?? "");

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

            return StepHandlerResult.FailedAttemptUi("Journey/_MfaTotpSetup", new MfaTotpSetupViewModel
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
        context.SetData(SetupVerifiedDataKey, user.Id);

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
        if (IsLockedOut(user))
        {
            return StepHandlerResult.Fail("account_locked", "This account is temporarily locked. Please try again later.");
        }

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

            // A wrong code costs a failed sign-in attempt, so guessing across journeys locks the account.
            await userService.RecordFailedLoginAsync(user.Id, cancellationToken);

            return StepHandlerResult.FailedAttemptUi("Journey/_MfaVerify", new MfaVerifyViewModel
            {
                Provider = provider,
                ErrorMessage = "Invalid verification code"
            });
        }

        // MFA successful
        context.SetData("amr", "mfa");
        context.SetData("mfa_provider", provider.ToLower());

        // Issue trusted-device cookie if the user opted in and the step
        // allows it. Read flags freshly here — caller already validated
        // them, but verification path may be reached without the initial
        // routing pass (e.g. provider switch).
        var rememberDevice = context.GetConfig("rememberDevice", true);
        var rememberDeviceDays = context.GetConfig("rememberDeviceDays", 30);
        var rememberInput = context.GetInput("remember_device");
        if (rememberDevice && !string.IsNullOrEmpty(rememberInput) &&
            (rememberInput.Equals("true", StringComparison.OrdinalIgnoreCase) || rememberInput == "1" || rememberInput.Equals("on", StringComparison.OrdinalIgnoreCase)))
        {
            IssueTrustedDeviceCookie(context, user.Id, tenantContext.TenantId, rememberDeviceDays, logger);
        }

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

        var outputData = new Dictionary<string, object>
        {
            ["amr"] = provider.ToLower()
        };
        return StepHandlerResult.SuccessOrBranch(context, outputData, "mfa_verified");
    }

    private async Task<StepHandlerResult> HandleRecoveryCodeVerificationAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        IMfaService? mfaService,
        IOlusoEventService? eventService,
        ITenantContext tenantContext,
        ILogger logger,
        string recoveryCode,
        CancellationToken cancellationToken)
    {
        if (mfaService == null)
        {
            return StepHandlerResult.Fail("mfa_service_unavailable", "MFA service not configured");
        }

        if (IsLockedOut(user))
        {
            return StepHandlerResult.Fail("account_locked", "This account is temporarily locked. Please try again later.");
        }

        var isValid = await mfaService.VerifyRecoveryCodeAsync(user.Id, recoveryCode, cancellationToken);

        if (!isValid)
        {
            logger.LogWarning("Recovery code verification failed for user {UserId}", user.Id);

            // Raise MFA failed event
            if (eventService != null)
            {
                await eventService.RaiseAsync(new MfaCompletedEvent
                {
                    TenantId = tenantContext.TenantId,
                    SubjectId = user.Id,
                    MfaMethod = "recovery_code",
                    Success = false
                }, cancellationToken);
            }

            await context.ServiceProvider.GetRequiredService<IOlusoUserService>()
                .RecordFailedLoginAsync(user.Id, cancellationToken);

            return StepHandlerResult.FailedAttemptUi("Journey/_MfaRecoveryVerify", new MfaRecoveryVerifyViewModel
            {
                ErrorMessage = "Invalid recovery code. Please try again."
            });
        }

        // Recovery code valid
        context.SetData("amr", "mfa");
        context.SetData("mfa_provider", "recovery_code");

        // Raise MFA success event
        if (eventService != null)
        {
            await eventService.RaiseAsync(new MfaCompletedEvent
            {
                TenantId = tenantContext.TenantId,
                SubjectId = user.Id,
                MfaMethod = "recovery_code",
                Success = true
            }, cancellationToken);
        }

        logger.LogInformation("Recovery code verification successful for user {UserId}", user.Id);

        var outputData = new Dictionary<string, object>
        {
            ["amr"] = "recovery_code"
        };
        return StepHandlerResult.SuccessOrBranch(context, outputData, "mfa_recovery_used");
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

    private async Task<StepHandlerResult> HandleEmailSetupVerificationAsync(
        StepExecutionContext context,
        OlusoUserInfo user,
        IMfaService? mfaService,
        ILogger logger,
        string code,
        CancellationToken cancellationToken)
    {
        if (mfaService == null)
        {
            return StepHandlerResult.Fail("mfa_service_unavailable", "MFA service not configured");
        }

        if (!await mfaService.VerifyEmailCodeAsync(user.Id, code.Trim(), cancellationToken))
        {
            logger.LogWarning("Email MFA setup verification failed for user {UserId}", user.Id);
            return StepHandlerResult.FailedAttemptUi("Journey/_MfaEmailSetup", new MfaEmailSetupViewModel
            {
                Email = MaskEmail(user.Email ?? ""),
                ErrorMessage = "Invalid verification code. Please try again."
            });
        }

        var enableResult = await mfaService.EnableMfaAsync(user.Id, "email", cancellationToken);
        if (!enableResult.Succeeded)
        {
            return StepHandlerResult.Fail("mfa_enable_failed", "Failed to enable email MFA");
        }

        logger.LogInformation("Email MFA setup completed for user {UserId}", user.Id);

        context.JourneyData.Remove("mfa_setup_email_pending");
        context.SetData(SetupVerifiedDataKey, user.Id);

        return StepHandlerResult.ShowUi("Journey/_MfaRecoveryCodes", new MfaRecoveryCodesViewModel
        {
            RecoveryCodes = enableResult.RecoveryCodes?.ToList() ?? new List<string>(),
            Provider = "Email"
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
            return StepHandlerResult.FailedAttemptUi("Journey/_MfaSmsSetup", new MfaSmsSetupViewModel
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
        context.SetData(SetupVerifiedDataKey, user.Id);

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

    private static bool IsLockedOut(OlusoUserInfo user) =>
        user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow;

    private static async Task<bool> IsSessionMfaFreshAsync(StepExecutionContext context, string userId, int maxAgeSeconds)
    {
        if (maxAgeSeconds <= 0) return false;

        // Only a journey started from this user's session may lean on that session.
        if (context.GetDataString(JourneyStartContext.SessionUserIdDataKey) != userId) return false;

        var httpContext = context.ServiceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext == null) return false;

        var session = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = session.Principal;
        if (!session.Succeeded || principal == null) return false;

        var sessionUserId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;
        if (sessionUserId != userId) return false;

        if (!principal.FindAll(OidcConstants.StandardClaims.Amr).Any(c => c.Value == "mfa")) return false;

        if (!long.TryParse(principal.FindFirst(OidcConstants.StandardClaims.AuthTime)?.Value, out var authTimeUnix)) return false;
        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
        return age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(maxAgeSeconds);
    }

    private static bool IsTrustedDeviceValid(StepExecutionContext context, string userId, string? tenantId, ILogger logger)
    {
        var httpContext = context.ServiceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext == null) return false;
        if (!httpContext.Request.Cookies.TryGetValue(TrustedDeviceCookieName, out var raw) || string.IsNullOrEmpty(raw))
            return false;

        var protector = context.ServiceProvider.GetService<IDataProtectionProvider>()?.CreateProtector(TrustedDeviceProtectorPurpose);
        if (protector == null) return false;

        try
        {
            var payload = protector.Unprotect(raw);
            var parts = payload.Split('|');
            if (parts.Length != 3) return false;
            if (!string.Equals(parts[0], userId, StringComparison.Ordinal)) return false;
            if (!string.Equals(parts[1], tenantId ?? string.Empty, StringComparison.Ordinal)) return false;
            if (!long.TryParse(parts[2], out var expiryUnix)) return false;
            return DateTimeOffset.FromUnixTimeSeconds(expiryUnix) > DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Trusted-device cookie failed validation for user {UserId}", userId);
            return false;
        }
    }

    private static void IssueTrustedDeviceCookie(StepExecutionContext context, string userId, string? tenantId, int days, ILogger logger)
    {
        var httpContext = context.ServiceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext == null) return;

        var protector = context.ServiceProvider.GetService<IDataProtectionProvider>()?.CreateProtector(TrustedDeviceProtectorPurpose);
        if (protector == null) return;

        if (days < 1) days = 1;
        var expiry = DateTimeOffset.UtcNow.AddDays(days);
        var payload = $"{userId}|{tenantId ?? string.Empty}|{expiry.ToUnixTimeSeconds()}";

        try
        {
            var protectedValue = protector.Protect(payload);
            httpContext.Response.Cookies.Append(TrustedDeviceCookieName, protectedValue, new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = expiry,
                IsEssential = true
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to issue trusted-device cookie for user {UserId}", userId);
        }
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
    public bool CanRememberDevice { get; set; }
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

    /// <summary>The QR code for <see cref="AuthenticatorUri"/>, rendered on the server as a PNG data URI.</summary>
    public string QrCodeImage => Oluso.Core.Security.QrCodeImages.ToPngDataUri(AuthenticatorUri);
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

public class MfaRecoveryVerifyViewModel
{
    public string? ErrorMessage { get; set; }
}

#endregion
