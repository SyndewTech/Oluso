using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles local username/password authentication step with optional passkey support
/// </summary>
public class LocalLoginStepHandler : IStepHandler
{
    public string StepType => "local_login";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<LocalLoginStepHandler>>();
        var eventService = context.ServiceProvider.GetService<IOlusoEventService>();
        var fido2Service = context.ServiceProvider.GetService<IFido2Service>();

        // Check if tenant allows local login (defaults to true if not set)
        var tenantEnableLocalLogin = tenantContext.Tenant?.EnableLocalLogin ?? true;

        // Check if client allows local login (EnableLocalLogin setting)
        // Client setting can only disable if tenant allows; cannot enable if tenant disables
        var clientEnableLocalLogin = context.GetData<bool?>("enable_local_login") ?? true;
        var enableLocalLogin = tenantEnableLocalLogin && clientEnableLocalLogin;

        if (!enableLocalLogin)
        {
            var disabledBy = !tenantEnableLocalLogin ? "tenant" : "client";
            logger.LogInformation("Local login is disabled by {DisabledBy} for client {ClientId}", disabledBy, context.ClientId);
            return StepHandlerResult.Fail("local_login_disabled",
                "Local username/password login is not enabled for this application. Please use an external identity provider.");
        }

        // Check if we're in password reset mode (delegate to PasswordResetStepHandler)
        if (context.GetData<bool>("password_reset_mode"))
        {
            var passwordResetHandler = new PasswordResetStepHandler();
            return await passwordResetHandler.ExecuteAsync(context, cancellationToken);
        }

        // Check for FIDO2/passkey assertion response
        var assertionResponse = context.GetInput("assertionResponse");
        if (!string.IsNullOrEmpty(assertionResponse) && fido2Service != null)
        {
            return await HandlePasskeyAssertionAsync(context, fido2Service, userService, tenantContext, eventService, assertionResponse, logger, cancellationToken);
        }

        // Check for passkey login initiation or forgot password
        var action = context.GetInput("action");

        if (action == "forgot_password")
        {
            // Set flag to indicate we're in password reset mode
            context.SetData("password_reset_mode", true);

            // Show the password reset request form
            return StepHandlerResult.ShowUi("Journey/_PasswordResetRequest", new PasswordResetRequestViewModel());
        }

        if (action == "passkey_login" && fido2Service != null)
        {
            return await InitiatePasskeyLoginAsync(context, fido2Service, tenantContext, logger, cancellationToken);
        }

        // Check if we have user input (form submission)
        var username = context.GetInput("username");
        var password = context.GetInput("password");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            // Show login UI - view path: Views/Journey/_LocalLogin.cshtml
            var loginHint = context.GetData<string>("login_hint");
            var showPasskey = context.GetConfig("showPasskey", true) && fido2Service != null;

            return StepHandlerResult.ShowUi("Journey/_LocalLogin", new LocalLoginViewModel
            {
                LoginHint = loginHint,
                Username = loginHint,
                ShowRememberMe = context.GetConfig("allowRememberMe", true),
                AllowRememberMe = context.GetConfig("allowRememberMe", true),
                AllowSelfRegistration = context.GetConfig("allowSelfRegistration", false),
                AllowRegistration = context.GetConfig("allowSelfRegistration", false),
                AllowForgotPassword = context.GetConfig("allowForgotPassword", true),
                TenantName = tenantContext.Tenant?.Name,
                ShowPasskey = showPasskey,
                PasskeyUsernameless = context.GetConfig("passkeyUsernameless", true)
            });
        }

        var clientId = context.GetData<string>("client_id") ?? "unknown";
        var ipAddress = context.GetData<string>("ip_address");

        // Validate credentials
        var result = await userService.ValidateCredentialsAsync(username, password, tenantContext.TenantId, cancellationToken);

        if (result.IsLockedOut)
        {
            logger.LogWarning("Login failed: account locked out for {Username}", username);

            // Raise lockout event
            if (eventService != null && result.User != null)
            {
                await eventService.RaiseAsync(new UserLockedOutEvent
                {
                    TenantId = tenantContext.TenantId,
                    SubjectId = result.User.Id,
                    Reason = "Too many failed login attempts"
                }, cancellationToken);
            }

            return StepHandlerResult.ShowUi("Journey/_Lockout", null);
        }

        if (!result.Succeeded)
        {
            logger.LogWarning("Login failed: {Error} for {Username}", result.Error, username);

            // Raise login failed event
            if (eventService != null)
            {
                await eventService.RaiseAsync(new UserSignInFailedEvent
                {
                    TenantId = tenantContext.TenantId,
                    Username = username,
                    ClientId = clientId,
                    FailureReason = result.Error ?? "Invalid credentials",
                    IpAddress = ipAddress
                }, cancellationToken);
            }

            return StepHandlerResult.ShowUi("Journey/_LocalLogin", new LocalLoginViewModel
            {
                ErrorMessage = result.ErrorDescription ?? "Invalid username or password",
                Username = username
            });
        }

        var user = result.User!;

        // Check MFA requirement
        if (result.RequiresMfa)
        {
            context.SetData("mfa_required", true);
            context.SetData("mfa_user_id", user.Id);
        }

        // Update journey context with authenticated user
        context.UserId = user.Id;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", "pwd");

        // Record login
        await userService.RecordLoginAsync(user.Id, cancellationToken);

        // Raise login success event
        if (eventService != null)
        {
            await eventService.RaiseAsync(new UserSignedInEvent
            {
                TenantId = tenantContext.TenantId,
                SubjectId = user.Id,
                Username = user.UserName ?? username,
                ClientId = clientId,
                AuthenticationMethod = "pwd",
                IpAddress = ipAddress,
                SessionId = context.JourneyId
            }, cancellationToken);
        }

        logger.LogInformation("User {UserId} authenticated via local login", user.Id);

        return BuildSuccessResult(user);
    }

    private async Task<StepHandlerResult> InitiatePasskeyLoginAsync(
        StepExecutionContext context,
        IFido2Service fido2Service,
        ITenantContext tenantContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var username = context.GetInput("username");
        var allowUsernameless = context.GetConfig("passkeyUsernameless", true);

        try
        {
            Fido2AssertionOptions options;

            if (!string.IsNullOrEmpty(username))
            {
                options = await fido2Service.CreateAssertionOptionsAsync(username, cancellationToken);
            }
            else if (allowUsernameless)
            {
                options = await fido2Service.CreateAssertionOptionsAsync(null, cancellationToken);
            }
            else
            {
                context.SetData("login_error", "Username is required for passkey login");
                return StepHandlerResult.ShowUi("Journey/_LocalLogin", new LocalLoginViewModel
                {
                    ErrorMessage = "Username is required for passkey login",
                    TenantName = tenantContext.Tenant?.Name,
                    ShowPasskey = true,
                    PasskeyUsernameless = false
                });
            }

            context.SetData("fido2_assertion_id", options.AssertionId);
            logger.LogDebug("Created FIDO2 assertion options for passkey login");

            return StepHandlerResult.ShowUi("Journey/_Fido2Assertion", new Fido2AssertionViewModel
            {
                Options = options,
                AssertionId = options.AssertionId
            });
        }
        catch (Fido2Exception ex)
        {
            logger.LogWarning(ex, "Failed to create FIDO2 assertion options");
            return StepHandlerResult.ShowUi("Journey/_LocalLogin", new LocalLoginViewModel
            {
                ErrorMessage = ex.Message,
                TenantName = tenantContext.Tenant?.Name,
                ShowPasskey = true,
                PasskeyUsernameless = context.GetConfig("passkeyUsernameless", true)
            });
        }
    }

    private async Task<StepHandlerResult> HandlePasskeyAssertionAsync(
        StepExecutionContext context,
        IFido2Service fido2Service,
        IOlusoUserService userService,
        ITenantContext tenantContext,
        IOlusoEventService? eventService,
        string assertionResponse,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var assertionId = context.GetInput("assertionId") ?? context.GetData<string>("fido2_assertion_id");

        if (string.IsNullOrEmpty(assertionId))
        {
            return StepHandlerResult.Fail("invalid_request", "Missing assertion ID");
        }

        try
        {
            var result = await fido2Service.VerifyAssertionAsync(assertionId, assertionResponse, cancellationToken);

            if (!result.Succeeded)
            {
                logger.LogWarning("FIDO2 assertion verification failed: {Error}", result.Error);
                return StepHandlerResult.ShowUi("Journey/_LocalLogin", new LocalLoginViewModel
                {
                    ErrorMessage = result.ErrorDescription ?? "Passkey verification failed",
                    TenantName = tenantContext.Tenant?.Name,
                    ShowPasskey = true,
                    PasskeyUsernameless = context.GetConfig("passkeyUsernameless", true)
                });
            }

            var user = await userService.FindByIdAsync(result.UserId!, cancellationToken);
            if (user == null)
            {
                return StepHandlerResult.Fail("user_not_found", "User not found");
            }

            if (!user.IsActive)
            {
                return StepHandlerResult.Fail("user_deactivated", "Account has been deactivated");
            }

            context.UserId = user.Id;
            context.SetData("authenticated_at", DateTime.UtcNow);
            context.SetData("auth_method", "fido2");

            await userService.RecordLoginAsync(user.Id, cancellationToken);

            // Raise login success event
            if (eventService != null)
            {
                await eventService.RaiseAsync(new UserSignedInEvent
                {
                    TenantId = tenantContext.TenantId,
                    SubjectId = user.Id,
                    Username = user.Username ?? user.Email ?? user.Id,
                    ClientId = context.GetData<string>("client_id") ?? "unknown",
                    AuthenticationMethod = "fido2",
                    IpAddress = context.GetData<string>("ip_address"),
                    SessionId = context.JourneyId
                }, cancellationToken);
            }

            logger.LogInformation("User {UserId} authenticated via passkey", user.Id);

            return BuildSuccessResult(user);
        }
        catch (Fido2Exception ex)
        {
            logger.LogError(ex, "FIDO2 assertion verification error");
            return StepHandlerResult.Fail("fido2_error", ex.Message);
        }
    }

    private static StepHandlerResult BuildSuccessResult(ValidatedUser user)
    {
        var outputData = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.UserName,
            ["email"] = user.Email ?? "",
            ["email_verified"] = true
        };

        if (!string.IsNullOrEmpty(user.FirstName))
            outputData["given_name"] = user.FirstName;
        if (!string.IsNullOrEmpty(user.LastName))
            outputData["family_name"] = user.LastName;

        return StepHandlerResult.Success(outputData);
    }

    private static StepHandlerResult BuildSuccessResult(OlusoUserInfo user)
    {
        var outputData = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.Username,
            ["email"] = user.Email ?? "",
            ["email_verified"] = user.EmailVerified
        };

        if (!string.IsNullOrEmpty(user.FirstName))
            outputData["given_name"] = user.FirstName;
        if (!string.IsNullOrEmpty(user.LastName))
            outputData["family_name"] = user.LastName;

        return StepHandlerResult.Success(outputData);
    }
}

/// <summary>
/// View model for the local login page.
/// NOTE: External providers should NOT be shown here - use CompositeLogin for that.
/// </summary>
public class LocalLoginViewModel
{
    public string? LoginHint { get; set; }
    public string? Username { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ShowRememberMe { get; set; } = true;
    public bool RememberMe { get; set; }
    public bool AllowRememberMe { get; set; } = true;
    public bool AllowSelfRegistration { get; set; }
    public bool AllowForgotPassword { get; set; } = true;
    public bool AllowRegistration { get; set; }
    public string? ForgotPasswordUrl { get; set; }
    public string? RegistrationUrl { get; set; }
    public string? TenantName { get; set; }
    public string? TenantLogo { get; set; }

    /// <summary>
    /// Whether to show the passkey/FIDO2 login option
    /// </summary>
    public bool ShowPasskey { get; set; }

    /// <summary>
    /// Whether to allow usernameless (discoverable credential) passkey login
    /// </summary>
    public bool PasskeyUsernameless { get; set; } = true;
}
