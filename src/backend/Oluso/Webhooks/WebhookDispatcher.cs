using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Events;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oluso.Webhooks;

/// <summary>
/// Dispatches webhooks to subscribed endpoints with retry logic and signature verification.
/// </summary>
public class WebhookDispatcher : IWebhookDispatcher
{
    private readonly IWebhookSubscriptionStore _store;
    private readonly IWebhookEventRegistry _registry;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookDispatcher> _logger;
    private readonly WebhookOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public WebhookDispatcher(
        IWebhookSubscriptionStore store,
        IWebhookEventRegistry registry,
        HttpClient httpClient,
        ILogger<WebhookDispatcher> logger,
        IOptions<WebhookOptions> options)
    {
        _store = store;
        _registry = registry;
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<WebhookDispatchResult> DispatchAsync(
        string tenantId,
        string eventType,
        object data,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        // Validate event type
        if (!_registry.IsValidEventType(eventType))
        {
            _logger.LogWarning("Unknown webhook event type: {EventType}", eventType);
            // Still dispatch - modules may register events dynamically
        }

        var payload = new WebhookPayload
        {
            EventType = eventType,
            TenantId = tenantId,
            Data = data,
            Metadata = metadata
        };

        return await DispatchPayloadAsync(payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RetryDeliveryAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        var delivery = await _store.GetDeliveryAsync(deliveryId, cancellationToken);
        if (delivery == null)
        {
            _logger.LogWarning("Delivery {DeliveryId} not found for retry", deliveryId);
            return false;
        }

        var endpoint = await _store.GetEndpointAsync(delivery.EndpointId, cancellationToken);
        if (endpoint == null || !endpoint.Enabled)
        {
            await _store.UpdateDeliveryAsync(
                deliveryId,
                WebhookDeliveryStatus.Cancelled,
                cancellationToken: cancellationToken);
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        int? httpStatus = null;
        string? responseBody = null;
        string? errorMessage = null;
        var success = false;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(delivery.Payload!, Encoding.UTF8, "application/json")
            };

            // Parse the original payload to get event info
            var payload = JsonSerializer.Deserialize<WebhookPayload>(delivery.Payload!, JsonOptions);

            request.Headers.Add("X-Webhook-ID", deliveryId);
            request.Headers.Add("X-Webhook-Event", delivery.EventType);
            request.Headers.Add("X-Webhook-Retry", (delivery.RetryCount + 1).ToString());

            if (!string.IsNullOrEmpty(endpoint.Secret) && payload != null)
            {
                var signature = ComputeSignature(delivery.Payload!, endpoint.Secret, payload.Timestamp);
                request.Headers.Add("X-Webhook-Signature", signature);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var response = await _httpClient.SendAsync(request, cts.Token);
            httpStatus = (int)response.StatusCode;

            responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            if (responseBody.Length > 1024)
            {
                responseBody = responseBody[..1024] + "...";
            }

            success = response.IsSuccessStatusCode;

            if (!success)
            {
                errorMessage = $"HTTP {httpStatus}: {response.ReasonPhrase}";
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
        }

        var newRetryCount = delivery.RetryCount + 1;
        var status = success
            ? WebhookDeliveryStatus.Success
            : newRetryCount >= _options.MaxRetries
                ? WebhookDeliveryStatus.Exhausted
                : WebhookDeliveryStatus.Failed;

        await _store.UpdateDeliveryAsync(
            deliveryId,
            status,
            httpStatus,
            responseBody,
            cancellationToken);

        return success;
    }

    /// <inheritdoc />
    public IEnumerable<WebhookEventDefinition> GetAvailableEventTypes()
    {
        return _registry.GetAllEventDefinitions().Select(e => new WebhookEventDefinition
        {
            EventType = e.EventType,
            Category = e.Category,
            DisplayName = e.DisplayName,
            Description = e.Description,
            PayloadSchema = e.PayloadSchema,
            EnabledByDefault = e.EnabledByDefault,
            ProviderId = e.ProviderId
        });
    }

    /// <summary>
    /// Dispatch a pre-built webhook payload.
    /// </summary>
    public async Task<WebhookDispatchResult> DispatchPayloadAsync(
        WebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all endpoints subscribed to this event
            var endpoints = await _store.GetSubscribedEndpointsAsync(
                payload.TenantId,
                payload.EventType,
                cancellationToken);

            if (endpoints.Count == 0)
            {
                _logger.LogDebug(
                    "No endpoints subscribed to {EventType} for tenant {TenantId}",
                    payload.EventType, payload.TenantId);
                return WebhookDispatchResult.None;
            }

            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var deliveryIds = new List<string>();
            var successCount = 0;
            var failCount = 0;

            // Dispatch to each endpoint
            foreach (var endpoint in endpoints)
            {
                if (!endpoint.Enabled)
                    continue;

                var deliveryId = Guid.NewGuid().ToString();
                deliveryIds.Add(deliveryId);

                try
                {
                    var result = await DeliverToEndpointAsync(
                        endpoint,
                        payload,
                        payloadJson,
                        deliveryId,
                        cancellationToken);

                    if (result.success)
                        successCount++;
                    else
                        failCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to deliver webhook to {EndpointUrl} for event {EventType}",
                        endpoint.Url, payload.EventType);
                    failCount++;
                }
            }

            _logger.LogInformation(
                "Dispatched webhook {EventType} to {Success} endpoints ({Failed} failed)",
                payload.EventType, successCount, failCount);

            return new WebhookDispatchResult
            {
                EndpointsNotified = successCount,
                EndpointsFailed = failCount,
                DeliveryIds = deliveryIds.ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch webhook {EventType}", payload.EventType);
            return WebhookDispatchResult.Failed(ex.Message);
        }
    }

    private async Task<(bool success, string? error)> DeliverToEndpointAsync(
        WebhookEndpoint endpoint,
        WebhookPayload payload,
        string payloadJson,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int? httpStatus = null;
        string? responseBody = null;
        string? errorMessage = null;
        var success = false;

        try
        {
            // Create the request
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            // Add standard headers
            request.Headers.Add("X-Webhook-ID", deliveryId);
            request.Headers.Add("X-Webhook-Event", payload.EventType);
            request.Headers.Add("X-Webhook-Timestamp", payload.Timestamp.ToString("O"));

            // Add signature if we have the secret
            if (!string.IsNullOrEmpty(endpoint.Secret))
            {
                var signature = ComputeSignature(payloadJson, endpoint.Secret, payload.Timestamp);
                request.Headers.Add("X-Webhook-Signature", signature);
            }

            // Add custom headers
            if (endpoint.Headers != null)
            {
                foreach (var (key, value) in endpoint.Headers)
                {
                    if (!key.StartsWith("X-Webhook-", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            }

            // Send the request
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var response = await _httpClient.SendAsync(request, cts.Token);
            httpStatus = (int)response.StatusCode;

            // Read response (truncated)
            responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            if (responseBody.Length > 1024)
            {
                responseBody = responseBody[..1024] + "...";
            }

            success = response.IsSuccessStatusCode;

            if (!success)
            {
                errorMessage = $"HTTP {httpStatus}: {response.ReasonPhrase}";
            }
        }
        catch (TaskCanceledException)
        {
            errorMessage = "Request timed out";
        }
        catch (HttpRequestException ex)
        {
            errorMessage = $"Connection error: {ex.Message}";
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
        }

        // Record the delivery
        var delivery = new WebhookDelivery
        {
            Id = deliveryId,
            EndpointId = endpoint.Id,
            EventType = payload.EventType,
            PayloadId = payload.Id,
            Payload = payloadJson,
            Status = success
                ? WebhookDeliveryStatus.Success
                : WebhookDeliveryStatus.Failed,
            HttpStatus = httpStatus,
            ResponseBody = responseBody,
            ErrorMessage = errorMessage,
            RetryCount = 0,
            NextRetryAt = success ? null : CalculateNextRetry(0),
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
            CreatedAt = DateTime.UtcNow
        };

        await _store.RecordDeliveryAsync(endpoint.Id, delivery, cancellationToken);

        return (success, errorMessage);
    }

    public async Task<int> ProcessPendingRetriesAsync(CancellationToken cancellationToken = default)
    {
        var pendingDeliveries = await _store.GetPendingRetriesAsync(
            _options.MaxRetries,
            _options.RetryBatchSize,
            cancellationToken);

        var retried = 0;

        foreach (var delivery in pendingDeliveries)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await RetryDeliveryAsync(delivery.Id, cancellationToken);
                retried++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying delivery {DeliveryId}", delivery.Id);
            }
        }

        if (retried > 0)
        {
            _logger.LogInformation("Processed {Count} webhook retries", retried);
        }

        return retried;
    }

    private static string ComputeSignature(string payload, string secret, DateTime timestamp)
    {
        var signedPayload = $"{timestamp:O}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private DateTime CalculateNextRetry(int retryCount)
    {
        // Exponential backoff: 1min, 5min, 30min, 2hr, 8hr
        var delayMinutes = retryCount switch
        {
            0 => 1,
            1 => 5,
            2 => 30,
            3 => 120,
            _ => 480
        };

        return DateTime.UtcNow.AddMinutes(delayMinutes);
    }
}
