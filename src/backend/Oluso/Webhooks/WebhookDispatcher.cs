using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Events;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ITenantContextAccessor = Oluso.Core.Domain.Interfaces.ITenantContextAccessor;
using ITenantStore = Oluso.Core.Domain.Interfaces.ITenantStore;

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
    private readonly ITenantContextAccessor? _tenantAccessor;
    private readonly ITenantStore? _tenantStore;
    private readonly IWebhookRetryProcessor? _retryProcessor;
    private readonly IWebhookTokenIssuer? _tokenIssuer;

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
        IOptions<WebhookOptions> options,
        ITenantContextAccessor? tenantAccessor = null,
        ITenantStore? tenantStore = null,
        IWebhookRetryProcessor? retryProcessor = null,
        IWebhookTokenIssuer? tokenIssuer = null)
    {
        _store = store;
        _registry = registry;
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
        _tenantAccessor = tenantAccessor;
        _tenantStore = tenantStore;
        _retryProcessor = retryProcessor;
        _tokenIssuer = tokenIssuer;
    }

    /// <inheritdoc />
    public async Task<WebhookDispatchResult> DispatchAsync(
        string tenantId,
        string eventType,
        object data,
        Dictionary<string, string>? metadata = null,
        EventOrigin? origin = null,
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
            Origin = origin,
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

        var payloadJson = delivery.Payload!;
        var definition = _registry.GetEventDefinition(delivery.EventType);
        if (endpoint.SensitiveDataAuthorizedAt == null && definition.HasSensitiveData())
        {
            payloadJson = SensitiveEventData.RedactPayloadJson(
                payloadJson, key => definition.IsConfidential(key, SensitiveEventData.WebhookEnvelopeKeys));
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-Webhook-ID", deliveryId);
            request.Headers.Add("X-Webhook-Event", delivery.EventType);
            request.Headers.Add("X-Webhook-Retry", (delivery.RetryCount + 1).ToString());

            // Add HMAC-SHA256 signature using the decrypted signing secret
            if (!string.IsNullOrEmpty(endpoint.Secret))
            {
                // The receiver verifies the signature against the timestamp header,
                // so both must come from the same instant.
                var timestamp = DateTime.UtcNow;
                var signature = ComputeSignature(payloadJson, endpoint.Secret, timestamp);
                request.Headers.Add("X-Webhook-Signature", signature);
                request.Headers.Add("X-Webhook-Timestamp", timestamp.ToString("O"));
            }
            else if (endpoint.AuthType == WebhookAuthType.IssuerSigned)
            {
                request.Headers.Add("X-Webhook-Timestamp", DateTime.UtcNow.ToString("O"));
            }

            // A fresh token for every attempt, with the same jti (the delivery id).
            await AddAuthenticationAsync(request, endpoint, deliveryId, delivery.EventType, payloadJson, cancellationToken);

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

        if (!success && status == WebhookDeliveryStatus.Failed && _retryProcessor != null)
        {
            var nextRetryAt = CalculateNextRetry(newRetryCount);
            await _retryProcessor.QueueRetryAsync(deliveryId, nextRetryAt, cancellationToken);
        }

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
            // Scope assertion — defends against a raise-site mistake routing
            // a Platform-only event to a tenant scope (or vice versa). The
            // event metadata is the source of truth; mismatches log a warning
            // and we route to the correct scope when possible.
            var definition = _registry.GetEventDefinition(payload.EventType);
            if (definition != null)
            {
                var routedTenant = NormaliseRoutingTenant(payload.TenantId, definition.Scope);
                if (!string.Equals(routedTenant, payload.TenantId, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Webhook scope mismatch on {EventType}: payload TenantId '{Supplied}' does not match Scope={Scope}; routing to '{Routed}'.",
                        payload.EventType, payload.TenantId, definition.Scope, routedTenant);
                    payload = payload with { TenantId = routedTenant };
                }
            }

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
            var hasSensitiveData = definition.HasSensitiveData();
            (WebhookPayload Payload, string Json)? withheld = null;
            var deliveryIds = new List<string>();
            var successCount = 0;
            var failCount = 0;

            // Dispatch to each endpoint
            foreach (var endpoint in endpoints)
            {
                if (!endpoint.Enabled)
                    continue;

                if (IsEcho(endpoint, payload.Origin))
                {
                    _logger.LogDebug(
                        "Skipping webhook {EventType} to endpoint {EndpointId}: caused by its own service client {ClientId}",
                        payload.EventType, endpoint.Id, endpoint.ClientId);
                    continue;
                }

                var deliveryId = Guid.NewGuid().ToString();
                deliveryIds.Add(deliveryId);

                var outgoing = (Payload: payload, Json: payloadJson);
                if (hasSensitiveData && endpoint.SensitiveDataAuthorizedAt == null)
                {
                    withheld ??= Withhold(payload, definition!);
                    outgoing = withheld.Value;
                }

                try
                {
                    var result = await DeliverToEndpointAsync(
                        endpoint,
                        outgoing.Payload,
                        outgoing.Json,
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

    /// <summary>
    /// The payload without its sensitive properties, for endpoints not authorised to receive them.
    /// </summary>
    private static (WebhookPayload Payload, string Json) Withhold(WebhookPayload payload, WebhookEventDefinition definition)
    {
        var data = SensitiveEventData.Redact(
            payload.Data, key => definition.IsConfidential(key, SensitiveEventData.WebhookEnvelopeKeys), out var removed);
        var redacted = payload with { Data = data, RedactedFields = removed.Count == 0 ? null : removed };
        return (redacted, JsonSerializer.Serialize(redacted, JsonOptions));
    }

    /// <summary>
    /// The endpoint belongs to the service client that caused the event and asked
    /// not to be told about its own changes.
    /// </summary>
    public static bool IsEcho(WebhookEndpoint endpoint, EventOrigin? origin) =>
        endpoint.SkipOwnClientEvents
        && origin?.Is(EventOriginKinds.ServiceClient, endpoint.ClientId) == true;

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

            // Add HMAC-SHA256 signature using the decrypted signing secret
            if (!string.IsNullOrEmpty(endpoint.Secret))
            {
                var signature = ComputeSignature(payloadJson, endpoint.Secret, payload.Timestamp);
                request.Headers.Add("X-Webhook-Signature", signature);
            }

            await AddAuthenticationAsync(request, endpoint, deliveryId, payload.EventType, payloadJson, cancellationToken);

            // Add custom headers
            if (endpoint.Headers != null)
            {
                foreach (var (key, value) in endpoint.Headers)
                {
                    // Skip reserved headers that are set above
                    if (key.StartsWith("X-Webhook-", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    request.Headers.TryAddWithoutValidation(key, value);
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

        if (!success && delivery.NextRetryAt.HasValue && _retryProcessor != null)
        {
            await _retryProcessor.QueueRetryAsync(deliveryId, delivery.NextRetryAt.Value, cancellationToken);
        }

        return (success, errorMessage);
    }

    public async Task<int> ProcessPendingRetriesAsync(CancellationToken cancellationToken = default)
    {
        var pendingDeliveries = await _store.GetPendingRetriesAsync(
            _options.MaxRetries,
            _options.RetryBatchSize,
            cancellationToken);

        var retried = 0;
        var tenantCache = new Dictionary<string, Tenant?>(StringComparer.Ordinal);

        foreach (var delivery in pendingDeliveries)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Establish tenant scope for this delivery so audit logs and any
            // downstream writes are attributed to the delivery's tenant.
            var contextSet = false;
            if (_tenantAccessor != null && _tenantStore != null && !string.IsNullOrEmpty(delivery.TenantId))
            {
                if (!tenantCache.TryGetValue(delivery.TenantId, out var tenant))
                {
                    tenant = await _tenantStore.GetByIdAsync(delivery.TenantId);
                    tenantCache[delivery.TenantId] = tenant;
                }
                if (tenant != null)
                {
                    _tenantAccessor.SetTenant(tenant);
                    contextSet = true;
                }
            }

            try
            {
                await RetryDeliveryAsync(delivery.Id, cancellationToken);
                retried++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying delivery {DeliveryId}", delivery.Id);
            }
            finally
            {
                if (contextSet) _tenantAccessor!.ClearTenant();
            }
        }

        if (retried > 0)
        {
            _logger.LogInformation("Processed {Count} webhook retries", retried);
        }

        return retried;
    }

    /// <summary>
    /// Coerces a payload tenant to match the event's declared scope.
    /// Platform events always route to the platform tenant. Tenant events
    /// keep their tenant unless empty (then route to platform as a safe
    /// default — orphan tenant events shouldn't exist). Both keeps as-is.
    /// </summary>
    private static string NormaliseRoutingTenant(string supplied, EventScope scope)
    {
        return scope switch
        {
            EventScope.Platform => PlatformConstants.TenantId,
            EventScope.Tenant => string.IsNullOrEmpty(supplied) ? PlatformConstants.TenantId : supplied,
            _ => supplied
        };
    }

    /// <summary>
    /// The endpoint's authentication header: its credential (bearer, basic, API key), or for
    /// <see cref="WebhookAuthType.IssuerSigned"/> a token the tenant's issuer signs over this body.
    /// </summary>
    private async Task AddAuthenticationAsync(
        HttpRequestMessage request,
        WebhookEndpoint endpoint,
        string deliveryId,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        if (endpoint.AuthType == WebhookAuthType.IssuerSigned)
        {
            // Never sent unauthenticated: without a token issuer the delivery fails and is retried.
            var issuer = _tokenIssuer
                ?? throw new InvalidOperationException("Issuer-signed webhooks are not available on this host");
            // The bytes StringContent sends (UTF-8, no BOM).
            var token = await issuer.CreateTokenAsync(
                endpoint, deliveryId, eventType, Encoding.UTF8.GetBytes(payloadJson), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return;
        }

        if (string.IsNullOrEmpty(endpoint.AuthCredential))
            return;

        switch (endpoint.AuthType)
        {
            case WebhookAuthType.BearerToken:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.AuthCredential);
                break;
            case WebhookAuthType.BasicAuth:
                var basicValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(endpoint.AuthCredential));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicValue);
                break;
            case WebhookAuthType.ApiKeyHeader:
                request.Headers.Add("X-API-Key", endpoint.AuthCredential);
                break;
        }
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

    /// <summary>
    /// Compute HMAC-SHA256 signature for a webhook payload.
    /// Format: "v1={hex(HMAC-SHA256(timestamp.payload, secret))}"
    /// Receivers verify by recomputing with the same whsec_ secret.
    /// </summary>
    private static string ComputeSignature(string payload, string secret, DateTime timestamp)
    {
        var timestampStr = timestamp.ToString("O");
        var signedContent = $"{timestampStr}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedContent));
        return $"v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
