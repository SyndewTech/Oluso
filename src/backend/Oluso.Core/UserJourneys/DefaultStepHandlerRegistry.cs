using Microsoft.Extensions.DependencyInjection;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Default implementation of step handler registry
/// </summary>
public class DefaultStepHandlerRegistry : IExtendedStepHandlerRegistry
{
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
            "Authenticates user with username/password",
            new Dictionary<string, object>
            {
                ["allowRememberMe"] = new { type = "boolean", @default = true, description = "Show 'Remember me' checkbox" },
                ["allowSelfRegistration"] = new { type = "boolean", @default = false, description = "Show link to registration" },
                ["loginHintClaim"] = new { type = "string", description = "Claim to use as login hint" }
            });

        RegisterTypeWithRawSchema("composite_login", "Composite Login", "Authentication",
            "Combines local and external login options",
            new Dictionary<string, object>
            {
                ["showLocalLogin"] = new { type = "boolean", @default = true, description = "Display username/password form" },
                ["showExternalProviders"] = new { type = "boolean", @default = true, description = "Display external IdP buttons" },
                ["showSignupLink"] = new { type = "boolean", @default = true, description = "Display link to registration" },
                ["showPasswordReset"] = new { type = "boolean", @default = true, description = "Display forgot password link" },
                ["primaryAction"] = new { type = "string", @default = "local", description = "Which action to emphasize visually" }
            });

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
                        ["descriptionField"] = "type"
                    }
                },
                ["autoProvision"] = new { type = "boolean", @default = true, description = "Auto-create user on first login" },
                ["autoRedirect"] = new { type = "boolean", @default = false, description = "Auto-redirect if only one provider" },
                ["proxyMode"] = new { type = "boolean", @default = false, description = "Pass through external claims without local user (federation broker)" },
                ["claimMappings"] = new { type = "object", description = "Map external claims to internal claims" }
            });

        RegisterTypeWithRawSchema("mfa", "Multi-Factor Authentication", "Authentication",
            "Requires additional verification factor",
            new Dictionary<string, object>
            {
                ["required"] = new { type = "boolean", @default = false, description = "Always require MFA" },
                ["methods"] = new { type = "array", items = "string", @default = new[] { "totp", "phone", "email" }, description = "Allowed MFA methods" },
                ["allowSetup"] = new { type = "boolean", @default = true, description = "Allow users to set up MFA during flow" },
                ["rememberDevice"] = new { type = "boolean", @default = true, description = "Remember trusted devices" },
                ["rememberDeviceDays"] = new { type = "number", @default = 30, description = "Days to remember device" }
            });

        RegisterTypeWithRawSchema("passwordless_email", "Passwordless Email", "Authentication",
            "Email-based passwordless login",
            new Dictionary<string, object>
            {
                ["codeLengthDigits"] = new { type = "number", @default = 6, description = "Number of digits in verification code" },
                ["codeExpiryMinutes"] = new { type = "number", @default = 15, description = "Minutes before code expires" },
                ["maxAttempts"] = new { type = "number", @default = 3, description = "Maximum verification attempts" },
                ["emailSubject"] = new { type = "string", description = "Custom email subject" },
                ["emailTemplate"] = new { type = "string", description = "Custom email template name" }
            });

        RegisterTypeWithRawSchema("passwordless_sms", "Passwordless SMS", "Authentication",
            "SMS-based passwordless login",
            new Dictionary<string, object>
            {
                ["codeLengthDigits"] = new { type = "number", @default = 6, description = "Number of digits in verification code" },
                ["codeExpiryMinutes"] = new { type = "number", @default = 10, description = "Minutes before code expires" },
                ["maxAttempts"] = new { type = "number", @default = 3, description = "Maximum verification attempts" },
                ["messageTemplate"] = new { type = "string", description = "Custom SMS message template" }
            });

        RegisterTypeWithRawSchema("webauthn", "WebAuthn/FIDO2", "Authentication",
            "Biometric/hardware key authentication",
            new Dictionary<string, object>
            {
                ["allowPlatformAuthenticator"] = new { type = "boolean", @default = true, description = "Allow built-in authenticators (TouchID, Windows Hello)" },
                ["allowCrossPlatform"] = new { type = "boolean", @default = true, description = "Allow security keys" },
                ["requireResidentKey"] = new { type = "boolean", @default = false, description = "Require discoverable credentials (passkeys)" },
                ["passkeyOnly"] = new { type = "boolean", @default = false, description = "Only allow passkey login (no username)" }
            });

        RegisterTypeWithRawSchema("ldap", "LDAP / Active Directory", "Authentication",
            "Authenticate against LDAP/AD server",
            new Dictionary<string, object>
            {
                ["serverUrl"] = new { type = "string", required = true, description = "LDAP server URL" },
                ["baseDn"] = new { type = "string", required = true, description = "Base DN for user search" },
                ["searchFilter"] = new { type = "string", @default = "(sAMAccountName={0})", description = "LDAP search filter" },
                ["bindDn"] = new { type = "string", description = "Service account DN for binding" },
                ["bindPassword"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Service account password", ["x-control"] = "secret-input" },
                ["useSsl"] = new { type = "boolean", @default = true, description = "Use SSL/TLS" },
                ["claimMappings"] = new { type = "object", description = "Map LDAP attributes to claims" }
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
        RegisterTypeWithRawSchema("signup", "Sign Up", "User Management",
            "Self-registration flow",
            new Dictionary<string, object>
            {
                ["emailClaim"] = new { type = "string", @default = "email", description = "Claim containing email" },
                ["usernameClaim"] = new { type = "string", description = "Claim containing username (defaults to email)" },
                ["passwordClaim"] = new { type = "string", @default = "password", description = "Claim containing password" },
                ["requireEmailVerification"] = new { type = "boolean", @default = true, description = "Require email verification" },
                ["defaultRoles"] = new { type = "array", items = "string", description = "Roles to assign to new user" },
                ["claimMappings"] = new { type = "object", description = "Map collected claims to user properties" }
            });

        RegisterTypeWithRawSchema("create_user", "Create User", "User Management",
            "Create a new user account",
            new Dictionary<string, object>
            {
                ["emailClaim"] = new { type = "string", @default = "email", description = "Claim containing email" },
                ["usernameClaim"] = new { type = "string", description = "Claim containing username (defaults to email)" },
                ["passwordClaim"] = new { type = "string", @default = "password", description = "Claim containing password" },
                ["requireEmailVerification"] = new { type = "boolean", @default = true, description = "Require email verification" },
                ["defaultRoles"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = "string",
                    ["description"] = "Roles to assign to new user",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/roles",
                        ["valueField"] = "name",
                        ["labelField"] = "name",
                        ["descriptionField"] = "description"
                    }
                },
                ["claimMappings"] = new { type = "object", description = "Map collected claims to user properties" }
            });

        RegisterTypeWithRawSchema("update_user", "Update User", "User Management",
            "Profile update flow",
            new Dictionary<string, object>
            {
                ["editableFields"] = new { type = "array", items = "string", description = "Fields user can edit" },
                ["requireCurrentPassword"] = new { type = "boolean", @default = false, description = "Require current password to save changes" }
            });

        RegisterTypeWithRawSchema("password_change", "Password Change", "User Management",
            "Change password flow",
            new Dictionary<string, object>
            {
                ["requireCurrentPassword"] = new { type = "boolean", @default = true, description = "Require current password" },
                ["minLength"] = new { type = "number", @default = 8, description = "Minimum password length" },
                ["requireUppercase"] = new { type = "boolean", @default = true, description = "Require uppercase letter" },
                ["requireLowercase"] = new { type = "boolean", @default = true, description = "Require lowercase letter" },
                ["requireDigit"] = new { type = "boolean", @default = true, description = "Require number" },
                ["requireSpecialChar"] = new { type = "boolean", @default = false, description = "Require special character" }
            });

        RegisterTypeWithRawSchema("password_reset", "Password Reset", "User Management",
            "Reset forgotten password",
            new Dictionary<string, object>
            {
                ["tokenLifetimeMinutes"] = new { type = "number", @default = 60, description = "Reset token lifetime" },
                ["requireCurrentPassword"] = new { type = "boolean", @default = false, description = "Require current password" }
            });

        RegisterTypeWithRawSchema("link_account", "Link Account", "User Management",
            "Link external account",
            new Dictionary<string, object>
            {
                ["provider"] = new { type = "string", description = "External provider to link" },
                ["allowUnlink"] = new { type = "boolean", @default = true, description = "Allow unlinking accounts" },
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
            });

        RegisterTypeWithRawSchema("claims_collection", "Claims Collection", "User Interaction",
            "Collects additional user information via configurable form",
            GetClaimsCollectionSchema());

        RegisterTypeWithRawSchema("captcha", "CAPTCHA Verification", "User Interaction",
            "Bot protection verification",
            new Dictionary<string, object>
            {
                ["provider"] = new { type = "string", @enum = new[] { "recaptcha", "hcaptcha", "cloudflare" }, @default = "recaptcha", description = "CAPTCHA provider" },
                ["siteKey"] = new { type = "string", required = true, description = "CAPTCHA site key" },
                ["secretKey"] = new Dictionary<string, object> { ["type"] = "string", ["required"] = true, ["description"] = "CAPTCHA secret key", ["x-control"] = "secret-input" },
                ["scoreThreshold"] = new { type = "number", @default = 0.5, description = "Minimum score (reCAPTCHA v3)" }
            });

        RegisterTypeWithRawSchema("terms_acceptance", "Terms Acceptance", "User Interaction",
            "Require acceptance of terms",
            new Dictionary<string, object>
            {
                ["termsUrl"] = new { type = "string", required = true, description = "URL to terms of service" },
                ["privacyUrl"] = new { type = "string", description = "URL to privacy policy" },
                ["requireCheckbox"] = new { type = "boolean", @default = true, description = "Require checkbox acceptance" },
                ["version"] = new { type = "string", description = "Terms version for tracking acceptance" }
            });

        // Flow Control
        RegisterTypeWithRawSchema("condition", "Condition", "Flow Control",
            "Conditional branching based on rules",
            GetConditionSchema());

        RegisterTypeWithRawSchema("branch", "Branch", "Flow Control",
            "Explicit branching to another step",
            new Dictionary<string, object>
            {
                ["targetStep"] = new { type = "string", required = true, description = "Step ID to branch to" }
            });

        RegisterTypeWithRawSchema("transform", "Claims Transform", "Flow Control",
            "Transform claims before issuing token",
            GetTransformSchema());

        RegisterTypeWithRawSchema("api_call", "API Call", "Flow Control",
            "Call external API during journey",
            GetApiCallSchema());

        RegisterTypeWithRawSchema("webhook", "Webhook", "Flow Control",
            "Notify external service",
            new Dictionary<string, object>
            {
                ["url"] = new { type = "string", required = true, description = "Webhook endpoint URL" },
                ["async"] = new { type = "boolean", @default = true, description = "Don't wait for response" },
                ["timeout"] = new { type = "number", @default = 10, description = "Request timeout in seconds" },
                ["headers"] = new { type = "object", description = "Custom HTTP headers", additionalProperties = new { type = "string" } },
                ["includeUserData"] = new { type = "boolean", @default = true, description = "Include user data in payload" },
                ["includeJourneyData"] = new { type = "boolean", @default = false, description = "Include journey state in payload" },
                ["signPayload"] = new { type = "boolean", @default = false, description = "Sign payload with HMAC" },
                ["secretKey"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Secret key for HMAC signature", ["x-control"] = "secret-input" }
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
            "Display custom HTML page or template",
            new Dictionary<string, object>
            {
                ["templatePath"] = new { type = "string", required = true, description = "Path to custom Razor template" },
                ["model"] = new { type = "object", description = "Data to pass to template" }
            });
    }

    private static Dictionary<string, object> GetClaimsCollectionSchema()
    {
        return new Dictionary<string, object>
        {
            ["title"] = new { type = "string", description = "Form title displayed to user" },
            ["description"] = new { type = "string", description = "Form description/instructions" },
            ["submitButtonText"] = new { type = "string", @default = "Continue", description = "Submit button label" },
            ["cancelButtonText"] = new { type = "string", description = "Cancel button label (if allowCancel)" },
            ["allowCancel"] = new { type = "boolean", @default = false, description = "Show cancel button" },
            ["viewName"] = new { type = "string", @default = "Journey/_DynamicForm", description = "Custom view name" },
            ["localizedTitles"] = new { type = "object", description = "Title translations by culture code" },
            ["localizedDescriptions"] = new { type = "object", description = "Description translations by culture code" },
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
                            @enum = new[] { "text", "email", "password", "number", "date", "tel", "url", "textarea", "select", "radio", "checkbox" },
                            description = "Field input type"
                        },
                        ["label"] = new { type = "string", description = "Field label displayed to user" },
                        ["placeholder"] = new { type = "string", description = "Input placeholder text" },
                        ["description"] = new { type = "string", description = "Help text below field" },
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
                        ["readOnly"] = new { type = "boolean", @default = false, description = "Field is read-only" },
                        ["hidden"] = new { type = "boolean", @default = false, description = "Field is hidden" },
                        ["group"] = new { type = "string", description = "Group name for fieldset grouping" },
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
                    properties = new Dictionary<string, object>
                    {
                        ["source"] = new { type = "string", @enum = new[] { "claim", "context", "previous_step" }, description = "Data source" },
                        ["key"] = new { type = "string", required = true, description = "Key/claim name to check" },
                        ["operator"] = new { type = "string", @enum = new[] { "equals", "notEquals", "contains", "exists", "notExists", "greaterThan", "lessThan", "regex" } },
                        ["value"] = new { type = "string", description = "Value to compare" },
                        ["negate"] = new { type = "boolean", @default = false, description = "Negate the result" }
                    }
                }
            },
            ["combineWith"] = new { type = "string", @enum = new[] { "and", "or" }, @default = "and", description = "How to combine conditions" },
            ["onTrue"] = new { type = "string", description = "Step ID when conditions are true" },
            ["onFalse"] = new { type = "string", description = "Step ID when conditions are false" }
        };
    }

    private static Dictionary<string, object> GetTransformSchema()
    {
        return new Dictionary<string, object>
        {
            ["mappings"] = new
            {
                type = "array",
                required = true,
                description = "Claim transformations",
                items = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["source"] = new { type = "string", required = true, description = "Source claim type" },
                        ["target"] = new { type = "string", required = true, description = "Target claim type" },
                        ["transform"] = new { type = "string", @enum = new[] { "copy", "uppercase", "lowercase", "hash", "split", "join", "regex", "template" }, @default = "copy" },
                        ["transformArg"] = new { type = "string", description = "Argument for transform (regex pattern, delimiter, template)" }
                    }
                }
            },
            ["removeSourceClaims"] = new { type = "boolean", @default = false, description = "Remove source claims after mapping" }
        };
    }

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
                    ["type"] = new { type = "string", @enum = new[] { "none", "bearer", "basic", "apikey" }, @default = "none", description = "Authentication type" },
                    ["token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Bearer token (supports placeholders like {claim:access_token})", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "bearer" } },
                    ["username"] = new { type = "string", description = "Basic auth username", showWhen = new { field = "type", value = "basic" } },
                    ["password"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Basic auth password", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "basic" } },
                    ["apiKey"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "API key value", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "apikey" } },
                    ["headerName"] = new { type = "string", @default = "X-API-Key", description = "Header name for API key", showWhen = new { field = "type", value = "apikey" } }
                }
            },
            ["retryCount"] = new { type = "number", @default = 0, description = "Number of retry attempts on failure" },
            ["retryDelay"] = new { type = "number", @default = 1000, description = "Delay between retries in milliseconds" },
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
                        ["branchTo"] = new { type = "string", required = true, description = "Step ID to branch to" }
                    }
                }
            },
            ["includeResponseMeta"] = new { type = "boolean", @default = false, description = "Add _api_status and _api_success claims" }
        };
    }

    private void RegisterType(string type, string name, string category, string description)
    {
        _typeInfo[type] = new StepTypeInfo
        {
            Type = type,
            Description = description,
            Category = category
        };
    }

    private void RegisterTypeWithRawSchema(string type, string name, string category, string description, Dictionary<string, object> schema)
    {
        _typeInfo[type] = new StepTypeInfo
        {
            Type = type,
            Description = description,
            Category = category,
            ConfigurationSchema = schema
        };
    }

    private void RegisterTypeWithSchema(string type, string name, string category, string description, Action<SchemaBuilder> configureSchema)
    {
        var schemaBuilder = new SchemaBuilder();
        configureSchema(schemaBuilder);

        _typeInfo[type] = new StepTypeInfo
        {
            Type = type,
            Description = description,
            Category = category,
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

        _typeInfo[stepType] = new StepTypeInfo
        {
            Type = stepType,
            Description = builder.Description,
            Category = builder.Category,
            Module = builder.Module,
            HandlerType = typeof(THandler),
            ConfigurationSchema = builder.ConfigurationSchema
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
