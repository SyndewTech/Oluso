using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles CAPTCHA verification step to prevent bot/automated attacks.
/// Supports reCAPTCHA v2/v3, hCaptcha, and Cloudflare Turnstile.
/// </summary>
/// <remarks>
/// Configuration options:
/// - provider: "recaptcha" (default), "hcaptcha", "cloudflare" or "turnstile"
/// - siteKey: public site key (required)
/// - secretKey: secret key for verification (required)
/// - scoreThreshold: minimum score for reCAPTCHA v3 (default: 0.5)
/// - action: action name for reCAPTCHA v3 (default: "submit")
/// - theme: "light" or "dark" (default: "light")
/// - size: "normal" or "compact" (default: "normal")
/// - language: language code (default: "en")
/// </remarks>
public class CaptchaStepHandler : IStepHandler, IStepConfigurationTester
{
    private static readonly IReadOnlyDictionary<string, string> VerifyUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["recaptcha"] = "https://www.google.com/recaptcha/api/siteverify",
        ["hcaptcha"] = "https://hcaptcha.com/siteverify",
        ["cloudflare"] = "https://challenges.cloudflare.com/turnstile/v0/siteverify",
    };

    /// <summary>
    /// Asks the CAPTCHA provider to verify a dummy answer with the step's secret: the provider
    /// rejects the answer either way, but says separately whether the secret itself is valid.
    /// </summary>
    public async Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var provider = context.GetConfig("provider", "recaptcha")!;
        var siteKey = context.GetConfig<string?>("siteKey", null);
        var secretKey = context.GetConfig<string?>("secretKey", null);
        if (!VerifyUrls.TryGetValue(provider, out var verifyUrl))
        {
            return StepConfigurationTestResult.Failed($"Unknown CAPTCHA provider '{provider}'");
        }
        if (string.IsNullOrWhiteSpace(siteKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            return StepConfigurationTestResult.Failed("Both the site key and the secret key are required");
        }

        var httpClientFactory = context.ServiceProvider.GetService<IHttpClientFactory>();
        if (httpClientFactory == null)
        {
            return StepConfigurationTestResult.Failed("HTTP client isn't available");
        }

        using var client = httpClientFactory.CreateClient("CaptchaVerification");
        client.Timeout = TimeSpan.FromSeconds(15);
        try
        {
            using var response = await client.PostAsync(verifyUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = secretKey,
                ["response"] = "oluso-configuration-test"
            }), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return StepConfigurationTestResult.Failed($"{provider} answered {(int)response.StatusCode} {response.StatusCode}");
            }

            using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var errors = body.RootElement.TryGetProperty("error-codes", out var codes) && codes.ValueKind == System.Text.Json.JsonValueKind.Array
                ? codes.EnumerateArray().Select(c => c.GetString()).OfType<string>().ToList()
                : new List<string>();
            var details = new[] { $"Provider: {provider}", $"Site key: {siteKey}" };
            return errors.Any(e => e is "invalid-input-secret" or "missing-input-secret")
                ? StepConfigurationTestResult.Failed($"{provider} rejected the secret key", details)
                : StepConfigurationTestResult.Passed($"{provider} accepted the secret key", details);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return StepConfigurationTestResult.Failed($"Couldn't reach {provider}: {ex.Message}");
        }
    }

    public string StepType => "captcha";

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(StepConfigurationHelpers.GetString(configuration, "siteKey")))
        {
            errors.Add("CAPTCHA site key is required");
        }
        if (string.IsNullOrWhiteSpace(StepConfigurationHelpers.GetString(configuration, "secretKey")))
        {
            errors.Add("CAPTCHA secret key is required");
        }
        return Task.FromResult(errors.Count == 0
            ? StepConfigurationValidationResult.Valid()
            : StepConfigurationValidationResult.Invalid(errors));
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var httpClientFactory = context.ServiceProvider.GetService<IHttpClientFactory>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<CaptchaStepHandler>>();

        var provider = context.GetConfig("provider", "recaptcha")!.ToLower();
        var siteKey = context.GetConfig<string?>("siteKey", null);
        var secretKey = context.GetConfig<string?>("secretKey", null);
        var scoreThreshold = context.GetConfig("scoreThreshold", 0.5);
        var action = context.GetConfig("action", "submit");

        if (string.IsNullOrEmpty(siteKey) || string.IsNullOrEmpty(secretKey))
        {
            logger.LogError("CAPTCHA configuration missing: siteKey or secretKey not provided");
            return StepHandlerResult.Fail("config_error", "CAPTCHA is not configured correctly");
        }

        // Check if we have a CAPTCHA response from the form
        var captchaResponseKey = GetCaptchaResponseKey(provider);
        var captchaResponse = context.GetInput(captchaResponseKey);

        if (string.IsNullOrEmpty(captchaResponse))
        {
            // Show CAPTCHA UI
            return StepHandlerResult.ShowUi("Journey/_Captcha", new CaptchaViewModel
            {
                Provider = provider,
                SiteKey = siteKey,
                Action = action,
                Theme = context.GetConfig("theme", "light")!,
                Size = context.GetConfig("size", "normal")!,
                Language = context.GetConfig("language", "en")
            });
        }

        if (httpClientFactory == null)
        {
            logger.LogError("IHttpClientFactory not available for CAPTCHA verification");
            return StepHandlerResult.Fail("config_error", "HTTP client not available");
        }

        // Verify the CAPTCHA response with the provider
        using var client = httpClientFactory.CreateClient("CaptchaVerification");

        try
        {
            var verificationResult = provider switch
            {
                "recaptcha" => await VerifyRecaptchaAsync(client, secretKey, captchaResponse, scoreThreshold, logger, cancellationToken),
                "hcaptcha" => await VerifyHcaptchaAsync(client, secretKey, captchaResponse, logger, cancellationToken),
                "cloudflare" or "turnstile" => await VerifyTurnstileAsync(client, secretKey, captchaResponse, logger, cancellationToken),
                _ => (Success: false, Error: "Unknown CAPTCHA provider")
            };

            if (!verificationResult.Success)
            {
                logger.LogWarning("CAPTCHA verification failed: {Error}", verificationResult.Error);
                return StepHandlerResult.ShowUi("Journey/_Captcha", new CaptchaViewModel
                {
                    Provider = provider,
                    SiteKey = siteKey,
                    Action = action,
                    Theme = context.GetConfig("theme", "light")!,
                    Size = context.GetConfig("size", "normal")!,
                    Language = context.GetConfig("language", "en"),
                    ErrorMessage = "CAPTCHA verification failed. Please try again."
                });
            }

            logger.LogDebug("CAPTCHA verification successful for provider {Provider}", provider);
            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["captcha_verified"] = true,
                ["captcha_provider"] = provider
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CAPTCHA verification error for provider {Provider}", provider);
            return StepHandlerResult.Fail("captcha_error", "Failed to verify CAPTCHA. Please try again.");
        }
    }

    private static string GetCaptchaResponseKey(string provider)
    {
        return provider switch
        {
            "recaptcha" => "g-recaptcha-response",
            "hcaptcha" => "h-captcha-response",
            "cloudflare" or "turnstile" => "cf-turnstile-response",
            _ => "captcha-response"
        };
    }

    private static async Task<(bool Success, string? Error)> VerifyRecaptchaAsync(
        HttpClient client,
        string secretKey,
        string response,
        double scoreThreshold,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secretKey,
            ["response"] = response
        });

        var httpResponse = await client.PostAsync(
            VerifyUrls["recaptcha"],
            content,
            cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            return (false, $"reCAPTCHA API error: {httpResponse.StatusCode}");
        }

        var result = await httpResponse.Content.ReadFromJsonAsync<RecaptchaResponse>(cancellationToken: cancellationToken);

        if (result == null)
        {
            return (false, "Invalid response from reCAPTCHA");
        }

        if (!result.Success)
        {
            var errors = result.ErrorCodes != null ? string.Join(", ", result.ErrorCodes) : "Unknown error";
            return (false, $"reCAPTCHA verification failed: {errors}");
        }

        // For reCAPTCHA v3, check the score
        if (result.Score.HasValue && result.Score.Value < scoreThreshold)
        {
            logger.LogWarning("reCAPTCHA score {Score} below threshold {Threshold}", result.Score, scoreThreshold);
            return (false, $"reCAPTCHA score too low: {result.Score}");
        }

        return (true, null);
    }

    private static async Task<(bool Success, string? Error)> VerifyHcaptchaAsync(
        HttpClient client,
        string secretKey,
        string response,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secretKey,
            ["response"] = response
        });

        var httpResponse = await client.PostAsync(
            VerifyUrls["hcaptcha"],
            content,
            cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            return (false, $"hCaptcha API error: {httpResponse.StatusCode}");
        }

        var result = await httpResponse.Content.ReadFromJsonAsync<HcaptchaResponse>(cancellationToken: cancellationToken);

        if (result == null)
        {
            return (false, "Invalid response from hCaptcha");
        }

        if (!result.Success)
        {
            var errors = result.ErrorCodes != null ? string.Join(", ", result.ErrorCodes) : "Unknown error";
            return (false, $"hCaptcha verification failed: {errors}");
        }

        return (true, null);
    }

    private static async Task<(bool Success, string? Error)> VerifyTurnstileAsync(
        HttpClient client,
        string secretKey,
        string response,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secretKey,
            ["response"] = response
        });

        var httpResponse = await client.PostAsync(
            VerifyUrls["cloudflare"],
            content,
            cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            return (false, $"Turnstile API error: {httpResponse.StatusCode}");
        }

        var result = await httpResponse.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken: cancellationToken);

        if (result == null)
        {
            return (false, "Invalid response from Turnstile");
        }

        if (!result.Success)
        {
            var errors = result.ErrorCodes != null ? string.Join(", ", result.ErrorCodes) : "Unknown error";
            return (false, $"Turnstile verification failed: {errors}");
        }

        return (true, null);
    }
}

#region Response Models

public class CaptchaViewModel
{
    public string Provider { get; set; } = "recaptcha";
    public string SiteKey { get; set; } = null!;
    public string? Action { get; set; }
    public string Theme { get; set; } = "light";
    public string Size { get; set; } = "normal";
    public string? Language { get; set; }
    public string? ErrorMessage { get; set; }
}

internal class RecaptchaResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("challenge_ts")]
    public DateTime? ChallengeTs { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("error-codes")]
    public List<string>? ErrorCodes { get; set; }
}

internal class HcaptchaResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("challenge_ts")]
    public DateTime? ChallengeTs { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("credit")]
    public bool? Credit { get; set; }

    [JsonPropertyName("error-codes")]
    public List<string>? ErrorCodes { get; set; }
}

internal class TurnstileResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("challenge_ts")]
    public DateTime? ChallengeTs { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("error-codes")]
    public List<string>? ErrorCodes { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("cdata")]
    public string? Cdata { get; set; }
}

#endregion
