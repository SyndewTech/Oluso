using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
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
public class ApiCallStepHandler : IStepHandler, IStepConfigurationTester
{
    public string StepType => "api_call";

    /// <summary>
    /// Gets the step's access token (client credentials) and checks the endpoint answers,
    /// with a HEAD request so nothing is created.
    /// </summary>
    public async Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<ApiCallStepHandler>>();
        var httpClientFactory = context.ServiceProvider.GetService<IHttpClientFactory>();
        var url = context.GetConfig<string?>("url", null);
        if (string.IsNullOrWhiteSpace(url))
        {
            return StepConfigurationTestResult.Failed("API URL is required");
        }
        if (httpClientFactory == null)
        {
            return StepConfigurationTestResult.Failed("HTTP client isn't available");
        }

        var details = new List<string> { $"The step sends {context.GetConfig("method", "GET")!.ToUpperInvariant()} {url}" };
        var authConfig = context.GetConfig<ApiAuthConfig?>("authentication", null);
        var authType = authConfig?.Type?.ToLowerInvariant();

        if (authType == "client_credentials")
        {
            if (string.IsNullOrWhiteSpace(authConfig!.TokenEndpoint) || string.IsNullOrWhiteSpace(authConfig.ClientId) || string.IsNullOrWhiteSpace(authConfig.ClientSecret))
            {
                return StepConfigurationTestResult.Failed("Client credentials need a token endpoint, client ID and client secret", details.ToArray());
            }

            var cache = context.ServiceProvider.GetService<IClientCredentialsTokenCache>();
            var token = cache == null ? null : await cache.GetAccessTokenAsync(
                SubstitutePlaceholders(authConfig.TokenEndpoint, context),
                SubstitutePlaceholders(authConfig.ClientId, context),
                SubstitutePlaceholders(authConfig.ClientSecret, context),
                string.IsNullOrEmpty(authConfig.Scope) ? null : SubstitutePlaceholders(authConfig.Scope, context),
                string.IsNullOrEmpty(authConfig.Audience) ? null : SubstitutePlaceholders(authConfig.Audience, context),
                string.IsNullOrEmpty(authConfig.Resource) ? null : SubstitutePlaceholders(authConfig.Resource, context),
                cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                details.Add("Check the token endpoint, client ID, client secret and scope; the server log has the token endpoint's answer");
                return StepConfigurationTestResult.Failed($"Couldn't get an access token from {authConfig.TokenEndpoint}", details.ToArray());
            }
            details.Add($"Access token obtained from {authConfig.TokenEndpoint}");
        }
        else if (authType is "bearer" or "basic" or "apikey"
                 && new[] { authConfig!.Token, authConfig.Username, authConfig.Password, authConfig.ApiKey }.Any(StepTestSupport.HasPlaceholder))
        {
            details.Add("Credentials use journey placeholders, so they're only known during a journey");
        }

        if (StepTestSupport.HasPlaceholder(url))
        {
            details.Add("The URL uses journey placeholders, so the endpoint can only be called during a journey");
            return StepConfigurationTestResult.Passed("Settings look complete", details.ToArray());
        }

        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        foreach (var (key, value) in context.GetConfig<Dictionary<string, string>>("headers", new())!)
        {
            if (!StepTestSupport.HasPlaceholder(value)) request.Headers.TryAddWithoutValidation(key, value);
        }
        if (authConfig != null)
        {
            await AddAuthenticationAsync(request, authConfig, context, logger, cancellationToken);
        }

        using var client = httpClientFactory.CreateClient("JourneyApiCall");
        client.Timeout = TimeSpan.FromSeconds(Math.Min(context.GetConfig("timeout", 30), 15));
        var (ok, isChecked, message) = await StepTestSupport.ProbeAsync(
            client, request, context.GetConfig("method", "GET")!.ToUpperInvariant(), cancellationToken);
        details.Add(message);
        return StepConfigurationTestResult.Of(ok,
            !ok ? "The API call would fail" : isChecked ? "The API answered" : "The API is reachable, but the call couldn't be checked", details);
    }

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var url = StepConfigurationHelpers.GetString(configuration, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            errors.Add("API endpoint URL is required");
        }
        else if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
              && !url.StartsWith("{"))
        {
            errors.Add("URL must start with http:// or https:// (or be a template like {claim:endpoint})");
        }

        return Task.FromResult(StepConfigurationValidationResult.Build(errors, warnings));
    }

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

        var method = context.GetConfig("method", "GET")!.ToUpper();
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
                foreach (var (key, value) in headers!)
                {
                    request.Headers.TryAddWithoutValidation(key, SubstitutePlaceholders(value, context));
                }

                // Add authentication
                if (authConfig != null)
                {
                    await AddAuthenticationAsync(request, authConfig, context, logger, cancellationToken);
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

        var failOnError = context.GetConfig("failOnError", true);
        var includeMeta = context.GetConfig("includeResponseMeta", false);

        // No response at all (timeout, connection refused): failOnError decides, as it does
        // for an error status.
        if (response == null)
        {
            var errorMessage = lastException?.Message ?? "No response received";
            logger.LogError(lastException, "API call failed after {Attempts} attempts: {Url}", retryCount + 1, url);
            if (failOnError)
            {
                return StepHandlerResult.Fail("api_error", errorMessage);
            }
            var unreachable = new Dictionary<string, object>();
            if (includeMeta)
            {
                unreachable["_api_success"] = false;
                unreachable["_api_error"] = errorMessage;
            }
            return StepHandlerResult.Success(unreachable);
        }

        var statusCode = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("API call returned: {StatusCode} {Body}", response.StatusCode, responseBody);
        }

        // Parse and map the response when it's JSON.
        var outputs = new Dictionary<string, object>();
        JsonDocument? parsedDoc = null;
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                parsedDoc = JsonDocument.Parse(responseBody);
                outputs = MapOutputData(parsedDoc.RootElement, context, logger);
                MapAdvancedOutputData(parsedDoc.RootElement, context, outputs, logger);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "API response from {Url} is not JSON", url);
            }
        }

        if (includeMeta)
        {
            outputs["_api_status"] = statusCode;
            outputs["_api_success"] = response.IsSuccessStatusCode;
        }

        using (parsedDoc)
        {
            var body = parsedDoc?.RootElement ?? default;

            // Branch-on-response comes first, for any status: a rule like onStatus [404]
            // exists precisely to route an error. Status-only rules need no body.
            var branchRules = context.GetConfig<List<ApiBranchRule>?>("branchOnResponse", null);
            if (branchRules != null)
            {
                foreach (var rule in branchRules)
                {
                    if (string.IsNullOrEmpty(rule.BranchTo)) continue;
                    if (EvaluateBranchRule(body, statusCode, rule))
                    {
                        logger.LogDebug("API branch-on-response matched, branching to {BranchTo}", rule.BranchTo);
                        return StepHandlerResult.Branch(rule.BranchTo, outputs);
                    }
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var continueOnStatus = context.GetConfig<List<int>>("continueOnStatus", new()) ?? new();
                if (!continueOnStatus.Contains(statusCode) && failOnError)
                {
                    return StepHandlerResult.Fail("api_error", $"API returned {response.StatusCode}");
                }
            }

            // Response validation: each rule is checked against the parsed body; first
            // failure aborts the journey with the rule's errorMessage.
            var validations = context.GetConfig<List<ApiResponseValidation>?>("responseValidation", null);
            if (validations != null && validations.Count > 0)
            {
                if (parsedDoc == null)
                {
                    logger.LogWarning("API response from {Url} can't be validated: it isn't JSON", url);
                    return StepHandlerResult.Fail("api_validation_failed", "The API's response couldn't be checked: it isn't JSON.");
                }

                foreach (var rule in validations)
                {
                    var (ok, message) = EvaluateValidation(body, rule);
                    if (!ok)
                    {
                        var errorMessage = rule.ErrorMessage ?? message ?? $"Response validation failed at {rule.Path}";
                        logger.LogWarning("API response validation failed: {Message}", errorMessage);
                        return StepHandlerResult.Fail("api_validation_failed", errorMessage);
                    }
                }
            }
        }

        logger.LogDebug("API call completed, mapped {Count} data values", outputs.Count);
        return StepHandlerResult.Success(outputs);
    }

    private void MapAdvancedOutputData(JsonElement root, StepExecutionContext context, Dictionary<string, object> outputs, ILogger logger)
    {
        var advanced = context.GetConfig<Dictionary<string, ApiOutputMappingAdvanced>?>("outputMappingAdvanced", null);
        if (advanced == null || advanced.Count == 0) return;

        foreach (var (claimType, mapping) in advanced)
        {
            var raw = !string.IsNullOrEmpty(mapping.Path) ? GetJsonValue(root, mapping.Path) : null;
            if (string.IsNullOrEmpty(raw)) raw = mapping.DefaultValue;

            if (string.IsNullOrEmpty(raw))
            {
                if (mapping.Required)
                {
                    logger.LogWarning("Required outputMappingAdvanced path '{Path}' missing from response", mapping.Path);
                }
                continue;
            }

            var transformed = ApplyTransform(raw, mapping.Transform);
            outputs[claimType] = transformed;
            context.SetData(claimType, transformed);
        }
    }

    private (bool ok, string? message) EvaluateValidation(JsonElement root, ApiResponseValidation rule)
    {
        var raw = !string.IsNullOrEmpty(rule.Path) ? GetJsonValue(root, rule.Path) : null;
        var t = rule.Type?.ToLowerInvariant();

        switch (t)
        {
            case "required":
                return (!string.IsNullOrEmpty(raw), $"Missing required field at {rule.Path}");
            case "equals":
                return (string.Equals(raw, rule.ExpectedValue, StringComparison.Ordinal), $"Expected {rule.Path} == '{rule.ExpectedValue}'");
            case "notequals":
                return (!string.Equals(raw, rule.ExpectedValue, StringComparison.Ordinal), $"Expected {rule.Path} != '{rule.ExpectedValue}'");
            case "contains":
                return (raw != null && rule.ExpectedValue != null && raw.Contains(rule.ExpectedValue, StringComparison.Ordinal),
                    $"Expected {rule.Path} to contain '{rule.ExpectedValue}'");
            case "matches":
                if (string.IsNullOrEmpty(rule.Pattern)) return (true, null);
                return (raw != null && System.Text.RegularExpressions.Regex.IsMatch(raw, rule.Pattern),
                    $"Expected {rule.Path} to match pattern");
            case "in":
                return (raw != null && (rule.AllowedValues?.Contains(raw) ?? false), $"Expected {rule.Path} to be one of allowed values");
            case "notin":
                return (raw == null || !(rule.AllowedValues?.Contains(raw) ?? false), $"Expected {rule.Path} to not be in disallowed values");
            default:
                return (true, null);
        }
    }

    private bool EvaluateBranchRule(JsonElement root, int statusCode, ApiBranchRule rule)
    {
        if (rule.OnStatus != null && rule.OnStatus.Count > 0 && !rule.OnStatus.Contains(statusCode))
        {
            return false;
        }

        if (string.IsNullOrEmpty(rule.Path) || string.IsNullOrEmpty(rule.Condition))
        {
            // No body condition — match purely on status (or unconditional if neither is set)
            return rule.OnStatus == null || rule.OnStatus.Count == 0 || rule.OnStatus.Contains(statusCode);
        }

        var raw = GetJsonValue(root, rule.Path);
        return rule.Condition.ToLowerInvariant() switch
        {
            "equals" => string.Equals(raw, rule.Value, StringComparison.Ordinal),
            "notequals" => !string.Equals(raw, rule.Value, StringComparison.Ordinal),
            "exists" => raw != null,
            "notexists" => raw == null,
            "contains" => raw != null && rule.Value != null && raw.Contains(rule.Value, StringComparison.Ordinal),
            "true" => string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase),
            "false" => string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private string? BuildRequestBody(StepExecutionContext context)
    {
        // Priority 1: inputMapping — full per-field mapping with transforms,
        // defaults and required flags. Wins over the simpler templates so an
        // admin can promote from bodyTemplate without losing structure.
        var inputMapping = context.GetConfig<Dictionary<string, ApiInputMapping>?>("inputMapping", null);
        if (inputMapping != null && inputMapping.Count > 0)
        {
            var body = new Dictionary<string, object?>();
            foreach (var (field, mapping) in inputMapping)
            {
                var resolved = ResolveInputMapping(mapping, context);
                if (resolved == null && !mapping.Required) continue;
                body[field] = resolved;
            }
            return JsonSerializer.Serialize(body);
        }

        // Priority 2: Body template with placeholders
        var bodyTemplate = context.GetConfig<string?>("bodyTemplate", null);
        if (!string.IsNullOrEmpty(bodyTemplate))
        {
            return SubstitutePlaceholders(bodyTemplate, context);
        }

        // Priority 3: Static body configuration. Text anywhere in it (nested objects and
        // lists too) may use placeholders.
        var bodyConfig = context.GetConfig<Dictionary<string, object>?>("body", null);
        if (bodyConfig != null)
        {
            var processedBody = bodyConfig.ToDictionary(kv => kv.Key, kv => FillBodyValue(kv.Value, context));
            return JsonSerializer.Serialize(processedBody);
        }

        // Priority 4: Body from claims / journey data keys.
        // bodyFromClaims (schema name) and bodyFromData (legacy) are aliases.
        var bodyFromClaims = context.GetConfig<List<string>?>("bodyFromClaims", null)
            ?? context.GetConfig<List<string>?>("bodyFromData", null);
        if (bodyFromClaims != null && bodyFromClaims.Count > 0)
        {
            var body = new Dictionary<string, object?>();
            foreach (var key in bodyFromClaims)
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

    private object? FillBodyValue(object? value, StepExecutionContext context) => value switch
    {
        string text => SubstitutePlaceholders(text, context),
        JsonElement { ValueKind: JsonValueKind.String } element => SubstitutePlaceholders(element.GetString() ?? "", context),
        JsonElement { ValueKind: JsonValueKind.Object } element => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => FillBodyValue(p.Value, context)),
        JsonElement { ValueKind: JsonValueKind.Array } element => element.EnumerateArray()
            .Select(item => FillBodyValue(item, context)).ToList(),
        JsonElement element => element.Clone(),
        _ => value
    };

    private object? ResolveInputMapping(ApiInputMapping mapping, StepExecutionContext context)
    {
        string? raw;
        switch (mapping.Source?.ToLowerInvariant())
        {
            case "constant":
                raw = mapping.Value;
                break;
            case "input":
                raw = context.GetInput(mapping.From ?? string.Empty);
                break;
            case "state":
            case "claim":
            default:
                raw = context.GetData<object>(mapping.From ?? string.Empty)?.ToString();
                break;
        }

        if (string.IsNullOrEmpty(raw))
        {
            raw = mapping.DefaultValue;
        }

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return ApplyTransform(raw, mapping.Transform);
    }

    private static string ApplyTransform(string value, string? transform)
    {
        if (string.IsNullOrEmpty(transform)) return value;
        return transform.ToLowerInvariant() switch
        {
            "uppercase" => value.ToUpperInvariant(),
            "lowercase" => value.ToLowerInvariant(),
            "trim" => value.Trim(),
            "base64encode" => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)),
            "base64decode" => SafeBase64Decode(value),
            "urlencode" => Uri.EscapeDataString(value),
            "urldecode" => Uri.UnescapeDataString(value),
            _ => value
        };
    }

    private static string SafeBase64Decode(string value)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        catch { return value; }
    }

    private Dictionary<string, object> MapOutputData(JsonElement root, StepExecutionContext context, ILogger logger)
    {
        var outputs = new Dictionary<string, object>();
        var outputMapping = context.GetConfig<Dictionary<string, string>>("outputMapping", new());

        foreach (var (jsonPath, dataKey) in outputMapping!)
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
        var tenantContext = context.ServiceProvider.GetService<ITenantContext>();

        // JourneyData lookups — supports {claim:name}, {state:name},
        // {data:name}. All three are aliases: claims emitted by upstream
        // steps land in JourneyData, which is also the journey-state bag,
        // which is also where {data:} pulls from. Documenting all three
        // means admins can use whichever phrasing reads best for the
        // particular field.
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

        // Identity references — {user:id} and {state:userId} both resolve
        // to the authenticated subject; {tenant:id} surfaces the active
        // tenant for tenant-scoped integrations.
        result = result.Replace("{user:id}", context.UserId ?? "");
        result = result.Replace("{state:userId}", context.UserId ?? "");
        result = result.Replace("{tenant:id}", tenantContext?.TenantId ?? "");
        result = result.Replace("{state:tenantId}", tenantContext?.TenantId ?? "");

        return result;
    }

    private async Task AddAuthenticationAsync(
        HttpRequestMessage request,
        ApiAuthConfig config,
        StepExecutionContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        switch (config.Type?.ToLowerInvariant())
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

            case "client_credentials":
                // OAuth 2.0 client_credentials with caching. Tokens are
                // shared across step executions via the singleton cache;
                // refresh happens automatically when the cached token is
                // within 60s of expiring.
                var cache = context.ServiceProvider.GetService<IClientCredentialsTokenCache>();
                if (cache == null)
                {
                    logger.LogError("client_credentials auth requested but IClientCredentialsTokenCache is not registered");
                    break;
                }
                var ccToken = await cache.GetAccessTokenAsync(
                    SubstitutePlaceholders(config.TokenEndpoint ?? "", context),
                    SubstitutePlaceholders(config.ClientId ?? "", context),
                    SubstitutePlaceholders(config.ClientSecret ?? "", context),
                    string.IsNullOrEmpty(config.Scope) ? null : SubstitutePlaceholders(config.Scope, context),
                    string.IsNullOrEmpty(config.Audience) ? null : SubstitutePlaceholders(config.Audience, context),
                    string.IsNullOrEmpty(config.Resource) ? null : SubstitutePlaceholders(config.Resource, context),
                    cancellationToken);
                if (!string.IsNullOrEmpty(ccToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ccToken);
                }
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

                    if (!current.TryGetPropertyIgnoreCase(propertyName, out current))
                        return null;

                    if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                        return null;

                    current = current[index];
                    continue;
                }
            }

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
            JsonValueKind.Null => null,
            _ => current.GetRawText()
        };
    }
}

#region Configuration Models

public class ApiInputMapping
{
    public string? From { get; set; }
    public string? Source { get; set; }
    public string? Value { get; set; }
    public string? Transform { get; set; }
    public string? DefaultValue { get; set; }
    public bool Required { get; set; }
}

public class ApiOutputMappingAdvanced
{
    public string? Path { get; set; }
    public string? Transform { get; set; }
    public string? DefaultValue { get; set; }
    public bool Required { get; set; }
}

public class ApiResponseValidation
{
    public string? Path { get; set; }
    public string? Type { get; set; }
    public string? ExpectedValue { get; set; }
    public string? Pattern { get; set; }
    public List<string>? AllowedValues { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ApiBranchRule
{
    public List<int>? OnStatus { get; set; }
    public string? Path { get; set; }
    public string? Condition { get; set; }
    public string? Value { get; set; }
    public string? BranchTo { get; set; }
}

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

    /// <summary>
    /// OAuth 2.0 token endpoint URL (for type=client_credentials).
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// OAuth 2.0 client ID (for type=client_credentials).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// OAuth 2.0 client secret (for type=client_credentials).
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Space-separated OAuth 2.0 scopes (optional, for type=client_credentials).
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Token audience, for identity providers that read an <c>audience</c> parameter
    /// (type=client_credentials).
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// RFC 8707 resource indicator(s), space-separated: the API(s) the token is for. Oluso and
    /// standards-based identity providers read this (type=client_credentials).
    /// </summary>
    public string? Resource { get; set; }
}

#endregion
