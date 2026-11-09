namespace Oluso.Core.Events;

/// <summary>
/// Result of a webhook dispatch operation
/// </summary>
public class WebhookDispatchResult
{
    /// <summary>
    /// Whether any endpoints were notified successfully
    /// </summary>
    public bool Success => EndpointsNotified > 0 || (DeliveryIds.Count > 0 && string.IsNullOrEmpty(Error));

    /// <summary>
    /// Number of endpoints that were notified
    /// </summary>
    public int EndpointsNotified { get; set; }

    /// <summary>
    /// Number of endpoints that failed immediately
    /// </summary>
    public int EndpointsFailed { get; set; }

    /// <summary>
    /// Delivery IDs for tracking
    /// </summary>
    public List<string> DeliveryIds { get; set; } = new();

    /// <summary>
    /// Error message if dispatch failed entirely
    /// </summary>
    public string? Error { get; set; }

    public static WebhookDispatchResult None => new() { EndpointsNotified = 0 };

    public static WebhookDispatchResult Failed(string error) => new()
    {
        EndpointsNotified = 0,
        Error = error
    };
}

/// <summary>
/// Definition of a webhook event that can be subscribed to.
/// </summary>
public record WebhookEventDefinition
{
    /// <summary>
    /// Unique event type identifier (e.g., "user.created", "passkey.registered")
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Category for grouping events in UI (e.g., "User", "Authentication", "FIDO2")
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Human-readable name for display
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Description of when this event is triggered
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Example payload schema (JSON Schema or sample JSON)
    /// </summary>
    public string? PayloadSchema { get; init; }

    /// <summary>
    /// Typed property definitions for the event payload.
    /// Used by workflow expression builders to show available properties.
    /// </summary>
    public IReadOnlyList<EventPropertyDefinition>? Properties { get; init; }

    /// <summary>
    /// Whether this event is enabled by default for new subscriptions
    /// </summary>
    public bool EnabledByDefault { get; init; }

    /// <summary>
    /// Provider ID that owns this event
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Subscription scope. Used by the subscription API to hide platform
    /// events from tenant admins and to validate dispatch routing.
    /// Defaults to Tenant for backward compatibility.
    /// </summary>
    public EventScope Scope { get; init; } = EventScope.Tenant;

    /// <summary>
    /// The event's declared classification. <see cref="DataSensitivity.Restricted"/> events need
    /// <see cref="RequiredPermission"/> to subscribe to or automate on.
    /// </summary>
    public DataSensitivity Sensitivity { get; init; }

    /// <summary>Permission required to subscribe to, automate on, or receive the sensitive data of this event.</summary>
    public string? RequiredPermission { get; init; }

    /// <summary>Why the event's data is restricted.</summary>
    public string? ClassificationPurpose { get; init; }
}

/// <summary>
/// Defines a typed property on an event payload.
/// Used by the workflow expression builder to show available properties and their types.
/// </summary>
public record EventPropertyDefinition
{
    /// <summary>
    /// Property name as it appears in the event data (snake_case, e.g., "user_id", "email")
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Human-readable display name (e.g., "User ID", "Email Address")
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Data type: "string", "number", "boolean", "object", "array", "datetime"
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Description of what this property contains
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this property is always present on the event
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Confidential: withheld from anything that leaves the platform unless the receiving endpoint or
    /// automation is authorised for sensitive data. See <see cref="DataClassificationAttribute"/>.
    /// </summary>
    public bool Sensitive { get; init; }
}

/// <summary>
/// Webhook payload that gets sent to external subscribers.
/// This is the curated, external-facing representation of an event.
/// </summary>
public record WebhookPayload
{
    /// <summary>
    /// Unique ID for this webhook delivery
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Event type (e.g., "user.created", "auth.login_success")
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Tenant ID where the event occurred
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// API version for payload format
    /// </summary>
    public string ApiVersion { get; init; } = "2026-01-01";

    /// <summary>
    /// What caused the event: <c>kind</c> (admin, service_client, scim, journey,
    /// user, anonymous, system), <c>id</c> (user id, client id, SCIM client id,
    /// journey policy id), <c>name</c> and <c>ip_address</c>. Null only for
    /// payloads that don't come from an event, such as the endpoint test.
    /// </summary>
    public EventOrigin? Origin { get; init; }

    /// <summary>
    /// The actual event data (curated for external consumption)
    /// </summary>
    public required object Data { get; init; }

    /// <summary>
    /// Optional metadata about the event
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Properties withheld from <see cref="Data"/> because they are sensitive and the endpoint isn't
    /// authorised to receive them. Absent when nothing was withheld.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RedactedFields { get; init; }
}

/// <summary>
/// Default implementation of IWebhookEventRegistry
/// </summary>
public class WebhookEventRegistry : IWebhookEventRegistry
{
    private readonly IEnumerable<IWebhookEventProvider> _providers;
    private readonly IEnumerable<IEventPropertySource> _propertySources;
    private readonly Lazy<IReadOnlyList<WebhookEventDefinition>> _allEvents;
    private readonly Lazy<IReadOnlyDictionary<string, WebhookEventDefinition>> _eventsByType;
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<WebhookEventDefinition>>> _eventsByCategory;

    public WebhookEventRegistry(
        IEnumerable<IWebhookEventProvider> providers,
        IEnumerable<IEventPropertySource> propertySources)
    {
        _providers = providers;
        _propertySources = propertySources;

        _allEvents = new Lazy<IReadOnlyList<WebhookEventDefinition>>(() =>
        {
            // Build a merged lookup of generated property definitions by webhook event type
            var generatedProps = new Dictionary<string, EventPropertyDefinition[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in _propertySources)
            {
                foreach (var kvp in source.GetPropertiesByWebhookType())
                {
                    generatedProps.TryAdd(kvp.Key, kvp.Value);
                }
            }

            return _providers
                .SelectMany(p => p.GetEventDefinitions()
                    .Select(e =>
                    {
                        // Auto-populate Properties from generated source if not manually set
                        if (e.Properties == null &&
                            generatedProps.TryGetValue(e.EventType, out var props))
                        {
                            e = e with { Properties = props, ProviderId = p.ProviderId };
                        }
                        else
                        {
                            e = e with { ProviderId = p.ProviderId };
                        }
                        return e;
                    }))
                .ToList();
        });

        _eventsByType = new Lazy<IReadOnlyDictionary<string, WebhookEventDefinition>>(() =>
        {
            return _allEvents.Value.ToDictionary(e => e.EventType, StringComparer.OrdinalIgnoreCase);
        });

        _eventsByCategory = new Lazy<IReadOnlyDictionary<string, IReadOnlyList<WebhookEventDefinition>>>(() =>
        {
            return _allEvents.Value
                .GroupBy(e => e.Category)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<WebhookEventDefinition>)g.ToList());
        });
    }

    public IReadOnlyList<IWebhookEventProvider> GetProviders() => _providers.ToList();

    public IReadOnlyList<WebhookEventDefinition> GetAllEventDefinitions() => _allEvents.Value;

    public IReadOnlyDictionary<string, IReadOnlyList<WebhookEventDefinition>> GetEventsByCategory() => _eventsByCategory.Value;

    public WebhookEventDefinition? GetEventDefinition(string eventType)
    {
        _eventsByType.Value.TryGetValue(eventType, out var definition);
        return definition;
    }

    public bool IsValidEventType(string eventType) => _eventsByType.Value.ContainsKey(eventType);
}

/// <summary>
/// Standard webhook event categories
/// </summary>
public static class WebhookEventCategories
{
    public const string User = "User";
    public const string Authentication = "Authentication";
    public const string Client = "Client";
    public const string Security = "Security";
    public const string Tenant = "Tenant";
    public const string System = "System";
}

/// <summary>
/// Core webhook event type constants
/// </summary>
public static class CoreWebhookEvents
{
    // User events
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeleted = "user.deleted";
    public const string UserDeactivated = "user.deactivated";
    public const string UserReactivated = "user.reactivated";
    public const string UserEmailVerified = "user.email_verified";
    public const string UserPasswordChanged = "user.password_changed";
    public const string UserLockedOut = "user.locked_out";
    public const string UserRoleAssigned = "user.role_assigned";
    public const string UserRoleRemoved = "user.role_removed";

    // Authentication events
    public const string LoginSuccess = "auth.login_success";
    public const string LoginFailed = "auth.login_failed";
    public const string Logout = "auth.logout";
    public const string TokenIssued = "auth.token_issued";
    public const string TokenRevoked = "auth.token_revoked";
    public const string MfaEnabled = "auth.mfa_enabled";
    public const string MfaDisabled = "auth.mfa_disabled";

    // Security events
    public const string ConsentGranted = "security.consent_granted";
    public const string ConsentRevoked = "security.consent_revoked";
    public const string SuspiciousActivity = "security.suspicious_activity";

    // Journey/Data Collection events
    public const string SubmissionCreated = "journey.submission_created";
}

/// <summary>
/// Message for queuing webhook retries to external processors.
/// Used when implementing queue-based retry processing.
/// </summary>
public record WebhookRetryMessage
{
    /// <summary>
    /// The delivery ID to retry
    /// </summary>
    public required string DeliveryId { get; init; }

    /// <summary>
    /// When this retry was scheduled
    /// </summary>
    public DateTime ScheduledAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Tenant ID for routing/filtering
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Event type for observability
    /// </summary>
    public string? EventType { get; init; }

    /// <summary>
    /// Current retry count
    /// </summary>
    public int RetryCount { get; init; }
}

/// <summary>
/// Options for webhook retry processing
/// </summary>
public class WebhookRetryOptions
{
    /// <summary>
    /// Interval between retry processing cycles (for in-process mode)
    /// </summary>
    public TimeSpan ProcessingInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum number of retries to process per cycle
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Whether to enable the in-process retry processor.
    /// Set to false when using external queue-based processing.
    /// </summary>
    public bool EnableInProcessRetries { get; set; } = true;
}
