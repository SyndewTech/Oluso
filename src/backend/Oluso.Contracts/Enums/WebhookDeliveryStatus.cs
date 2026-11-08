using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Interfaces;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WebhookDeliveryStatus
{
    Pending,
    Success,
    Failed,
    Retrying
}
