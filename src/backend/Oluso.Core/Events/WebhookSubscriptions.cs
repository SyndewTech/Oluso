namespace Oluso.Core.Events;

/// <summary>
/// Store interface for webhook subscriptions.
/// Manages tenant webhook endpoints and event subscriptions.
/// </summary>
public interface IWebhookSubscriptionStore
{
    #region Webhook Endpoints

    /// <summary>
    /// Get all webhook endpoints for a tenant
    /// </summary>
    Task<IReadOnlyList<WebhookEndpoint>> GetEndpointsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific webhook endpoint
    /// </summary>
    Task<WebhookEndpoint?> GetEndpointAsync(
        string endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new webhook endpoint
    /// </summary>
    Task<WebhookEndpoint> CreateEndpointAsync(
        string tenantId,
        CreateWebhookEndpoint endpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a webhook endpoint
    /// </summary>
    Task<WebhookEndpoint> UpdateEndpointAsync(
        string endpointId,
        UpdateWebhookEndpoint update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a webhook endpoint
    /// </summary>
    Task DeleteEndpointAsync(
        string endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotate the secret for a webhook endpoint
    /// </summary>
    Task<string> RotateSecretAsync(
        string endpointId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Event Subscriptions

    /// <summary>
    /// Get endpoints subscribed to a specific event type for a tenant
    /// </summary>
    Task<IReadOnlyList<WebhookEndpoint>> GetSubscribedEndpointsAsync(
        string tenantId,
        string eventType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update event subscriptions for an endpoint
    /// </summary>
    Task UpdateEventSubscriptionsAsync(
        string endpointId,
        IEnumerable<string> eventTypes,
        CancellationToken cancellationToken = default);

    #endregion

    #region Delivery Logs

    /// <summary>
    /// Record a webhook delivery attempt
    /// </summary>
    Task<WebhookDelivery> RecordDeliveryAsync(
        string endpointId,
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get delivery history for an endpoint
    /// </summary>
    Task<IReadOnlyList<WebhookDelivery>> GetDeliveryHistoryAsync(
        string endpointId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific delivery by ID
    /// </summary>
    Task<WebhookDelivery?> GetDeliveryAsync(
        string deliveryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get failed deliveries that need retry
    /// </summary>
    Task<IReadOnlyList<WebhookDelivery>> GetPendingRetriesAsync(
        int maxRetries = 5,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update delivery status after retry
    /// </summary>
    Task UpdateDeliveryAsync(
        string deliveryId,
        WebhookDeliveryStatus status,
        int? httpStatus = null,
        string? responseBody = null,
        CancellationToken cancellationToken = default);

    #endregion
}

#region Models

/// <summary>
/// Webhook endpoint configuration
/// </summary>
public record WebhookEndpoint
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }

    /// <summary>
    /// Display name for this endpoint
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Description of what this webhook is used for
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The URL to send webhooks to
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Secret used to sign webhook payloads (HMAC-SHA256)
    /// Only returned when endpoint is created or secret is rotated
    /// </summary>
    public string? Secret { get; init; }

    /// <summary>
    /// Whether this endpoint is enabled
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Event types this endpoint is subscribed to
    /// </summary>
    public IReadOnlyList<string> EventTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Custom headers to include with webhook requests
    /// </summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// API version for payload format
    /// </summary>
    public string ApiVersion { get; init; } = "2024-01-01";

    /// <summary>
    /// Created timestamp
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// Statistics about this endpoint
    /// </summary>
    public WebhookEndpointStats? Stats { get; init; }
}

/// <summary>
/// Statistics for a webhook endpoint
/// </summary>
public record WebhookEndpointStats
{
    public int TotalDeliveries { get; init; }
    public int SuccessfulDeliveries { get; init; }
    public int FailedDeliveries { get; init; }
    public DateTime? LastDeliveryAt { get; init; }
    public DateTime? LastSuccessAt { get; init; }
    public DateTime? LastFailureAt { get; init; }
}

/// <summary>
/// Create a new webhook endpoint
/// </summary>
public record CreateWebhookEndpoint
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Url { get; init; }
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> EventTypes { get; init; } = Array.Empty<string>();
    public Dictionary<string, string>? Headers { get; init; }
    public string ApiVersion { get; init; } = "2024-01-01";
}

/// <summary>
/// Update a webhook endpoint
/// </summary>
public record UpdateWebhookEndpoint
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public bool? Enabled { get; init; }
    public IReadOnlyList<string>? EventTypes { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public string? ApiVersion { get; init; }
}

/// <summary>
/// Webhook delivery record
/// </summary>
public record WebhookDelivery
{
    public required string Id { get; init; }
    public required string EndpointId { get; init; }
    public required string EventType { get; init; }
    public required string PayloadId { get; init; }

    /// <summary>
    /// The webhook payload that was sent
    /// </summary>
    public string? Payload { get; init; }

    /// <summary>
    /// Delivery status
    /// </summary>
    public WebhookDeliveryStatus Status { get; init; }

    /// <summary>
    /// HTTP status code from the endpoint
    /// </summary>
    public int? HttpStatus { get; init; }

    /// <summary>
    /// Response body from the endpoint (truncated)
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>
    /// Error message if delivery failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// When the next retry is scheduled
    /// </summary>
    public DateTime? NextRetryAt { get; init; }

    /// <summary>
    /// When the delivery was first attempted
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the delivery was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds
    /// </summary>
    public int? ResponseTimeMs { get; init; }
}

/// <summary>
/// Webhook delivery status
/// </summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Pending delivery</summary>
    Pending = 0,

    /// <summary>Successfully delivered (2xx response)</summary>
    Success = 1,

    /// <summary>Failed delivery, will retry</summary>
    Failed = 2,

    /// <summary>Failed after all retries exhausted</summary>
    Exhausted = 3,

    /// <summary>Cancelled (endpoint disabled or deleted)</summary>
    Cancelled = 4
}

#endregion

/// <summary>
/// Options for webhook dispatcher
/// </summary>
public class WebhookOptions
{
    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Number of retries to process in each batch
    /// </summary>
    public int RetryBatchSize { get; set; } = 100;

    /// <summary>
    /// Whether to require HTTPS for webhook endpoints
    /// </summary>
    public bool RequireHttps { get; set; } = true;
}
