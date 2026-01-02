using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Events;

namespace Oluso.Webhooks;

/// <summary>
/// Extension methods for configuring webhook dispatching
/// </summary>
public static class WebhookExtensions
{
    /// <summary>
    /// Add webhook dispatching services
    /// </summary>
    public static IServiceCollection AddOlusoWebhooks(
        this IServiceCollection services,
        Action<WebhookOptions>? configure = null)
    {
        // Configure options
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<WebhookOptions>(_ => { });
        }

        // Register HttpClient for webhook dispatcher
        services.AddHttpClient<WebhookDispatcher>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Oluso-Webhooks/1.0");
        });

        // Register the unified webhook dispatcher interface
        services.AddScoped<IWebhookDispatcher>(sp => sp.GetRequiredService<WebhookDispatcher>());

        // Register registry
        services.AddSingleton<IWebhookEventRegistry, WebhookEventRegistry>();

        return services;
    }

    /// <summary>
    /// Add webhook retry processing with the default in-process processor.
    /// For production multi-instance deployments, use AddQueueBasedWebhookRetries() from Oluso.Enterprise.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional retry options configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddWebhookRetryProcessing(
        this IServiceCollection services,
        Action<WebhookRetryOptions>? configure = null)
    {
        // Configure retry options
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<WebhookRetryOptions>(_ => { });
        }

        // Register the in-process retry processor as both the interface and hosted service
        services.AddSingleton<InProcessWebhookRetryProcessor>();
        services.AddSingleton<IWebhookRetryProcessor>(sp => sp.GetRequiredService<InProcessWebhookRetryProcessor>());
        services.AddHostedService(sp => sp.GetRequiredService<InProcessWebhookRetryProcessor>());

        return services;
    }

    /// <summary>
    /// Disable the in-process retry processor.
    /// Use this when an external queue-based processor handles retries.
    /// </summary>
    public static IServiceCollection DisableInProcessWebhookRetries(this IServiceCollection services)
    {
        services.Configure<WebhookRetryOptions>(options => options.EnableInProcessRetries = false);
        return services;
    }

    /// <summary>
    /// Add a webhook event provider for registering custom events
    /// </summary>
    public static IServiceCollection AddWebhookEventProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IWebhookEventProvider
    {
        services.AddSingleton<IWebhookEventProvider, TProvider>();
        return services;
    }

    /// <summary>
    /// Add the core webhook event provider with standard Oluso events
    /// </summary>
    public static IServiceCollection AddCoreWebhookEvents(this IServiceCollection services)
    {
        services.AddSingleton<IWebhookEventProvider, CoreWebhookEventProvider>();
        return services;
    }
}

/// <summary>
/// Provider for core Oluso webhook events
/// </summary>
public class CoreWebhookEventProvider : IWebhookEventProvider
{
    public string ProviderId => "core";
    public string DisplayName => "Core Events";

    public IReadOnlyList<WebhookEventDefinition> GetEventDefinitions() => new[]
    {
        // User events
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.UserCreated,
            Category = WebhookEventCategories.User,
            DisplayName = "User Created",
            Description = "Triggered when a new user account is created",
            EnabledByDefault = true
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.UserUpdated,
            Category = WebhookEventCategories.User,
            DisplayName = "User Updated",
            Description = "Triggered when a user profile is updated"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.UserDeleted,
            Category = WebhookEventCategories.User,
            DisplayName = "User Deleted",
            Description = "Triggered when a user account is deleted"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.UserEmailVerified,
            Category = WebhookEventCategories.User,
            DisplayName = "Email Verified",
            Description = "Triggered when a user verifies their email address"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.UserPasswordChanged,
            Category = WebhookEventCategories.User,
            DisplayName = "Password Changed",
            Description = "Triggered when a user changes their password"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.UserLockedOut,
            Category = WebhookEventCategories.Security,
            DisplayName = "User Locked Out",
            Description = "Triggered when a user account is locked"
        },

        // Authentication events
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.LoginSuccess,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Login Success",
            Description = "Triggered when a user successfully logs in",
            EnabledByDefault = true
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.LoginFailed,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Login Failed",
            Description = "Triggered when a login attempt fails"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.Logout,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Logout",
            Description = "Triggered when a user logs out"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.TokenIssued,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Token Issued",
            Description = "Triggered when an access token is issued"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.TokenRevoked,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Token Revoked",
            Description = "Triggered when a token is revoked"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.MfaEnabled,
            Category = WebhookEventCategories.Security,
            DisplayName = "MFA Enabled",
            Description = "Triggered when MFA is enabled for a user"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.MfaDisabled,
            Category = WebhookEventCategories.Security,
            DisplayName = "MFA Disabled",
            Description = "Triggered when MFA is disabled for a user"
        },

        // Security events
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.ConsentGranted,
            Category = WebhookEventCategories.Security,
            DisplayName = "Consent Granted",
            Description = "Triggered when a user grants consent to an application"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.ConsentRevoked,
            Category = WebhookEventCategories.Security,
            DisplayName = "Consent Revoked",
            Description = "Triggered when a user revokes consent from an application"
        },
        new WebhookEventDefinition
        {
            EventType = CoreWebhookEvents.SuspiciousActivity,
            Category = WebhookEventCategories.Security,
            DisplayName = "Suspicious Activity",
            Description = "Triggered when suspicious activity is detected",
            EnabledByDefault = true
        },

        // Admin events
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.UserCreated,
            Category = AdminCategory,
            DisplayName = "Admin: User Created",
            Description = "Triggered when an admin creates a user account"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.UserUpdated,
            Category = AdminCategory,
            DisplayName = "Admin: User Updated",
            Description = "Triggered when an admin updates a user account"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.UserDeleted,
            Category = AdminCategory,
            DisplayName = "Admin: User Deleted",
            Description = "Triggered when an admin deletes a user account"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.PasswordReset,
            Category = AdminCategory,
            DisplayName = "Admin: Password Reset",
            Description = "Triggered when an admin resets a user's password"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.ClientCreated,
            Category = AdminCategory,
            DisplayName = "Admin: Client Created",
            Description = "Triggered when an admin creates an OAuth client"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.ClientUpdated,
            Category = AdminCategory,
            DisplayName = "Admin: Client Updated",
            Description = "Triggered when an admin updates an OAuth client"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.ClientDeleted,
            Category = AdminCategory,
            DisplayName = "Admin: Client Deleted",
            Description = "Triggered when an admin deletes an OAuth client"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.RoleCreated,
            Category = AdminCategory,
            DisplayName = "Admin: Role Created",
            Description = "Triggered when an admin creates a role"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.RoleDeleted,
            Category = AdminCategory,
            DisplayName = "Admin: Role Deleted",
            Description = "Triggered when an admin deletes a role"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.IdpCreated,
            Category = AdminCategory,
            DisplayName = "Admin: Identity Provider Created",
            Description = "Triggered when an admin creates an identity provider"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.IdpDeleted,
            Category = AdminCategory,
            DisplayName = "Admin: Identity Provider Deleted",
            Description = "Triggered when an admin deletes an identity provider"
        },
        new WebhookEventDefinition
        {
            EventType = AdminWebhookEvents.SigningKeyChanged,
            Category = AdminCategory,
            DisplayName = "Admin: Signing Key Changed",
            Description = "Triggered when an admin creates, rotates, or revokes a signing key"
        },

        // OIDC Protocol events
        new WebhookEventDefinition
        {
            EventType = OidcWebhookEvents.AuthorizationRequest,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Authorization Request",
            Description = "Triggered when an OIDC authorization request is received"
        },
        new WebhookEventDefinition
        {
            EventType = OidcWebhookEvents.AuthorizationCodeIssued,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Authorization Code Issued",
            Description = "Triggered when an OIDC authorization code is issued"
        },
        new WebhookEventDefinition
        {
            EventType = OidcWebhookEvents.AuthorizationFailed,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Authorization Failed",
            Description = "Triggered when an OIDC authorization request fails"
        },
        new WebhookEventDefinition
        {
            EventType = OidcWebhookEvents.TokenFailed,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Token Request Failed",
            Description = "Triggered when a token request fails"
        },
        new WebhookEventDefinition
        {
            EventType = OidcWebhookEvents.RefreshTokenUsed,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Refresh Token Used",
            Description = "Triggered when a refresh token is used"
        },

        // SAML events
        new WebhookEventDefinition
        {
            EventType = SamlWebhookEvents.SsoRequestReceived,
            Category = SamlCategory,
            DisplayName = "SAML SSO Request Received",
            Description = "Triggered when a SAML SSO request is received"
        },
        new WebhookEventDefinition
        {
            EventType = SamlWebhookEvents.AssertionIssued,
            Category = SamlCategory,
            DisplayName = "SAML Assertion Issued",
            Description = "Triggered when a SAML assertion is issued to a service provider"
        },
        new WebhookEventDefinition
        {
            EventType = SamlWebhookEvents.SsoFailed,
            Category = SamlCategory,
            DisplayName = "SAML SSO Failed",
            Description = "Triggered when a SAML SSO request fails"
        },
        new WebhookEventDefinition
        {
            EventType = SamlWebhookEvents.LogoutRequestReceived,
            Category = SamlCategory,
            DisplayName = "SAML Logout Request Received",
            Description = "Triggered when a SAML logout request is received"
        },
        new WebhookEventDefinition
        {
            EventType = SamlWebhookEvents.LogoutCompleted,
            Category = SamlCategory,
            DisplayName = "SAML Logout Completed",
            Description = "Triggered when a SAML logout is completed"
        },
        new WebhookEventDefinition
        {
            EventType = SamlWebhookEvents.SpLogin,
            Category = SamlCategory,
            DisplayName = "SAML SP Login",
            Description = "Triggered when a user authenticates via SAML from an external IdP"
        },

        // LDAP events
        new WebhookEventDefinition
        {
            EventType = LdapWebhookEvents.LoginSuccess,
            Category = LdapCategory,
            DisplayName = "LDAP Login Success",
            Description = "Triggered when a user successfully authenticates via LDAP"
        },
        new WebhookEventDefinition
        {
            EventType = LdapWebhookEvents.LoginFailed,
            Category = LdapCategory,
            DisplayName = "LDAP Login Failed",
            Description = "Triggered when an LDAP authentication attempt fails"
        },
        new WebhookEventDefinition
        {
            EventType = LdapWebhookEvents.UserProvisioned,
            Category = LdapCategory,
            DisplayName = "LDAP User Provisioned",
            Description = "Triggered when a new user is provisioned from LDAP"
        }
    };

    private const string AdminCategory = "Admin";
    private const string SamlCategory = "SAML";
    private const string LdapCategory = "LDAP";
}

/// <summary>
/// Admin webhook event type constants
/// </summary>
public static class AdminWebhookEvents
{
    public const string UserCreated = "admin.user_created";
    public const string UserUpdated = "admin.user_updated";
    public const string UserDeleted = "admin.user_deleted";
    public const string PasswordReset = "admin.password_reset";
    public const string ClientCreated = "admin.client_created";
    public const string ClientUpdated = "admin.client_updated";
    public const string ClientDeleted = "admin.client_deleted";
    public const string RoleCreated = "admin.role_created";
    public const string RoleUpdated = "admin.role_updated";
    public const string RoleDeleted = "admin.role_deleted";
    public const string UserRoleAssigned = "admin.user_role_assigned";
    public const string UserRoleRemoved = "admin.user_role_removed";
    public const string IdpCreated = "admin.idp_created";
    public const string IdpUpdated = "admin.idp_updated";
    public const string IdpDeleted = "admin.idp_deleted";
    public const string WebhookCreated = "admin.webhook_created";
    public const string WebhookUpdated = "admin.webhook_updated";
    public const string WebhookDeleted = "admin.webhook_deleted";
    public const string TenantSettingsUpdated = "admin.tenant_settings_updated";
    public const string SigningKeyChanged = "admin.signing_key_changed";
    public const string JourneyPolicyUpdated = "admin.journey_policy_updated";
}

/// <summary>
/// OIDC webhook event type constants
/// </summary>
public static class OidcWebhookEvents
{
    public const string AuthorizationRequest = "oidc.authorization_request";
    public const string AuthorizationCodeIssued = "oidc.authorization_code_issued";
    public const string AuthorizationFailed = "oidc.authorization_failed";
    public const string TokenFailed = "oidc.token_failed";
    public const string RefreshTokenUsed = "oidc.refresh_token_used";
}

/// <summary>
/// SAML webhook event type constants
/// </summary>
public static class SamlWebhookEvents
{
    public const string SsoRequestReceived = "saml.sso_request_received";
    public const string AssertionIssued = "saml.assertion_issued";
    public const string SsoFailed = "saml.sso_failed";
    public const string LogoutRequestReceived = "saml.logout_request_received";
    public const string LogoutCompleted = "saml.logout_completed";
    public const string SpLogin = "saml.sp_login";
}

/// <summary>
/// LDAP webhook event type constants
/// </summary>
public static class LdapWebhookEvents
{
    public const string LoginSuccess = "ldap.login_success";
    public const string LoginFailed = "ldap.login_failed";
    public const string UserProvisioned = "ldap.user_provisioned";
}
