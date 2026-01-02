using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.Enterprise.Fido2.Steps;

/// <summary>
/// Handles FIDO2/WebAuthn passkey registration as a User Journey step.
/// This step should be placed after authentication to allow users to register passkeys.
/// </summary>
/// <remarks>
/// Use cases:
/// - Post-login passkey enrollment for users who don't have passkeys
/// - Prompt new users to set up a passkey after registration
/// - Step-up passkey registration for enhanced security
///
/// Configuration options:
/// - promptNewUsers: Show prompt to users without passkeys (default: true)
/// - promptExistingUsers: Show prompt to users with passkeys (default: false)
/// - skipIfHasPasskey: Skip if user already has at least one passkey (default: true)
/// - requireResidentKey: Require discoverable credentials (default: false)
/// - authenticatorType: Preferred authenticator type ("platform" or "cross-platform")
/// - maxPasskeys: Maximum passkeys per user before skipping (default: 10)
/// - title: Custom title for the registration UI
/// - description: Custom description for the registration UI
/// </remarks>
public class Fido2RegistrationStepHandler : IStepHandler
{
    public string StepType => "fido2_register";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var fido2Service = context.ServiceProvider.GetRequiredService<IFido2Service>();
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<Fido2RegistrationStepHandler>>();

        // This step requires an authenticated user
        if (string.IsNullOrEmpty(context.UserId))
        {
            logger.LogDebug("Skipping FIDO2 registration: no authenticated user");
            return StepHandlerResult.Continue();
        }

        var user = await userService.FindByIdAsync(context.UserId, cancellationToken);
        if (user == null)
        {
            logger.LogWarning("User {UserId} not found for FIDO2 registration", context.UserId);
            return StepHandlerResult.Continue();
        }

        // Check configuration
        var promptNewUsers = context.GetConfig("promptNewUsers", true);
        var promptExistingUsers = context.GetConfig("promptExistingUsers", false);
        var skipIfHasPasskey = context.GetConfig("skipIfHasPasskey", true);
        var maxPasskeys = context.GetConfig("maxPasskeys", 10);

        // Get existing credentials
        var existingCredentials = await fido2Service.GetCredentialsAsync(context.UserId, cancellationToken);
        var credentialCount = existingCredentials.Count();

        // Skip logic
        if (credentialCount >= maxPasskeys)
        {
            logger.LogDebug("User {UserId} has maximum passkeys ({Count}), skipping registration", context.UserId, credentialCount);
            return StepHandlerResult.Continue();
        }

        if (credentialCount > 0 && skipIfHasPasskey && !promptExistingUsers)
        {
            logger.LogDebug("User {UserId} already has passkeys and skipIfHasPasskey=true, skipping", context.UserId);
            return StepHandlerResult.Continue();
        }

        if (credentialCount == 0 && !promptNewUsers)
        {
            logger.LogDebug("User {UserId} has no passkeys but promptNewUsers=false, skipping", context.UserId);
            return StepHandlerResult.Continue();
        }

        // Check if user explicitly skipped
        var action = context.GetInput("action");
        if (action == "skip")
        {
            logger.LogDebug("User {UserId} skipped FIDO2 registration", context.UserId);
            context.SetData("fido2_registration_skipped", true);
            return StepHandlerResult.Continue();
        }

        // Handle registration response
        var attestationResponse = context.GetInput("attestationResponse");
        if (!string.IsNullOrEmpty(attestationResponse))
        {
            return await HandleRegistrationResponseAsync(
                context, fido2Service, user, attestationResponse, logger, cancellationToken);
        }

        // Check if user wants to register a passkey
        if (action == "register")
        {
            return await InitiateRegistrationAsync(
                context, fido2Service, user, tenantContext, logger, cancellationToken);
        }

        // Show registration prompt UI
        return ShowRegistrationPrompt(context, tenantContext, credentialCount);
    }

    private StepHandlerResult ShowRegistrationPrompt(
        StepExecutionContext context,
        ITenantContext tenantContext,
        int existingCredentialCount)
    {
        var title = context.GetConfig("title", existingCredentialCount > 0
            ? "Add Another Passkey"
            : "Set Up a Passkey");

        var description = context.GetConfig("description", existingCredentialCount > 0
            ? "Add a passkey on this device for quick, secure sign-in."
            : "Passkeys let you sign in with your fingerprint, face, or security key instead of a password.");

        var allowSkip = context.GetConfig("allowSkip", true);

        return StepHandlerResult.ShowUi("Journey/_Fido2RegistrationPrompt", new Fido2RegistrationPromptViewModel
        {
            Title = title,
            Description = description,
            AllowSkip = allowSkip,
            ExistingCredentialCount = existingCredentialCount,
            TenantName = tenantContext.Tenant?.Name
        });
    }

    private async Task<StepHandlerResult> InitiateRegistrationAsync(
        StepExecutionContext context,
        IFido2Service fido2Service,
        OlusoUserInfo user,
        ITenantContext tenantContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var authenticatorType = context.GetConfig<string?>("authenticatorType", null);
        var requireResidentKey = context.GetConfig("requireResidentKey", false);

        try
        {
            var options = await fido2Service.CreateRegistrationOptionsAsync(
                user.Id,
                user.Username ?? user.Email ?? user.Id,
                user.DisplayName ?? user.Username ?? user.Email ?? user.Id,
                authenticatorType,
                requireResidentKey,
                cancellationToken);

            // Store registration ID in journey data
            context.SetData("fido2_registration_id", options.RegistrationId);
            context.SetData("fido2_registration_challenge", options.Challenge);

            logger.LogDebug("Created FIDO2 registration options for user {UserId}", user.Id);

            return StepHandlerResult.ShowUi("Journey/_Fido2Registration", new Fido2RegistrationViewModel
            {
                Options = options,
                RegistrationId = options.RegistrationId,
                UserId = user.Id,
                Username = user.Username ?? user.Email,
                DisplayName = user.DisplayName ?? user.Username,
                TenantName = tenantContext.Tenant?.Name,
                AllowSkip = context.GetConfig("allowSkip", true)
            });
        }
        catch (Fido2Exception ex)
        {
            logger.LogWarning(ex, "Failed to create FIDO2 registration options for user {UserId}", user.Id);
            return StepHandlerResult.ShowUi("Journey/_Fido2RegistrationPrompt", new Fido2RegistrationPromptViewModel
            {
                ErrorMessage = ex.Message,
                AllowSkip = context.GetConfig("allowSkip", true),
                TenantName = tenantContext.Tenant?.Name
            });
        }
    }

    private async Task<StepHandlerResult> HandleRegistrationResponseAsync(
        StepExecutionContext context,
        IFido2Service fido2Service,
        OlusoUserInfo user,
        string attestationResponse,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var registrationId = context.GetInput("registrationId")
            ?? context.GetData<string>("fido2_registration_id");

        if (string.IsNullOrEmpty(registrationId))
        {
            logger.LogWarning("Missing registration ID for FIDO2 registration");
            return StepHandlerResult.ShowUi("Journey/_Fido2RegistrationPrompt", new Fido2RegistrationPromptViewModel
            {
                ErrorMessage = "Registration session expired. Please try again.",
                AllowSkip = context.GetConfig("allowSkip", true)
            });
        }

        try
        {
            var result = await fido2Service.VerifyRegistrationAsync(
                registrationId, attestationResponse, credentialName: null, cancellationToken);

            if (!result.Succeeded)
            {
                logger.LogWarning("FIDO2 registration failed for user {UserId}: {Error}", user.Id, result.Error);
                return StepHandlerResult.ShowUi("Journey/_Fido2RegistrationPrompt", new Fido2RegistrationPromptViewModel
                {
                    ErrorMessage = result.ErrorDescription ?? result.Error ?? "Registration failed",
                    AllowSkip = context.GetConfig("allowSkip", true)
                });
            }

            // Registration successful
            context.SetData("fido2_credential_registered", true);
            context.SetData("fido2_credential_id", result.CredentialId);

            logger.LogInformation("FIDO2 passkey registered for user {UserId}, credential {CredentialId}",
                user.Id, result.CredentialId);

            // Check if we should show success message or continue
            var showSuccess = context.GetConfig("showSuccessMessage", true);
            if (showSuccess)
            {
                return StepHandlerResult.ShowUi("Journey/_Fido2RegistrationSuccess", new Fido2RegistrationSuccessViewModel
                {
                    CredentialId = result.CredentialId!,
                    DisplayName = context.GetInput("credentialDisplayName") ?? "New Passkey"
                });
            }

            return StepHandlerResult.Continue();
        }
        catch (Fido2Exception ex)
        {
            logger.LogError(ex, "FIDO2 registration error for user {UserId}", user.Id);
            return StepHandlerResult.ShowUi("Journey/_Fido2RegistrationPrompt", new Fido2RegistrationPromptViewModel
            {
                ErrorMessage = ex.Message,
                AllowSkip = context.GetConfig("allowSkip", true)
            });
        }
    }
}

#region ViewModels

/// <summary>
/// View model for the initial passkey registration prompt
/// </summary>
public class Fido2RegistrationPromptViewModel
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ErrorMessage { get; set; }
    public bool AllowSkip { get; set; } = true;
    public int ExistingCredentialCount { get; set; }
    public string? TenantName { get; set; }
}

/// <summary>
/// View model for successful registration confirmation
/// </summary>
public class Fido2RegistrationSuccessViewModel
{
    public required string CredentialId { get; init; }
    public string? DisplayName { get; set; }
}

#endregion
