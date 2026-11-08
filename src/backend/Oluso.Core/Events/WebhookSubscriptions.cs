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
    /// Authentication type for outbound delivery
    /// </summary>
    public WebhookAuthType AuthType { get; init; }

    /// <summary>
    /// Authentication credential (API key or bearer token).
    /// Only returned when endpoint is created.
    /// </summary>
    public string? AuthCredential { get; init; }

    /// <summary>
    /// API version for payload format
    /// </summary>
    public string ApiVersion { get; init; } = "2024-01-01";

    /// <summary>
    /// The OAuth client (client_id) that owns this endpoint, when the endpoint
    /// belongs to an integration that also calls the Service API.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Don't deliver events whose origin is <see cref="ClientId"/>: the
    /// integration already knows about changes it made itself.
    /// </summary>
    public bool SkipOwnClientEvents { get; init; } = true;

    /// <summary>
    /// When the endpoint was authorised to receive sensitive event properties. Null: they are
    /// withheld from its deliveries (see <see cref="SensitiveEventData"/>).
    /// </summary>
    public DateTime? SensitiveDataAuthorizedAt { get; init; }

    /// <summary>User id of the administrator who authorised sensitive data.</summary>
    public string? SensitiveDataAuthorizedBy { get; init; }

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
    public WebhookAuthType AuthType { get; init; }
    public string? AuthCredential { get; init; }
    public string ApiVersion { get; init; } = "2024-01-01";
    public string? ClientId { get; init; }
    public bool SkipOwnClientEvents { get; init; } = true;
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
    public WebhookAuthType? AuthType { get; init; }
    public string? AuthCredential { get; init; }
    public string? ApiVersion { get; init; }

    /// <summary>Empty string unlinks the client; null leaves it unchanged.</summary>
    public string? ClientId { get; init; }
    public bool? SkipOwnClientEvents { get; init; }
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
    /// Tenant that owns this delivery — needed by background retry to set
    /// per-tenant context before re-dispatching, so audit logs and any
    /// downstream writes are correctly attributed.
    /// </summary>
    public string? TenantId { get; init; }

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
/// Authentication type for outbound webhook delivery.
/// The HMAC signature (X-Webhook-Signature) is sent for payload integrity whenever the endpoint has a secret,
/// whatever the auth type.
/// </summary>
public enum WebhookAuthType
{
    /// <summary>No additional authentication (HMAC signature only)</summary>
    None = 0,

    /// <summary>Authorization: Bearer {credential} (OAuth tokens, iak_ API keys, etc.)</summary>
    BearerToken = 1,

    /// <summary>Authorization: Basic {base64(credential)} where credential is "username:password"</summary>
    BasicAuth = 2,

    /// <summary>X-API-Key: {credential}</summary>
    ApiKeyHeader = 3,

    /// <summary>
    /// Authorization: Bearer {jwt} signed by the tenant's issuer with its current signing key (the keys at its
    /// <c>jwks_uri</c>), so the receiver needs no shared secret: <c>typ</c> <c>webhook+jwt</c>; <c>iss</c> the tenant's
    /// issuer as discovery states it, <c>aud</c> the endpoint URL, <c>sub</c> the owning client id (else the endpoint
    /// id), <c>iat</c>, <c>exp</c> (iat + 300 s), <c>jti</c> the X-Webhook-ID, <c>webhook_event</c> and
    /// <c>body_sha256</c> (base64url SHA-256 of the body bytes). Minted afresh for each attempt (same <c>jti</c>).
    /// No credential; the HMAC signature is still sent when the endpoint has a secret.
    /// </summary>
    IssuerSigned = 4
}

/// <summary>
/// Mints the bearer token of a <see cref="WebhookAuthType.IssuerSigned"/> delivery (see there for the claims).
/// </summary>
public interface IWebhookTokenIssuer
{
    /// <param name="endpoint">The endpoint delivered to (its tenant signs; its URL is the audience).</param>
    /// <param name="deliveryId">The delivery's <c>X-Webhook-ID</c>, the token's <c>jti</c>.</param>
    /// <param name="eventType">The event type, the token's <c>webhook_event</c>.</param>
    /// <param name="body">The exact request body bytes sent.</param>
    Task<string> CreateTokenAsync(
        WebhookEndpoint endpoint,
        string deliveryId,
        string eventType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);
}

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
