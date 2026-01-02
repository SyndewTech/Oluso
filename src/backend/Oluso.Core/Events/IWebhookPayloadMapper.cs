namespace Oluso.Core.Events;

/// <summary>
/// Mapper interface for converting OlusoEvents to WebhookPayloads.
/// Enterprise packages can implement this to provide custom payload mapping.
/// </summary>
public interface IWebhookPayloadMapper
{
    /// <summary>
    /// Check if this mapper can handle the given event
    /// </summary>
    bool CanMap(OlusoEvent evt);

    /// <summary>
    /// Map the event to a webhook-safe data object
    /// </summary>
    object MapToPayloadData(OlusoEvent evt);
}
