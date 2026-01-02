using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Events;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Fido2.Configuration;
using Oluso.Enterprise.Fido2.Controllers;
using Oluso.Enterprise.Fido2.Services;
using Oluso.Enterprise.Fido2.Steps;

namespace Oluso.Enterprise.Fido2;

/// <summary>
/// Extension methods for adding FIDO2/WebAuthn Passkey support to Oluso
/// </summary>
public static class Fido2Extensions
{
    /// <summary>
    /// Adds FIDO2/WebAuthn Passkey support.
    /// This is a licensed enterprise add-on feature.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddMultiTenancy()
    ///     .AddUserJourneyEngine()
    ///     .AddFido2();
    /// </code>
    /// </example>
    public static OlusoBuilder AddFido2(this OlusoBuilder builder)
    {
        var section = builder.Configuration.GetSection(Fido2Options.SectionName);
        builder.Services.Configure<Fido2Options>(section);

        return builder.AddFido2Internal();
    }

    /// <summary>
    /// Adds FIDO2/WebAuthn Passkey support with custom configuration.
    /// This is a licensed enterprise add-on feature.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddFido2(options =>
    ///     {
    ///         options.RelyingPartyId = "example.com";
    ///         options.RelyingPartyName = "Example App";
    ///         options.Origins.Add("https://example.com");
    ///     });
    /// </code>
    /// </example>
    public static OlusoBuilder AddFido2(
        this OlusoBuilder builder,
        Action<Fido2Options> configure)
    {
        var options = new Fido2Options();
        configure(options);
        builder.Services.Configure<Fido2Options>(opt =>
        {
            opt.RelyingPartyId = options.RelyingPartyId;
            opt.RelyingPartyName = options.RelyingPartyName;
            opt.RelyingPartyIcon = options.RelyingPartyIcon;
            opt.Origins = options.Origins;
            opt.Timeout = options.Timeout;
            opt.AttestationConveyancePreference = options.AttestationConveyancePreference;
            opt.UserVerificationRequirement = options.UserVerificationRequirement;
            opt.AuthenticatorAttachment = options.AuthenticatorAttachment;
            opt.ResidentKeyRequirement = options.ResidentKeyRequirement;
            opt.StoreAttestationData = options.StoreAttestationData;
            opt.MaxCredentialsPerUser = options.MaxCredentialsPerUser;
            opt.MetadataService = options.MetadataService;
        });

        return builder.AddFido2Internal();
    }

    /// <summary>
    /// Adds FIDO2/WebAuthn Passkey support with fluent builder.
    /// This is a licensed enterprise add-on feature.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddFido2(fido2 => fido2
    ///         .WithRelyingParty("example.com", "Example App")
    ///         .WithOrigins("https://example.com")
    ///         .RequireUserVerification()
    ///         .PreferPlatformAuthenticator());
    /// </code>
    /// </example>
    public static OlusoBuilder AddFido2(
        this OlusoBuilder builder,
        Action<Fido2Builder> configure)
    {
        var fido2Builder = new Fido2Builder(builder.Services);
        configure(fido2Builder);
        fido2Builder.Build();

        return builder.AddFido2Internal();
    }

    private static OlusoBuilder AddFido2Internal(this OlusoBuilder builder)
    {
        // Ensure HttpContextAccessor is registered (required for origin detection)
        builder.Services.AddHttpContextAccessor();

        // Register FIDO2 authentication service (internal implementation)
        builder.Services.AddScoped<IFido2AuthenticationService, Fido2AuthenticationService>();

        // Register the adapter that implements IFido2Service for User Journey step handlers
        builder.Services.AddScoped<Oluso.Core.Services.IFido2Service, Fido2ServiceAdapter>();

        // Register step handlers for new User Journey Engine
        builder.Services.AddScoped<IStepHandler, Fido2LoginStepHandler>();
        builder.Services.AddScoped<IStepHandler, Fido2RegistrationStepHandler>();

        // Register step handler for legacy User Journey Engine
        builder.Services.AddScoped<Fido2StepHandler>();

        // Register FIDO2 webhook event provider for webhook subscriptions
        builder.Services.AddSingleton<IWebhookEventProvider, Fido2WebhookEventProvider>();

        // Add session support (required for FIDO2 registration/assertion state).
        // Multiple calls to AddSession are safe (idempotent).
        // NOTE: Host app must call app.UseSession() in the middleware pipeline.
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        return builder;
    }

    /// <summary>
    /// Gets the assembly containing FIDO2 controllers for explicit registration.
    /// Use this when you need to add controllers from this assembly explicitly.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddControllers()
    ///     .AddApplicationPart(Fido2Extensions.ControllersAssembly);
    /// </code>
    /// </example>
    public static Assembly ControllersAssembly => typeof(Fido2Controller).Assembly;
}

/// <summary>
/// Fluent builder for FIDO2 configuration
/// </summary>
public class Fido2Builder
{
    private readonly IServiceCollection _services;
    private readonly Fido2Options _options = new();

    public Fido2Builder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configure the relying party (your application)
    /// </summary>
    public Fido2Builder WithRelyingParty(string id, string name, string? icon = null)
    {
        _options.RelyingPartyId = id;
        _options.RelyingPartyName = name;
        _options.RelyingPartyIcon = icon;
        return this;
    }

    /// <summary>
    /// Configure allowed origins for WebAuthn requests
    /// </summary>
    public Fido2Builder WithOrigins(params string[] origins)
    {
        _options.Origins = new HashSet<string>(origins);
        return this;
    }

    /// <summary>
    /// Configure timeout for WebAuthn ceremonies
    /// </summary>
    public Fido2Builder WithTimeout(uint milliseconds)
    {
        _options.Timeout = milliseconds;
        return this;
    }

    /// <summary>
    /// Require user verification (biometric/PIN)
    /// </summary>
    public Fido2Builder RequireUserVerification()
    {
        _options.UserVerificationRequirement = "required";
        return this;
    }

    /// <summary>
    /// Require discoverable credentials (true passkeys)
    /// </summary>
    public Fido2Builder RequireResidentKey()
    {
        _options.ResidentKeyRequirement = "required";
        return this;
    }

    /// <summary>
    /// Prefer platform authenticators (Touch ID, Windows Hello, Face ID)
    /// </summary>
    public Fido2Builder PreferPlatformAuthenticator()
    {
        _options.AuthenticatorAttachment = "platform";
        return this;
    }

    /// <summary>
    /// Prefer cross-platform authenticators (security keys)
    /// </summary>
    public Fido2Builder PreferCrossPlatformAuthenticator()
    {
        _options.AuthenticatorAttachment = "cross-platform";
        return this;
    }

    /// <summary>
    /// Configure attestation preference
    /// </summary>
    public Fido2Builder WithAttestation(string preference)
    {
        _options.AttestationConveyancePreference = preference;
        return this;
    }

    /// <summary>
    /// Store attestation data for enterprise scenarios
    /// </summary>
    public Fido2Builder StoreAttestationData(bool store = true)
    {
        _options.StoreAttestationData = store;
        return this;
    }

    /// <summary>
    /// Configure maximum credentials per user
    /// </summary>
    public Fido2Builder WithMaxCredentialsPerUser(int max)
    {
        _options.MaxCredentialsPerUser = max;
        return this;
    }

    internal IServiceCollection Build()
    {
        _services.Configure<Fido2Options>(opt =>
        {
            opt.RelyingPartyId = _options.RelyingPartyId;
            opt.RelyingPartyName = _options.RelyingPartyName;
            opt.RelyingPartyIcon = _options.RelyingPartyIcon;
            opt.Origins = _options.Origins;
            opt.Timeout = _options.Timeout;
            opt.AttestationConveyancePreference = _options.AttestationConveyancePreference;
            opt.UserVerificationRequirement = _options.UserVerificationRequirement;
            opt.AuthenticatorAttachment = _options.AuthenticatorAttachment;
            opt.ResidentKeyRequirement = _options.ResidentKeyRequirement;
            opt.StoreAttestationData = _options.StoreAttestationData;
            opt.MaxCredentialsPerUser = _options.MaxCredentialsPerUser;
            opt.MetadataService = _options.MetadataService;
        });

        return _services;
    }
}
