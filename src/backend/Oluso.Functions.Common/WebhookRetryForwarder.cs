using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Functions.Common;

/// <summary>
/// Drains the platform's webhook retry queue (<see cref="QueueName"/>) by handing each
/// message back to <c>POST api/system/maintenance/webhook-retries</c>. Webhook secrets
/// and credentials are decrypted by the platform, so a function app never sends the
/// webhook itself. A function app registers it with <see cref="AddWebhookRetryForwarding"/>
/// and calls <see cref="ForwardAsync"/> from its queue trigger; a refused or failed
/// callback throws so the queue tries the message again.
/// </summary>
public class WebhookRetryForwarder
{
    public const string QueueName = "oluso-webhook-retries";
    public const string UrlConfigKey = "Oluso:Functions:WebhookRetries:Url";
    public const string ApiKeyConfigKey = "Oluso:Functions:WebhookRetries:ApiKey";
    public const string HttpClientName = "OlusoWebhookRetries";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookRetryForwardingOptions _options;
    private readonly ILogger<WebhookRetryForwarder> _logger;

    public WebhookRetryForwarder(IHttpClientFactory httpClientFactory, IOptions<WebhookRetryForwardingOptions> options, ILogger<WebhookRetryForwarder> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ForwardAsync(string message, CancellationToken cancellationToken = default)
    {
        var url = _options.Url;
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"Webhook retries can't be forwarded: {UrlConfigKey} is not configured.");

        var apiKey = _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Webhook retries can't be forwarded: no callback key ({ApiKeyConfigKey}).");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(message, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

        using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Webhook retry callback returned {(int)response.StatusCode}: {(body.Length > 500 ? body[..500] : body)}");

        _logger.LogInformation("Webhook retry handed to the platform: {Result}", body);
    }
}

/// <summary>
/// Where <see cref="WebhookRetryForwarder"/> hands retries back. Read from
/// <see cref="WebhookRetryForwarder.UrlConfigKey"/> and <see cref="WebhookRetryForwarder.ApiKeyConfigKey"/>
/// (the key falls back to the maintenance or event relay key) unless set in code.
/// </summary>
public class WebhookRetryForwardingOptions
{
    public string? Url { get; set; }
    public string? ApiKey { get; set; }
}

public static class WebhookRetryForwardingExtensions
{
    /// <summary>
    /// Register <see cref="WebhookRetryForwarder"/> for a function app's queue trigger on
    /// <see cref="WebhookRetryForwarder.QueueName"/>.
    /// </summary>
    public static IServiceCollection AddWebhookRetryForwarding(this IServiceCollection services, Action<WebhookRetryForwardingOptions>? configure = null)
    {
        services.AddOptions<WebhookRetryForwardingOptions>().Configure<IConfiguration>((o, c) =>
        {
            o.Url = c[WebhookRetryForwarder.UrlConfigKey];
            o.ApiKey = c[WebhookRetryForwarder.ApiKeyConfigKey] ?? c["Oluso:Maintenance:ApiKey"] ?? c["EventRelay:ApiKey"];
        });
        if (configure != null) services.Configure(configure);
        services.AddHttpClient(WebhookRetryForwarder.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<WebhookRetryForwarder>();
        return services;
    }
}
