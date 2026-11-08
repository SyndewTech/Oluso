using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
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
/// - requirePassword: bool (default: true) — set to false for passwordless flows (e.g. passkey-only signup)
/// - requireEmailVerification: bool (default: true)
/// - requireTermsAcceptance: bool (default: false)
/// - allowedEmailDomains: list of domains (or comma-separated text); empty = all the tenant allows
/// - termsUrl: string (default: null)
/// - privacyUrl: string (default: null)
/// </remarks>
public class SignUpStepHandler : IStepHandler
{
    public string StepType => "signup";

    /// <summary>Branch taken by "Already have an account? Sign in"; the link shows only when it's configured.</summary>
    public const string SignInBranch = "login";

    /// <summary>The account sign-up created that still needs its email verified.</summary>
    public const string PendingVerificationUserDataKey = "email_verification_user_id";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(context, cancellationToken);
        if (result.StepResult?.ViewModel is SignUpViewModel model)
        {
            model.ShowSignInLink = context.Configuration.Branches?.ContainsKey(SignInBranch) == true;
        }
        return result;
    }

    private async Task<StepHandlerResult> RunAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<SignUpStepHandler>>();
        var roleStore = context.ServiceProvider.GetService<IRoleStore>();

        // Tenant settings are the floor — admin policy at the tenant level
        // can't be relaxed by a step config, only further restricted. So:
        //   - allowSelfRegistration: AND (tenant=false closes registration
        //     for the whole tenant; a step can't override that)
        //   - requireEmailVerification / requireTermsAcceptance: OR
        //     (tenant=true forces them on; the step can additionally require)
        //   - termsUrl / privacyUrl: step value wins when explicitly set;
        //     otherwise fall back to the tenant URL so admins set them
        //     once at the tenant level
        //   - allowedEmailDomains: tenant list is authoritative; step list
        //     further restricts (intersection). Empty tenant list = any.
        var tenant = tenantContext.Tenant;

        var stepAllowSelfRegistration = context.GetConfig("allowSelfRegistration", true);
        var allowSelfRegistration = stepAllowSelfRegistration
            && (tenant?.AllowSelfRegistration ?? true);

        var requirePassword = context.GetConfig("requirePassword", true);

        var stepRequireEmailVerification = context.GetConfig("requireEmailVerification", true);
        var requireEmailVerification = stepRequireEmailVerification
            || (tenant?.RequireEmailVerification ?? false);

        var stepRequireTermsAcceptance = context.GetConfig("requireTermsAcceptance", false);
        var requireTermsAcceptance = stepRequireTermsAcceptance
            || (tenant?.RequireTermsAcceptance ?? false);

        var stepTermsUrl = context.GetConfig<string>("termsUrl", null);
        var termsUrl = !string.IsNullOrEmpty(stepTermsUrl) ? stepTermsUrl : tenant?.TermsOfServiceUrl;

        var stepPrivacyUrl = context.GetConfig<string>("privacyUrl", null);
        var privacyUrl = !string.IsNullOrEmpty(stepPrivacyUrl) ? stepPrivacyUrl : tenant?.PrivacyPolicyUrl;

        // Combine tenant + step domain allowlists. Empty list means "any" —
        // intersecting with "any" is a no-op, so we just take the non-empty
        // sources and intersect them. Both sources accept comma-separated.
        var stepAllowedEmailDomains = context.GetConfig<List<string>>("allowedEmailDomains", null) is { Count: > 0 } stepDomains
            ? string.Join(",", stepDomains)
            : null;
        var allowedEmailDomains = CombineAllowedDomains(tenant?.AllowedEmailDomains, stepAllowedEmailDomains);
        // When verification is required, branch to this step ID so the
        // user proves email ownership before the journey advances. The
        // target is typically a passwordless_email step (mode=otp). When
        // unset and verification is required, we log a warning and fall
        // through with a "pending" claim — the journey continues but
        // downstream code can choose to gate on email_verified itself.
        var verificationStepId = context.GetConfig<string>("verificationStepId", null);

        // Claim/property mapping config — lets a journey carry email/
        // username/password forward from a prior data-collection step
        // (or an external-IdP claim) instead of requiring the sign-up
        // form to be the source of truth. Falls back to the standard
        // input names when not set.
        var emailClaimName = context.GetConfig<string>("emailClaim", "email") ?? "email";
        var usernameClaimName = context.GetConfig<string>("usernameClaim", null);
        // "email" (default): the account's username is its email. "ask": the form asks for one.
        var askUsername = string.Equals(context.GetConfig<string>("usernameMode", "email"), "ask", StringComparison.OrdinalIgnoreCase);
        var passwordClaimName = context.GetConfig<string>("passwordClaim", "password") ?? "password";
        var defaultRoles = context.GetConfig<List<string>>("defaultRoles", null) ?? new List<string>();
        var claimMappings = context.GetConfig<Dictionary<string, string>>("claimMappings", null)
            ?? new Dictionary<string, string>();

        // Check if self-registration is allowed
        // Registration closed: the step stands aside (or takes its registration_disabled
        // branch), as its setting describes, rather than ending the journey.
        if (!allowSelfRegistration)
        {
            logger.LogInformation("Sign-up step {StepId} skipped: self-registration is off", context.StepId);
            return StepHandlerResult.SkipOrBranch(context, "registration_disabled");
        }

        // Check if user wants to switch to login
        var actionType = context.GetInput("action_type");
        if (actionType == "login" && context.Configuration.Branches?.ContainsKey(SignInBranch) == true)
        {
            return StepHandlerResult.Branch(SignInBranch);
        }

        // Check if we have user input (form submission). Fall back to
        // claims/state populated by prior steps when the form fields
        // aren't present — that's how upstream data-collection or an
        // external-IdP step can drive sign-up without re-prompting.
        var email = context.GetInput("email")
            ?? (string.IsNullOrEmpty(emailClaimName) ? null : context.GetData<string>(emailClaimName));
        var password = context.GetInput("password")
            ?? (requirePassword && !string.IsNullOrEmpty(passwordClaimName) ? context.GetData<string>(passwordClaimName) : null);
        var confirmPassword = context.GetInput("confirmPassword");
        var usernameInput = (askUsername ? context.GetInput("username") : null)
            ?? (string.IsNullOrEmpty(usernameClaimName) ? null : context.GetData<string>(usernameClaimName));
        usernameInput = string.IsNullOrWhiteSpace(usernameInput) ? null : usernameInput.Trim();

        var hasRequiredInput = !string.IsNullOrEmpty(email) && (!requirePassword || !string.IsNullOrEmpty(password));

        if (!hasRequiredInput)
        {
            // Show registration UI
            var loginHint = context.GetData<string>("login_hint");
            return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                requireTermsAcceptance,
                termsUrl,
                privacyUrl,
                requirePassword: requirePassword,
                askUsername: askUsername,
                username: usernameInput,
                loginHint: loginHint,
                isEmailDisabled: !string.IsNullOrWhiteSpace(loginHint)));
        }

        if (askUsername && usernameInput == null)
        {
            return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                requireTermsAcceptance,
                termsUrl,
                privacyUrl,
                requirePassword: requirePassword,
                askUsername: askUsername,
                username: usernameInput,
                errorMessage: "Choose a username.",
                email: email,
                firstName: context.GetInput("firstName"),
                lastName: context.GetInput("lastName")));
        }

        // Validate passwords match (only when password is required)
        if (requirePassword && password != confirmPassword)
        {
            var firstName = context.GetInput("firstName");
            var lastName = context.GetInput("lastName");
            return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                requireTermsAcceptance,
                termsUrl,
                privacyUrl,
                requirePassword: requirePassword,
                askUsername: askUsername,
                username: usernameInput,
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
                var emailDomain = email!.Contains('@') ? email.Split('@')[1].ToLowerInvariant() : "";
                if (!domains.Contains(emailDomain))
                {
                    logger.LogWarning("Registration rejected: email domain {Domain} not allowed", emailDomain);
                    return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                        tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                        requireTermsAcceptance,
                        termsUrl,
                        privacyUrl,
                        requirePassword: requirePassword,
                askUsername: askUsername,
                username: usernameInput,
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
                    requirePassword: requirePassword,
                askUsername: askUsername,
                username: usernameInput,
                    errorMessage: "You must accept the terms and conditions to register.",
                    email: email,
                    firstName: firstName,
                    lastName: lastName));
            }
        }

        // Create user via user service. ClaimMappings let the journey
        // pull additional fields (firstName/lastName/phone/etc.) from
        // upstream claim names instead of relying on the form layout —
        // useful when sign-up is the third step after a data-collection
        // form or an external IdP that already filled them in.
        var firstNameInput = ResolveValue(context, "firstName", claimMappings);
        var lastNameInput = ResolveValue(context, "lastName", claimMappings);
        var phoneInput = ResolveValue(context, "phoneNumber", claimMappings);

        var customProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, source) in claimMappings)
        {
            // Built-ins are mapped directly above; everything else
            // becomes a custom property on the user.
            if (target.Equals("firstName", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("lastName", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("phoneNumber", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var value = context.GetInput(source) ?? context.GetData<string>(source);
            if (!string.IsNullOrEmpty(value))
            {
                customProperties[target] = value;
            }
        }

        var createResult = await userService.CreateUserAsync(new CreateUserRequest
        {
            Username = usernameInput ?? email!,
            Email = email!,
            Password = requirePassword ? password : null,
            FirstName = firstNameInput,
            LastName = lastNameInput,
            PhoneNumber = string.IsNullOrEmpty(phoneInput) ? null : phoneInput,
            TenantId = tenantContext.TenantId,
            RequireEmailVerification = requireEmailVerification,
            CustomProperties = customProperties.Count > 0 ? customProperties : null,
            ClientId = context.GetData<string>("client_id") ?? context.ClientId,
            PolicyId = context.PolicyId
        }, cancellationToken);

        if (!createResult.Succeeded)
        {
            logger.LogWarning("User registration failed: {Error}", createResult.Error);

            // Simplify password policy errors for users
            var errorMessage = createResult.Error?.Contains("Password") == true
                ? "Password does not meet requirements. Please use at least 8 characters with uppercase, lowercase, and a number."
                : createResult.MessageOr("Registration failed");

            return StepHandlerResult.ShowUi("Journey/_SignUp", CreateViewModel(
                tenantContext.Tenant?.DisplayName ?? tenantContext.Tenant?.Name,
                requireTermsAcceptance,
                termsUrl,
                privacyUrl,
                requirePassword: requirePassword,
                askUsername: askUsername,
                username: usernameInput,
                errorMessage: errorMessage,
                email: email,
                firstName: firstNameInput,
                lastName: lastNameInput));
        }

        var user = createResult.User!;
        logger.LogInformation("User {UserId} created via sign-up journey", user.Id);

        // Terms accepted earlier in this journey, before the account existed.
        if (!await TermsAcceptanceStepHandler.RecordPendingAcceptanceAsync(context, userService, user.Id, cancellationToken))
        {
            logger.LogError("Recording terms accepted before sign-up failed for user {UserId}", user.Id);
        }

        // Default roles are role ids; only this tenant's roles and platform-wide ones are granted.
        // A missing role is logged but doesn't fail the sign-up.
        var defaultRoleIds = defaultRoles.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (defaultRoleIds.Count > 0 && roleStore != null)
        {
            var roles = (await roleStore.GetByIdsAsync(defaultRoleIds, cancellationToken))
                .Where(r => r.TenantId == null || r.TenantId == tenantContext.TenantId)
                .ToDictionary(r => r.Id);
            foreach (var roleId in defaultRoleIds)
            {
                if (!roles.TryGetValue(roleId, out var role))
                {
                    logger.LogWarning(
                        "Sign-up step: configured default role {RoleId} was not found for tenant {TenantId}; skipping",
                        roleId, tenantContext.TenantId);
                    continue;
                }
                try
                {
                    await context.ServiceProvider.GetRequiredService<IRoleGrantService>()
                        .GrantAsync(role.Id, user.Id, null, RoleGrantActor.System("Sign-up"), cancellationToken);
                    logger.LogDebug("Sign-up: assigned role {Role} ({RoleId}) to user {UserId}", role.Name, role.Id, user.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Sign-up: failed to assign role {Role} ({RoleId}) to user {UserId}", role.Name, role.Id, user.Id);
                }
            }
        }

        // Handle email verification requirement. When verificationStepId is
        // set, branch to that step so the user proves email ownership
        // before the journey continues. The target step (typically
        // passwordless_email) reads the email from journey state, sends a
        // code, validates it, and persists user.EmailVerified=true on
        // success — then the journey advances forward from there.
        if (requireEmailVerification && !user.EmailVerified)
        {
            context.UserId = user.Id;
            context.SetData(PendingVerificationUserDataKey, user.Id);
            // Pre-populate email so the verification step can pick it up
            // from state without re-prompting.
            context.SetData("email", email!);

            if (!string.IsNullOrEmpty(verificationStepId))
            {
                logger.LogInformation(
                    "Sign-up complete for {UserId}; branching to verification step {StepId}",
                    user.Id, verificationStepId);
                return StepHandlerResult.Branch(verificationStepId, new Dictionary<string, object>
                {
                    ["sub"] = user.Id,
                    ["email"] = email!,
                    ["email_verified"] = false,
                    ["pending_verification"] = true
                });
            }

            // No explicit verification step configured — emit pending
            // markers and continue. The journey author is responsible
            // for placing a verification step downstream or gating on
            // pending_verification themselves.
            logger.LogWarning(
                "Sign-up requires email verification but no verificationStepId is configured — journey continues with pending_verification=true");
            context.SetData("pending_email_verification", true);

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["sub"] = user.Id,
                ["email"] = email!,
                ["email_verified"] = false,
                ["pending_verification"] = true
            });
        }

        // User is fully registered - set as authenticated
        context.UserId = user.Id;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", requirePassword ? "pwd" : "signup");

        return StepHandlerResult.Success(new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.Username,
            ["email"] = email!,
            ["email_verified"] = user.EmailVerified,
            ["given_name"] = firstNameInput,
            ["family_name"] = lastNameInput
        });
    }

    /// <summary>
    /// Intersects the tenant-level and step-level domain allowlists into a
    /// single comma-separated string the existing domain check at the call
    /// site can consume. Empty / null lists mean "any" — intersecting with
    /// "any" is a no-op, so:
    /// <list type="bullet">
    ///   <item>tenant null + step null → null (no restriction)</item>
    ///   <item>tenant set + step null → tenant list (tenant restricts)</item>
    ///   <item>tenant null + step set → step list (step restricts)</item>
    ///   <item>both set → intersection (most restrictive — a step can only
    ///     further narrow what the tenant already allows, never expand it)</item>
    /// </list>
    /// </summary>
    private static string? CombineAllowedDomains(string? tenantList, string? stepList)
    {
        var tenantSet = SplitDomains(tenantList);
        var stepSet = SplitDomains(stepList);

        if (tenantSet == null && stepSet == null) return null;
        if (tenantSet == null) return string.Join(",", stepSet!);
        if (stepSet == null) return string.Join(",", tenantSet);

        var intersection = tenantSet.Intersect(stepSet, StringComparer.OrdinalIgnoreCase).ToList();
        // If the step tries to allow domains the tenant doesn't, the
        // intersection collapses to empty — which would normally mean
        // "any" but here means the opposite (no overlap). Keep the
        // tenant list authoritative in that case so we don't silently
        // un-restrict registration.
        return intersection.Count > 0 ? string.Join(",", intersection) : string.Join(",", tenantSet);
    }

    private static HashSet<string>? SplitDomains(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var set = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set.Count > 0 ? set : null;
    }

    /// <summary>
    /// Resolve a target user-property value from the journey context.
    /// Order: form input by target name → claim mapped via
    /// <paramref name="claimMappings"/> → state value by target name.
    /// </summary>
    private static string ResolveValue(StepExecutionContext context, string target, Dictionary<string, string> claimMappings)
    {
        var fromForm = context.GetInput(target);
        if (!string.IsNullOrEmpty(fromForm)) return fromForm;

        if (claimMappings.TryGetValue(target, out var sourceKey) && !string.IsNullOrEmpty(sourceKey))
        {
            var fromClaim = context.GetInput(sourceKey) ?? context.GetData<string>(sourceKey);
            if (!string.IsNullOrEmpty(fromClaim)) return fromClaim;
        }

        return context.GetData<string>(target) ?? string.Empty;
    }

    private static SignUpViewModel CreateViewModel(
        string? tenantName,
        bool requireTermsAcceptance,
        string? termsUrl,
        string? privacyUrl,
        bool requirePassword = true,
        bool askUsername = false,
        string? username = null,
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
            RequirePassword = requirePassword,
            AskUsername = askUsername,
            Username = username,
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

    /// <summary>Whether the step has a sign-in branch for "Already have an account?".</summary>
    public bool ShowSignInLink { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? LoginHint { get; set; }
    public string? TenantName { get; set; }
    public string? TenantLogo { get; set; }
    public bool RequirePassword { get; set; } = true;

    /// <summary>The form asks for a username; otherwise the email is the username.</summary>
    public bool AskUsername { get; set; }
    public string? Username { get; set; }

    public bool RequireTermsAcceptance { get; set; }
    public string? TermsUrl { get; set; }
    public string? PrivacyUrl { get; set; }
}
