using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Oluso.Messaging.WhatsApp;

/// <summary>
/// Outcome of one Graph API call. <see cref="Error"/> carries Meta's own
/// message so operators see "(#131047) Re-engagement message" rather than a
/// bare HTTP status.
/// </summary>
public sealed record GraphResponse(
    bool Success,
    HttpStatusCode StatusCode,
    JsonDocument? Body,
    string? Error,
    int? ErrorCode,
    int? ErrorSubcode,
    bool Retryable)
{
    public static GraphResponse Ok(HttpStatusCode status, JsonDocument body) =>
        new(true, status, body, null, null, null, false);
}

/// <summary>
/// Thin, tenant-agnostic wrapper over the Meta Graph API for WhatsApp. Auth is
/// attached per request because the HttpClient is shared across tenants.
/// Transient failures (429, 5xx, connection errors) are retried with backoff,
/// honouring Retry-After; everything else returns immediately with Meta's
/// error decoded.
/// </summary>
public sealed class MetaGraphClient
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(400);

    private readonly HttpClient _http;
    private readonly ILogger<MetaGraphClient> _logger;

    /// <summary>Overridable for tests so retries don't sleep.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MetaGraphClient(HttpClient http, ILogger<MetaGraphClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<GraphResponse> SendMessageAsync(ResolvedWhatsAppSettings settings, object payload, CancellationToken cancellationToken) =>
        SendAsync(settings, HttpMethod.Post, $"{settings.ApiVersion}/{settings.PhoneNumberId}/messages", payload, cancellationToken);

    /// <summary>GET /{phone-number-id}: proves the token works for this number.</summary>
    public Task<GraphResponse> GetPhoneNumberAsync(ResolvedWhatsAppSettings settings, CancellationToken cancellationToken) =>
        SendAsync(settings, HttpMethod.Get,
            $"{settings.ApiVersion}/{settings.PhoneNumberId}?fields=display_phone_number,verified_name,quality_rating,code_verification_status",
            null, cancellationToken);

    /// <summary>GET /{waba-id}/message_templates, following paging cursors.</summary>
    public async Task<(bool Success, string? Error, List<JsonElement> Templates)> ListTemplatesAsync(
        ResolvedWhatsAppSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.BusinessAccountId))
            return (false, "Business Account ID is required to sync templates.", new List<JsonElement>());

        var results = new List<JsonElement>();
        var path = $"{settings.ApiVersion}/{settings.BusinessAccountId}/message_templates?fields=id,name,language,status,category,components&limit=100";

        for (var page = 0; page < 50 && path != null; page++)
        {
            var response = await SendAsync(settings, HttpMethod.Get, path, null, cancellationToken);
            if (!response.Success || response.Body == null)
                return (false, response.Error ?? "Template listing failed", results);

            using var body = response.Body;
            if (body.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                    results.Add(item.Clone());
            }

            path = null;
            if (body.RootElement.TryGetProperty("paging", out var paging)
                && paging.TryGetProperty("next", out var next)
                && next.ValueKind == JsonValueKind.String)
            {
                // Meta returns an absolute URL; strip the origin so it goes through the same client.
                var url = next.GetString()!;
                var idx = url.IndexOf("graph.facebook.com/", StringComparison.OrdinalIgnoreCase);
                path = idx >= 0 ? url[(idx + "graph.facebook.com/".Length)..] : null;
            }
        }

        return (true, null, results);
    }

    private async Task<GraphResponse> SendAsync(
        ResolvedWhatsAppSettings settings, HttpMethod method, string relativePath, object? payload, CancellationToken cancellationToken)
    {
        var url = $"https://graph.facebook.com/{relativePath}";
        GraphResponse? last = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (payload != null)
                    request.Content = JsonContent.Create(payload, options: JsonOptions);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _http.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text); }
                    catch (JsonException) { doc = JsonDocument.Parse("{}"); }
                    return GraphResponse.Ok(response.StatusCode, doc);
                }

                last = Decode(response, text);
                if (!last.Retryable || attempt == MaxAttempts)
                    return last;

                var wait = RetryAfter(response) ?? BaseDelay * Math.Pow(2, attempt - 1);
                _logger.LogWarning("Graph API {Status} on attempt {Attempt}; retrying in {Delay}ms", (int)response.StatusCode, attempt, wait.TotalMilliseconds);
                await Delay(wait, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                last = new GraphResponse(false, 0, null, $"Network error calling Meta: {ex.Message}", null, null, true);
                if (attempt == MaxAttempts) return last;
                await Delay(BaseDelay * Math.Pow(2, attempt - 1), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                last = new GraphResponse(false, 0, null, "Timed out calling Meta", null, null, true);
                if (attempt == MaxAttempts) return last;
                await Delay(BaseDelay * Math.Pow(2, attempt - 1), cancellationToken);
            }
        }

        return last ?? new GraphResponse(false, 0, null, "Meta call did not complete", null, null, true);
    }

    private static GraphResponse Decode(HttpResponseMessage response, string text)
    {
        int? code = null, subcode = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci)) code = ci;
                if (err.TryGetProperty("error_subcode", out var sc) && sc.TryGetInt32(out var sci)) subcode = sci;
                if (err.TryGetProperty("message", out var m)) message = m.GetString();
                if (err.TryGetProperty("error_data", out var ed) && ed.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.String)
                    message = $"{message} — {det.GetString()}";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (HTML from a proxy, empty body): keep the status text.
        }

        var status = (int)response.StatusCode;
        // 130429 = throughput limit, 131056 = pair rate limit, 80007 = app rate limit.
        var retryable = status == 429 || status >= 500 || code is 130429 or 131056 or 80007 or 4 or 17 or 32;
        var error = message != null
            ? $"Meta error {code?.ToString() ?? status.ToString()}: {message}"
            : $"Meta HTTP {status}";
        return new GraphResponse(false, response.StatusCode, null, error, code, subcode, retryable);
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header == null) return null;
        if (header.Delta.HasValue) return header.Delta.Value;
        if (header.Date.HasValue) return header.Date.Value - DateTimeOffset.UtcNow;
        return null;
    }
}
