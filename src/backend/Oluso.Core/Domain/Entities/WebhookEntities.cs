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
