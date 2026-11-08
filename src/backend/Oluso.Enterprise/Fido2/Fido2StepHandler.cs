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

    /// <summary>
    /// Discoverable-credential options for the passkey button, issued by the step so their challenge is bound to
    /// this journey (null on pages rendered by older steps, which fetch their own).
    /// </summary>
    public AssertionOptions? Options { get; set; }
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
public class Fido2StepHandler : ICustomStepHandler, ICustomStepHandlerMetadata, IPrimaryAuthenticationStep
{
    public string StepType => "webauthn";
    public string DisplayName => "WebAuthn / Passkeys";
    public string Description => "Authenticate using FIDO2/WebAuthn passkeys or security keys";
    public string Category => "Authentication";
    public string? Icon => "fingerprint";
    public string? Module => "Oluso.Enterprise.Fido2";

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
        if (context.UserInput.TryGetValue("fido2_response", out var responseJson))
        {
            // Only a challenge this step issued in this journey, once: the stored one is taken before the
            // response is verified, and a posted challenge must be that one.
            var issued = context.GetDataString(ChallengeKey);
            context.JourneyData.Remove(ChallengeKey);
            var posted = context.UserInput.TryGetValue("fido2_challenge", out var postedValue) ? postedValue?.ToString() : null;
            if (string.IsNullOrEmpty(issued)
                || (!string.IsNullOrEmpty(posted) && !string.Equals(posted, issued, StringComparison.Ordinal)))
            {
                logger.LogWarning("Passkey response with a challenge this journey did not issue (or already used)");
                return await CreateFailureResultAsync("Authentication failed", "invalid_challenge", true, context, fido2Service, cancellationToken);
            }

            return await HandleAssertionAsync(
                context, fido2Service, userService, logger,
                responseJson?.ToString() ?? "",
                issued,
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
                    var options = Remember(context, await fido2Service.CreateAssertionOptionsAsync(userInfo.Username, cancellationToken));
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

        // Check for username input (non-passkey flow). The answer is the same whether or not the account exists
        // or has passkeys (the service gives decoy credentials then), so the form can't be used to probe accounts;
        // a sign-in for such an account just fails and falls back like any failed passkey.
        if (!passkeyOnly && context.UserInput.TryGetValue("username", out var usernameObj))
        {
            var username = usernameObj?.ToString() ?? "";
            var options = Remember(context, await fido2Service.CreateAssertionOptionsAsync(username, cancellationToken));
            return StepHandlerResult.ShowUi("Journey/_Fido2Assert", new Fido2AssertViewModel
            {
                Options = options,
                Username = username,
                DisplayName = username,
                AllowFallback = allowFallback
            });
        }

        // Show initial UI (passkey button or username entry)
        if (passkeyOnly)
        {
            // Discoverable credential flow - no username needed
            var options = Remember(context, await fido2Service.CreateAssertionOptionsAsync(cancellationToken: cancellationToken));
            return StepHandlerResult.ShowUi("Journey/_Fido2Assert", new Fido2AssertViewModel
            {
                Options = options,
                IsPasskeyOnly = true,
                AllowFallback = allowFallback
            });
        }

        // Show login form with passkey option; the passkey button uses options issued for this journey.
        return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
        {
            AllowPasskeyOnly = passkeyOnly,
            AllowFallback = allowFallback,
            Options = Remember(context, await fido2Service.CreateAssertionOptionsAsync(cancellationToken: cancellationToken))
        });
    }

    /// <summary>Journey data holding the challenge this step issued and has not seen answered yet.</summary>
    public const string ChallengeKey = "fido2_step_challenge";

    private static AssertionOptions Remember(StepExecutionContext context, AssertionOptions options)
    {
        context.SetData(ChallengeKey, options.Challenge);
        return options;
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
            var response = System.Text.Json.JsonSerializer.Deserialize<AuthenticatorAssertionResponse>(responseJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (response == null)
            {
                return await CreateFailureResultAsync("Invalid response", "invalid_response", true, context, fido2Service, cancellationToken);
            }

            var result = await fido2Service.VerifyAssertionAsync(response, challenge, cancellationToken);

            if (!result.Success)
            {
                logger.LogWarning("FIDO2 assertion failed: {Error}", result.Error);
                return await CreateFailureResultAsync(result.Error ?? "Authentication failed", result.ErrorCode ?? "assertion_failed", true, context, fido2Service, cancellationToken);
            }

            // Step-up / second factor: the passkey must be the journey's user's own.
            if (!string.IsNullOrEmpty(context.UserId) && !string.Equals(context.UserId, result.UserId, StringComparison.Ordinal))
            {
                logger.LogWarning("Passkey of {PasskeyUser} presented in a journey for {UserId}", result.UserId, context.UserId);
                return await CreateFailureResultAsync("Authentication failed", "account_mismatch", true, context, fido2Service, cancellationToken);
            }

            // Get the user
            var userInfo = await userService.FindByIdAsync(result.UserId!, cancellationToken);
            if (userInfo == null || !userInfo.IsActive)
            {
                return await CreateFailureResultAsync("Authentication failed", userInfo == null ? "user_not_found" : "user_deactivated",
                    true, context, fido2Service, cancellationToken);
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
            return await CreateFailureResultAsync("Authentication failed", "internal_error", true, context, fido2Service, cancellationToken);
        }
    }

    private static async Task<StepHandlerResult> CreateFailureResultAsync(
        string message, string code, bool allowRetry, StepExecutionContext context,
        IFido2AuthenticationService fido2Service, CancellationToken cancellationToken)
    {
        if (allowRetry)
        {
            return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
            {
                ErrorMessage = message,
                ErrorCode = code,
                AllowFallback = context.GetConfig("allowFallback", true),
                Options = Remember(context, await fido2Service.CreateAssertionOptionsAsync(cancellationToken: cancellationToken))
            });
        }
        return StepHandlerResult.Failure(code, message);
    }

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }
}
