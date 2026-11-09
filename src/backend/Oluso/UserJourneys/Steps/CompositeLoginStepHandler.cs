using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Composite login step that combines local, passkey, and external login options in a single UI.
/// Shows username/password form, passkey button, and external provider buttons.
/// </summary>
public class CompositeLoginStepHandler : IPrimaryAuthenticationStep, IStepConfigurationTester
{
    public Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
        StepTestSupport.TestExternalProvidersAsync(context, "externalProviders", cancellationToken);

    public string StepType => "composite_login";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (await OrganizationSignInStep.TryCompleteAsync(context, cancellationToken) is { } organizationResult)
        {
            return organizationResult;
        }

        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var externalAuthService = context.ServiceProvider.GetService<IExternalAuthService>();
        var fido2Service = context.ServiceProvider.GetService<IFido2Service>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<CompositeLoginStepHandler>>();

        // Check if tenant allows local login (defaults to true if not set)
        var tenantEnableLocalLogin = tenantContext.Tenant?.EnableLocalLogin ?? true;

        // Check if client allows local login (from client's EnableLocalLogin setting)
        // Client setting can only disable if tenant allows; cannot enable if tenant disables
        var clientEnableLocalLogin = context.GetData<bool?>("enable_local_login") ?? true;
        var enableLocalLogin = tenantEnableLocalLogin && clientEnableLocalLogin;

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
            if (!string.IsNullOrEmpty(changedUser.TenantId))
                context.SetData("tenant_id", changedUser.TenantId);
            await userService.RecordLoginAsync(changedUser.Id, cancellationToken);
            return StepHandlerResult.SuccessOrBranch(context, BuildOutputClaims(changedUser), "auth_local");
        }

        // An external sign-in waiting for the person to confirm the account its email belongs to. Once linked, the
        // external sign-in (still held) finds the account by its login below.
        if (externalAuthService != null
            && ExistingAccountLink.PendingProvider(context) is { } pendingProvider
            && !pendingProvider.StartsWith("saml:", StringComparison.OrdinalIgnoreCase))
        {
            var outcome = await ExistingAccountLink.ContinueAsync(context, cancellationToken);
            if (outcome.Result != null)
            {
                return outcome.Result;
            }
        }

        // Check if this is an external provider callback
        if (externalAuthService != null)
        {
            var externalResult = await externalAuthService.GetExternalLoginResultAsync(cancellationToken);
            if (externalResult != null && externalResult.Succeeded)
            {
                return await HandleExternalCallbackAsync(context, externalAuthService, userService, externalResult, logger, cancellationToken);
            }
        }

        // Check for FIDO2/passkey assertion response
        var assertionResponse = context.GetInput("assertionResponse");
        if (!string.IsNullOrEmpty(assertionResponse) && fido2Service != null)
        {
            return await HandlePasskeyAssertionAsync(context, fido2Service, userService, assertionResponse, logger, cancellationToken);
        }

        // Check for passkey login initiation
        var action = context.GetInput("action");
        if (action == "passkey_login" && fido2Service != null)
        {
            return await InitiatePasskeyLoginAsync(context, fido2Service, logger, cancellationToken);
        }

        // Check for external provider selection
        var selectedProvider = context.GetInput("provider");
        if (!string.IsNullOrEmpty(selectedProvider) && externalAuthService != null)
        {
            return await InitiateExternalLoginAsync(context, externalAuthService, selectedProvider, logger, cancellationToken);
        }

        // Check for local login submission
        var username = context.GetInput("username");
        var password = context.GetInput("password");

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            // Block local login if disabled for this tenant or client
            if (!enableLocalLogin)
            {
                var disabledBy = !tenantEnableLocalLogin ? "tenant" : "client";
                logger.LogWarning("Local login attempt blocked for client {ClientId} - EnableLocalLogin is false ({DisabledBy})", context.ClientId, disabledBy);
                context.SetData("login_error", "Local username/password login is not enabled for this application.");
                return await ShowLoginUiAsync(context, externalAuthService, fido2Service, tenantContext, enableLocalLogin, cancellationToken);
            }
            return await HandleLocalLoginAsync(context, userService, username, password, logger, cancellationToken);
        }

        // Show composite login UI
        return await ShowLoginUiAsync(context, externalAuthService, fido2Service, tenantContext, enableLocalLogin, cancellationToken);
    }

    private async Task<StepHandlerResult> ShowLoginUiAsync(
        StepExecutionContext context,
        IExternalAuthService? externalAuthService,
        IFido2Service? fido2Service,
        ITenantContext tenantContext,
        bool enableLocalLogin,
        CancellationToken cancellationToken)
    {
        var loginHint = context.GetData<string>("login_hint");
        var errorMessage = context.GetData<string>("login_error");

        // Get external providers, filtered by client IdP restrictions
        var externalProviders = new List<ExternalProviderViewModel>();
        if (externalAuthService != null)
        {
            // Use client-filtered providers if client ID is available
            var providers = !string.IsNullOrEmpty(context.ClientId)
                ? await externalAuthService.GetAvailableProvidersAsync(context.ClientId, cancellationToken)
                : await externalAuthService.GetAvailableProvidersAsync(cancellationToken);

            // Also apply step-level config filter
            var allowedProviders = context.GetConfig<List<string>>("externalProviders", null);
            if (allowedProviders != null)
            {
                providers = providers.Where(p => allowedProviders.Contains(p.Scheme)).ToList();
            }

            externalProviders = providers.Select(p => new ExternalProviderViewModel
            {
                Scheme = p.Scheme,
                DisplayName = p.DisplayName ?? p.Scheme,
                IconUrl = p.IconUrl,
                ProviderType = p.ProviderType
            }).ToList();
        }

        // Check if passkey is available
        var showPasskey = context.GetConfig("showPasskey", true) && fido2Service != null;
        var passkeyUsernameless = context.GetConfig("passkeyUsernameless", true);

        // Respect client's EnableLocalLogin setting AND step-level config
        var showLocalLogin = enableLocalLogin && context.GetConfig("showLocalLogin", true);

        var settingsProvider = context.ServiceProvider.GetRequiredService<ITenantSettingsProvider>();
        var branding = await settingsProvider.GetBrandingSettingsAsync(cancellationToken);

        var viewModel = new CompositeLoginViewModel
        {
            LoginHint = loginHint,
            Username = loginHint,
            ErrorMessage = errorMessage,
            ShowLocalLogin = showLocalLogin,
            ShowExternalLogin = context.GetConfig("showExternalLogin", true) && externalProviders.Any(),
            ShowPasskey = showPasskey,
            PasskeyUsernameless = passkeyUsernameless,
            AllowRememberMe = context.GetConfig("allowRememberMe", true),
            AllowRegistration = context.GetConfig("allowRegistration", false),
            AllowForgotPassword = context.GetConfig("allowForgotPassword", true),
            ExternalProviders = externalProviders,
            TenantName = tenantContext.Tenant?.Name,
            TenantLogo = branding.LogoUrl,
            RegistrationUrl = context.GetConfig<string>("registrationUrl", null),
            ForgotPasswordUrl = context.GetConfig<string>("forgotPasswordUrl", null),
            // primaryAction tells the view which sign-in path to make
            // visually dominant on screens that show all three (local /
            // external / passkey). The view treats it as a CSS hint.
            PrimaryAction = context.GetConfig("primaryAction", "local")!,
            LoginLayout = context.GetConfig("loginLayout", "standard")!
        };

        return StepHandlerResult.ShowUi("Journey/_CompositeLogin", viewModel);
    }

    private async Task<StepHandlerResult> InitiatePasskeyLoginAsync(
        StepExecutionContext context,
        IFido2Service fido2Service,
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
                var enableLocalLogin = context.GetData<bool?>("enable_local_login") ?? true;
                return await ShowLoginUiAsync(context,
                    context.ServiceProvider.GetService<IExternalAuthService>(),
                    fido2Service,
                    context.ServiceProvider.GetRequiredService<ITenantContext>(),
                    enableLocalLogin,
                    cancellationToken);
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
            context.SetData("login_error", ex.Message);
            var enableLocalLoginOnError = context.GetData<bool?>("enable_local_login") ?? true;
            return await ShowLoginUiAsync(context,
                context.ServiceProvider.GetService<IExternalAuthService>(),
                fido2Service,
                context.ServiceProvider.GetRequiredService<ITenantContext>(),
                enableLocalLoginOnError,
                cancellationToken);
        }
    }

    private async Task<StepHandlerResult> HandlePasskeyAssertionAsync(
        StepExecutionContext context,
        IFido2Service fido2Service,
        IOlusoUserService userService,
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
                context.SetData("login_error", result.ErrorDescription ?? "Passkey verification failed");
                var enableLocalLoginOnFail = context.GetData<bool?>("enable_local_login") ?? true;
                return await ShowLoginUiAsync(context,
                    context.ServiceProvider.GetService<IExternalAuthService>(),
                    fido2Service,
                    context.ServiceProvider.GetRequiredService<ITenantContext>(),
                    enableLocalLoginOnFail,
                    cancellationToken);
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
            if (!string.IsNullOrEmpty(user.TenantId))
                context.SetData("tenant_id", user.TenantId);

            await userService.RecordLoginAsync(user.Id, cancellationToken);
            logger.LogInformation("User {UserId} authenticated via passkey", user.Id);

            return StepHandlerResult.SuccessOrBranch(context, BuildOutputClaims(user, "passkey"), "auth_passkey");
        }
        catch (Fido2Exception ex)
        {
            logger.LogError(ex, "FIDO2 assertion verification error");
            return StepHandlerResult.Fail("fido2_error", ex.Message);
        }
    }

    private async Task<StepHandlerResult> HandleLocalLoginAsync(
        StepExecutionContext context,
        IOlusoUserService userService,
        string username,
        string password,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var result = await userService.ValidateCredentialsAsync(username, password, tenantContext.TenantId, cancellationToken);

        if (result.IsLockedOut)
        {
            logger.LogWarning("Login failed: account locked out for {Username}", username);
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
            context.SetData("login_error", result.ErrorDescription ?? "Invalid username or password");
            var externalAuthService = context.ServiceProvider.GetService<IExternalAuthService>();
            var fido2Service = context.ServiceProvider.GetService<IFido2Service>();
            var enableLocalLoginOnFail = context.GetData<bool?>("enable_local_login") ?? true;
            return (await ShowLoginUiAsync(context, externalAuthService, fido2Service, tenantContext, enableLocalLoginOnFail, cancellationToken))
                .AsFailedAttempt();
        }

        var user = result.User!;

        // Check MFA requirement
        if (result.RequiresMfa)
        {
            context.SetData("mfa_required", true);
            context.SetData("mfa_user_id", user.Id);
        }

        context.UserId = user.Id;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("remember_me", context.GetInput("rememberMe") is "true" or "on");
        context.SetData("auth_method", "pwd");
        if (!string.IsNullOrEmpty(user.TenantId))
            context.SetData("tenant_id", user.TenantId);

        await userService.RecordLoginAsync(user.Id, cancellationToken);
        logger.LogInformation("User {UserId} authenticated via local login", user.Id);

        var outputClaims = BuildOutputClaims(user);
        if (result.RequiresMfa)
            return StepHandlerResult.SuccessOrBranch(context, outputClaims, "mfa_required", "auth_local");

        return StepHandlerResult.SuccessOrBranch(context, outputClaims, "auth_local");
    }

    private async Task<StepHandlerResult> InitiateExternalLoginAsync(
        StepExecutionContext context,
        IExternalAuthService authService,
        string provider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Initiating external login with provider {Provider}", provider);

        var callbackUrl = $"/journey/{context.JourneyId}/callback";

        var extraParams = ExternalLoginStepHandler.BuildExternalProviderParams(context);
        var challengeResult = await authService.ChallengeAsync(provider, callbackUrl, extraParams, cancellationToken);

        if (!challengeResult.Succeeded)
        {
            return StepHandlerResult.Fail("external_auth_failed", challengeResult.Error ?? "Failed to initiate external login");
        }

        return StepHandlerResult.Redirect(challengeResult.RedirectUrl ?? callbackUrl);
    }

    private async Task<StepHandlerResult> HandleExternalCallbackAsync(
        StepExecutionContext context,
        IExternalAuthService authService,
        IOlusoUserService userService,
        ExternalLoginResult result,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var provider = result.Provider;
        var providerKey = result.ProviderKey;

        logger.LogInformation("External login callback from {Provider}", provider);

        // Find user by external login
        var userId = await authService.FindUserByLoginAsync(provider, providerKey, cancellationToken);
        OlusoUserInfo? user = null;

        var emailTrusted = await ExternalEmailTrust.IsTrustedAsync(
            provider,
            type => result.Claims?.FirstOrDefault(c => string.Equals(c.Key, type, StringComparison.Ordinal)).Value,
            result.Email,
            tenantContext.TenantId,
            context.ServiceProvider.GetService<IExternalProviderConfigStore>(),
            context.ServiceProvider.GetRequiredService<ITenantEmailDomainStore>(),
            cancellationToken);

        if (userId != null)
        {
            user = await userService.FindByIdAsync(userId, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(result.Email))
        {
            // The account with this email joins only when the provider can vouch for the address and the account's
            // own was proven; otherwise the person confirms it with a code first.
            var owner = await userService.FindByEmailAsync(result.Email, cancellationToken);
            if (owner != null)
            {
                if (!emailTrusted || !owner.EmailVerified)
                {
                    logger.LogInformation(
                        "External login from {Provider} matches account {UserId} by email; asking them to confirm it", provider, owner.Id);
                    return await ExistingAccountLink.BeginAsync(context, owner.Id, provider, providerKey, cancellationToken);
                }

                var linked = await authService.LinkLoginAsync(owner.Id, provider, providerKey, provider, cancellationToken);
                if (!linked.Succeeded)
                {
                    await authService.SignOutExternalAsync(cancellationToken);
                    return StepHandlerResult.Fail("link_failed", "Failed to link external account");
                }
                user = owner;
            }
        }

        // Auto-provision if configured
        var autoProvision = context.GetConfig("autoProvision", true);
        if (user == null && autoProvision && !string.IsNullOrEmpty(result.Email))
        {
            var createResult = await userService.CreateUserAsync(new CreateUserRequest
            {
                Email = result.Email,
                Password = Guid.NewGuid().ToString(),
                FirstName = result.FirstName,
                LastName = result.LastName,
                TenantId = tenantContext.TenantId,
                ClientId = context.ClientId
            }, cancellationToken);

            if (createResult.Succeeded)
            {
                await authService.LinkLoginAsync(createResult.UserId!, provider, providerKey, provider, cancellationToken);
                if (emailTrusted)
                {
                    await userService.SetEmailVerifiedAsync(createResult.UserId!, true, cancellationToken);
                }
                user = await userService.FindByIdAsync(createResult.UserId!, cancellationToken);
                logger.LogInformation("Created new user {UserId} from external provider {Provider}", user?.Id, provider);
            }
        }

        await authService.SignOutExternalAsync(cancellationToken);

        if (user == null)
        {
            return StepHandlerResult.Fail("user_not_found", "No account found. Please register first.");
        }

        if (!user.IsActive)
        {
            return StepHandlerResult.Fail("user_deactivated", "Your account has been deactivated");
        }

        context.UserId = user.Id;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("remember_me", context.GetInput("rememberMe") is "true" or "on");
        context.SetData("auth_method", provider.ToLower());
        context.SetData("idp", provider);
        if (!string.IsNullOrEmpty(user.TenantId))
            context.SetData("tenant_id", user.TenantId);

        await userService.RecordLoginAsync(user.Id, cancellationToken);
        logger.LogInformation("User {UserId} authenticated via external provider {Provider}", user.Id, provider);

        return StepHandlerResult.SuccessOrBranch(context, BuildOutputClaims(user, provider),
            $"auth_{provider.ToLowerInvariant()}", "auth_external");
    }

    private static IDictionary<string, object> BuildOutputClaims(ValidatedUser user)
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

        return outputData;
    }

    private static IDictionary<string, object> BuildOutputClaims(OlusoUserInfo user, string? idp = null)
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
        if (!string.IsNullOrEmpty(idp))
            outputData["idp"] = idp;

        return outputData;
    }
}

public class CompositeLoginViewModel
{
    public string? LoginHint { get; set; }
    public string? Username { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ShowLocalLogin { get; set; } = true;
    public bool ShowExternalLogin { get; set; } = true;
    public bool ShowPasskey { get; set; }
    public bool PasskeyUsernameless { get; set; } = true;
    public bool AllowRememberMe { get; set; } = true;
    public bool RememberMe { get; set; }
    public bool AllowRegistration { get; set; }
    public bool AllowForgotPassword { get; set; } = true;
    public string? RegistrationUrl { get; set; }
    public string? ForgotPasswordUrl { get; set; }
    public string? TenantName { get; set; }
    public string? TenantLogo { get; set; }
    public List<ExternalProviderViewModel> ExternalProviders { get; set; } = new();

    /// <summary>
    /// Which sign-in action the view should emphasise when multiple are
    /// shown. Values: "local" (default), "external", "passkey". The view
    /// uses this to add a primary-button class to the matching block.
    /// </summary>
    public string PrimaryAction { get; set; } = "local";

    /// <summary>
    /// Order the sign-in method blocks render in. Values: "standard"
    /// (default — password, passkey, social), "social-first",
    /// "passkey-first". The view applies a login-layout-{value} class and
    /// CSS flex ordering does the rest.
    /// </summary>
    public string LoginLayout { get; set; } = "standard";
}
