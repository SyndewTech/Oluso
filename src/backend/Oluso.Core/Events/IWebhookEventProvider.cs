namespace Oluso.Core.Events;

/// <summary>
/// Interface for modules/packages to declare webhook events they support.
/// Enterprise packages implement this to register their webhook event types.
/// </summary>
/// <example>
/// <code>
/// public class Fido2WebhookEventProvider : IWebhookEventProvider
/// {
///     public string ProviderId => "fido2";
///     public string DisplayName => "FIDO2/Passkeys";
///
///     public IReadOnlyList&lt;WebhookEventDefinition&gt; GetEventDefinitions() => new[]
///     {
///         new WebhookEventDefinition
///         {
///             EventType = "passkey.registered",
///             Category = "Authentication",
///             DisplayName = "Passkey Registered",
///             Description = "Triggered when a user registers a new passkey"
///         }
///     };
/// }
/// </code>
/// </example>
public interface IWebhookEventProvider
{
    /// <summary>
    /// Unique identifier for this provider (e.g., "core", "fido2", "ldap")
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Display name for the provider
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Get all webhook events supported by this provider
    /// </summary>
    IReadOnlyList<WebhookEventDefinition> GetEventDefinitions();
}
