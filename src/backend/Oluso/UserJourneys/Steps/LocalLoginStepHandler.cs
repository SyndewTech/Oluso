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
public class LocalLoginStepHandler : IPrimaryAuthenticationStep
{
    public string StepType => "local_login";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (await OrganizationSignInStep.TryCompleteAsync(context, cancellationToken) is { } organizationResult)
        {
            return organizationResult;
        }

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

        if (ExpiredPasswordChange.IsPending(context))
        {
            var (changedUser, pending) = await ExpiredPasswordChange.CompleteAsync(context, userService, logger, cancellationToken);
            if (pending != null)
            {
                return pending;
            }

            context.UserId = changedUser!.Id;
            context.SetData("authenticated_at", DateTime.UtcNow);
            context.SetData("auth_method", "pwd");
            await userService.RecordLoginAsync(changedUser.Id, cancellationToken);
            return BuildSuccessOrBranchResult(context, changedUser, "auth_local");
        }

        // Back from "forgot password" to signing in with a password.
        if (context.GetInput("action") == "back_to_login" && context.GetData<bool>("password_reset_mode"))
        {
            context.JourneyData.Remove("password_reset_mode");
            PasswordResetStepHandler.ClearResetState(context);
            // Carries on below to the sign-in form (no credentials were submitted).
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
            // loginHintClaim picks the journey-state key to pre-populate
            // the username field from. Defaults to OIDC's standard
            // "login_hint" claim; policies can point at a tenant-specific
            // claim (e.g. "company_email") when the upstream step
            // populates that instead.
            var loginHintClaim = context.GetConfig<string>("loginHintClaim", "login_hint") ?? "login_hint";
            var loginHint = context.GetData<string>(loginHintClaim);
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

        // Pre-signin hook — lets subscribers veto with a reason that surfaces
        // to the user (compliance hold, IP reputation block, etc.).
        if (eventService != null)
        {
            var signingIn = new UserSigningInEvent
            {
                TenantId = tenantContext.TenantId,
                Username = username,
                ClientId = clientId,
                IpAddress = ipAddress,
                UserAgent = context.GetData<string>("user_agent")
            };
            await eventService.RaiseAsync(signingIn, cancellationToken);
            if (signingIn.Cancel)
            {
                logger.LogWarning("Sign-in cancelled by signing-in subscriber: {Reason}", signingIn.CancelReason);
                return StepHandlerResult.ShowUi("Journey/_LocalLogin", new LocalLoginViewModel
                {
                    ErrorMessage = signingIn.CancelReason ?? "Sign-in not allowed",
                    Username = username
                });
            }
        }

        // A chosen organization becomes the same email@slug qualifier a user can
        // type, so both routes share one code path.
        var organizationSlug = context.GetInput("organization");
        var loginIdentifier = string.IsNullOrWhiteSpace(organizationSlug)
            ? username
            : $"{username}@{organizationSlug.Trim()}";

        // Validate credentials
        var result = await userService.ValidateCredentialsAsync(loginIdentifier, password, tenantContext.TenantId, cancellationToken);

        if (result.RequiresOrganizationChoice)
        {
            logger.LogInformation("Multiple accounts matched in journey; prompting for organization");

            return StepHandlerResult.ShowUi("Journey/_LocalLogin", new LocalLoginViewModel
            {
                Username = username,
                AvailableOrganizations = result.AvailableOrganizations,
                TenantName = tenantContext.Tenant?.Name,
                AllowForgotPassword = context.GetConfig("allowForgotPassword", true)
            });
        }

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

        if (result.PasswordExpired)
        {
            logger.LogInformation("Password expired for user {UserId}; requiring a change before sign-in", result.User!.Id);
            return await ExpiredPasswordChange.BeginAsync(context, userService, result.User, cancellationToken);
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

            return StepHandlerResult.FailedAttemptUi("Journey/_LocalLogin", new LocalLoginViewModel
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
        context.SetData("remember_me", context.GetInput("rememberMe") is "true" or "on");
        context.SetData("auth_method", "pwd");

        // Record login. The sign-in event itself is raised once when the journey
        // completes and the session is issued (after MFA and any later steps).
        await userService.RecordLoginAsync(user.Id, cancellationToken);

        logger.LogInformation("User {UserId} authenticated via local login", user.Id);

        return BuildSuccessOrBranchResult(context, user, "auth_local");
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

            JourneyPasskeyAssertion.Remember(context, options.AssertionId);
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
        // Only the assertion this journey asked for, once.
        var assertionId = JourneyPasskeyAssertion.Take(context);

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
            context.SetData("remember_me", context.GetInput("rememberMe") is "true" or "on");
            context.SetData("auth_method", "fido2");

            await userService.RecordLoginAsync(user.Id, cancellationToken);

            logger.LogInformation("User {UserId} authenticated via passkey", user.Id);

            return BuildSuccessOrBranchResult(context, user, "auth_passkey");
        }
        catch (Fido2Exception ex)
        {
            logger.LogError(ex, "FIDO2 assertion verification error");
            return StepHandlerResult.Fail("fido2_error", ex.Message);
        }
    }

    private static StepHandlerResult BuildSuccessOrBranchResult(StepExecutionContext context, ValidatedUser user, string branchId)
    {
        var outputData = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.UserName,
            ["email"] = user.Email ?? "",
            ["email_verified"] = user.EmailVerified
        };

        if (!string.IsNullOrEmpty(user.FirstName))
            outputData["given_name"] = user.FirstName;
        if (!string.IsNullOrEmpty(user.LastName))
            outputData["family_name"] = user.LastName;

        return StepHandlerResult.SuccessOrBranch(context, outputData, branchId);
    }

    private static StepHandlerResult BuildSuccessOrBranchResult(StepExecutionContext context, OlusoUserInfo user, string branchId)
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

        return StepHandlerResult.SuccessOrBranch(context, outputData, branchId);
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

    /// <summary>Set when the credentials matched accounts in more than one organization.</summary>
    public List<Core.Services.OrganizationOption>? AvailableOrganizations { get; set; }

    /// <summary>
    /// Whether to show the passkey/FIDO2 login option
    /// </summary>
    public bool ShowPasskey { get; set; }

    /// <summary>
    /// Whether to allow usernameless (discoverable credential) passkey login
    /// </summary>
    public bool PasskeyUsernameless { get; set; } = true;
}
