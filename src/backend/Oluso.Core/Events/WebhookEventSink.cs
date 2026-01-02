using Microsoft.Extensions.Logging;

namespace Oluso.Core.Events;

/// <summary>
/// Event sink that automatically dispatches events as webhooks.
/// Only dispatches events that have a WebhookEventType defined.
/// Uses IWebhookPayloadMapper instances to convert events to external-safe payloads.
/// </summary>
public class WebhookEventSink : IOlusoEventSink
{
    private readonly IWebhookDispatcher _dispatcher;
    private readonly IEnumerable<IWebhookPayloadMapper> _mappers;
    private readonly ILogger<WebhookEventSink> _logger;

    public string Name => "Webhook";

    public WebhookEventSink(
        IWebhookDispatcher dispatcher,
        IEnumerable<IWebhookPayloadMapper> mappers,
        ILogger<WebhookEventSink> logger)
    {
        _dispatcher = dispatcher;
        _mappers = mappers;
        _logger = logger;
    }

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        // Only dispatch if the event has a webhook event type
        if (string.IsNullOrEmpty(evt.WebhookEventType))
            return;

        // Skip if no tenant context
        if (string.IsNullOrEmpty(evt.TenantId))
        {
            _logger.LogDebug(
                "Skipping webhook for {EventType} - no tenant context",
                evt.EventType);
            return;
        }

        try
        {
            // Map the event to external payload
            var payloadData = MapToPayloadData(evt);

            var result = await _dispatcher.DispatchAsync(
                evt.TenantId,
                evt.WebhookEventType,
                payloadData,
                new Dictionary<string, string>
                {
                    ["event_id"] = evt.Id,
                    ["activity_id"] = evt.ActivityId ?? ""
                },
                cancellationToken);

            if (result.Success)
            {
                _logger.LogDebug(
                    "Dispatched webhook {WebhookEventType} to {Count} endpoints",
                    evt.WebhookEventType, result.EndpointsNotified);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to dispatch webhook for event {EventType}",
                evt.EventType);
            // Don't rethrow - webhook failures shouldn't break the event flow
        }
    }

    private object MapToPayloadData(OlusoEvent evt)
    {
        // Try custom mappers first
        foreach (var mapper in _mappers)
        {
            if (mapper.CanMap(evt))
            {
                return mapper.MapToPayloadData(evt);
            }
        }

        // Fall back to default mapping
        return DefaultMapper(evt);
    }

    /// <summary>
    /// Default mapping that creates an external-safe payload from an event.
    /// Excludes internal fields and sensitive data.
    /// </summary>
    private static object DefaultMapper(OlusoEvent evt)
    {
        return evt switch
        {
            UserRegisteredEvent e => new
            {
                user_id = e.SubjectId,
                email = e.Email,
                username = e.Username,
                created_at = e.Timestamp
            },
            UserSignedInEvent e => new
            {
                user_id = e.SubjectId,
                username = e.Username,
                client_id = e.ClientId,
                authentication_method = e.AuthenticationMethod,
                timestamp = e.Timestamp
            },
            UserSignInFailedEvent e => new
            {
                username = e.Username,
                client_id = e.ClientId,
                reason = e.FailureReason,
                timestamp = e.Timestamp
            },
            UserSignedOutEvent e => new
            {
                user_id = e.SubjectId,
                timestamp = e.Timestamp
            },
            UserUpdatedEvent e => new
            {
                user_id = e.SubjectId,
                email = e.Email,
                changed_fields = e.ChangedFields?.Keys.ToList(),
                timestamp = e.Timestamp
            },
            UserDeletedEvent e => new
            {
                user_id = e.SubjectId,
                deleted_by_admin = e.DeletedByAdmin,
                timestamp = e.Timestamp
            },
            UserEmailVerifiedEvent e => new
            {
                user_id = e.SubjectId,
                email = e.Email,
                timestamp = e.Timestamp
            },
            UserPasswordChangedEvent e => new
            {
                user_id = e.SubjectId,
                changed_by_admin = e.ChangedByAdmin,
                timestamp = e.Timestamp
            },
            UserLockedOutEvent e => new
            {
                user_id = e.SubjectId,
                reason = e.Reason,
                lockout_end = e.LockoutEnd,
                timestamp = e.Timestamp
            },
            TokenIssuedEvent e => new
            {
                user_id = e.SubjectId,
                client_id = e.ClientId,
                token_type = e.TokenType,
                scopes = e.Scopes.ToList(),
                timestamp = e.Timestamp
            },
            ConsentGrantedEvent e => new
            {
                user_id = e.SubjectId,
                client_id = e.ClientId,
                scopes = e.Scopes.ToList(),
                remember = e.RememberConsent,
                timestamp = e.Timestamp
            },
            ConsentDeniedEvent e => new
            {
                user_id = e.SubjectId,
                client_id = e.ClientId,
                timestamp = e.Timestamp
            },
            // Generic fallback - just expose safe fields
            _ => new
            {
                event_type = evt.EventType,
                timestamp = evt.Timestamp
            }
        };
    }
}

/// <summary>
/// Core webhook event provider for built-in Oluso events.
/// </summary>
public class CoreWebhookEventProvider : IWebhookEventProvider
{
    public string ProviderId => "core";
    public string DisplayName => "Oluso Core";

    public IReadOnlyList<WebhookEventDefinition> GetEventDefinitions() => new List<WebhookEventDefinition>
    {
        // User events
        new()
        {
            EventType = CoreWebhookEvents.UserCreated,
            Category = WebhookEventCategories.User,
            DisplayName = "User Created",
            Description = "Triggered when a new user is registered or created",
            EnabledByDefault = true,
            PayloadSchema = """
            {
                "user_id": "string",
                "email": "string",
                "username": "string",
                "created_at": "datetime"
            }
            """
        },
        new()
        {
            EventType = CoreWebhookEvents.UserUpdated,
            Category = WebhookEventCategories.User,
            DisplayName = "User Updated",
            Description = "Triggered when a user's profile is updated",
            EnabledByDefault = true
        },
        new()
        {
            EventType = CoreWebhookEvents.UserDeleted,
            Category = WebhookEventCategories.User,
            DisplayName = "User Deleted",
            Description = "Triggered when a user account is deleted",
            EnabledByDefault = true
        },
        new()
        {
            EventType = CoreWebhookEvents.UserEmailVerified,
            Category = WebhookEventCategories.User,
            DisplayName = "Email Verified",
            Description = "Triggered when a user verifies their email address",
            EnabledByDefault = false
        },
        new()
        {
            EventType = CoreWebhookEvents.UserPasswordChanged,
            Category = WebhookEventCategories.User,
            DisplayName = "Password Changed",
            Description = "Triggered when a user changes their password",
            EnabledByDefault = false
        },
        new()
        {
            EventType = CoreWebhookEvents.UserLockedOut,
            Category = WebhookEventCategories.Security,
            DisplayName = "User Locked Out",
            Description = "Triggered when a user account is locked due to failed login attempts",
            EnabledByDefault = true
        },

        // Authentication events
        new()
        {
            EventType = CoreWebhookEvents.LoginSuccess,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Login Success",
            Description = "Triggered when a user successfully logs in",
            EnabledByDefault = false
        },
        new()
        {
            EventType = CoreWebhookEvents.LoginFailed,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Login Failed",
            Description = "Triggered when a login attempt fails",
            EnabledByDefault = false
        },
        new()
        {
            EventType = CoreWebhookEvents.Logout,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "User Logout",
            Description = "Triggered when a user logs out",
            EnabledByDefault = false
        },
        new()
        {
            EventType = CoreWebhookEvents.TokenIssued,
            Category = WebhookEventCategories.Authentication,
            DisplayName = "Token Issued",
            Description = "Triggered when an access token is issued",
            EnabledByDefault = false
        },

        // Security events
        new()
        {
            EventType = CoreWebhookEvents.ConsentGranted,
            Category = WebhookEventCategories.Security,
            DisplayName = "Consent Granted",
            Description = "Triggered when a user grants consent to a client",
            EnabledByDefault = false
        },
        new()
        {
            EventType = CoreWebhookEvents.ConsentRevoked,
            Category = WebhookEventCategories.Security,
            DisplayName = "Consent Revoked",
            Description = "Triggered when a user revokes consent from a client",
            EnabledByDefault = false
        }
    };
}
