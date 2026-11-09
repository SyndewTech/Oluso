using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Oluso.Functions.Common;

/// <summary>
/// Hourly trigger for the platform's signing key maintenance pass (expire,
/// rotate ahead of expiry, clean up). Key material and the vault live with the
/// platform host, so the function only calls back in:
/// <c>POST {Oluso:Functions:SigningKeyMaintenance:Url}</c> with the shared
/// callback key. Opt in per function app by configuring the URL; apps without
/// it stay quiet, so only one app needs to drive it. The platform host can also
/// run the same pass in-process (<c>OlusoOptions.RunSigningKeyMaintenanceInProcess</c>);
/// both are idempotent.
/// </summary>
public class SigningKeyMaintenanceFunction
{
    public const string UrlConfigKey = "Oluso:Functions:SigningKeyMaintenance:Url";
    public const string EnabledConfigKey = "Oluso:Functions:SigningKeyMaintenance:Enabled";
    public const string ApiKeyConfigKey = "Oluso:Functions:SigningKeyMaintenance:ApiKey";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SigningKeyMaintenanceFunction> _logger;

    public SigningKeyMaintenanceFunction(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<SigningKeyMaintenanceFunction> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [Function("SigningKeyMaintenance")]
    public async Task Run(
        [TimerTrigger("0 20 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue(EnabledConfigKey, true))
        {
            return;
        }

        var url = _configuration[UrlConfigKey];
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogDebug("Signing key maintenance not configured for this function app ({ConfigKey} missing)", UrlConfigKey);
            return;
        }

        var apiKey = _configuration[ApiKeyConfigKey]
            ?? _configuration["Oluso:Maintenance:ApiKey"]
            ?? _configuration["EventRelay:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Signing key maintenance skipped: no callback API key configured ({ConfigKey})", ApiKeyConfigKey);
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("OlusoFunctionsMaintenance");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Signing key maintenance pass triggered at {Url}; next at {NextScheduled:o}",
                    url, timer.ScheduleStatus?.Next);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Signing key maintenance callback returned {StatusCode} from {Url}: {Body}",
                    (int)response.StatusCode, url, body.Length > 500 ? body[..500] : body);
            }
        }
        catch (Exception ex)
        {
            // A failed callback must never bring down the function host; the platform's
            // in-process pass and the next tick cover it.
            _logger.LogWarning(ex, "Signing key maintenance callback to {Url} failed", url);
        }
    }
}
