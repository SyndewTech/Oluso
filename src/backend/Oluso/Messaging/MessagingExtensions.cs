using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Integrations;
using Oluso.Core.Services;

namespace Oluso.Messaging;

/// <summary>
/// Extension methods for registering messaging services
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Adds email sender with configuration from appsettings
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    /// <example>
    /// // In appsettings.json:
    /// {
    ///   "Email": {
    ///     "Provider": "SendGrid",
    ///     "FromAddress": "noreply@oluso.io",
    ///     "FromName": "My App",
    ///     "SendGrid": {
    ///       "ApiKey": "SG.xxxxx"
    ///     }
    ///   }
    /// }
    ///
    /// // In Program.cs:
    /// services.AddEmailSender(configuration);
    /// </example>
    public static IServiceCollection AddEmailSender(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);
        services.Configure<EmailOptions>(section);

        var options = section.Get<EmailOptions>() ?? new EmailOptions();
        return services.AddEmailSenderCore(options.Provider);
    }

    /// <summary>
    /// Adds email sender with custom configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEmailSender(
        this IServiceCollection services,
        Action<EmailOptions> configure)
    {
        var options = new EmailOptions();
        configure(options);
        services.Configure(configure);

        return services.AddEmailSenderCore(options.Provider);
    }

    /// <summary>
    /// Adds a custom email sender implementation
    /// </summary>
    /// <typeparam name="TEmailSender">The email sender implementation type</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEmailSender<TEmailSender>(this IServiceCollection services)
        where TEmailSender : class, IEmailSender
    {
        services.AddHttpClient<IEmailSender, TEmailSender>(client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.TryAddScoped<IEmailSender, TEmailSender>();
        return services;
    }

    /// <summary>
    /// Adds SMS sender with configuration from appsettings
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    /// <example>
    /// // In appsettings.json:
    /// {
    ///   "Sms": {
    ///     "Provider": "Infobip",
    ///     "Infobip": {
    ///       "ApiKey": "xxxxx",
    ///       "BaseUrl": "https://api.infobip.com",
    ///       "SenderId": "MyApp"
    ///     }
    ///   }
    /// }
    ///
    /// // In Program.cs:
    /// services.AddSmsSender(configuration);
    /// </example>
    public static IServiceCollection AddSmsSender(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(SmsOptions.SectionName);
        services.Configure<SmsOptions>(section);

        var options = section.Get<SmsOptions>() ?? new SmsOptions();
        return services.AddSmsSenderCore(options.Provider);
    }

    /// <summary>
    /// Adds SMS sender with custom configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSmsSender(
        this IServiceCollection services,
        Action<SmsOptions> configure)
    {
        var options = new SmsOptions();
        configure(options);
        services.Configure(configure);

        return services.AddSmsSenderCore(options.Provider);
    }

    /// <summary>
    /// Adds a custom SMS sender implementation
    /// </summary>
    /// <typeparam name="TSmsSender">The SMS sender implementation type</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSmsSender<TSmsSender>(this IServiceCollection services)
        where TSmsSender : class, ISmsSender
    {
        services.AddHttpClient<ISmsSender, TSmsSender>(client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.TryAddScoped<ISmsSender, TSmsSender>();
        return services;
    }

    /// <summary>
    /// Adds WhatsApp sender with configuration from appsettings
    /// </summary>
    public static IServiceCollection AddWhatsAppSender(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(WhatsAppOptions.SectionName);
        services.Configure<WhatsAppOptions>(section);

        var options = section.Get<WhatsAppOptions>() ?? new WhatsAppOptions();
        if (!string.Equals(options.Provider, "Meta", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Oluso:Messaging:WhatsApp:Provider '{options.Provider}' is not supported. Only 'Meta' (WhatsApp Cloud API) is implemented.");
        }

        // Tenant credentials (access token, app secret) live in the encrypted
        // "WhatsApp" tenant section. Registering the key here means every
        // save through ITenantConfigurationService is DataProtection-wrapped.
        services.Configure<TenantConfigurationOptions>(o => o.EncryptedSections.Add(WhatsAppSettings.SectionKey));

        services.AddHttpClient<Messaging.WhatsApp.MetaGraphClient>(client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.TryAddScoped<Messaging.WhatsApp.IWhatsAppSettingsResolver, Messaging.WhatsApp.WhatsAppSettingsResolver>();
        services.TryAddScoped<Messaging.WhatsApp.IWhatsAppContactService, Messaging.WhatsApp.WhatsAppContactService>();
        services.TryAddScoped<Messaging.WhatsApp.IWhatsAppWebhookProcessor, Messaging.WhatsApp.WhatsAppWebhookProcessor>();
        services.TryAddScoped<Messaging.WhatsApp.IWhatsAppTemplateService, Messaging.WhatsApp.WhatsAppTemplateService>();
        services.TryAddScoped<IWhatsAppSender, MetaWhatsAppSender>();

        return services;
    }

    /// <summary>
    /// Adds both email and SMS senders with configuration from appsettings
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddEmailSender(configuration);
        services.AddSmsSender(configuration);

        // Per-tenant integration credential store (WhatsApp/SMS/email provider
        // configs in Tenant.Configuration). Platform infra — wired here at the
        // host, not piggybacked on a feature module.
        services.AddIntegrationTenantSettings();

        // Always register the WhatsApp sender — it resolves credentials per
        // tenant at send time (the tenant's whatsapp-meta integration config),
        // so a tenant can bring its own creds even when no global WhatsApp
        // section exists. The appsettings section is the optional fallback.
        services.AddWhatsAppSender(configuration);

        // Platform email templates that ship with Oluso. A template
        // bootstrapper, when registered, resolves every IPlatformTemplateSeeder at
        // startup and persists the templates under the "_platform" tenant.
        // Registered as a transient because it's just a stateless seed
        // catalogue — and as IEnumerable<IPlatformTemplateSeeder> so a
        // consumer can add their own seeders alongside.
        services.AddTransient<IPlatformTemplateSeeder, OlusoAuthEmailTemplates>();

        // Starter template — per-tenant scope, NOT _platform. Lands in
        // each tenant's own template table on creation via
        // TenantSeedEventSink, so tenant admins can see + duplicate it
        // without any access to platform-tenant rows.
        services.AddTransient<ICoreEmailTemplateSeeder, OlusoStarterEmailTemplates>();

        // Security notifications (module admin assigned) — per-tenant, each
        // with its auto-created workflow on the triggering event.
        services.AddTransient<ICoreEmailTemplateSeeder, OlusoSecurityEmailTemplates>();

        return services;
    }

    private static IServiceCollection AddEmailSenderCore(this IServiceCollection services, string provider)
    {
        // Register tenant-aware email sender that resolves per-tenant settings at runtime,
        // falling back to global EmailOptions when no tenant override exists.
        services.AddHttpClient<IEmailSender, TenantAwareEmailSender>(client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.TryAddScoped<IEmailSender, TenantAwareEmailSender>();

        // IEmailService is a legacy alias of IEmailSender. Some call sites
        // (e.g. PasswordlessEmailStepHandler) resolve by the alias type;
        // forward the registration so they pick up the same tenant-aware
        // sender as everyone else instead of silently getting null and
        // skipping the send.
        services.TryAddScoped<IEmailService>(sp => (IEmailService)sp.GetRequiredService<IEmailSender>());

        // Reply-to inboxes are confirmed by a magic link mailed to them.
        services.TryAddScoped<IReplyToVerificationService, ReplyToVerificationService>();
        services.AddScoped<Oluso.Core.Authentication.IActionTokenHandler, ReplyToVerificationTokenHandler>();

        // Profile-targeted send (admin test, campaign send) — same instance,
        // exposes the SendThroughProfileAsync path that refuses to fall back.
        services.TryAddScoped<IProfileEmailSender>(sp => (IProfileEmailSender)sp.GetRequiredService<IEmailSender>());

        // Per-profile credentials are stored under "SenderProfile:{Id}" section keys.
        // Register that prefix so every profile's payload is encrypted at rest.
        services.Configure<TenantConfigurationOptions>(o =>
        {
            o.EncryptedSectionPrefixes.Add(SenderCredentials.SectionKeyPrefix);
        });

        // Also register the unified IMessagingService if both email and SMS are available
        services.TryAddScoped<IMessagingService, UnifiedMessagingService>();

        return services;
    }

    private static IServiceCollection AddSmsSenderCore(this IServiceCollection services, string provider)
    {
        switch (provider.ToLowerInvariant())
        {
            case "twilio":
                services.AddHttpClient<ISmsSender, TwilioSmsSender>(client =>
                {
                    client.DefaultRequestHeaders.Add("Accept", "application/json");
                });
                services.TryAddScoped<ISmsSender, TwilioSmsSender>();
                break;

            case "infobip":
            default:
                services.AddHttpClient<ISmsSender, InfobipSmsSender>(client =>
                {
                    client.DefaultRequestHeaders.Add("Accept", "application/json");
                });
                services.TryAddScoped<ISmsSender, InfobipSmsSender>();
                break;
        }

        // ISmsService is the legacy alias of ISmsSender; forward so call
        // sites resolving the alias (e.g. PasswordlessSmsStepHandler) get
        // the same registered sender instead of silently getting null.
        services.TryAddScoped<ISmsService>(sp => (ISmsService)sp.GetRequiredService<ISmsSender>());

        // Also register the unified IMessagingService if both email and SMS are available
        services.TryAddScoped<IMessagingService, UnifiedMessagingService>();

        return services;
    }
}

/// <summary>
/// Unified messaging service that wraps IEmailSender and ISmsSender
/// </summary>
internal class UnifiedMessagingService : IMessagingService
{
    private readonly IEmailSender? _emailSender;
    private readonly ISmsSender? _smsSender;

    public UnifiedMessagingService(
        IEmailSender? emailSender = null,
        ISmsSender? smsSender = null)
    {
        _emailSender = emailSender;
        _smsSender = smsSender;
    }

    public async Task SendEmailAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (_emailSender == null)
        {
            throw new InvalidOperationException("Email sender is not configured. Call AddEmailSender() in your startup.");
        }

        await _emailSender.SendAsync(to, subject, body, cancellationToken);
    }

    public async Task SendSmsAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (_smsSender == null)
        {
            throw new InvalidOperationException("SMS sender is not configured. Call AddSmsSender() in your startup.");
        }

        await _smsSender.SendAsync(phoneNumber, message, cancellationToken);
    }
}
