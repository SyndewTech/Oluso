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
    /// Hashed secret (SHA-256). Used to verify the secret if presented.
    /// </summary>
    public string SecretHash { get; set; } = default!;

    /// <summary>
    /// Signing secret encrypted via IKeyEncryptionService.
    /// Decrypted at dispatch time to compute HMAC-SHA256 payload signatures.
    /// </summary>
    public string? SigningSecretEncrypted { get; set; }

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
    /// Authentication type for outbound delivery.
    /// 0 = None (HMAC only), 1 = BearerToken, 2 = BasicAuth, 3 = ApiKeyHeader (X-API-Key)
    /// </summary>
    public int AuthType { get; set; }

    /// <summary>
    /// Authentication credential (API key or bearer token) sent as Authorization header.
    /// Stored encrypted. Only used when AuthType != None.
    /// </summary>
    public string? AuthCredential { get; set; }

    /// <summary>
    /// The OAuth client (client_id) that owns this endpoint, when the endpoint
    /// belongs to an integration that also calls the Service API.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Don't deliver events whose origin is <see cref="ClientId"/>. Has no effect
    /// without a linked client.
    /// </summary>
    public bool SkipOwnClientEvents { get; set; } = true;

    /// <summary>
    /// When an administrator holding the restricted events' permissions authorised this endpoint to
    /// receive sensitive event properties. Null: sensitive properties are withheld from its deliveries.
    /// </summary>
    public DateTime? SensitiveDataAuthorizedAt { get; set; }

    /// <summary>User id of the administrator who authorised sensitive data.</summary>
    public string? SensitiveDataAuthorizedBy { get; set; }

    /// <summary>Display name of the administrator who authorised sensitive data, as it was then.</summary>
    public string? SensitiveDataAuthorizedByName { get; set; }

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
