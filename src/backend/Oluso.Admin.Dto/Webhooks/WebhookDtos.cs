using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Dto.Webhooks;

public class WebhookEventsResponse
{
    public List<WebhookEventInfo> Events { get; set; } = new();
    public List<string> Categories { get; set; } = new();
}

public class WebhookEventInfo
{
    public string EventType { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public bool EnabledByDefault { get; set; }
    public string? ProviderId { get; set; }
    public string? ProviderDisplayName { get; set; }
    public string Scope { get; set; } = "Tenant";

    /// <summary>Subscribing needs <see cref="RequiredPermission"/>; sensitive properties are withheld from unauthorised endpoints.</summary>
    public bool Restricted { get; set; }
    public string? RequiredPermission { get; set; }
    public string? ClassificationPurpose { get; set; }

    /// <summary>Payload properties withheld from endpoints not authorised for sensitive data.</summary>
    public List<string> SensitiveProperties { get; set; } = new();

    /// <summary>Whether the current administrator may subscribe to the event.</summary>
    public bool CanSubscribe { get; set; } = true;
}

public class WebhookEndpointDto
{
    public string Id { get; set; } = null!;
    public string TenantId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Url { get; set; } = null!;
    public bool Enabled { get; set; }
    public List<string> EventTypes { get; set; } = new();
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
    public string? Secret { get; set; }
    public string? ClientId { get; set; }
    public bool SkipOwnClientEvents { get; set; }
    public DateTime? SensitiveDataAuthorizedAt { get; set; }
    public string? SensitiveDataAuthorizedBy { get; set; }
    public string? SensitiveDataAuthorizedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateWebhookEndpointDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Url { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public List<string> EventTypes { get; set; } = new();
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
}

public class UpdateWebhookEndpointDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? EventTypes { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
}

public class CreateWebhookEndpointRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Url { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public List<string> EventTypes { get; set; } = new();
    public Dictionary<string, string>? Headers { get; set; }
    public int AuthType { get; set; }
    public string? AuthCredential { get; set; }
    public string? ApiVersion { get; set; }

    /// <summary>
    /// The OAuth client (client_id) of this tenant that owns the endpoint, when the
    /// receiving integration also calls the Service API.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>Don't deliver events the linked client caused itself.</summary>
    public bool SkipOwnClientEvents { get; set; } = true;

    /// <summary>
    /// Send the sensitive properties of restricted events to this endpoint. Needs the permission of
    /// every restricted event it subscribes to; recorded with who authorised it and when.
    /// </summary>
    public bool AuthorizeSensitiveData { get; set; }
}

public class UpdateWebhookEndpointRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? EventTypes { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public int? AuthType { get; set; }
    public string? AuthCredential { get; set; }
    public string? ApiVersion { get; set; }

    /// <summary>Empty string unlinks the client; null leaves it unchanged.</summary>
    public string? ClientId { get; set; }
    public bool? SkipOwnClientEvents { get; set; }

    /// <summary>True authorises sensitive data (needs the restricted events' permissions), false revokes it, null leaves it.</summary>
    public bool? AuthorizeSensitiveData { get; set; }
}

public class RotateSecretResponse
{
    public string Secret { get; set; } = null!;
}

public class TestWebhookResponse
{
    public bool Success { get; set; }
    public string? DeliveryId { get; set; }
    public string? Error { get; set; }
}

public class RetryDeliveryResponse
{
    public bool Success { get; set; }
}

public class WebhookDeliveryDto
{
    public string Id { get; set; } = null!;
    public string EndpointId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public WebhookDeliveryStatus Status { get; set; }
    public int HttpStatusCode { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}
