using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;
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
public class WebhookStepHandler : IStepHandler
{
    public string StepType => "webhook";

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
        var waitForResponse = context.GetConfig("waitForResponse", false);
        var timeout = context.GetConfig("timeout", 30);
        var payloadTemplate = context.GetConfig<Dictionary<string, object>?>("payload", null);

        // Build payload
        var payload = BuildPayload(context, payloadTemplate);

        try
        {
            using var client = httpClientFactory.CreateClient("JourneyWebhook");
            client.Timeout = TimeSpan.FromSeconds(timeout);

            var request = new HttpRequestMessage(HttpMethod.Post, url);

            // Add headers
            foreach (var (key, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(key, SubstitutePlaceholders(value, context));
            }

            // Add payload
            var jsonPayload = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            logger.LogDebug("Sending webhook to {Url}", url);

            if (waitForResponse)
            {
                var response = await client.SendAsync(request, cancellationToken);
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
                if (responseMapping.Count > 0 && !string.IsNullOrEmpty(responseBody))
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
                // Fire and forget
                _ = client.SendAsync(request, CancellationToken.None);
                logger.LogDebug("Webhook dispatched (fire-and-forget) to {Url}", url);

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

    private object BuildPayload(StepExecutionContext context, Dictionary<string, object>? template)
    {
        if (template == null)
        {
            // Build default payload from journey context
            return new
            {
                journey_id = context.JourneyId,
                user_id = context.UserId,
                timestamp = DateTime.UtcNow,
                data = context.JourneyData.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value)
            };
        }

        // Process template with placeholders
        return ProcessPayloadTemplate(template, context);
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

        // Data: {data:key}
        foreach (var kvp in context.JourneyData)
        {
            result = result.Replace($"{{data:{kvp.Key}}}", kvp.Value?.ToString() ?? "");
        }

        // User input: {input:key}
        foreach (var kvp in context.UserInput)
        {
            result = result.Replace($"{{input:{kvp.Key}}}", kvp.Value?.ToString() ?? "");
        }

        // User/journey: {user:property}, {journey:property}
        result = result.Replace("{user:id}", context.UserId ?? "");
        result = result.Replace("{journey:id}", context.JourneyId ?? "");

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

            if (!current.TryGetProperty(part, out current))
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
