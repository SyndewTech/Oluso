using System.Net.Http.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Oluso.Functions.Common;

/// <summary>
/// Hourly heartbeat for any Functions host that references this library.
/// Logs a structured "alive" entry and, when configured, POSTs the same
/// payload to a platform heartbeat endpoint so an admin can see at a
/// glance which modules are reporting in.
/// </summary>
/// <remarks>
/// <para>
/// The TimerTrigger CRON <c>0 0 * * * *</c> fires at the top of every
/// hour. Set <c>Oluso:Functions:Heartbeat:Enabled=false</c> to opt out
/// of the timer (e.g. for local <c>func start</c> sessions).
/// </para>
/// <para>
/// When <c>Oluso:Functions:Heartbeat:Url</c> is set, the function POSTs
/// a JSON heartbeat envelope. When unset, the structured log entry is
/// the only signal — admins watching Loki / the Telemetry Logs page
/// see the heartbeat live there.
/// </para>
/// <para>
/// The optional module name (<c>Oluso:Functions:ModuleName</c>) lets a
/// single library deployed across multiple module hosts emit
/// distinct labels per module. Defaults to the entry assembly name.
/// </para>
/// </remarks>
public class HeartbeatFunction
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HeartbeatFunction> _logger;

    public HeartbeatFunction(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<HeartbeatFunction> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [Function("Heartbeat")]
    public async Task Run(
        [TimerTrigger("0 0 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var enabled = _configuration.GetValue("Oluso:Functions:Heartbeat:Enabled", true);
        if (!enabled)
        {
            return;
        }

        var module = _configuration["Oluso:Functions:ModuleName"]
            ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name
            ?? "Oluso.Functions";

        var payload = new HeartbeatPayload(
            Module: module,
            Host: Environment.MachineName,
            ProcessId: Environment.ProcessId,
            Timestamp: DateTimeOffset.UtcNow,
            Version: System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0",
            NextScheduled: timer.ScheduleStatus?.Next);

        // Always log — Loki / TelemetryLogs picks this up regardless of
        // whether a phone-home URL is configured.
        _logger.LogInformation(
            "Functions heartbeat from {Module} on {Host} (pid {Pid}); next at {NextScheduled:o}",
            payload.Module, payload.Host, payload.ProcessId, payload.NextScheduled);

        var url = _configuration["Oluso:Functions:Heartbeat:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("OlusoFunctionsHeartbeat");
            var apiKey = _configuration["Oluso:Functions:Heartbeat:ApiKey"]
                ?? _configuration["EventRelay:ApiKey"];
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload)
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken);
                _logger.LogWarning(
                    "Heartbeat POST returned {StatusCode} from {Url}: {Body}",
                    (int)response.StatusCode, url, body);
            }
        }
        catch (Exception ex)
        {
            // Heartbeat failures must never bring down the function host.
            _logger.LogWarning(ex, "Heartbeat POST to {Url} failed", url);
        }
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }

    private sealed record HeartbeatPayload(
        string Module,
        string Host,
        int ProcessId,
        DateTimeOffset Timestamp,
        string Version,
        DateTime? NextScheduled);
}
