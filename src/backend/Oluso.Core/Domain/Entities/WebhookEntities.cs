namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents a webhook endpoint configuration for a tenant
/// </summary>
public class WebhookEndpointEntity : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for this endpoint
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Description of what this webhook is used for
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The URL to send webhooks to
    /// </summary>
    public string Url { get; set; } = default!;

    /// <summary>
    /// Hashed secret for signing payloads (HMAC-SHA256)
    /// </summary>
    public string SecretHash { get; set; } = default!;

    /// <summary>
    /// Whether this endpoint is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// API version for payload format
    /// </summary>
    public string ApiVersion { get; set; } = "2024-01-01";

    /// <summary>
    /// Custom headers to include (JSON serialized)
    /// </summary>
    public string? HeadersJson { get; set; }

    /// <summary>
    /// Event subscriptions for this endpoint
    /// </summary>
    public ICollection<WebhookEventSubscriptionEntity> EventSubscriptions { get; set; } = new List<WebhookEventSubscriptionEntity>();

    /// <summary>
    /// Delivery history for this endpoint
    /// </summary>
    public ICollection<WebhookDeliveryEntity> Deliveries { get; set; } = new List<WebhookDeliveryEntity>();

    // Statistics
    public int TotalDeliveries { get; set; }
    public int SuccessfulDeliveries { get; set; }
    public int FailedDeliveries { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastFailureAt { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Represents an event subscription for a webhook endpoint
/// </summary>
public class WebhookEventSubscriptionEntity
{
    public int Id { get; set; }

    /// <summary>
    /// The endpoint this subscription belongs to
    /// </summary>
    public string EndpointId { get; set; } = default!;
    public WebhookEndpointEntity Endpoint { get; set; } = default!;

    /// <summary>
    /// The event type subscribed to (e.g., "user.created", "auth.login_success")
    /// </summary>
    public string EventType { get; set; } = default!;

    /// <summary>
    /// Whether this subscription is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When this subscription was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a webhook delivery attempt
/// </summary>
public class WebhookDeliveryEntity : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The endpoint this delivery was sent to
    /// </summary>
    public string EndpointId { get; set; } = default!;
    public WebhookEndpointEntity Endpoint { get; set; } = default!;

    /// <summary>
    /// The event type that triggered this delivery
    /// </summary>
    public string EventType { get; set; } = default!;

    /// <summary>
    /// The ID of the webhook payload
    /// </summary>
    public string PayloadId { get; set; } = default!;

    /// <summary>
    /// The JSON payload that was sent
    /// </summary>
    public string Payload { get; set; } = default!;

    /// <summary>
    /// Delivery status
    /// </summary>
    public WebhookDeliveryStatusEnum Status { get; set; } = WebhookDeliveryStatusEnum.Pending;

    /// <summary>
    /// HTTP status code from the endpoint
    /// </summary>
    public int? HttpStatus { get; set; }

    /// <summary>
    /// Response body from the endpoint (truncated)
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// Error message if delivery failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When the next retry is scheduled
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Response time in milliseconds
    /// </summary>
    public int? ResponseTimeMs { get; set; }

    /// <summary>
    /// When the delivery was first attempted
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the delivery was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Webhook delivery status
/// </summary>
public enum WebhookDeliveryStatusEnum
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
