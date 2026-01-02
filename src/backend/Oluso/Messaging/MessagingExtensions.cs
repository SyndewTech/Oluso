using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    ///     "FromAddress": "noreply@example.com",
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
        return services;
    }

    private static IServiceCollection AddEmailSenderCore(this IServiceCollection services, string provider)
    {
        // Register HTTP client for email service
        services.AddHttpClient<IEmailSender, SendGridEmailSender>(client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        switch (provider.ToLowerInvariant())
        {
            case "sendgrid":
            default:
                services.TryAddScoped<IEmailSender, SendGridEmailSender>();
                break;
        }

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
