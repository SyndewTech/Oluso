using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Notifications;

namespace Oluso.Notifications;

public static class PushNotificationExtensions
{
    /// <summary>
    /// Registers OneSignal as the platform <see cref="IPushSender"/>. Push is
    /// delivered opt-in, per automation, by the workflow <c>SendPushActionHandler</c>
    /// (mirroring email/SMS) — never blanket-fired on every event. Reads the
    /// platform-level "OneSignal" config section; the sender no-ops when
    /// AppId/RestApiKey are unset.
    /// </summary>
    public static IServiceCollection AddOlusoPush(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OneSignalOptions>(configuration.GetSection("OneSignal"));

        services.AddHttpClient<IPushSender, OneSignalPushSender>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        return services;
    }
}
