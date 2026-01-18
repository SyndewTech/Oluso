namespace Oluso.Core.Domain.Entities;

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
