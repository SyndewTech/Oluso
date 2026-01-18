namespace Oluso.Core.Domain.Entities;

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
