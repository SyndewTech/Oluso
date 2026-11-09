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
    /// Add a custom step handler. Registered Singleton — every step
    /// handler in the codebase resolves its dependencies via
    /// <c>context.ServiceProvider.GetService&lt;&gt;()</c> inside
    /// <c>ExecuteAsync</c> rather than constructor injection, so handlers
    /// are stateless dispatchers and one instance for the host lifetime is
    /// sufficient. The scoped resolution still works because
    /// <c>context.ServiceProvider</c> is the per-request scope.
    /// </summary>
    public UserJourneyBuilder AddStepHandler<THandler>() where THandler : class, IStepHandler
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, THandler>());
        return this;
    }

    /// <summary>
    /// Add a custom step handler instance. Same Singleton lifetime as the
    /// type-based overload.
    /// </summary>
    public UserJourneyBuilder AddStepHandler(IStepHandler handler)
    {
        Services.AddSingleton(handler);
        return this;
    }

    /// <summary>Add built-in local login step handler</summary>
    public UserJourneyBuilder AddLocalLogin() => Add<LocalLoginStepHandler>();

    /// <summary>Add built-in composite-login step handler (presents multiple login methods on one screen)</summary>
    public UserJourneyBuilder AddCompositeLogin() => Add<CompositeLoginStepHandler>();

    /// <summary>Add built-in sign-up step handler</summary>
    public UserJourneyBuilder AddSignUp() => Add<SignUpStepHandler>();

    /// <summary>Add built-in MFA step handler</summary>
    public UserJourneyBuilder AddMfa() => Add<MfaStepHandler>();

    /// <summary>Add built-in consent step handler</summary>
    public UserJourneyBuilder AddConsent() => Add<ConsentStepHandler>();

    /// <summary>Add built-in password reset step handler</summary>
    public UserJourneyBuilder AddPasswordReset() => Add<PasswordResetStepHandler>();

    /// <summary>Add built-in password change step handler (logged-in user changing their password)</summary>
    public UserJourneyBuilder AddPasswordChange() => Add<PasswordChangeStepHandler>();

    /// <summary>Add built-in external login step handler</summary>
    public UserJourneyBuilder AddExternalLogin() => Add<ExternalLoginStepHandler>();

    /// <summary>Add built-in condition step handler (no UI — logic only)</summary>
    public UserJourneyBuilder AddCondition() => Add<ConditionStepHandler>();

    /// <summary>Add built-in branch step handler (no UI — logic only)</summary>
    public UserJourneyBuilder AddBranch() => Add<BranchStepHandler>();

    /// <summary>Add built-in transform step handler (no UI — data transformation)</summary>
    public UserJourneyBuilder AddTransform() => Add<TransformStepHandler>();

    /// <summary>Add built-in API call step handler (no UI — external API integration)</summary>
    public UserJourneyBuilder AddApiCall()
    {
        Add<ApiCallStepHandler>();
        EnsureHttpClientRegistered();
        return this;
    }

    /// <summary>Add built-in webhook step handler (no UI — event notifications)</summary>
    public UserJourneyBuilder AddWebhook()
    {
        Add<WebhookStepHandler>();
        EnsureHttpClientRegistered();
        return this;
    }

    /// <summary>Add passwordless email step handler (OTP or magic link)</summary>
    public UserJourneyBuilder AddPasswordlessEmail() => Add<PasswordlessEmailStepHandler>();

    /// <summary>Add passwordless SMS step handler (OTP)</summary>
    public UserJourneyBuilder AddPasswordlessSms() => Add<PasswordlessSmsStepHandler>();

    /// <summary>
    /// Add email-verification step handler — proves ownership of an email
    /// address without authenticating a user. Use as a pre-condition for
    /// sign-up, password reset, or sensitive profile changes.
    /// </summary>
    public UserJourneyBuilder AddEmailVerification() => Add<EmailVerificationStepHandler>();

    /// <summary>Add dynamic form step handler (claims collection)</summary>
    public UserJourneyBuilder AddDynamicForm() => Add<DynamicFormStepHandler>();

    /// <summary>
    /// Add the custom page step handler, which renders a host-authored Razor
    /// partial from <c>Views/Shared/Journey/Templates/</c> as a journey screen.
    /// </summary>
    public UserJourneyBuilder AddCustomPage() => Add<CustomPageStepHandler>();

    /// <summary>Add link account step handler (for linking external providers)</summary>
    public UserJourneyBuilder AddLinkAccount() => Add<LinkAccountStepHandler>();

    /// <summary>Add CAPTCHA verification step handler</summary>
    public UserJourneyBuilder AddCaptcha()
    {
        Add<CaptchaStepHandler>();
        EnsureHttpClientRegistered();
        return this;
    }

    /// <summary>Add built-in create-user step handler (programmatic — no UI)</summary>
    public UserJourneyBuilder AddCreateUser() => Add<CreateUserStepHandler>();

    /// <summary>Add built-in update-user step handler (programmatic — no UI)</summary>
    public UserJourneyBuilder AddUpdateUser() => Add<UpdateUserStepHandler>();

    /// <summary>Add built-in assign-roles step handler (programmatic — no UI)</summary>
    public UserJourneyBuilder AddAssignRoles() => Add<AssignRolesStepHandler>();

    /// <summary>Add built-in terms-acceptance step handler</summary>
    public UserJourneyBuilder AddTermsAcceptance() => Add<TermsAcceptanceStepHandler>();

    /// <summary>
    /// Add the dispatcher step handler that delegates to a registered
    /// <see cref="ICustomStepHandler"/> by name. Required to make plugins
    /// added via <c>AddCustomJourneyStep&lt;T&gt;</c> reachable from
    /// journey policies.
    /// </summary>
    public UserJourneyBuilder AddCustomPluginDispatcher() => Add<CustomPluginStepHandler>();

    private UserJourneyBuilder Add<THandler>() where THandler : class, IStepHandler
    {
        // Singleton: handlers resolve all dependencies via
        // context.ServiceProvider inside ExecuteAsync, so they're stateless
        // dispatchers. Scoped resolution still works through that path.
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStepHandler, THandler>());
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

        // Singleton cache so client_credentials tokens are shared across
        // step executions within the same host process. Reuses the
        // JourneyApiCall named client for outbound token fetches.
        Services.AddSingleton<IClientCredentialsTokenCache, ClientCredentialsTokenCache>();
    }

    /// <summary>
    /// Add all built-in authentication step handlers (UI steps)
    /// </summary>
    public UserJourneyBuilder AddBuiltInSteps()
    {
        AddLocalLogin();
        AddCompositeLogin();
        AddSignUp();
        AddMfa();
        AddConsent();
        AddPasswordReset();
        AddPasswordChange();
        AddExternalLogin();
        AddPasswordlessEmail();
        AddPasswordlessSms();
        AddEmailVerification();
        AddDynamicForm();
        AddCustomPage();
        AddLinkAccount();
        AddTermsAcceptance();
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
        AddCreateUser();
        AddUpdateUser();
        AddAssignRoles();
        AddCustomPluginDispatcher();
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

}
