using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.Enterprise.Fido2.Steps;

/// <summary>
/// Handles FIDO2/WebAuthn (passkey) authentication as a standalone User Journey step.
/// Supports both username-based and usernameless (resident credential) flows.
/// </summary>
/// <remarks>
/// This step handler is registered when <c>AddFido2()</c> is called.
/// For composite login (passkey + password + external), use the composite_login step
/// which integrates passkey support when FIDO2 is enabled.
/// </remarks>
public class Fido2LoginStepHandler : ICustomStepHandler, ICustomStepHandlerMetadata, IPrimaryAuthenticationStep
{
    public string StepType => "fido2_login";
    public string DisplayName => "Passkey Login";
    public string Description => "Authenticate using FIDO2/WebAuthn passkey (supports username-based and usernameless flows)";
    public string Category => "Authentication";
    public string? Icon => "fingerprint";

    public Dictionary<string, object>? GetConfigurationSchema() => new()
    {
        ["allowUsernameless"] = new { type = "boolean", @default = true, description = "Let users sign in with a passkey without typing a username" },
        ["showAsOption"] = new { type = "boolean", @default = true, description = "Offer passkey sign-in as an option next to other sign-in methods" },
        ["title"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Heading of the passkey sign-in page", ["x-localizable"] = true },
        ["description"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Text under the heading", ["x-localizable"] = true },
    };
    public string? Module => "Oluso.Enterprise.Fido2";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var fido2Service = context.ServiceProvider.GetRequiredService<IFido2Service>();
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<Fido2LoginStepHandler>>();

        // Tenant-level kill switch — admin has disabled passkey for this
        // tenant via Tenant.EnablePasskey. Skip the step so the journey
        // moves to the next step (typically a password fallback). Mirrors
        // the same toggle that filters the passkey provider out of the
        // standalone Login page's PasswordlessProviders list.
        if (tenantContext.Tenant?.EnablePasskey == false)
        {
            logger.LogDebug(
                "Skipping Fido2 login step — passkey is disabled for tenant {TenantId}",
                tenantContext.TenantId);
            return StepHandlerResult.Skip();
        }

        // Check if this is a verification callback
        var assertionResponse = context.GetInput("assertionResponse");
        if (!string.IsNullOrEmpty(assertionResponse))
        {
            return await HandleAssertionResponseAsync(context, fido2Service, userService, assertionResponse, logger, cancellationToken);
        }

        // Check if user wants to authenticate with passkey
        var action = context.GetInput("action");
        if (action == "passkey_login")
        {
            return await InitiatePasskeyLoginAsync(context, fido2Service, logger, cancellationToken);
        }

        // Show passkey login UI
        var allowUsernameless = context.GetConfig("allowUsernameless", true);
        var showAsOption = context.GetConfig("showAsOption", true);

        return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
        {
            AllowUsernameless = allowUsernameless,
            ShowAsOption = showAsOption,
            TenantName = tenantContext.Tenant?.Name,
            Title = context.GetLocalizedConfig("title") ?? Oluso.Core.Locale.UiStringDefaults.Resolve("fido2_login_title"),
            Description = context.GetLocalizedConfig("description") ?? Oluso.Core.Locale.UiStringDefaults.Resolve("fido2_login_subtitle")
        });
    }

    private async Task<StepHandlerResult> InitiatePasskeyLoginAsync(
        StepExecutionContext context,
        IFido2Service fido2Service,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var username = context.GetInput("username");
        var allowUsernameless = context.GetConfig("allowUsernameless", true);

        try
        {
            // Create assertion options
            Fido2AssertionOptions options;

            if (!string.IsNullOrEmpty(username))
            {
                // Username-based flow - get credentials for specific user
                options = await fido2Service.CreateAssertionOptionsAsync(username, cancellationToken);
            }
            else if (allowUsernameless)
            {
                // Usernameless flow - let the authenticator identify the user
                options = await fido2Service.CreateAssertionOptionsAsync(null, cancellationToken);
            }
            else
            {
                return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
                {
                    ErrorMessage = "Username is required for passkey login",
                    AllowUsernameless = false
                });
            }

            // Store challenge in journey data for verification
            context.SetData("fido2_challenge", options.Challenge);
            JourneyPasskeyAssertion.Remember(context, options.AssertionId);

            logger.LogDebug("Created FIDO2 assertion options for {Username}", username ?? "(usernameless)");

            // Return options to client for WebAuthn API call
            return StepHandlerResult.ShowUi("Journey/_Fido2Assertion", new Oluso.Core.Services.Fido2AssertionViewModel
            {
                Options = options,
                AssertionId = options.AssertionId
            });
        }
        catch (Fido2Exception ex)
        {
            logger.LogWarning(ex, "Failed to create FIDO2 assertion options");
            return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
            {
                ErrorMessage = ex.Message,
                AllowUsernameless = context.GetConfig("allowUsernameless", true)
            });
        }
    }

    private async Task<StepHandlerResult> HandleAssertionResponseAsync(
        StepExecutionContext context,
        IFido2Service fido2Service,
        IOlusoUserService userService,
        string assertionResponse,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Only the assertion this journey asked for, once (JourneyPasskeyAssertion).
        var assertionId = JourneyPasskeyAssertion.Take(context);

        if (string.IsNullOrEmpty(assertionId))
        {
            logger.LogWarning("Passkey response posted with no matching assertion pending in this journey");
            return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
            {
                ErrorMessage = "Your passkey request expired. Please try again.",
                AllowUsernameless = context.GetConfig("allowUsernameless", true)
            });
        }

        try
        {
            // Verify the assertion
            var result = await fido2Service.VerifyAssertionAsync(assertionId, assertionResponse, cancellationToken);

            if (!result.Succeeded)
            {
                logger.LogWarning("FIDO2 assertion verification failed: {Error}", result.Error);
                return StepHandlerResult.ShowUi("Journey/_Fido2Login", new Fido2LoginViewModel
                {
                    ErrorMessage = result.ErrorDescription ?? "Passkey verification failed",
                    AllowUsernameless = context.GetConfig("allowUsernameless", true)
                });
            }

            // Get the user
            var user = await userService.FindByIdAsync(result.UserId!, cancellationToken);
            if (user == null)
            {
                return StepHandlerResult.Fail("user_not_found", "User not found");
            }

            if (!user.IsActive)
            {
                return StepHandlerResult.Fail("user_deactivated", "Account has been deactivated");
            }

            // Authentication successful
            context.UserId = user.Id;
            context.SetData("authenticated_at", DateTime.UtcNow);
            context.SetData("auth_method", "fido2");
            context.SetData("credential_id", result.CredentialId ?? "");

            await userService.RecordLoginAsync(user.Id, cancellationToken);

            logger.LogInformation("User {UserId} authenticated via FIDO2 passkey", user.Id);

            // Build output claims
            var outputData = new Dictionary<string, object>
            {
                ["sub"] = user.Id,
                ["name"] = user.DisplayName ?? user.Username,
                ["email"] = user.Email ?? "",
                ["email_verified"] = user.EmailVerified,
                ["amr"] = new[] { "hwk" } // Hardware key authentication method
            };

            if (!string.IsNullOrEmpty(user.FirstName))
                outputData["given_name"] = user.FirstName;
            if (!string.IsNullOrEmpty(user.LastName))
                outputData["family_name"] = user.LastName;

            return StepHandlerResult.Success(outputData);
        }
        catch (Fido2Exception ex)
        {
            logger.LogError(ex, "FIDO2 assertion verification error");
            return StepHandlerResult.Fail("fido2_error", ex.Message);
        }
    }
}

#region ViewModels

public class Fido2LoginViewModel
{
    public string? ErrorMessage { get; set; }
    public bool AllowUsernameless { get; set; } = true;
    public bool ShowAsOption { get; set; } = true;
    public string? TenantName { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
}

public class Fido2AssertionViewModel
{
    public required Fido2AssertionOptions Options { get; init; }
    public required string AssertionId { get; init; }
}

public class Fido2RegistrationViewModel
{
    public required Fido2RegistrationOptions Options { get; init; }
    public required string RegistrationId { get; init; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? TenantName { get; set; }
    public string? ErrorMessage { get; set; }
    public bool AllowSkip { get; set; } = true;
}

#endregion
