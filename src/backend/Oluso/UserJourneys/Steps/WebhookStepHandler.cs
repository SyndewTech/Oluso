using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Sends webhook notifications during the journey. Useful for notifying
/// external systems about journey events.
/// </summary>
/// <remarks>
/// Configuration options:
/// - url: Webhook endpoint URL (required, supports placeholders)
/// - headers: Dictionary of headers to send
/// - payload: Custom payload template (optional, defaults to journey context)
/// - waitForResponse: Whether to wait for response (default: false)
/// - timeout: Request timeout in seconds (default: 30)
/// - failOnError: Whether to fail the journey on webhook error (default: false)
/// - responseMapping: Map response JSON paths to journey data (if waitForResponse=true)
/// </remarks>
public class WebhookStepHandler : IStepHandler, IStepConfigurationTester
{
    /// <summary>Checks the webhook's signing settings and that its URL answers (HEAD, nothing is delivered).</summary>
    public async Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var url = context.GetConfig<string?>("url", null);
        if (string.IsNullOrWhiteSpace(url))
        {
            return StepConfigurationTestResult.Failed("Webhook URL is required");
        }

        var details = new List<string> { $"POST {url}" };
        if (context.GetConfig("signPayload", false))
        {
            if (string.IsNullOrEmpty(context.GetConfig<string?>("secretKey", null)))
            {
                return StepConfigurationTestResult.Failed("Signing is on but no secret key is set", details.ToArray());
            }
            details.Add("Payloads are signed with the secret key");
        }

        if (StepTestSupport.HasPlaceholder(url))
        {
            details.Add("The URL uses journey placeholders, so it can only be called during a journey");
            return StepConfigurationTestResult.Passed("Settings look complete", details.ToArray());
        }

        var httpClientFactory = context.ServiceProvider.GetService<IHttpClientFactory>();
        if (httpClientFactory == null)
        {
            return StepConfigurationTestResult.Failed("HTTP client isn't available", details.ToArray());
        }

        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        foreach (var (key, value) in context.GetConfig<Dictionary<string, string>>("headers", new())!)
        {
            if (!StepTestSupport.HasPlaceholder(value)) request.Headers.TryAddWithoutValidation(key, value);
        }
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Min(context.GetConfig("timeout", 30), 15));
        var (ok, isChecked, message) = await StepTestSupport.ProbeAsync(client, request, "POST", cancellationToken);
        details.Add(message);
        return StepConfigurationTestResult.Of(ok,
            !ok ? "The webhook endpoint can't be reached" : isChecked ? "The webhook endpoint answered" : "The webhook endpoint is reachable, but the call couldn't be checked", details);
    }

    public string StepType => "webhook";

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var errors = new List<string>();
        var url = StepConfigurationHelpers.GetString(configuration, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            errors.Add("Webhook URL is required");
        }
        else if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
              && !url.StartsWith("{"))
        {
            errors.Add("Webhook URL must start with http:// or https:// (or be a template)");
        }

        if (configuration?.TryGetValue("payload", out var payload) == true && payload != null)
        {
            var element = payload is JsonElement json ? json : JsonSerializer.SerializeToElement(payload);
            if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
            {
                try
                {
                    element = JsonDocument.Parse(element.GetString()!).RootElement.Clone();
                }
                catch (JsonException)
                {
                    errors.Add("The payload template isn't valid JSON");
                }
            }
            if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.String))
            {
                errors.Add("The payload template must be a JSON object");
            }
        }

        return Task.FromResult(errors.Count == 0
            ? StepConfigurationValidationResult.Valid()
            : StepConfigurationValidationResult.Invalid(errors));
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var httpClientFactory = context.ServiceProvider.GetService<IHttpClientFactory>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<WebhookStepHandler>>();

        var url = context.GetConfig<string?>("url", null);
        if (string.IsNullOrEmpty(url))
        {
            return StepHandlerResult.Fail("config_error", "Webhook URL is required");
        }

        var failOnError = context.GetConfig("failOnError", false);

        if (httpClientFactory == null)
        {
            logger.LogWarning("IHttpClientFactory not available for webhook");
            return failOnError
                ? StepHandlerResult.Fail("config_error", "HTTP client not available")
                : StepHandlerResult.Success();
        }

        url = SubstitutePlaceholders(url, context);

        var headers = context.GetConfig<Dictionary<string, string>>("headers", new());
        // `async` is the legacy alias for !waitForResponse. Either explicit
        // setting wins; default keeps current behaviour (fire-and-forget).
        var waitForResponse = context.GetConfig("waitForResponse", false);
        var asyncFlag = context.GetConfig<bool?>("async", null);
        if (asyncFlag.HasValue)
        {
            waitForResponse = !asyncFlag.Value;
        }
        var timeout = context.GetConfig("timeout", 30);
        var payloadTemplate = ReadPayloadTemplate(context, logger, out var payloadError);
        if (payloadError != null)
        {
            return StepHandlerResult.Fail("webhook_payload_invalid", payloadError);
        }
        var includeUserData = context.GetConfig("includeUserData", true);
        var includeJourneyData = context.GetConfig("includeJourneyData", false);
        var signPayload = context.GetConfig("signPayload", false);
        var secretKey = context.GetConfig<string?>("secretKey", null);

        // Build payload
        var payload = await BuildPayloadAsync(context, payloadTemplate, includeUserData, includeJourneyData, cancellationToken);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);

            // Add headers
            foreach (var (key, value) in headers!)
            {
                request.Headers.TryAddWithoutValidation(key, SubstitutePlaceholders(value, context));
            }

            // Add payload
            var jsonPayload = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Optional HMAC-SHA256 signature so receivers can verify the
            // payload originated from us. Receivers should recompute the
            // signature against the raw body and reject on mismatch.
            if (signPayload)
            {
                if (string.IsNullOrEmpty(secretKey))
                {
                    logger.LogWarning("Webhook signPayload=true but secretKey is not configured — skipping signature");
                }
                else
                {
                    var signature = ComputeHmacSignature(jsonPayload, secretKey);
                    request.Headers.TryAddWithoutValidation("X-Oluso-Signature", $"sha256={signature}");
                }
            }

            logger.LogDebug("Sending webhook to {Url}", url);

            if (waitForResponse)
            {
                using var client = httpClientFactory.CreateClient("JourneyWebhook");
                client.Timeout = TimeSpan.FromSeconds(timeout);
                using var response = await client.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Webhook returned {StatusCode}: {Body}", response.StatusCode, responseBody);

                    if (failOnError)
                    {
                        return StepHandlerResult.Fail("webhook_error", $"Webhook returned {response.StatusCode}");
                    }
                }

                var outputs = new Dictionary<string, object>
                {
                    ["webhook_sent"] = true,
                    ["webhook_status"] = (int)response.StatusCode,
                    ["webhook_success"] = response.IsSuccessStatusCode
                };

                // Map response if configured
                var responseMapping = context.GetConfig<Dictionary<string, string>>("responseMapping", new());
                if (responseMapping!.Count > 0 && !string.IsNullOrEmpty(responseBody))
                {
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(responseBody);
                        foreach (var (path, dataKey) in responseMapping)
                        {
                            var value = GetJsonValue(jsonDoc.RootElement, path);
                            if (value != null)
                            {
                                outputs[dataKey] = value;
                                context.SetData(dataKey, value);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Failed to parse webhook response as JSON");
                    }
                }

                return StepHandlerResult.Success(outputs);
            }
            else
            {
                // The journey doesn't wait; the send runs on its own and reports how it went.
                _ = SendDetachedAsync(httpClientFactory, request, TimeSpan.FromSeconds(timeout), url, context.StepId, logger);
                logger.LogDebug("Webhook dispatched without waiting to {Url}", url);

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["webhook_sent"] = true
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send webhook to {Url}", url);

            if (failOnError)
            {
                return StepHandlerResult.Fail("webhook_error", $"Failed to send webhook: {ex.Message}");
            }

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["webhook_sent"] = false,
                ["webhook_error"] = ex.Message
            });
        }
    }

    /// <summary>
    /// Sends a webhook the journey doesn't wait for. It owns its client and request, so
    /// returning from the step doesn't cancel it, and failures are logged rather than lost.
    /// </summary>
    private static async Task SendDetachedAsync(
        IHttpClientFactory httpClientFactory, HttpRequestMessage request, TimeSpan timeout, string url, string stepId, ILogger logger)
    {
        try
        {
            using var client = httpClientFactory.CreateClient("JourneyWebhook");
            client.Timeout = timeout;
            using var response = await client.SendAsync(request, CancellationToken.None);
            if (response.IsSuccessStatusCode)
            {
                logger.LogDebug("Webhook from step {StepId} delivered to {Url} ({StatusCode})", stepId, url, (int)response.StatusCode);
            }
            else
            {
                logger.LogWarning("Webhook from step {StepId} to {Url} returned {StatusCode}", stepId, url, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook from step {StepId} to {Url} could not be delivered", stepId, url);
        }
        finally
        {
            request.Dispose();
        }
    }

    private async Task<object> BuildPayloadAsync(
        StepExecutionContext context,
        Dictionary<string, object>? template,
        bool includeUserData,
        bool includeJourneyData,
        CancellationToken cancellationToken)
    {
        if (template == null)
        {
            // Default system-built payload. The user/journey data toggles
            // let admins keep the body minimal when their integration only
            // cares about an event ping.
            var payload = new Dictionary<string, object?>
            {
                ["journey_id"] = context.JourneyId,
                ["user_id"] = context.UserId,
                ["tenant_id"] = context.ServiceProvider.GetService<ITenantContext>()?.TenantId,
                ["timestamp"] = DateTime.UtcNow
            };

            if (includeUserData && !string.IsNullOrEmpty(context.UserId))
            {
                var userService = context.ServiceProvider.GetService<IOlusoUserService>();
                if (userService != null)
                {
                    var user = await userService.FindByIdAsync(context.UserId, cancellationToken);
                    if (user != null)
                    {
                        payload["user"] = new
                        {
                            id = user.Id,
                            email = user.Email,
                            username = user.Username,
                            firstName = user.FirstName,
                            lastName = user.LastName,
                            displayName = user.DisplayName,
                            phoneNumber = user.PhoneNumber,
                            tenantId = user.TenantId
                        };
                    }
                }
            }

            if (includeJourneyData)
            {
                // Only the listed values: journey data also holds codes, request details and
                // internal state. Without a list, the journey's standard identity claims.
                var keys = context.GetConfig<List<string>?>("journeyDataKeys", null) is { Count: > 0 } listed
                    ? listed
                    : (context.ServiceProvider.GetService<UserJourneyOptions>() ?? new UserJourneyOptions()).EffectiveDefaultOutputClaims;
                payload["data"] = keys
                    .Where(key => context.JourneyData.TryGetValue(key, out var value) && value != null)
                    .ToDictionary(key => key, key => context.JourneyData[key]);
            }

            return payload;
        }

        // Process template with placeholders
        return ProcessPayloadTemplate(template, context);
    }

    private static string ComputeHmacSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// The payload template: a JSON object, or JSON text (as older settings forms saved it).
    /// Null when no template is set, so the default payload is sent.
    /// </summary>
    private static Dictionary<string, object>? ReadPayloadTemplate(StepExecutionContext context, ILogger logger, out string? error)
    {
        error = null;
        var setting = context.GetConfig<JsonElement?>("payload", null);
        if (setting is not { } payload || payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (payload.ValueKind == JsonValueKind.String)
        {
            var text = payload.GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                payload = JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (JsonException ex)
            {
                logger.LogError("Webhook step {StepId} payload is not valid JSON: {Error}", context.StepId, ex.Message);
                error = "The webhook payload template isn't valid JSON.";
                return null;
            }
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            logger.LogError("Webhook step {StepId} payload must be a JSON object, not {Kind}", context.StepId, payload.ValueKind);
            error = "The webhook payload template must be a JSON object.";
            return null;
        }

        return payload.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
    }

    private Dictionary<string, object> ProcessPayloadTemplate(Dictionary<string, object> template, StepExecutionContext context)
    {
        var result = new Dictionary<string, object>();

        foreach (var (key, value) in template)
        {
            if (value is string strValue)
            {
                result[key] = SubstitutePlaceholders(strValue, context);
            }
            else if (value is JsonElement jsonElement)
            {
                result[key] = ProcessJsonElement(jsonElement, context);
            }
            else if (value is Dictionary<string, object> nestedDict)
            {
                result[key] = ProcessPayloadTemplate(nestedDict, context);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    private object ProcessJsonElement(JsonElement element, StepExecutionContext context)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => SubstitutePlaceholders(element.GetString() ?? "", context),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ProcessJsonElement(p.Value, context)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(e => ProcessJsonElement(e, context))
                .ToList(),
            _ => element.Clone()
        };
    }

    private string SubstitutePlaceholders(string value, StepExecutionContext context)
    {
        var result = value;
        var tenantContext = context.ServiceProvider.GetService<ITenantContext>();

        // {claim:name} / {state:name} / {data:name} all resolve from the
        // same JourneyData bag — claims emitted by upstream steps land
        // there, which is also the journey-state bag.
        foreach (var kvp in context.JourneyData)
        {
            var raw = kvp.Value?.ToString() ?? "";
            result = result.Replace($"{{claim:{kvp.Key}}}", raw);
            result = result.Replace($"{{state:{kvp.Key}}}", raw);
            result = result.Replace($"{{data:{kvp.Key}}}", raw);
        }

        // User input: {input:key}
        foreach (var kvp in context.UserInput)
        {
            result = result.Replace($"{{input:{kvp.Key}}}", kvp.Value?.ToString() ?? "");
        }

        // Identity / context references
        result = result.Replace("{user:id}", context.UserId ?? "");
        result = result.Replace("{state:userId}", context.UserId ?? "");
        result = result.Replace("{journey:id}", context.JourneyId ?? "");
        result = result.Replace("{tenant:id}", tenantContext?.TenantId ?? "");
        result = result.Replace("{state:tenantId}", tenantContext?.TenantId ?? "");

        return result;
    }

    private string? GetJsonValue(JsonElement element, string path)
    {
        var parts = path.Split('.');
        var current = element;

        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return null;

            if (!current.TryGetPropertyIgnoreCase(part, out current))
                return null;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => current.GetRawText()
        };
    }
}
