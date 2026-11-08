using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Tokens of issuer-signed endpoints (WebhookAuthType.IssuerSigned), signed with the tenant's own key.
        services.TryAddSingleton<IWebhookTokenIssuer, IssuerWebhookTokenIssuer>();

        // Events with a webhook type reach the dispatcher, which makes the first delivery in process. Queue-based retries
        // (and the function apps that drain them) only take over failed deliveries, so without this sink no event
        // webhook was ever sent.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOlusoEventSink, WebhookEventSink>());

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
    /// No-op — core webhook events are now auto-generated via AddOlusoEvents() + AddEventPropertySource.
    /// Kept for backward compatibility with existing sample code.
    /// </summary>
    public static IServiceCollection AddCoreWebhookEvents(this IServiceCollection services) => services;
}

/// <summary>
/// Admin webhook event type constants
/// </summary>
public static class AdminWebhookEvents
{
    public const string ClientCreated = "admin.client_created";
    public const string ClientUpdated = "admin.client_updated";
    public const string ClientDeleted = "admin.client_deleted";
    public const string RoleCreated = "admin.role_created";
    public const string RoleUpdated = "admin.role_updated";
    public const string RoleDeleted = "admin.role_deleted";
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
