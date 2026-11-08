using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Notifications;

namespace Oluso.Notifications;

/// <summary>
/// <see cref="IPushSender"/> backed by the OneSignal REST API, targeting users by
/// external id (the Oluso user id set via OneSignal.login on the client — no
/// device-token table needed). Platform-level credentials: one OneSignal app for
/// the platform mobile app.
/// </summary>
public sealed class OneSignalPushSender : IPushSender
{
    private readonly HttpClient _http;
    private readonly OneSignalOptions _options;
    private readonly ILogger<OneSignalPushSender> _logger;

    public OneSignalPushSender(
        HttpClient http, IOptions<OneSignalOptions> options, ILogger<OneSignalPushSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public async Task SendAsync(
        string userId, string title, string body, string eventType,
        IReadOnlyDictionary<string, string>? data = null, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("OneSignal not configured (AppId/RestApiKey) — push skipped for {User}.", userId);
            return;
        }

        var payload = new
        {
            app_id = _options.AppId,
            target_channel = "push",
            include_aliases = new { external_id = new[] { userId } },
            headings = new { en = title },
            contents = new { en = body },
            data,
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_options.ApiBaseUrl.TrimEnd('/')}/notifications")
        {
            Content = JsonContent.Create(payload),
        };
        // OneSignal's current REST API uses the "Key" auth scheme with the REST API key.
        request.Headers.TryAddWithoutValidation("Authorization", $"Key {_options.RestApiKey}");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "OneSignal push failed for {User}: {Status} {Body}", userId, (int)response.StatusCode, content);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OneSignal push request errored for {User}.", userId);
        }
    }
}
