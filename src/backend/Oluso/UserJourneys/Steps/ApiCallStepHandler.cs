using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Makes HTTP API calls during the journey. Useful for enrichment,
/// validation, or integrating with external systems.
/// </summary>
/// <remarks>
/// Configuration options:
/// - url: API endpoint URL (supports {data:key}, {input:key} placeholders)
/// - method: HTTP method (GET, POST, PUT, DELETE, PATCH)
/// - headers: Dictionary of headers to send
/// - authentication: Bearer token, basic auth, or API key config
/// - timeout: Request timeout in seconds (default: 30)
/// - body: Request body for POST/PUT/PATCH
/// - bodyTemplate: JSON template with placeholders
/// - outputMapping: Map JSON response paths to journey data
/// - failOnError: Whether to fail the journey on API error (default: true)
/// - continueOnStatus: HTTP status codes that should continue
/// - retryCount: Number of retries on failure (default: 0)
/// - retryDelayMs: Delay between retries in milliseconds (default: 1000)
/// </remarks>
public class ApiCallStepHandler : IStepHandler
{
    public string StepType => "api_call";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var httpClientFactory = context.ServiceProvider.GetService<IHttpClientFactory>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<ApiCallStepHandler>>();

        if (httpClientFactory == null)
        {
            return StepHandlerResult.Fail("config_error", "IHttpClientFactory not registered");
        }

        var url = context.GetConfig<string?>("url", null);
        if (string.IsNullOrEmpty(url))
        {
            return StepHandlerResult.Fail("config_error", "API URL is required");
        }

        // Substitute placeholders in URL
        url = SubstitutePlaceholders(url, context);

        var method = context.GetConfig("method", "GET").ToUpper();
        var timeout = context.GetConfig("timeout", 30);
        var headers = context.GetConfig<Dictionary<string, string>>("headers", new());
        var authConfig = context.GetConfig<ApiAuthConfig?>("authentication", null);
        var retryCount = context.GetConfig("retryCount", 0);
        var retryDelayMs = context.GetConfig("retryDelayMs", 1000);

        using var client = httpClientFactory.CreateClient("JourneyApiCall");
        client.Timeout = TimeSpan.FromSeconds(timeout);

        HttpResponseMessage? response = null;
        string? responseBody = null;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                var request = new HttpRequestMessage(new HttpMethod(method), url);

                // Add headers
                foreach (var (key, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(key, SubstitutePlaceholders(value, context));
                }

                // Add authentication
                if (authConfig != null)
                {
                    AddAuthentication(request, authConfig, context);
                }

                // Build request body
                if (method is "POST" or "PUT" or "PATCH")
                {
                    var body = BuildRequestBody(context);
                    if (body != null)
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    }
                }

                logger.LogDebug("Making API call (attempt {Attempt}): {Method} {Url}", attempt + 1, method, url);

                response = await client.SendAsync(request, cancellationToken);
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // Success or no more retries
                if (response.IsSuccessStatusCode || attempt == retryCount)
                    break;

                logger.LogWarning("API call attempt {Attempt} failed with {StatusCode}, retrying...", attempt + 1, response.StatusCode);
                await Task.Delay(retryDelayMs, cancellationToken);
            }
            catch (Exception ex) when (attempt < retryCount)
            {
                lastException = ex;
                logger.LogWarning(ex, "API call attempt {Attempt} failed, retrying...", attempt + 1);
                await Task.Delay(retryDelayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        if (response == null)
        {
            var errorMessage = lastException?.Message ?? "No response received";
            logger.LogError(lastException, "API call failed after {Attempts} attempts: {Url}", retryCount + 1, url);
            return StepHandlerResult.Fail("api_error", errorMessage);
        }

        // Check if this status code should continue
        var continueOnStatus = context.GetConfig<List<int>>("continueOnStatus", new());
        var statusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("API call returned: {StatusCode} {Body}", response.StatusCode, responseBody);

            if (!continueOnStatus.Contains(statusCode))
            {
                var failOnError = context.GetConfig("failOnError", true);
                if (failOnError)
                {
                    return StepHandlerResult.Fail("api_error", $"API returned {response.StatusCode}");
                }
            }
        }

        // Parse and map response
        var outputs = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(responseBody))
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(responseBody);
                outputs = MapOutputData(jsonDoc.RootElement, context, logger);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse API response as JSON");
            }
        }

        // Add response metadata if configured
        if (context.GetConfig("includeResponseMeta", false))
        {
            outputs["_api_status"] = statusCode;
            outputs["_api_success"] = response.IsSuccessStatusCode;
        }

        logger.LogDebug("API call successful, mapped {Count} data values", outputs.Count);
        return StepHandlerResult.Success(outputs);
    }

    private string? BuildRequestBody(StepExecutionContext context)
    {
        // Priority 1: Body template with placeholders
        var bodyTemplate = context.GetConfig<string?>("bodyTemplate", null);
        if (!string.IsNullOrEmpty(bodyTemplate))
        {
            return SubstitutePlaceholders(bodyTemplate, context);
        }

        // Priority 2: Static body configuration
        var bodyConfig = context.GetConfig<Dictionary<string, object>?>("body", null);
        if (bodyConfig != null)
        {
            // Process placeholders in body values
            var processedBody = new Dictionary<string, object>();
            foreach (var (key, value) in bodyConfig)
            {
                if (value is string strValue)
                {
                    processedBody[key] = SubstitutePlaceholders(strValue, context);
                }
                else
                {
                    processedBody[key] = value;
                }
            }
            return JsonSerializer.Serialize(processedBody);
        }

        // Priority 3: Body from journey data keys
        var bodyFromData = context.GetConfig<List<string>?>("bodyFromData", null);
        if (bodyFromData != null && bodyFromData.Count > 0)
        {
            var body = new Dictionary<string, object?>();
            foreach (var key in bodyFromData)
            {
                var value = context.GetData<object>(key);
                if (value != null)
                {
                    body[key] = value;
                }
            }
            return JsonSerializer.Serialize(body);
        }

        return null;
    }

    private Dictionary<string, object> MapOutputData(JsonElement root, StepExecutionContext context, ILogger logger)
    {
        var outputs = new Dictionary<string, object>();
        var outputMapping = context.GetConfig<Dictionary<string, string>>("outputMapping", new());

        foreach (var (jsonPath, dataKey) in outputMapping)
        {
            var value = GetJsonValue(root, jsonPath);
            if (value != null)
            {
                outputs[dataKey] = value;
                context.SetData(dataKey, value);
            }
        }

        return outputs;
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

        // User: {user:property}
        result = result.Replace("{user:id}", context.UserId ?? "");

        return result;
    }

    private void AddAuthentication(HttpRequestMessage request, ApiAuthConfig config, StepExecutionContext context)
    {
        switch (config.Type?.ToLower())
        {
            case "bearer":
                var token = SubstitutePlaceholders(config.Token ?? "", context);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;

            case "basic":
                var username = SubstitutePlaceholders(config.Username ?? "", context);
                var password = SubstitutePlaceholders(config.Password ?? "", context);
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                break;

            case "apikey":
                var headerName = config.HeaderName ?? "X-API-Key";
                var apiKey = SubstitutePlaceholders(config.ApiKey ?? "", context);
                request.Headers.TryAddWithoutValidation(headerName, apiKey);
                break;
        }
    }

    private string? GetJsonValue(JsonElement element, string path)
    {
        var parts = path.Split('.');
        var current = element;

        foreach (var part in parts)
        {
            // Handle array indexing like "items[0]"
            if (part.Contains('['))
            {
                var match = System.Text.RegularExpressions.Regex.Match(part, @"(\w+)\[(\d+)\]");
                if (match.Success)
                {
                    var propertyName = match.Groups[1].Value;
                    var index = int.Parse(match.Groups[2].Value);

                    if (!current.TryGetProperty(propertyName, out current))
                        return null;

                    if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                        return null;

                    current = current[index];
                    continue;
                }
            }

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
            JsonValueKind.Null => null,
            _ => current.GetRawText()
        };
    }
}

#region Configuration Models

/// <summary>
/// API authentication configuration
/// </summary>
public class ApiAuthConfig
{
    /// <summary>
    /// Authentication type: bearer, basic, apikey
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Bearer token (for type=bearer)
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Username (for type=basic)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password (for type=basic)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// API key value (for type=apikey)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Header name for API key (default: X-API-Key)
    /// </summary>
    public string? HeaderName { get; set; }
}

#endregion
