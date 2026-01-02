using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;

namespace Oluso.UserJourneys;

/// <summary>
/// Builder for configuring user journey step handlers
/// </summary>
public class UserJourneyBuilder
{
    public IServiceCollection Services { get; }
    internal OlusoBuilder OlusoBuilder { get; }

    internal UserJourneyBuilder(OlusoBuilder builder)
    {
        OlusoBuilder = builder;
        Services = builder.Services;
    }

    /// <summary>
    /// Add a custom step handler
    /// </summary>
    public UserJourneyBuilder AddStepHandler<THandler>() where THandler : class, IStepHandler
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, THandler>());
        return this;
    }

    /// <summary>
    /// Add a custom step handler instance
    /// </summary>
    public UserJourneyBuilder AddStepHandler(IStepHandler handler)
    {
        Services.AddSingleton(handler);
        return this;
    }

    /// <summary>
    /// Add built-in local login step handler
    /// </summary>
    public UserJourneyBuilder AddLocalLogin()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, LocalLoginStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in sign-up step handler
    /// </summary>
    public UserJourneyBuilder AddSignUp()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, SignUpStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in MFA step handler
    /// </summary>
    public UserJourneyBuilder AddMfa()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, MfaStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in consent step handler
    /// </summary>
    public UserJourneyBuilder AddConsent()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, ConsentStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in password reset step handler
    /// </summary>
    public UserJourneyBuilder AddPasswordReset()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, PasswordResetStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in external login step handler
    /// </summary>
    public UserJourneyBuilder AddExternalLogin()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, ExternalLoginStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in condition step handler (no UI - logic only)
    /// </summary>
    public UserJourneyBuilder AddCondition()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, ConditionStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in branch step handler (no UI - logic only)
    /// </summary>
    public UserJourneyBuilder AddBranch()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, BranchStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in transform step handler (no UI - data transformation)
    /// </summary>
    public UserJourneyBuilder AddTransform()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, TransformStepHandler>());
        return this;
    }

    /// <summary>
    /// Add built-in API call step handler (no UI - external API integration)
    /// </summary>
    public UserJourneyBuilder AddApiCall()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, ApiCallStepHandler>());
        EnsureHttpClientRegistered();
        return this;
    }

    /// <summary>
    /// Add built-in webhook step handler (no UI - event notifications)
    /// </summary>
    public UserJourneyBuilder AddWebhook()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, WebhookStepHandler>());
        EnsureHttpClientRegistered();
        return this;
    }

    /// <summary>
    /// Add passwordless email step handler (OTP or magic link)
    /// </summary>
    public UserJourneyBuilder AddPasswordlessEmail()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, PasswordlessEmailStepHandler>());
        return this;
    }

    /// <summary>
    /// Add passwordless SMS step handler (OTP)
    /// </summary>
    public UserJourneyBuilder AddPasswordlessSms()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, PasswordlessSmsStepHandler>());
        return this;
    }

    /// <summary>
    /// Add dynamic form step handler (claims collection)
    /// </summary>
    public UserJourneyBuilder AddDynamicForm()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, DynamicFormStepHandler>());
        return this;
    }

    /// <summary>
    /// Add link account step handler (for linking external providers)
    /// </summary>
    public UserJourneyBuilder AddLinkAccount()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, LinkAccountStepHandler>());
        return this;
    }

    /// <summary>
    /// Add CAPTCHA verification step handler
    /// </summary>
    public UserJourneyBuilder AddCaptcha()
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, CaptchaStepHandler>());
        EnsureHttpClientRegistered();
        return this;
    }

    private bool _httpClientRegistered;

    private void EnsureHttpClientRegistered()
    {
        if (_httpClientRegistered) return;
        _httpClientRegistered = true;

        // Register typed HTTP clients for API calls, webhooks, and CAPTCHA verification
        Services.AddHttpClient("JourneyApiCall", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Oluso-Journey/1.0");
        });

        Services.AddHttpClient("JourneyWebhook", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Oluso-Journey/1.0");
        });

        Services.AddHttpClient("CaptchaVerification", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Oluso-Journey/1.0");
        });
    }

    /// <summary>
    /// Add all built-in authentication step handlers (UI steps)
    /// </summary>
    public UserJourneyBuilder AddBuiltInSteps()
    {
        AddLocalLogin();
        AddSignUp();
        AddMfa();
        AddConsent();
        AddPasswordReset();
        AddExternalLogin();
        AddPasswordlessEmail();
        AddPasswordlessSms();
        AddDynamicForm();
        AddLinkAccount();
        AddCaptcha();
        return this;
    }

    /// <summary>
    /// Add all built-in logic step handlers (no-UI steps)
    /// </summary>
    public UserJourneyBuilder AddLogicSteps()
    {
        AddCondition();
        AddBranch();
        AddTransform();
        AddApiCall();
        AddWebhook();
        return this;
    }

    /// <summary>
    /// Add all built-in step handlers (both UI and logic steps)
    /// </summary>
    public UserJourneyBuilder AddAllBuiltInSteps()
    {
        AddBuiltInSteps();
        AddLogicSteps();
        return this;
    }

    /// <summary>
    /// Use a custom state store (e.g., Redis, database).
    /// This replaces the default in-memory store.
    /// </summary>
    public UserJourneyBuilder UseStateStore<TStore>() where TStore : class, IJourneyStateStore
    {
        // Remove existing registrations and add the custom one
        RemoveService<IJourneyStateStore>();
        Services.AddSingleton<IJourneyStateStore, TStore>();
        return this;
    }

    /// <summary>
    /// Use a custom state store instance.
    /// This replaces the default in-memory store.
    /// </summary>
    public UserJourneyBuilder UseStateStore(IJourneyStateStore store)
    {
        RemoveService<IJourneyStateStore>();
        Services.AddSingleton(store);
        return this;
    }

    /// <summary>
    /// Use distributed cache (Redis, SQL, etc.) for journey state storage.
    /// Requires IDistributedCache to be configured (e.g., AddStackExchangeRedisCache).
    /// </summary>
    public UserJourneyBuilder UseDistributedCache()
    {
        RemoveService<IJourneyStateStore>();
        Services.AddSingleton<IJourneyStateStore, DistributedCacheJourneyStateStore>();
        return this;
    }

    /// <summary>
    /// Use a custom policy store (e.g., database).
    /// This replaces the default in-memory store.
    /// </summary>
    public UserJourneyBuilder UsePolicyStore<TStore>() where TStore : class, IJourneyPolicyStore
    {
        RemoveService<IJourneyPolicyStore>();
        Services.AddSingleton<IJourneyPolicyStore, TStore>();
        return this;
    }

    /// <summary>
    /// Use a custom submission store for data collection journeys.
    /// </summary>
    public UserJourneyBuilder UseSubmissionStore<TStore>() where TStore : class, IJourneySubmissionStore
    {
        RemoveService<IJourneySubmissionStore>();
        Services.AddSingleton<IJourneySubmissionStore, TStore>();
        return this;
    }

    private void RemoveService<T>()
    {
        var descriptor = Services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
        {
            Services.Remove(descriptor);
        }
    }
}

/// <summary>
/// Extensions for configuring user journeys on OlusoBuilder
/// </summary>
public static class UserJourneyBuilderExtensions
{
    /// <summary>
    /// Configure user journey engine with step handlers
    /// </summary>
    public static OlusoBuilder AddUserJourneys(this OlusoBuilder builder, Action<UserJourneyBuilder>? configure = null)
    {
        var journeyBuilder = new UserJourneyBuilder(builder);

        // Register core services
        builder.Services.TryAddSingleton<IStepHandlerRegistry, DefaultStepHandlerRegistry>();
        builder.Services.TryAddSingleton<IJourneyPolicyStore, InMemoryJourneyPolicyStore>();
        builder.Services.TryAddSingleton<IJourneyStateStore, InMemoryJourneyStateStore>();
        builder.Services.TryAddSingleton<IJourneySubmissionStore, InMemoryJourneySubmissionStore>();
        builder.Services.TryAddScoped<IJourneyOrchestrator, DefaultJourneyOrchestrator>();

        // Configure step handlers
        configure?.Invoke(journeyBuilder);

        builder.Options.UserJourneyEngineEnabled = true;

        return builder;
    }

    /// <summary>
    /// Configure user journeys with all built-in steps
    /// </summary>
    public static OlusoBuilder AddUserJourneysWithDefaults(this OlusoBuilder builder)
    {
        return builder.AddUserJourneys(j => j.AddBuiltInSteps());
    }
}
