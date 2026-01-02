using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Fido2.Services;

namespace Oluso.Enterprise.Fido2;

#region View Models

/// <summary>
/// View model for the FIDO2 login page
/// </summary>
public class Fido2LoginViewModel
{
    public string? Username { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public bool AllowPasskeyOnly { get; set; }
    public bool AllowFallback { get; set; } = true;
    public bool ShowFallback { get; set; }
}

/// <summary>
/// View model for the FIDO2 assertion (authentication) page
/// </summary>
public class Fido2AssertViewModel
{
    public AssertionOptions? Options { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public bool IsPasskeyOnly { get; set; }
    public bool AllowFallback { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// View model for the FIDO2 registration page
/// </summary>
public class Fido2RegisterViewModel
{
    public CredentialCreateOptions? Options { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? ErrorMessage { get; set; }
}

#endregion

/// <summary>
/// User Journey step handler for FIDO2/WebAuthn Passkey authentication
/// </summary>
public class Fido2StepHandler : ICustomStepHandler
{
    public string StepType => "webauthn";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var fido2Service = context.ServiceProvider.GetService<IFido2AuthenticationService>();
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<Fido2StepHandler>>();

        if (fido2Service == null)
        {
            logger.LogWarning("FIDO2 service not configured, skipping step");
            return StepHandlerResult.Skip();
        }

        // Check if this is a passkey-only flow (no username required)
        var passkeyOnly = context.GetConfig("passkeyOnly", false);
        var allowFallback = context.GetConfig("allowFallback", true);

        // Check for assertion response (user completed WebAuthn ceremony)
        if (context.UserInput.TryGetValue("fido2_response", out var responseJson) &&
            context.UserInput.TryGetValue("fido2_challenge", out var challenge))
        {
            return await HandleAssertionAsync(
                context, fido2Service, userService, logger,
                responseJson?.ToString() ?? "",
                challenge?.ToString() ?? "",
                cancellationToken);
        }

        // Check if we already have an authenticated user (for step-up or adding as 2FA)
        var existingUserId = context.UserId;
        if (!string.IsNullOrEmpty(existingUserId))
        {
            var userInfo = await userService.FindByIdAsync(existingUserId, cancellationToken);
            if (userInfo != null)
            {
                var credentials = await fido2Service.GetUserCredentialsAsync(existingUserId, cancellationToken);
                if (credentials.Any())
                {
                    // User has FIDO2 credentials, show assertion UI
                    var options = await fido2Service.CreateAssertionOptionsAsync(userInfo.Username, cancellationToken);
                    return StepHandlerResult.ShowUi("Journey/_Fido2Assert", new Fido2AssertViewModel
                    {
                        Options = options,
                        Username = userInfo.Username ?? "",
                        DisplayName = userInfo.DisplayName ?? userInfo.Username ?? "",
                        AllowFallback = allowFallback
                    });
                }
            }
            // No FIDO2 credentials, skip this step
            return StepHandlerResult.Skip();
        }

        // Check for username input (non-passkey flow)
        if (!passkeyOnly && context.UserInput.TryGetValue("username", out var usernameObj))
        {
            var username = usernameObj?.ToString() ?? "";
            var userInfo = await userService.FindByUsernameAsync(username, cancellationToken)
                        ?? await userService.FindByEmailAsync(username, cancellationToken);

            if (userInfo == null)
            {
                return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
                {
                    ErrorMessage = "User not found",
                    AllowPasskeyOnly = passkeyOnly,
                    AllowFallback = allowFallback
                });
            }

            var credentials = await fido2Service.GetUserCredentialsAsync(userInfo.Id, cancellationToken);
            if (!credentials.Any())
            {
                if (allowFallback)
                {
                    return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
                    {
                        ErrorMessage = "No passkeys registered. Please use another method.",
                        Username = username,
                        AllowPasskeyOnly = passkeyOnly,
                        AllowFallback = allowFallback,
                        ShowFallback = true
                    });
                }
                return StepHandlerResult.Failure("no_passkeys", "No passkeys registered for this account");
            }

            // Show assertion UI with user's credentials
            var options = await fido2Service.CreateAssertionOptionsAsync(username, cancellationToken);
            return StepHandlerResult.ShowUi("Journey/_Fido2Assert", new Fido2AssertViewModel
            {
                Options = options,
                Username = username,
                DisplayName = userInfo.DisplayName ?? username,
                AllowFallback = allowFallback
            });
        }

        // Show initial UI (passkey button or username entry)
        if (passkeyOnly)
        {
            // Discoverable credential flow - no username needed
            var options = await fido2Service.CreateAssertionOptionsAsync(cancellationToken: cancellationToken);
            return StepHandlerResult.ShowUi("Journey/_Fido2Assert", new Fido2AssertViewModel
            {
                Options = options,
                IsPasskeyOnly = true,
                AllowFallback = allowFallback
            });
        }

        // Show login form with passkey option
        return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
        {
            AllowPasskeyOnly = passkeyOnly,
            AllowFallback = allowFallback
        });
    }

    private async Task<StepHandlerResult> HandleAssertionAsync(
        StepExecutionContext context,
        IFido2AuthenticationService fido2Service,
        IOlusoUserService userService,
        ILogger logger,
        string responseJson,
        string challenge,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = System.Text.Json.JsonSerializer.Deserialize<AuthenticatorAssertionResponse>(responseJson);
            if (response == null)
            {
                return CreateFailureResult("Invalid response", "invalid_response", true, context);
            }

            var result = await fido2Service.VerifyAssertionAsync(response, challenge, cancellationToken);

            if (!result.Success)
            {
                logger.LogWarning("FIDO2 assertion failed: {Error}", result.Error);
                return CreateFailureResult(result.Error ?? "Authentication failed", result.ErrorCode ?? "assertion_failed", true, context);
            }

            // Get the user
            var userInfo = await userService.FindByIdAsync(result.UserId!, cancellationToken);
            if (userInfo == null)
            {
                return CreateFailureResult("User not found", "user_not_found", true, context);
            }

            // Successful authentication using the context helper method
            context.SetAuthenticated(userInfo.Id, "fido2");
            context.SetData("amr", new[] { "hwk" });

            logger.LogInformation("User {UserId} authenticated via FIDO2/WebAuthn", userInfo.Id);

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["sub"] = userInfo.Id,
                ["name"] = userInfo.DisplayName ?? userInfo.Username ?? "",
                ["email"] = userInfo.Email ?? ""
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing FIDO2 assertion");
            return CreateFailureResult("Authentication failed", "internal_error", true, context);
        }
    }

    private static StepHandlerResult CreateFailureResult(string message, string code, bool allowRetry, StepExecutionContext context)
    {
        if (allowRetry)
        {
            return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
            {
                ErrorMessage = message,
                ErrorCode = code,
                AllowFallback = context.GetConfig("allowFallback", true)
            });
        }
        return StepHandlerResult.Failure(code, message);
    }

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }
}
