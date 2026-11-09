namespace Oluso.Core.Events;

/// <summary>
/// Marks an event class as a webhook event and provides metadata for the webhook system.
/// The source generator uses this to produce <see cref="WebhookEventDefinition"/> entries automatically.
/// Events with a non-null <c>WebhookEventType</c> but without this attribute get auto-derived metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class WebhookEventAttribute : Attribute
{
    /// <summary>
    /// Human-readable display name (e.g., "Record Created", "Request Submitted").
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Description of when this event is triggered.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this event is enabled by default for new webhook subscriptions.
    /// </summary>
    public bool EnabledByDefault { get; set; }

    public WebhookEventAttribute(string displayName) => DisplayName = displayName;
}

/// <summary>
/// Assembly-level attribute to configure the webhook provider metadata for events in this assembly.
/// If not specified, ProviderId and DisplayName are derived from the assembly name.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class WebhookProviderAttribute : Attribute
{
    /// <summary>
    /// Provider ID (the module's key, e.g. "core").
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Human-readable provider name (e.g., "Oluso Core").
    /// </summary>
    public string DisplayName { get; }

    public WebhookProviderAttribute(string providerId, string displayName)
    {
        ProviderId = providerId;
        DisplayName = displayName;
    }
}
