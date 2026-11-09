using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Default implementation of step handler registry
/// </summary>
public class DefaultStepHandlerRegistry : IExtendedStepHandlerRegistry
{
    // Every source and operator a condition can use (Oluso.Core.Conditions.ConditionOperators,
    // JourneyConditionValues). previous_step reads the journey's data, where earlier steps' output lives.
    private static readonly string[] ConditionSources = { "data", "claim", "context", "user", "input", "config", "path", "previous_step" };

    private static readonly string[] ConditionOperatorChoices =
    {
        "equals", "notEquals", "gt", "gte", "lt", "lte", "greaterThan", "lessThan",
        "contains", "notContains", "startsWith", "endsWith", "matches", "regex",
        "exists", "notExists", "empty", "notEmpty", "in", "notIn", "true", "false"
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _handlerTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StepTypeInfo> _typeInfo = new(StringComparer.OrdinalIgnoreCase);

    public DefaultStepHandlerRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        // Register built-in step types metadata
        RegisterBuiltInTypes();

        // Discover and register custom step handlers with metadata
        DiscoverCustomStepHandlers();

        // Run any IConfigureStepHandlers contributors. This is how plugin
        // modules (e.g. LDAP, SAML) inject their step-type metadata into
        // the registry so it shows up in the journey-builder UI. Without
        // this loop, plugins could register IStepHandler services but
        // their metadata never reached _typeInfo, so admins couldn't see
        // them as options in the builder. Run last so plugin contributors
        // can override built-in / discovery defaults if they need to.
        var configurators = _serviceProvider.GetServices<IConfigureStepHandlers>();
        foreach (var configurator in configurators)
        {
            try
            {
                configurator.Configure(this);
            }
            catch (Exception ex)
            {
                // A buggy configurator shouldn't break registry init for
                // every other plugin. Surface via the service provider's
                // logger if one is available.
                var logger = _serviceProvider.GetService(typeof(Microsoft.Extensions.Logging.ILogger<DefaultStepHandlerRegistry>))
                    as Microsoft.Extensions.Logging.ILogger<DefaultStepHandlerRegistry>;
                logger?.LogError(ex,
                    "IConfigureStepHandlers contributor {Type} threw during Configure",
                    configurator.GetType().FullName);
            }
        }

        // Last: a step that describes its own settings is the authority on them.
        ApplyStepSettingsSchemas();
    }

    private void ApplyStepSettingsSchemas()
    {
        var handlers = _serviceProvider.GetServices<IStepHandler>()
            .Concat(_handlerTypes.Values.Select(t => _serviceProvider.GetService(t)).OfType<IStepHandler>());
        foreach (var handler in handlers)
        {
            if (handler is not IStepSettingsSchema described || !_typeInfo.TryGetValue(handler.StepType, out var info)) continue;
            _typeInfo[handler.StepType] = new StepTypeInfo
            {
                Type = info.Type,
                DisplayName = info.DisplayName,
                Description = info.Description,
                Category = info.Category,
                Icon = info.Icon,
                Module = info.Module,
                HandlerType = info.HandlerType ?? handler.GetType(),
                ConfigurationSchema = described.GetConfigurationSchema(),
                AvailableBranches = info.AvailableBranches,
            };
        }
    }

    private void DiscoverCustomStepHandlers()
    {
        // Get all registered ICustomStepHandler services
        var customHandlers = _serviceProvider.GetServices<ICustomStepHandler>();

        foreach (var handler in customHandlers)
        {
            // Skip if already registered (built-in types take precedence)
            if (_typeInfo.ContainsKey(handler.StepType))
                continue;

            // Check if handler provides metadata
            if (handler is ICustomStepHandlerMetadata metadata)
            {
                _typeInfo[handler.StepType] = new StepTypeInfo
                {
                    Type = handler.StepType,
                    DisplayName = metadata.DisplayName,
                    Description = metadata.Description,
                    Category = metadata.Category,
                    Icon = metadata.Icon,
                    Module = metadata.Module,
                    HandlerType = handler.GetType(),
                    ConfigurationSchema = metadata.GetConfigurationSchema()
                };
            }
            else
            {
                // Register with minimal metadata (at least it shows up in the list)
                _typeInfo[handler.StepType] = new StepTypeInfo
                {
                    Type = handler.StepType,
                    DisplayName = handler.StepType,
                    Description = $"Custom step: {handler.StepType}",
                    Category = "Custom",
                    HandlerType = handler.GetType()
                };
            }
        }
    }

    private void RegisterBuiltInTypes()
    {
        // Authentication
        RegisterTypeWithRawSchema("local_login", "Local Login", "Authentication",
            "Authenticate user with username/password (plus optional passkey)",
            new Dictionary<string, object>
            {
                ["allowRememberMe"] = new { type = "boolean", @default = true, description = "Show 'Remember me' checkbox" },
                ["allowSelfRegistration"] = new { type = "boolean", @default = false, description = "Show link to self-registration" },
                ["allowForgotPassword"] = new { type = "boolean", @default = true, description = "Show 'Forgot password?' link" },
                ["showPasskey"] = new { type = "boolean", @default = true, description = "Show passkey/WebAuthn sign-in option" },
                ["passkeyUsernameless"] = new { type = "boolean", @default = true, description = "Allow usernameless passkey login (no email entered first)" },
                ["loginHintClaim"] = new { type = "string", description = "Claim name to pre-populate the username field from when present in journey state" },
                ["reauthenticateAfter"] = new { type = "number", @default = 0, description = "When the policy runs for a signed-in user, the session counts as this sign-in only if it signed in within this many seconds; otherwise the same user signs in again. 0 accepts any live session." }
            },
            [
                Branch("auth_local", "Password login", "User authenticated with username/password"),
                Branch("auth_passkey", "Passkey login", "User authenticated with a FIDO2 passkey")
            ]);

        RegisterTypeWithRawSchema("composite_login", "Composite Login", "Authentication",
            "Combined login screen — local password, passkey, and external IdPs in one step",
            new Dictionary<string, object>
            {
                ["showLocalLogin"] = new { type = "boolean", @default = true, description = "Display username/password form" },
                ["showExternalLogin"] = new { type = "boolean", @default = true, description = "Display external IdP buttons" },
                ["showPasskey"] = new { type = "boolean", @default = true, description = "Display passkey/WebAuthn sign-in option" },
                ["passkeyUsernameless"] = new { type = "boolean", @default = true, description = "Allow usernameless passkey login (no email entered first)" },
                ["allowRememberMe"] = new { type = "boolean", @default = true, description = "Show 'Remember me' checkbox" },
                ["allowRegistration"] = new { type = "boolean", @default = false, description = "Show link to registration" },
                ["allowForgotPassword"] = new { type = "boolean", @default = true, description = "Show 'Forgot password?' link" },
                ["forgotPasswordUrl"] = new { type = "string", description = "Override URL for the forgot-password link" },
                ["registrationUrl"] = new { type = "string", description = "Override URL for the registration link" },
                ["autoProvision"] = new { type = "boolean", @default = true, description = "Auto-create user on first external login" },
                ["externalProviders"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = "string",
                    ["description"] = "Allowed external provider schemes (leave empty to allow all)",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/identity-providers",
                        ["valueField"] = "scheme",
                        ["labelField"] = "displayName",
                        ["descriptionField"] = "providerType"
                    }
                },
                ["primaryAction"] = new { type = "string", @enum = new[] { "local", "external", "passkey" }, @default = "local", description = "Which action to emphasise visually" },
                ["loginLayout"] = new { type = "string", @enum = new[] { "standard", "social-first", "passkey-first", "email-expandable" }, @default = "standard", description = "Order the sign-in methods appear in: standard (password, passkey, social), social-first, passkey-first, or email-expandable (social + passkey as buttons, with email/password collapsed behind a 'Continue with email' button at the bottom)" },
                ["reauthenticateAfter"] = new { type = "number", @default = 0, description = "When the policy runs for a signed-in user, the session counts as this sign-in only if it signed in within this many seconds; otherwise the same user signs in again. 0 accepts any live session." }
            },
            [
                Branch("auth_local", "Password login", "User authenticated with username/password"),
                Branch("auth_passkey", "Passkey login", "User authenticated with a FIDO2 passkey"),
                Branch("auth_external", "External login", "User authenticated via any external IdP"),
                Branch("mfa_required", "MFA required", "Credentials valid but multi-factor authentication is needed")
            ]);

        RegisterTypeWithRawSchema("external_login", "External Login", "Authentication",
            "Authenticate via external OAuth/OIDC identity provider (Google, Microsoft, etc.)",
            new Dictionary<string, object>
            {
                ["providers"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = "string",
                    ["description"] = "Allowed identity provider schemes (leave empty for all)",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/identity-providers",
                        ["valueField"] = "scheme",
                        ["labelField"] = "displayName",
                        ["descriptionField"] = "providerType"
                    }
                },
                ["autoProvision"] = new { type = "boolean", @default = true, description = "Auto-create user on first login" },
                ["autoRedirect"] = new { type = "boolean", @default = false, description = "Auto-redirect when only one provider matches" },
                ["proxyMode"] = new { type = "boolean", @default = false, description = "Pass through external claims without creating a local user (federation broker)" },
                ["claimMappings"] = new Dictionary<string, object> { ["type"] = "object", ["additionalProperties"] = new Dictionary<string, object> { ["type"] = "string" }, ["description"] = "Provider claims to carry into the journey: external claim type → claim name. The account's own identity claims (sub, email, name) aren't replaced." }
            },
            [
                Branch("auth_external", "External login", "User authenticated via any external IdP"),
                Branch("auth_proxy", "Proxy mode", "User authenticated in proxy/federation-broker mode")
            ]);

        RegisterTypeWithRawSchema("mfa", "Multi-Factor Authentication", "Authentication",
            "Require an additional verification factor (TOTP / SMS / email)",
            new Dictionary<string, object>
            {
                ["required"] = new { type = "boolean", @default = false, description = "Always require MFA. When false, MFA is only required for users who already have it enabled." },
                ["methods"] = new
                {
                    type = "array",
                    items = "string",
                    @default = new[] { "totp", "email" },
                    description = "Allowed MFA methods (any of: totp, sms, email)",
                    @enum = new[] { "totp", "sms", "email" }
                },
                ["allowSetup"] = new { type = "boolean", @default = true, description = "Allow users to set up MFA during the flow when they don't have it enabled yet" },
                ["rememberDevice"] = new { type = "boolean", @default = true, description = "Skip MFA for trusted devices on subsequent sign-ins" },
                ["rememberDeviceDays"] = new { type = "number", @default = 30, description = "Days to remember a trusted device before re-prompting" },
                ["reuseSessionMfaSeconds"] = new { type = "number", @default = 0, description = "When the policy runs for a signed-in user, accept MFA that session completed within this many seconds. 0 always asks again." }
            },
            [
                Branch("mfa_verified", "MFA verified", "User successfully completed MFA verification"),
                Branch("mfa_recovery_used", "Recovery code used", "User authenticated with a recovery code"),
                Branch("mfa_not_required", "MFA not required", "MFA skipped — user has no MFA configured and it is not required"),
                Branch("mfa_setup_complete", "MFA setup complete", "User just completed first-time MFA enrolment"),
                Branch("mfa_trusted_device", "Trusted device", "MFA skipped — device is already trusted"),
                Branch("mfa_session", "Session MFA", "MFA skipped — the current session completed MFA recently")
            ]);

        // Schema field names MUST match the keys the handler reads via
        // context.GetConfig(...) — otherwise admin-set values land under
        // ignored keys and the handler silently uses compile-time defaults.
        RegisterTypeWithRawSchema("passwordless_email", "Passwordless Email", "Authentication",
            "Email-based passwordless login (OTP code or magic link)",
            new Dictionary<string, object>
            {
                ["mode"] = new { type = "string", @enum = new[] { "otp", "magic-link" }, @default = "otp", description = "Whether to email a one-time code or a magic link" },
                ["codeLength"] = new { type = "number", @default = 6, description = "Number of digits in the verification code" },
                ["expirationMinutes"] = new { type = "number", @default = 15, description = "Minutes before the code/link expires" },
                ["allowSignUp"] = new { type = "boolean", @default = false, description = "Allow new users to register via this flow (when no account exists for the email)" },
                ["autoProvision"] = new { type = "boolean", @default = false, description = "When sign-up is allowed, create the account inline (Username = Email, no password) and complete the step instead of handing off to a downstream signup step. No effect when allowSignUp is false.", showWhen = new { field = "allowSignUp", value = true } },
                ["maxAttempts"] = new { type = "number", @default = 3, description = "Maximum invalid-code attempts before the journey fails (0 = unlimited)" },
                ["emailSubject"] = new { type = "string", description = "Custom email subject — overrides the rendered template subject when set" },
                ["emailTemplate"] = new { type = "string", description = "Override platform email template name (e.g. \"auth.passwordless.otp\"). Leave blank to use the default for the chosen mode." }
            });

        RegisterTypeWithRawSchema("email_verification", "Email Verification", "User Interaction",
            "Prove ownership of an email address (OTP or magic link) without authenticating. Emits the verified address as 'email' and email_verified=true into journey state — use as a pre-condition for sign-up, password reset, or sensitive profile changes.",
            new Dictionary<string, object>
            {
                ["mode"] = new { type = "string", @enum = new[] { "otp", "magic-link" }, @default = "otp", description = "Whether to email a one-time code or a magic link" },
                ["codeLength"] = new { type = "number", @default = 6, description = "Number of digits in the verification code" },
                ["expirationMinutes"] = new { type = "number", @default = 15, description = "Minutes before the code/link expires" },
                ["maxAttempts"] = new { type = "number", @default = 3, description = "Maximum invalid-code attempts before the journey fails (0 = unlimited)" },
                ["emailSubject"] = new { type = "string", description = "Custom email subject — overrides the rendered template subject when set" },
                ["emailTemplate"] = new { type = "string", description = "Override platform email template name. Leave blank to use the default for the chosen mode." },
                ["completeSignUp"] = new { type = "boolean", @default = true, description = "When this step follows sign-up, verifying the new account's own address marks it verified and signs the user in" },
                ["toLinkRecipient"] = new { type = "boolean", @default = false, description = "Verify the address the journey link was sent to: the code goes there at once and it can't be changed. Runs not opened from a link sent to someone skip this step." }
            });

        RegisterTypeWithRawSchema("passwordless_sms", "Passwordless SMS", "Authentication",
            "SMS-based passwordless login (OTP code only)",
            new Dictionary<string, object>
            {
                ["codeLength"] = new { type = "number", @default = 6, description = "Number of digits in the verification code" },
                ["expirationMinutes"] = new { type = "number", @default = 10, description = "Minutes before the code expires" },
                ["allowSignUp"] = new { type = "boolean", @default = false, description = "Allow new users to register via this flow (when no account exists for the phone number)" },
                ["maxAttempts"] = new { type = "number", @default = 3, description = "Maximum invalid-code attempts before the journey fails (0 = unlimited)" },
                ["messageTemplate"] = new { type = "string", description = "Custom SMS message. Supports {code} and {expirationMinutes} placeholders." },
                ["toLinkRecipient"] = new { type = "boolean", @default = false, description = "Verify the phone the journey link was sent to, without signing anyone in: the code goes to that number at once and it can't be changed. Runs not opened from a link sent to someone skip this step." }
            });

        RegisterTypeWithRawSchema("webauthn", "WebAuthn/FIDO2", "Authentication",
            "Biometric/hardware key authentication",
            new Dictionary<string, object>
            {
                // Signs in with an existing passkey; which authenticators can be registered is
                // the fido2_register step's setting.
                ["passkeyOnly"] = new { type = "boolean", @default = false, description = "Sign in with a passkey only, without asking for a username first" },
                ["allowFallback"] = new { type = "boolean", @default = true, description = "Let the user continue another way when they have no passkey or it fails" }
            });

        RegisterTypeWithRawSchema("ldap", "LDAP / Active Directory", "Authentication",
            "Authenticate against LDAP/AD server",
            new Dictionary<string, object>
            {
                ["ldapProvider"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["title"] = "LDAP directory",
                    ["description"] = "The organisation's LDAP identity provider to sign in against. Leave empty to use the directory configured for the LDAP module.",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/identity-providers",
                        ["valueField"] = "scheme",
                        ["labelField"] = "displayName",
                        ["filters"] = new Dictionary<string, object> { ["type"] = "Ldap" }
                    }
                },
                ["server"] = new Dictionary<string, object> { ["type"] = "string", ["title"] = "LDAP server override", ["description"] = "Server hostname to use instead of the provider's (leave empty to use the provider settings)" },
                ["baseDn"] = new Dictionary<string, object> { ["type"] = "string", ["title"] = "Base DN override", ["description"] = "Base distinguished name to use instead of the provider's (leave empty to use the provider settings)" },
                ["userSearchFilter"] = new Dictionary<string, object> { ["type"] = "string", ["title"] = "User search filter", ["description"] = "LDAP filter for finding users, {0} is the username, e.g. (sAMAccountName={0}) (leave empty to use the provider settings)" },
                ["bindDn"] = new Dictionary<string, object> { ["type"] = "string", ["title"] = "Bind DN", ["description"] = "Service account DN used to search for users, with the provider's bind password (leave empty to use the provider settings)" },
                ["useSsl"] = new Dictionary<string, object> { ["type"] = "boolean", ["title"] = "Use SSL/TLS", ["description"] = "Connect using SSL/TLS (LDAPS). Unset uses the provider settings" },
                ["displayName"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the directory shown on the sign-in form (defaults to the provider's display name, then \"Corporate Directory\")", ["x-localizable"] = true },
                ["autoProvision"] = new { type = "boolean", @default = true, description = "Create a local account the first time a directory user signs in" },
                ["allowPasswordChange"] = new { type = "boolean", @default = false, description = "Let users change their directory password from the sign-in form (Active Directory needs SSL or StartTLS)" },
                ["syncGroups"] = new { type = "boolean", @default = true, description = "Add the user's directory groups (and the roles mapped from them) to the claims" }
            });

        RegisterTypeWithRawSchema("saml", "SAML 2.0", "Authentication",
            "Authenticate via SAML 2.0 IdP",
            new Dictionary<string, object>
            {
                ["idpName"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["title"] = "SAML Identity Provider",
                    ["description"] = "Select a configured SAML Identity Provider",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/identity-providers",
                        ["valueField"] = "scheme",
                        ["labelField"] = "displayName",
                        ["filters"] = new Dictionary<string, object> { ["type"] = "Saml2" }
                    }
                },
                ["autoRedirect"] = new { type = "boolean", @default = false, description = "Auto-redirect if only one IdP configured" },
                ["forceAuthn"] = new { type = "boolean", @default = false, description = "Force re-authentication at IdP" },
                ["isPassive"] = new { type = "boolean", @default = false, description = "Don't show IdP login UI if not authenticated" },
                ["autoProvision"] = new { type = "boolean", @default = true, description = "Auto-create local user on first login" },
                ["proxyMode"] = new { type = "boolean", @default = false, description = "Pass through external claims without local user (federation broker)" }
            });

        // User Management
        // signup and create_user share a handler (CreateUserStepHandler
        // delegates to SignUpStepHandler) — keep one schema definition,
        // register both step types against it so the admin form stays
        // consistent regardless of which alias the policy uses.
        var signUpSchema = GetSignUpSchema();
        RegisterTypeWithRawSchema("signup", "Sign Up", "User Management",
            "Self-registration flow — collects email/password and creates a local user",
            signUpSchema,
            [
                Branch("login", "Already have an account", "Shows the \"Sign in\" link on the sign-up form and goes here when it's used"),
                Branch("registration_disabled", "Registration closed", "Taken when self-registration is off; without it the step is skipped"),
            ]);
        RegisterTypeWithRawSchema("create_user", "Create User", "User Management",
            "Create a new user account — alias of Sign Up. Same schema, same handler.",
            signUpSchema);

        RegisterTypeWithRawSchema("assign_roles", "Assign Roles", "User Management",
            "Gives the journey's user roles, e.g. owner rights once a business is registered. Place it after the step that creates or signs in the account; on a branch it only runs when that branch is taken.",
            new Dictionary<string, object>
            {
                ["roles"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = "string",
                    ["required"] = true,
                    ["description"] = "Roles to give the user. Roles they already hold are left as they are.",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/roles",
                        ["valueField"] = "id",
                        ["labelField"] = "displayName",
                        ["descriptionField"] = "description"
                    }
                }
            });

        RegisterTypeWithRawSchema("update_user", "Update User", "User Management",
            "Profile-update step — renders an editable form bound to user properties",
            new Dictionary<string, object>
            {
                ["title"] = new Dictionary<string, object> { ["type"] = "string", ["default"] = "Update Your Profile", ["description"] = "Form title shown to the user", ["x-localizable"] = true },
                ["description"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Form description / instructions shown above the fields", ["x-localizable"] = true },
                ["submitButtonText"] = new Dictionary<string, object> { ["type"] = "string", ["default"] = "Update Profile", ["description"] = "Submit-button label", ["x-localizable"] = true },
                ["requireEmailVerification"] = new { type = "boolean", @default = true, description = "When the email changes, email a code to the new address and apply it (as verified) only once the user enters it; the old address stays until then" },
                ["fields"] = new { type = "array", description = "Editable field definitions (UpdateFieldConfig — see step handler for shape). Phase 2 schema renderer will surface this as a structured editor." },
                ["editableFields"] = new { type = "array", items = "string", description = "Shorthand list of user-property names the user is allowed to edit (used when 'fields' is not set)" },
                ["requireCurrentPassword"] = new { type = "boolean", @default = false, description = "Require current password to save sensitive changes" }
            });

        RegisterTypeWithRawSchema("password_change", "Password Change", "User Management",
            "Change password flow",
            new Dictionary<string, object>
            {
                ["requireCurrentPassword"] = new { type = "boolean", @default = true, description = "Require current password" },
                ["minPasswordLength"] = new { type = "number", @default = 8, description = "Minimum password length" },
                ["requireUppercase"] = new { type = "boolean", @default = true, description = "Require uppercase letter" },
                ["requireLowercase"] = new { type = "boolean", @default = true, description = "Require lowercase letter" },
                ["requireDigit"] = new { type = "boolean", @default = true, description = "Require number" },
                ["requireSpecialChar"] = new { type = "boolean", @default = false, description = "Require special character" }
            });

        RegisterTypeWithRawSchema("password_reset", "Password Reset", "User Management",
            "Reset a forgotten password and enforce the new-password policy",
            new Dictionary<string, object>
            {
                ["tokenExpirationMinutes"] = new { type = "number", @default = 60, description = "Minutes before the reset token expires" },
                ["minLength"] = new { type = "number", @default = 8, description = "Minimum password length" },
                ["requireUppercase"] = new { type = "boolean", @default = true, description = "Require uppercase letter" },
                ["requireLowercase"] = new { type = "boolean", @default = true, description = "Require lowercase letter" },
                ["requireDigit"] = new { type = "boolean", @default = true, description = "Require digit" },
                ["requireNonAlphanumeric"] = new { type = "boolean", @default = false, description = "Require special / non-alphanumeric character" },
                ["requireCurrentPassword"] = new { type = "boolean", @default = false, description = "Require current password (rare for password-reset; mostly used by the change-password flow)" }
            });

        RegisterTypeWithRawSchema("link_account", "Link Account", "User Management",
            "Link an external identity provider to the current user",
            new Dictionary<string, object>
            {
                ["allowedProviders"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = "string",
                    ["description"] = "External provider schemes the user can link (leave empty to allow all configured)",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/identity-providers",
                        ["valueField"] = "scheme",
                        ["labelField"] = "displayName",
                        ["descriptionField"] = "providerType"
                    }
                },
                ["allowUnlink"] = new { type = "boolean", @default = true, description = "Allow unlinking previously linked providers" },
                ["requireConfirmation"] = new { type = "boolean", @default = true, description = "Require user confirmation before linking" }
            });

        // User Interaction
        RegisterTypeWithRawSchema("consent", "OAuth Consent", "User Interaction",
            "Displays consent screen for scopes",
            new Dictionary<string, object>
            {
                ["allowRemember"] = new { type = "boolean", @default = true, description = "Allow user to remember consent" },
                ["rememberDays"] = new { type = "number", @default = 365, description = "Days to remember consent" },
                ["showResourceScopes"] = new { type = "boolean", @default = true, description = "Show API resource scopes" },
                ["showIdentityScopes"] = new { type = "boolean", @default = true, description = "Show identity scopes" }
            },
            [
                Branch("consent_granted", "Consent granted", "User explicitly granted consent"),
                Branch("consent_not_required", "Consent not required", "Client does not require consent"),
                Branch("consent_already_granted", "Already consented", "User has valid prior consent")
            ]);

        RegisterTypeWithRawSchema("dynamic_form", "Dynamic Form", "User Interaction",
            "Collects user information via configurable form with optional persistence to user properties",
            GetDynamicFormSchema());

        RegisterTypeWithRawSchema("captcha", "CAPTCHA Verification", "User Interaction",
            "Bot protection verification (reCAPTCHA / hCaptcha / Cloudflare Turnstile)",
            new Dictionary<string, object>
            {
                ["provider"] = new { type = "string", @enum = new[] { "recaptcha", "hcaptcha", "cloudflare" }, @default = "recaptcha", description = "CAPTCHA provider" },
                ["siteKey"] = new { type = "string", required = true, description = "CAPTCHA site key" },
                ["secretKey"] = new Dictionary<string, object> { ["type"] = "string", ["required"] = true, ["description"] = "CAPTCHA secret key", ["x-control"] = "secret-input" },
                ["scoreThreshold"] = new { type = "number", @default = 0.5, description = "Minimum score (reCAPTCHA v3 only)" },
                ["action"] = new { type = "string", @default = "submit", description = "Action label sent with the CAPTCHA challenge (reCAPTCHA v3)" },
                ["language"] = new { type = "string", @default = "en", description = "Widget language (BCP-47 code, e.g. \"en\", \"fr\", \"de\")" },
                ["size"] = new { type = "string", @enum = new[] { "normal", "compact", "invisible" }, @default = "normal", description = "Widget size" },
                ["theme"] = new { type = "string", @enum = new[] { "light", "dark" }, @default = "light", description = "Widget theme" }
            });

        RegisterTypeWithRawSchema("terms_acceptance", "Terms Acceptance", "User Interaction",
            "Require the user to accept terms of service and/or a privacy policy",
            new Dictionary<string, object>
            {
                ["termsTitle"] = new Dictionary<string, object> { ["type"] = "string", ["default"] = "Terms of Service", ["description"] = "Heading shown above the terms section", ["x-localizable"] = true },
                ["termsUrl"] = new { type = "string", description = "URL to the terms of service (used when termsContent is not provided)" },
                ["termsContent"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Inline terms HTML/Markdown — overrides termsUrl when set", ["x-control"] = "textarea" },
                ["termsVersion"] = new { type = "string", @default = "1.0", description = "Terms version used to track which version the user accepted" },
                ["showTermsInline"] = new { type = "boolean", @default = false, description = "Render the terms inline (true) instead of linking out (false)" },
                ["privacyTitle"] = new Dictionary<string, object> { ["type"] = "string", ["default"] = "Privacy Policy", ["description"] = "Heading shown above the privacy section", ["x-localizable"] = true },
                ["privacyUrl"] = new { type = "string", description = "URL to the privacy policy (used when privacyContent is not provided)" },
                ["privacyContent"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Inline privacy HTML/Markdown — overrides privacyUrl when set", ["x-control"] = "textarea" },
                ["privacyVersion"] = new { type = "string", @default = "1.0", description = "Privacy version used to track which version the user accepted" },
                ["showPrivacyInline"] = new { type = "boolean", @default = false, description = "Render the privacy policy inline (true) instead of linking out (false)" },
                ["requireBoth"] = new { type = "boolean", @default = true, description = "Require acceptance of BOTH terms and privacy (false = either alone is enough)" },
                ["requireCheckbox"] = new { type = "boolean", @default = true, description = "Render an explicit acceptance checkbox (off = consider acceptance implicit on Continue)" },
                ["canDecline"] = new { type = "boolean", @default = false, description = "Show a 'Decline' button that aborts the journey" },
                ["acceptButtonText"] = new Dictionary<string, object> { ["type"] = "string", ["default"] = "I Accept", ["description"] = "Accept-button label", ["x-localizable"] = true },
                ["declineButtonText"] = new Dictionary<string, object> { ["type"] = "string", ["default"] = "Decline", ["description"] = "Decline-button label (only shown when canDecline is true)", ["x-localizable"] = true },
                ["version"] = new { type = "string", description = "Legacy combined version string. Prefer termsVersion / privacyVersion for separate tracking." }
            });

        // Flow Control
        RegisterTypeWithRawSchema("condition", "Condition", "Flow Control",
            "Conditional branching based on rules",
            GetConditionSchema());

        RegisterTypeWithRawSchema("branch", "Branch", "Flow Control",
            "Multi-way branching with priority-based or first-match evaluation. For simple if/then routing use the Condition step instead.",
            new Dictionary<string, object>
            {
                ["branches"] = new
                {
                    type = "array",
                    description = "Branch rules — each is evaluated in order (first-match) or by priority. Phase 2 schema renderer will surface this as a structured editor; for now edit the policy JSON or use the array repeater.",
                    items = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["name"] = new { type = "string", description = "Optional name for logging" },
                            ["description"] = new { type = "string", description = "What this branch does" },
                            ["targetStepId"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["required"] = true,
                                ["description"] = "Step ID to branch to when conditions match",
                                ["x-enumSource"] = new Dictionary<string, object>
                                {
                                    ["endpoint"] = "$steps",
                                    ["valueField"] = "id",
                                    ["labelField"] = "id"
                                }
                            },
                            ["priority"] = new { type = "number", @default = 0, description = "Higher priority = evaluated first (highest-priority mode only)" },
                            ["logicOperator"] = new { type = "string", @enum = new[] { "and", "or" }, @default = "and", description = "How to combine the branch's conditions" },
                            ["conditions"] = new
                            {
                                type = "array",
                                description = "Conditions that must match for this branch (empty = always match)",
                                items = new Dictionary<string, object>
                                {
                                    ["type"] = "object",
                                    ["properties"] = new Dictionary<string, object>
                                    {
                                        ["source"] = new { type = "string", @enum = ConditionSources, @default = "data", description = "Where to read the value from" },
                                        ["field"] = new { type = "string", description = "Field/claim name" },
                                        ["operator"] = new { type = "string", @enum = ConditionOperatorChoices, @default = "equals" },
                                        ["value"] = new { type = "string", description = "Expected value" },
                                        ["values"] = new { type = "array", items = "string", description = "Values list (for in/notIn)" },
                                        ["negate"] = new { type = "boolean", @default = false, description = "Invert the condition result" }
                                    }
                                }
                            }
                        }
                    }
                },
                ["defaultBranch"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Step ID to branch to when no branch's conditions match",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "$steps",
                        ["valueField"] = "id",
                        ["labelField"] = "id"
                    }
                },
                ["evaluationMode"] = new { type = "string", @enum = new[] { "first-match", "highest-priority" }, @default = "first-match", description = "How branches are evaluated" }
            });

        RegisterTypeWithRawSchema("transform", "Claims Transform", "Flow Control",
            "Transform claims before issuing token",
            GetTransformSchema());

        RegisterTypeWithRawSchema("api_call", "API Call", "Flow Control",
            "Call external API during journey",
            GetApiCallSchema());

        RegisterTypeWithRawSchema("webhook", "Webhook", "Flow Control",
            "POST a payload to an external service during the journey",
            new Dictionary<string, object>
            {
                ["url"] = new { type = "string", required = true, description = "Webhook endpoint URL (supports {claim:name} / {state:key} substitution)" },
                ["timeout"] = new { type = "number", @default = 30, description = "Request timeout in seconds" },
                ["waitForResponse"] = new { type = "boolean", @default = false, description = "Block the journey until the webhook returns. False = fire-and-forget." },
                ["failOnError"] = new { type = "boolean", @default = false, description = "Fail the journey if the webhook returns non-2xx (only applies when waitForResponse is true)" },
                ["headers"] = new { type = "object", description = "Custom HTTP headers", additionalProperties = new { type = "string" } },
                ["payload"] = new Dictionary<string, object> { ["type"] = "object", ["description"] = "JSON payload template; text values may use placeholders such as {claim:email}. Leave empty for the default payload with user and journey context.", ["x-control"] = "json" },
                ["responseMapping"] = new { type = "object", description = "Map JSON response paths to claim types (e.g. { \"data.id\": \"external_id\" }) — only applies when waitForResponse is true", additionalProperties = new { type = "string" } },
                ["async"] = new { type = "boolean", @default = true, description = "Legacy alias for !waitForResponse. Setting either has the same effect." },
                ["includeUserData"] = new { type = "boolean", @default = true, description = "Include user data in the default payload (only applies when payload template is empty)" },
                ["includeJourneyData"] = new { type = "boolean", @default = false, description = "Include journey values in the default payload (only applies when payload template is empty)" },
                ["journeyDataKeys"] = new { type = "array", items = "string", description = "Journey values to include when includeJourneyData is on. When empty, only the standard identity claims (Oluso:UserJourneys:DefaultOutputClaims) are sent." },
                ["signPayload"] = new { type = "boolean", @default = false, description = "Sign the request body with HMAC-SHA256 and add an X-Oluso-Signature header" },
                ["secretKey"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Secret key for HMAC signature (used when signPayload is true)", ["x-control"] = "secret-input" }
            });

        // Plugins
        RegisterTypeWithRawSchema("custom_plugin", "Custom Plugin", "Plugins",
            "Execute custom WASM or managed plugin",
            new Dictionary<string, object>
            {
                ["pluginName"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["required"] = true,
                    ["description"] = "Plugin to execute (WASM or managed)",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/plugins",
                        ["valueField"] = "name",
                        ["labelField"] = "displayName",
                        ["descriptionField"] = "description"
                    }
                },
                ["entryPoint"] = new { type = "string", @default = "execute", description = "Plugin entry point function" },
                ["config"] = new { type = "object", description = "Custom configuration passed to plugin" }
            });

        RegisterTypeWithRawSchema("custom_page", "Custom Page", "Plugins",
            "Render a host-authored Razor partial as a journey screen",
            new Dictionary<string, object>
            {
                ["templatePath"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["required"] = true,
                    ["description"] = "Template to render. Maps to Views/Shared/Journey/Templates/_{name}.cshtml shipped by the host app or a referenced Razor Class Library.",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/journeys/journey-templates",
                        ["valueField"] = "name",
                        ["labelField"] = "displayName",
                        ["descriptionField"] = "description"
                    }
                },
                ["model"] = new { type = "object", description = "Static data exposed to the template as CustomPageViewModel.Config (labels, copy, flags)." }
            });
    }

    private static Dictionary<string, object> GetSignUpSchema()
    {
        return new Dictionary<string, object>
        {
            ["allowSelfRegistration"] = new { type = "boolean", @default = true, description = "Allow new users to register through this flow (when false, the step skips and falls through)" },
            ["requireEmailVerification"] = new { type = "boolean", @default = true, description = "Require the user to verify their email before continuing. When true, set verificationStepId to the step that performs the verification (typically a passwordless_email step)." },
            ["verificationStepId"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Step in this journey to branch to after the user is created. Pick a passwordless_email step (OTP mode) — receiving the OTP at the user's mailbox proves email ownership and persists EmailVerified=true. When unset, the journey continues with a pending_verification claim.",
                // Special endpoint "$steps" tells the journey-builder to
                // resolve this against the policy's local step list rather
                // than make an HTTP call. filterByType narrows to step
                // types that can actually verify an email.
                ["x-enumSource"] = new Dictionary<string, object>
                {
                    ["endpoint"] = "$steps",
                    ["filterByType"] = "passwordless_email",
                    ["valueField"] = "id",
                    ["labelField"] = "id"
                },
                ["showWhen"] = new { field = "requireEmailVerification", value = true }
            },
            ["requirePassword"] = new { type = "boolean", @default = true, description = "Collect a password during sign-up (false means passwordless or external-only)" },
            ["requireTermsAcceptance"] = new { type = "boolean", @default = false, description = "Require the user to accept terms before account creation" },
            ["termsUrl"] = new { type = "string", description = "URL to the terms of service shown alongside the sign-up form" },
            ["privacyUrl"] = new { type = "string", description = "URL to the privacy policy shown alongside the sign-up form" },
            ["allowedEmailDomains"] = new
            {
                type = "array",
                items = "string",
                description = "Restrict sign-up to specific email domains (e.g. \"acme.com\"). Leave empty to allow any."
            },
            ["emailClaim"] = new { type = "string", @default = "email", description = "Claim/input name carrying the email address" },
            ["usernameMode"] = new
            {
                type = "string",
                @enum = new[] { "email", "ask" },
                @default = "email",
                description = "Username: \"email\" uses the email address as the username; \"ask\" adds a username field to the sign-up form"
            },
            ["usernameClaim"] = new { type = "string", description = "Claim carrying the username from an earlier step (falls back to email when blank)" },
            ["passwordClaim"] = new { type = "string", @default = "password", description = "Claim/input name carrying the password" },
            ["defaultRoles"] = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = "string",
                ["description"] = "Roles to assign to the newly created user",
                ["x-enumSource"] = new Dictionary<string, object>
                {
                    ["endpoint"] = "/roles",
                    ["valueField"] = "id",
                    ["labelField"] = "displayName",
                    ["descriptionField"] = "description"
                }
            },
            ["claimMappings"] = new { type = "object", description = "Map collected claims onto user properties (e.g. firstName, lastName, phoneNumber)" }
        };
    }

    private static Dictionary<string, object> GetDynamicFormSchema()
    {
        return new Dictionary<string, object>
        {
            ["title"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Form title displayed to user", ["x-localizable"] = true },
            ["description"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Form description/instructions", ["x-localizable"] = true },
            ["submitButtonText"] = new Dictionary<string, object> { ["type"] = "string", ["default"] = "Continue", ["description"] = "Submit button label", ["x-localizable"] = true },
            ["cancelButtonText"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Cancel button label (if allowCancel)", ["x-localizable"] = true },
            ["allowCancel"] = new { type = "boolean", @default = false, description = "Show cancel button" },
            ["viewName"] = new { type = "string", @default = "Journey/_DynamicForm", description = "Custom view name" },
            ["fields"] = new
            {
                type = "array",
                required = true,
                description = "Form fields to collect",
                items = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["name"] = new { type = "string", required = true, description = "Field name (used as form input name)" },
                        ["type"] = new
                        {
                            type = "string",
                            required = true,
                            @enum = new[] { "text", "email", "password", "number", "date", "tel", "url", "textarea", "select", "radio", "checkbox", "file" },
                            description = "Field input type. File fields store uploads as attachments of the journey (checked by content, never public) and keep the attachment ids as the value."
                        },
                        ["label"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Field label displayed to user", ["x-localizable"] = true },
                        ["placeholder"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Input placeholder text", ["x-localizable"] = true },
                        ["description"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Help text below field", ["x-localizable"] = true },
                        ["required"] = new { type = "boolean", @default = false, description = "Field is required" },
                        ["claimType"] = new { type = "string", description = "Claim type to store value (defaults to name)" },
                        ["defaultValue"] = new { type = "string", description = "Default value" },
                        ["pattern"] = new { type = "string", description = "Regex validation pattern" },
                        ["patternError"] = new { type = "string", description = "Error message for pattern mismatch" },
                        ["minLength"] = new { type = "number", description = "Minimum character length" },
                        ["maxLength"] = new { type = "number", description = "Maximum character length" },
                        ["min"] = new { type = "string", description = "Minimum value (for number/date)" },
                        ["max"] = new { type = "string", description = "Maximum value (for number/date)" },
                        ["rows"] = new { type = "number", @default = 3, description = "Rows for textarea" },
                        ["phoneFormat"] = new
                        {
                            type = "string",
                            @enum = new[] { "international", "local" },
                            @default = "international",
                            description = "Phone fields: 'international' shows a country picker and stores the number as E.164 (+2348031234567); 'local' takes the number exactly as dialled locally"
                        },
                        ["phoneDefaultCountry"] = new { type = "string", description = "Phone fields: ISO 3166-1 alpha-2 country preselected in the picker (e.g. NG); defaults to the journey culture's region" },
                        ["fileTypes"] = new { type = "array", items = "string", @enum = new[] { "pdf", "jpeg", "png", "gif", "webp", "heic" }, @default = new[] { "pdf", "jpeg", "png" }, description = "File fields: kinds accepted, recognised by content (the file name must also carry a matching extension)" },
                        ["maxFileSizeMb"] = new { type = "number", @default = 10, description = "File fields: largest file accepted, in MB" },
                        ["maxFiles"] = new { type = "number", @default = 1, description = "File fields: most files accepted" },
                        ["capture"] = new { type = "boolean", @default = false, description = "File fields: also offer 'Take a photo', which opens the camera on phones. Embedded journeys need the host iframe to allow camera." },
                        ["readOnly"] = new { type = "boolean", @default = false, description = "Field is read-only" },
                        ["hidden"] = new { type = "boolean", @default = false, description = "Field is hidden" },
                        ["group"] = new { type = "string", description = "Group name for fieldset grouping" },
                        ["persistToUser"] = new { type = "boolean", @default = false, description = "Persist field value to user properties" },
                        ["userPropertyName"] = new { type = "string", description = "User property name (e.g., firstName, lastName, phoneNumber, picture, or custom property key)" },
                        ["options"] = new
                        {
                            type = "array",
                            description = "Options for select/radio fields",
                            items = new
                            {
                                type = "object",
                                properties = new Dictionary<string, object>
                                {
                                    ["value"] = new { type = "string", required = true },
                                    ["label"] = new { type = "string", required = true },
                                    ["localizedLabels"] = new { type = "object", description = "Label translations" }
                                }
                            }
                        },
                        ["showWhen"] = new
                        {
                            type = "object",
                            description = "Conditional visibility",
                            properties = new Dictionary<string, object>
                            {
                                ["field"] = new { type = "string", required = true, description = "Field name to check" },
                                ["operator"] = new { type = "string", @enum = new[] { "equals", "notEquals", "contains", "notEmpty", "empty" }, @default = "equals" },
                                ["value"] = new { type = "string", description = "Value to compare" }
                            }
                        },
                        ["localizedLabels"] = new { type = "object", description = "Label translations by culture" },
                        ["localizedPlaceholders"] = new { type = "object", description = "Placeholder translations" },
                        ["localizedDescriptions"] = new { type = "object", description = "Description translations" }
                    }
                }
            }
        };
    }

    private static Dictionary<string, object> GetConditionSchema()
    {
        return new Dictionary<string, object>
        {
            ["conditions"] = new
            {
                type = "array",
                required = true,
                description = "Conditions to evaluate",
                items = new
                {
                    type = "object",
                    // Same shape as step conditions (StepCondition), evaluated the same way.
                    properties = new Dictionary<string, object>
                    {
                        ["type"] = new { type = "string", @enum = ConditionSources, @default = "data", required = true, description = "Where to read the value from" },
                        ["field"] = new { type = "string", required = true, description = "Key/claim name to check" },
                        ["operator"] = new { type = "string", @enum = ConditionOperatorChoices, @default = "equals", required = true },
                        ["value"] = new { type = "string", description = "Value to compare" },
                        ["logicalOperator"] = new { type = "string", @enum = new[] { "and", "or" }, @default = "and", description = "How the next condition joins this one" },
                        ["negate"] = new { type = "boolean", @default = false, description = "Negate the result" }
                    }
                }
            },
            ["onTrue"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Step to continue to when the conditions are true (blank = the next step)",
                ["x-enumSource"] = new Dictionary<string, object>
                {
                    ["endpoint"] = "$steps",
                    ["valueField"] = "id",
                    ["labelField"] = "id"
                }
            },
            ["onFalse"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Step to continue to when the conditions are false (blank = skip to the next step)",
                ["x-enumSource"] = new Dictionary<string, object>
                {
                    ["endpoint"] = "$steps",
                    ["valueField"] = "id",
                    ["labelField"] = "id"
                }
            }
        };
    }

    /// <summary>The transform step publishes its own schema (with its transform catalog).</summary>
    private static Dictionary<string, object> GetTransformSchema() => new();

    private static Dictionary<string, object> GetApiCallSchema()
    {
        return new Dictionary<string, object>
        {
            ["url"] = new { type = "string", required = true, description = "API endpoint URL (supports {claim:name}, {state:userId}, {input:key} substitution)" },
            ["method"] = new { type = "string", @enum = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }, @default = "GET", description = "HTTP method" },
            ["timeout"] = new { type = "number", @default = 30, description = "Request timeout in seconds" },
            ["headers"] = new
            {
                type = "object",
                description = "Custom HTTP headers to send with request (supports placeholder substitution)",
                additionalProperties = new { type = "string" }
            },
            ["authentication"] = new
            {
                type = "object",
                description = "API authentication configuration",
                properties = new Dictionary<string, object>
                {
                    ["type"] = new { type = "string", @enum = new[] { "none", "bearer", "basic", "apikey", "client_credentials" }, @default = "none", description = "Authentication type" },
                    ["token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Bearer token (supports placeholders like {claim:access_token})", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "bearer" } },
                    ["username"] = new { type = "string", description = "Basic auth username", showWhen = new { field = "type", value = "basic" } },
                    ["password"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Basic auth password", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "basic" } },
                    ["apiKey"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "API key value", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "apikey" } },
                    ["headerName"] = new { type = "string", @default = "X-API-Key", description = "Header name for API key", showWhen = new { field = "type", value = "apikey" } },
                    // OAuth 2.0 client_credentials. Tokens are cached by
                    // (tokenEndpoint, clientId, scope, audience, resource) and refreshed
                    // automatically when within 60s of expiry.
                    ["tokenEndpoint"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "OAuth 2.0 token endpoint URL", ["showWhen"] = new { field = "type", value = "client_credentials" } },
                    ["clientId"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "OAuth 2.0 client ID", ["showWhen"] = new { field = "type", value = "client_credentials" } },
                    ["clientSecret"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "OAuth 2.0 client secret", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "client_credentials" } },
                    ["scope"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Space-separated OAuth 2.0 scopes (optional)", ["showWhen"] = new { field = "type", value = "client_credentials" } },
                    ["resource"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Resource indicator(s) (RFC 8707), space-separated: the API URI(s) the token is for. Use this for Oluso and other standards-based token endpoints", ["showWhen"] = new { field = "type", value = "client_credentials" } },
                    ["audience"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Audience parameter, for identity providers that read 'audience' instead of 'resource' (not part of the client_credentials standard)", ["showWhen"] = new { field = "type", value = "client_credentials" } }
                }
            },
            ["retryCount"] = new { type = "number", @default = 0, description = "Number of retry attempts on failure" },
            ["retryDelayMs"] = new { type = "number", @default = 1000, description = "Delay between retries in milliseconds" },
            ["failOnError"] = new { type = "boolean", @default = true, description = "Fail journey if API returns error status" },
            ["continueOnStatus"] = new { type = "array", items = "number", description = "HTTP status codes that should continue (e.g., [404, 409])" },
            ["bodyTemplate"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "JSON template with placeholders for request body", ["x-control"] = "textarea" },
            ["bodyFromClaims"] = new { type = "array", items = "string", description = "List of claim types to include in request body" },
            ["inputMapping"] = new
            {
                type = "object",
                description = "Advanced input mapping from claims/state/input to request body fields",
                additionalProperties = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["from"] = new { type = "string", description = "Source claim/state/input name" },
                        ["source"] = new { type = "string", @enum = new[] { "claim", "state", "input", "constant" }, description = "Data source type" },
                        ["value"] = new { type = "string", description = "Constant value (when source=constant)" },
                        ["transform"] = new { type = "string", @enum = new[] { "uppercase", "lowercase", "trim", "base64encode", "base64decode", "urlencode", "urldecode" }, description = "Transform to apply" },
                        ["defaultValue"] = new { type = "string", description = "Default if source value is empty" },
                        ["required"] = new { type = "boolean", @default = false, description = "Include even if empty" }
                    }
                }
            },
            ["outputMapping"] = new { type = "object", description = "Map JSON response paths to claim types (e.g., {\"user.id\": \"external_id\"})" },
            ["outputMappingAdvanced"] = new
            {
                type = "object",
                description = "Advanced output mapping with transforms",
                additionalProperties = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["path"] = new { type = "string", description = "JSON path in response" },
                        ["transform"] = new { type = "string", description = "Transform to apply" },
                        ["defaultValue"] = new { type = "string", description = "Default if not found" },
                        ["required"] = new { type = "boolean", @default = false, description = "Log warning if not found" }
                    }
                }
            },
            ["responseValidation"] = new
            {
                type = "array",
                description = "Validation rules for response",
                items = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["path"] = new { type = "string", required = true, description = "JSON path to validate" },
                        ["type"] = new { type = "string", @enum = new[] { "required", "equals", "notequals", "contains", "matches", "in", "notin" }, description = "Validation type" },
                        ["expectedValue"] = new { type = "string", description = "Expected value for equals/contains" },
                        ["pattern"] = new { type = "string", description = "Regex pattern for matches" },
                        ["allowedValues"] = new { type = "array", items = "string", description = "Allowed values for 'in' validation" },
                        ["errorMessage"] = new { type = "string", description = "Custom error message" }
                    }
                }
            },
            ["branchOnResponse"] = new
            {
                type = "array",
                description = "Branch to different steps based on response",
                items = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["onStatus"] = new { type = "array", items = "number", description = "HTTP status codes to match" },
                        ["path"] = new { type = "string", description = "JSON path to check" },
                        ["condition"] = new { type = "string", @enum = new[] { "equals", "notequals", "exists", "notexists", "contains", "true", "false" }, description = "Condition to evaluate" },
                        ["value"] = new { type = "string", description = "Value to compare" },
                        ["branchTo"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["required"] = true,
                            ["description"] = "Step ID to branch to",
                            ["x-enumSource"] = new Dictionary<string, object>
                            {
                                ["endpoint"] = "$steps",
                                ["valueField"] = "id",
                                ["labelField"] = "id"
                            }
                        }
                    }
                }
            },
            ["includeResponseMeta"] = new { type = "boolean", @default = false, description = "Add _api_status and _api_success claims" },
            // Layout hint for the schema-driven config form. Groups
            // related fields into collapsible sections so the very long
            // api_call form is approachable. Field names must match the
            // schema keys above; any field not listed lands in an
            // "Other" section appended at the bottom.
            ["x-sections"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["id"] = "basic",
                    ["label"] = "Basic Settings",
                    ["expanded"] = true,
                    ["fields"] = new[] { "url", "method", "timeout" }
                },
                new Dictionary<string, object>
                {
                    ["id"] = "auth",
                    ["label"] = "Authentication",
                    ["fields"] = new[] { "authentication" }
                },
                new Dictionary<string, object>
                {
                    ["id"] = "headers",
                    ["label"] = "Headers",
                    ["fields"] = new[] { "headers" }
                },
                new Dictionary<string, object>
                {
                    ["id"] = "body",
                    ["label"] = "Request Body",
                    ["fields"] = new[] { "bodyTemplate", "bodyFromClaims", "inputMapping" }
                },
                new Dictionary<string, object>
                {
                    ["id"] = "retry",
                    ["label"] = "Retry & Error Handling",
                    ["fields"] = new[] { "retryCount", "retryDelayMs", "failOnError", "continueOnStatus" }
                },
                new Dictionary<string, object>
                {
                    ["id"] = "output",
                    ["label"] = "Response Handling",
                    ["fields"] = new[] { "outputMapping", "outputMappingAdvanced", "responseValidation", "branchOnResponse", "includeResponseMeta" }
                }
            }
        };
    }

    /// <summary>Builder icons for the built-in steps (ui-icon names).</summary>
    private static readonly IReadOnlyDictionary<string, string> BuiltInIcons = new Dictionary<string, string>
    {
        ["local_login"] = "key",
        ["composite_login"] = "squares-2x2",
        ["external_login"] = "globe-alt",
        ["mfa"] = "shield-check",
        ["passwordless_email"] = "envelope",
        ["email_verification"] = "at-symbol",
        ["passwordless_sms"] = "device-phone-mobile",
        ["webauthn"] = "fingerprint",
        ["ldap"] = "building-office",
        ["saml"] = "identification",
        ["signup"] = "user-plus",
        ["create_user"] = "user",
        ["update_user"] = "pencil-square",
        ["assign_roles"] = "user-group",
        ["password_change"] = "lock",
        ["password_reset"] = "arrow-path",
        ["link_account"] = "link",
        ["consent"] = "hand-raised",
        ["dynamic_form"] = "clipboard-document-list",
        ["captcha"] = "shield",
        ["terms_acceptance"] = "document-text",
        ["condition"] = "funnel",
        ["branch"] = "arrows-right-left",
        ["transform"] = "adjustments-horizontal",
        ["api_call"] = "server",
        ["webhook"] = "bolt",
        ["custom_plugin"] = "puzzle",
        ["custom_page"] = "window",
    };

    private void RegisterType(string type, string name, string category, string description)
    {
        _typeInfo[type] = new StepTypeInfo
        {
            Type = type,
            DisplayName = name,
            Description = description,
            Category = category,
            Icon = BuiltInIcons.GetValueOrDefault(type)
        };
    }

    private void RegisterTypeWithRawSchema(string type, string name, string category, string description, Dictionary<string, object> schema, IReadOnlyList<StepBranchInfo>? branches = null)
    {
        _typeInfo[type] = new StepTypeInfo
        {
            Type = type,
            DisplayName = name,
            Description = description,
            Category = category,
            Icon = BuiltInIcons.GetValueOrDefault(type),
            ConfigurationSchema = schema,
            AvailableBranches = branches
        };
    }

    private static StepBranchInfo Branch(string id, string displayName, string? description = null)
        => new() { Id = id, DisplayName = displayName, Description = description };


    private void RegisterTypeWithSchema(string type, string name, string category, string description, Action<SchemaBuilder> configureSchema)
    {
        var schemaBuilder = new SchemaBuilder();
        configureSchema(schemaBuilder);

        _typeInfo[type] = new StepTypeInfo
        {
            Type = type,
            DisplayName = name,
            Description = description,
            Category = category,
            Icon = BuiltInIcons.GetValueOrDefault(type),
            ConfigurationSchema = schemaBuilder.Build()
        };
    }

    /// <summary>
    /// Registers a handler type
    /// </summary>
    public void Register(string stepType, Type handlerType)
    {
        _handlerTypes[stepType] = handlerType;
    }

    /// <summary>
    /// Registers a handler type with metadata
    /// </summary>
    public void Register<THandler>(string stepType, Action<StepTypeBuilder>? configure = null)
        where THandler : IStepHandler
    {
        _handlerTypes[stepType] = typeof(THandler);

        var builder = new StepTypeBuilder();
        configure?.Invoke(builder);

        // A module registering its handler for a known type keeps what it doesn't restate.
        _typeInfo.TryGetValue(stepType, out var existing);
        _typeInfo[stepType] = new StepTypeInfo
        {
            Type = stepType,
            DisplayName = builder.DisplayName ?? existing?.DisplayName,
            Description = builder.Description ?? existing?.Description,
            Category = builder.Category ?? existing?.Category,
            Icon = builder.Icon ?? existing?.Icon,
            Module = builder.Module ?? existing?.Module,
            HandlerType = typeof(THandler),
            ConfigurationSchema = builder.ConfigurationSchema ?? existing?.ConfigurationSchema,
            AvailableBranches = builder.AvailableBranches is { Count: > 0 } branches ? branches : existing?.AvailableBranches
        };
    }

    public IStepHandler? GetHandler(string stepType)
    {
        // First check if we have a registered handler type
        if (_handlerTypes.TryGetValue(stepType, out var handlerType))
        {
            return _serviceProvider.GetService(handlerType) as IStepHandler;
        }

        // Look for handler in registered IStepHandler services
        var handlers = _serviceProvider.GetServices<IStepHandler>();
        var handler = handlers.FirstOrDefault(h => h.StepType.Equals(stepType, StringComparison.OrdinalIgnoreCase));
        if (handler != null)
        {
            return handler;
        }

        // Try to find a custom handler
        var customHandlers = _serviceProvider.GetServices<ICustomStepHandler>();
        return customHandlers.FirstOrDefault(h => h.StepType.Equals(stepType, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<StepTypeInfo> GetRegisteredTypes()
    {
        return _typeInfo.Values;
    }
}
