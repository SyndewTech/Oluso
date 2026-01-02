using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles user registration/sign-up step in journey flows.
/// Creates a new user account and optionally requires email verification.
/// </summary>
/// <remarks>
/// Configuration options:
/// - allowSelfRegistration: bool (default: true)
/// - requireEmailVerification: bool (default: true)
/// - requireTermsAcceptance: bool (default: false)
/// - allowedEmailDomains: string (comma-separated, default: null = all allowed)
/// - termsUrl: string (default: null)
/// - privacyUrl: string (default: null)
/// </remarks>
public class SignUpStepHandler : IStepHandler
{
    public string StepType => "signup";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<SignUpStepHandler>>();
        var eventService = context.ServiceProvider.GetService<IOlusoEventService>();

        // Get configuration from step settings
        var allowSelfRegistration = context.GetConfig("allowSelfRegistration", true);
        var requireEmailVerification = context.GetConfig("requireEmailVerification", true);
        var requireTermsAcceptance = context.GetConfig("requireTermsAcceptance", false);
        var allowedEmailDomains = context.GetConfig<string>("allowedEmailDomains", null);
        var termsUrl = context.GetConfig<string>("termsUrl", null);
        var privacyUrl = context.GetConfig<string>("privacyUrl", null);

        // Check if self-registration is allowed
        if (!allowSelfRegistration)
        {
            return StepHandlerResult.Fail("registration_disabled", "Self-registration is not available for this organization.");
        }

        // Check if user wants to switch to login
        var actionType = context.GetInput("action_type");
        if (actionType == "login")
        {
            return StepHandlerResult.Branch("login");
        }

        // Check if we have user input (form submission)
        var email = context.GetInput("email");
        var password = context.GetInput("password");
        var confirmPassword = context.GetInput("confirmPassword");

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            // Show registration UI
            var loginHint = context.GetData<string>("login_hint");
            return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                requireTermsAcceptance,
                termsUrl,
                privacyUrl,
                loginHint: loginHint,
                isEmailDisabled: !string.IsNullOrWhiteSpace(loginHint)));
        }

        // Validate passwords match
        if (password != confirmPassword)
        {
            var firstName = context.GetInput("firstName");
            var lastName = context.GetInput("lastName");
            return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                requireTermsAcceptance,
                termsUrl,
                privacyUrl,
                errorMessage: "Passwords do not match",
                email: email,
                firstName: firstName,
                lastName: lastName));
        }

        // Check email domain restrictions
        if (!string.IsNullOrEmpty(allowedEmailDomains))
        {
            var domains = allowedEmailDomains
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.ToLowerInvariant())
                .ToList();

            if (domains.Count > 0)
            {
                var emailDomain = email.Contains('@') ? email.Split('@')[1].ToLowerInvariant() : "";
                if (!domains.Contains(emailDomain))
                {
                    logger.LogWarning("Registration rejected: email domain {Domain} not allowed", emailDomain);
                    return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                        tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                        requireTermsAcceptance,
                        termsUrl,
                        privacyUrl,
                        errorMessage: "Registration is only allowed for specific email domains.",
                        email: email));
                }
            }
        }

        // Check terms acceptance if required
        if (requireTermsAcceptance)
        {
            var acceptTerms = context.GetInput("acceptTerms");
            if (!string.Equals(acceptTerms, "true", StringComparison.OrdinalIgnoreCase))
            {
                var firstName = context.GetInput("firstName");
                var lastName = context.GetInput("lastName");
                return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                    tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                    requireTermsAcceptance,
                    termsUrl,
                    privacyUrl,
                    errorMessage: "You must accept the terms and conditions to register.",
                    email: email,
                    firstName: firstName,
                    lastName: lastName));
            }
        }

        // Create user via user service
        var firstNameInput = context.GetInput("firstName") ?? "";
        var lastNameInput = context.GetInput("lastName") ?? "";

        var createResult = await userService.CreateUserAsync(new CreateUserRequest
        {
            Email = email,
            Password = password,
            FirstName = firstNameInput,
            LastName = lastNameInput,
            TenantId = tenantContext.TenantId,
            RequireEmailVerification = requireEmailVerification
        }, cancellationToken);

        if (!createResult.Succeeded)
        {
            logger.LogWarning("User registration failed: {Error}", createResult.Error);

            // Simplify password policy errors for users
            var errorMessage = createResult.Error?.Contains("Password") == true
                ? "Password does not meet requirements. Please use at least 8 characters with uppercase, lowercase, and a number."
                : createResult.ErrorDescription ?? createResult.Error ?? "Registration failed";

            return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                requireTermsAcceptance,
                termsUrl,
                privacyUrl,
                errorMessage: errorMessage,
                email: email,
                firstName: firstNameInput,
                lastName: lastNameInput));
        }

        var user = createResult.User!;
        logger.LogInformation("User {UserId} created via sign-up journey", user.Id);

        // Raise user registered event
        var clientId = context.GetData<string>("client_id") ?? "unknown";
        if (eventService != null)
        {
            await eventService.RaiseAsync(new UserRegisteredEvent
            {
                TenantId = tenantContext.TenantId,
                SubjectId = user.Id,
                Email = email,
                Username = email,
                ClientId = clientId
            }, cancellationToken);
        }

        // Handle email verification requirement
        if (requireEmailVerification && !user.EmailVerified)
        {
            context.SetData("pending_email_verification", true);
            context.SetData("email_verification_user_id", user.Id);
            context.UserId = user.Id;

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["sub"] = user.Id,
                ["email"] = email,
                ["email_verified"] = false,
                ["pending_verification"] = true
            });
        }

        // User is fully registered - set as authenticated
        context.UserId = user.Id;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", "pwd");

        return StepHandlerResult.Success(new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.Username,
            ["email"] = email,
            ["email_verified"] = user.EmailVerified,
            ["given_name"] = firstNameInput,
            ["family_name"] = lastNameInput
        });
    }

    private static SignUpViewModel CreateViewModel(
        string? tenantName,
        bool requireTermsAcceptance,
        string? termsUrl,
        string? privacyUrl,
        string? errorMessage = null,
        string? email = null,
        string? firstName = null,
        string? lastName = null,
        string? loginHint = null,
        bool isEmailDisabled = false)
    {
        return new SignUpViewModel
        {
            ErrorMessage = errorMessage,
            Email = email ?? loginHint,
            IsEmailDisabled = isEmailDisabled,
            FirstName = firstName,
            LastName = lastName,
            LoginHint = loginHint,
            TenantName = tenantName,
            RequireTermsAcceptance = requireTermsAcceptance,
            TermsUrl = termsUrl,
            PrivacyUrl = privacyUrl
        };
    }
}

/// <summary>
/// View model for the sign-up page
/// </summary>
public class SignUpViewModel
{
    public string? ErrorMessage { get; set; }
    public string? Email { get; set; }
    public bool IsEmailDisabled { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? LoginHint { get; set; }
    public string? TenantName { get; set; }
    public string? TenantLogo { get; set; }
    public bool RequireTermsAcceptance { get; set; }
    public string? TermsUrl { get; set; }
    public string? PrivacyUrl { get; set; }
}
